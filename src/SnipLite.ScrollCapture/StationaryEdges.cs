using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SnipLite.ScrollCapture;

internal static class StationaryEdges
{
    private const int ConsecutiveMovingLines = 3;
    private const int Contrast = 12;

    internal static Padding Find(Bitmap previous, Bitmap current, int offset = 0)
    {
        var width = current.Width;
        var height = current.Height;
        using var before = new BitmapRows(previous);
        using var after = new BitmapRows(current);
        var sameColumns = new int[width];
        var shiftedColumns = new int[width];
        var columnColors = new int[width];
        var columnDetail = new bool[width];
        var sameRows = new int[height];
        var shiftedRows = new int[height];
        var rowDetail = new bool[height];
        var top = height / 6;
        var bottom = height - top;

        for (var y = top; y < bottom; y++)
        {
            ReadSameRow(y);
            if (y == top) before.Pixels.CopyTo(columnColors, 0);
            for (var x = 0; x < width; x++)
            {
                if (Different(before.Pixels[x], after.Pixels[x])) sameColumns[x]++;
                columnDetail[x] |= Different(before.Pixels[x], columnColors[x]);
            }
            if (!ReadShiftedRow(y)) continue;
            for (var x = 0; x < width; x++)
                if (Different(before.Pixels[x], after.Pixels[x])) shiftedColumns[x]++;
        }

        var left = FindBoundary(sameColumns, shiftedColumns, columnDetail, bottom - top, false, offset != 0);
        var right = FindBoundary(sameColumns, shiftedColumns, columnDetail, bottom - top, true, offset != 0);
        var bodyWidth = width - left - right;
        if (bodyWidth < 32) return Padding.Empty;

        for (var y = 0; y < height; y++)
        {
            if (y >= height / 3 && y < height - height / 3) continue;
            ReadSameRow(y);
            sameRows[y] = Differences(before.Pixels, after.Pixels, left, width - right);
            rowDetail[y] = HasDetail(before.Pixels.AsSpan(left, bodyWidth));
            if (ReadShiftedRow(y))
                shiftedRows[y] = Differences(before.Pixels, after.Pixels, left, width - right);
        }

        var header = FindBoundary(sameRows, shiftedRows, rowDetail, bodyWidth, false, offset != 0);
        var footer = FindBoundary(sameRows, shiftedRows, rowDetail, bodyWidth, true, offset != 0);
        return height - header - footer >= 40 ? new Padding(left, header, right, footer) : Padding.Empty;

        void ReadSameRow(int y)
        {
            before.Read(y);
            after.Read(y);
        }

        bool ReadShiftedRow(int y)
        {
            if (offset == 0) return false;
            // For the top edge, compare its current pixels to the previous frame
            // farther down. For the bottom edge, compare in the other direction.
            var upperHalf = y < height / 2;
            var shiftedRow = upperHalf ? y + offset : y - offset;
            if ((uint)shiftedRow >= (uint)height) return false;
            if (upperHalf) before.Read(shiftedRow);
            else after.Read(shiftedRow);
            return true;
        }
    }

    internal static Rectangle Body(Size size, Padding edges) =>
        new(edges.Left, edges.Top, size.Width - edges.Horizontal, size.Height - edges.Vertical);

    private static int FindBoundary(int[] same, int[] shifted, bool[] detail, int samples, bool fromEnd, bool verify)
    {
        var significant = Math.Max(3, samples / 25);
        var tolerance = Math.Max(2, samples / 50);
        var movingLines = 0;
        var fixedLines = 0;
        for (var distance = 0; distance < same.Length / 3; distance++)
        {
            var i = fromEnd ? same.Length - 1 - distance : distance;
            if (same[i] >= significant)
            {
                if (++movingLines < ConsecutiveMovingLines) continue;
                var edge = distance + 1 - movingLines;
                return edge >= 3 && (!verify || fixedLines >= Math.Max(3, edge / 12)) ? edge : 0;
            }
            movingLines = 0;
            if (detail[i] && same[i] <= tolerance && shifted[i] >= same[i] + significant) fixedLines++;
        }
        return 0;
    }

    private static int Differences(int[] before, int[] after, int left, int right)
    {
        var count = 0;
        for (var x = left; x < right; x++)
            if (Different(before[x], after[x])) count++;
        return count;
    }

    private static bool Different(int a, int b) =>
        Math.Abs((a & 255) - (b & 255)) >= Contrast ||
        Math.Abs(((a >> 8) & 255) - ((b >> 8) & 255)) >= Contrast ||
        Math.Abs(((a >> 16) & 255) - ((b >> 16) & 255)) >= Contrast;

    private static bool HasDetail(ReadOnlySpan<int> pixels)
    {
        foreach (var pixel in pixels[1..])
            if (Different(pixels[0], pixel)) return true;
        return false;
    }

    private sealed class BitmapRows : IDisposable
    {
        private readonly Bitmap _image;
        private readonly BitmapData _data;
        public int[] Pixels { get; }

        public BitmapRows(Bitmap image)
        {
            _image = image;
            Pixels = new int[image.Width];
            _data = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        }

        public void Read(int y) => Marshal.Copy(_data.Scan0 + y * _data.Stride, Pixels, 0, Pixels.Length);
        public void Dispose() => _image.UnlockBits(_data);
    }
}
