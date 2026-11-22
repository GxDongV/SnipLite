using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SnipLite.ScrollCapture;

internal enum MatchKind { Unchanged, Forward, Reverse, Uncertain }
internal readonly record struct FrameMatch(MatchKind Kind, int Offset = 0);

/// <summary>Immutable sampled pixel rows. No GDI objects cross the matching worker boundary.</summary>
internal sealed class FrameSample
{
    public const int Columns = 48;
    public int Width { get; }
    public int Height { get; }
    public byte[] Gray { get; }

    internal FrameSample(int width, int height, byte[] gray)
    {
        if (gray.Length != height * Columns) throw new InvalidDataException("Invalid cached frame sample.");
        Width = width; Height = height; Gray = gray;
    }

    public FrameSample(Bitmap image)
    {
        Width = image.Width; Height = image.Height; Gray = new byte[Height * Columns];
        var data = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[Width * 4];
            for (var y = 0; y < Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                for (var col = 0; col < Columns; col++)
                {
                    var x = 4 + col * Math.Max(1, Width - 9) / (Columns - 1);
                    x = Math.Clamp(x, 1, Width - 2);
                    var sum = 0;
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var p = (x + dx) * 4;
                        sum += (row[p] * 29 + row[p + 1] * 150 + row[p + 2] * 77) >> 8;
                    }
                    Gray[y * Columns + col] = (byte)(sum / 3);
                }
            }
        }
        finally { image.UnlockBits(data); }
    }
}

internal static class FrameMatcher
{
    internal static string Diagnose(FrameSample previous, FrameSample current)
    {
        if (previous.Width != current.Width || previous.Height != current.Height) return "size-changed";
        var candidates = Enumerable.Range(1, Math.Max(1, current.Height - Math.Max(40, current.Height / 5)))
            .Select(offset => (Offset: offset, Error: Error(previous, current, offset, 160, 1, true)))
            .OrderBy(c => c.Error).ToArray();
        var best = candidates.Take(4);
        var winner = candidates[0];
        var runner = candidates.FirstOrDefault(c => Math.Abs(c.Offset - winner.Offset) > 2);
        return "stationary=" + Error(previous, current, 0, 64, 1).ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
            + "; candidates=" + string.Join(',', best.Select(c => $"{c.Offset}:{c.Error:F3}"))
            + $"; distinct={runner.Offset}:{runner.Error:F3}; verified={Error(previous, current, winner.Offset, current.Height, 1, true):F3}";
    }
    public static FrameMatch Match(FrameSample previous, FrameSample current, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (previous.Width != current.Width || previous.Height != current.Height) return new(MatchKind.Uncertain);
        if (Error(previous, current, 0, 64, 1) <= .65) return new(MatchKind.Unchanged);
        if (!Textured(previous) || !Textured(current)) return new(MatchKind.Uncertain);
        var forward = Find(previous, current, token);
        if (forward > 0) return new(MatchKind.Forward, forward);
        return Find(current, previous, token) > 0 ? new(MatchKind.Reverse) : new(MatchKind.Uncertain);
    }

    private static bool Textured(FrameSample frame)
    {
        double sum = 0, squares = 0;
        foreach (var pixel in frame.Gray) { sum += pixel; squares += pixel * pixel; }
        var count = frame.Gray.Length;
        return squares / count - Math.Pow(sum / count, 2) > 45;
    }

    private static int Find(FrameSample previous, FrameSample current, CancellationToken token)
    {
        var minimumOverlap = Math.Max(40, current.Height / 5);
        var candidates = new List<(int Offset, double Score)>();
        for (var offset = 1; offset <= current.Height - minimumOverlap; offset++)
        {
            if ((offset & 31) == 0) token.ThrowIfCancellationRequested();
            candidates.Add((offset, Error(previous, current, offset, 28, 3, true)));
        }
        var best = candidates.OrderBy(c => c.Score).Take(32)
            .Select(c => (c.Offset, Score: Error(previous, current, c.Offset, 160, 1, true))).OrderBy(c => c.Score).ToArray();
        if (best.Length == 0 || best[0].Score > 8) return 0;
        // Repeated rows can produce equally plausible offsets. Never guess past them.
        var runner = best.FirstOrDefault(c => Math.Abs(c.Offset - best[0].Offset) > 2);
        if (runner.Offset > 0 && runner.Score <= best[0].Score * 1.6 + 1.5) return 0;
        if (best[0].Score > 5.5)
        {
            // Small repaint/color differences can exceed the strict threshold even
            // at the correct offset. Accept these only with a much stronger unique
            // peak, pixel-accurate neighbors, and verification across every row.
            if (runner.Offset == 0 || runner.Score <= best[0].Score * 2 + 2) return 0;
            foreach (var neighbor in new[] { best[0].Offset - 1, best[0].Offset + 1 })
            {
                if (neighbor <= 0 || neighbor > current.Height - minimumOverlap) continue;
                if (Error(previous, current, neighbor, 160, 1, true) <= best[0].Score * 1.35 + 1) return 0;
            }
            token.ThrowIfCancellationRequested();
            if (Error(previous, current, best[0].Offset, current.Height, 1, true) > 8) return 0;
        }
        return best[0].Offset;
    }

    private static double Error(FrameSample previous, FrameSample current, int offset, int rowCount, int columnStep, bool ignoreEdgeColumns = false)
    {
        var overlap = current.Height - offset;
        var rows = Math.Min(rowCount, overlap);
        long total = 0; var count = 0;
        Span<int> columnErrors = stackalloc int[FrameSample.Columns];
        columnErrors.Clear();
        for (var i = 0; i < rows; i++)
        {
            var y = rows <= 1 ? 0 : i * (overlap - 1) / (rows - 1);
            for (var x = 0; x < FrameSample.Columns; x += columnStep)
            {
                var difference = Math.Abs(previous.Gray[(y + offset) * FrameSample.Columns + x] - current.Gray[y * FrameSample.Columns + x]);
                total += difference; columnErrors[x] += difference;
                count++;
            }
        }
        if (ignoreEdgeColumns)
        {
            // A scrollbar, native border or fixed side gutter can stay still while
            // the article moves. Ignore at most one sixth of sampled columns, and
            // only at the two outside edges. Interior content always participates.
            // Stationary/stability comparisons intentionally keep every column.
            var sampled = (FrameSample.Columns + columnStep - 1) / columnStep;
            var budget = sampled / 6;
            var left = 0; var right = (sampled - 1) * columnStep;
            for (var removed = 0; removed < budget; removed++)
            {
                var x = columnErrors[left] > columnErrors[right] ? left : right;
                total -= columnErrors[x]; count -= rows;
                if (x == left) left += columnStep; else right -= columnStep;
            }
        }
        return (double)total / Math.Max(1, count);
    }
}
