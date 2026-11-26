using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Sdcb.SimdPaddleOCR;
using Sdcb.SimdPaddleOCR.Models.ChineseV6Small;

namespace SnipLite;

// Windows is kept only so older settings files that stored it still deserialize; it is never used to pick an engine.
internal enum OcrEngineKind { SimdPaddle, Windows }

/// <summary>A single, lazily loaded offline model, shared by capture and pin OCR.</summary>
internal static class SimdOcr
{
    // Run one inference at a time; canceled waiters can leave without loading models.
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static PaddleOcrAll? _engine;

    public static async Task<OcrDocument> RecognizeAsync(Bitmap source, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Keep both model initialization and inference off the UI thread, even on first use.
            return await Task.Run(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                _engine ??= await PaddleOcrAll.LoadAsync(ChineseV6SmallModels.Default, new PaddleOcrOptions
                {
                    LineWorkerCount = 2,
                    DetIntraOpThreads = Math.Min(Environment.ProcessorCount, 4),
                    Detector = new() { Backend = OcrBackend.Cpu, MaxPooledSessions = 1 },
                    Classifier = new() { Backend = OcrBackend.Cpu, MaxPooledSessions = 2 },
                    Recognizer = new() { Backend = OcrBackend.Cpu, MaxPooledSessions = 2 }
                }).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                // Bound exceptionally large selections. Normal screenshots retain their pixels.
                var scale = Math.Min(1d, Math.Min(4096d / Math.Max(source.Width, source.Height),
                    Math.Sqrt(16_000_000d / ((long)source.Width * source.Height))));
                using var bitmap = new Bitmap(Math.Max(1, (int)Math.Round(source.Width * scale)),
                    Math.Max(1, (int)Math.Round(source.Height * scale)), PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.White);
                    graphics.DrawImage(source, new Rectangle(Point.Empty, bitmap.Size));
                }

                var result = RunOnBitmap(_engine, bitmap, cancellationToken);
                // Inference is synchronous. Let it finish safely, then
                // discard canceled results without interrupting its resources.
                cancellationToken.ThrowIfCancellationRequested();
                return ToDocument(result, source.Size, bitmap.Size);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { Gate.Release(); }
    }

    private static unsafe PaddleOcrResult RunOnBitmap(PaddleOcrAll engine, Bitmap bitmap, CancellationToken cancellationToken)
    {
        var stride = checked(bitmap.Width * 4);
        var length = checked(stride * bitmap.Height);
        var data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            ReadOnlySpan<byte> pixels;
            if (data.Stride == stride)
            {
                // Run reads this span synchronously while the bitmap stays locked.
                pixels = new ReadOnlySpan<byte>(data.Scan0.ToPointer(), length);
            }
            else
            {
                var packed = new byte[length];
                for (var y = 0; y < bitmap.Height; y++)
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), packed, y * stride, stride);
                pixels = packed;
            }

            cancellationToken.ThrowIfCancellationRequested();
            return engine.Run(pixels, bitmap.Width, bitmap.Height, returnCtcAlignment: true,
                sourceStride: stride, format: ImagePixelFormat.Bgra32);
        }
        finally { bitmap.UnlockBits(data); }
    }

    internal static OcrDocument ToDocument(PaddleOcrResult result, Size original, Size input)
    {
        var characters = new List<RecognizedCharacter>();
        var lineBounds = new List<RectangleF>();
        var sx = (float)original.Width / input.Width;
        var sy = (float)original.Height / input.Height;
        foreach (var line in result.Lines)
        {
            if (string.IsNullOrEmpty(line.Text)) continue;
            var detected = line.Box;
            var bounds = Bounds(detected.X1, detected.Y1, detected.X2, detected.Y2, detected.X3, detected.Y3, detected.X4, detected.Y4);
            lineBounds.Add(bounds);
            var first = characters.Count;
            var prefix = characters.Count == 0 ? "" : Environment.NewLine;
            var boxes = line.EstimateCharacterBoxes();
            if (boxes.Length == 0 || string.Concat(boxes.Select(b => b.Text)) != line.Text)
            {
                // Retain all text when a future model cannot return token alignment.
                var fallback = new OcrDocument(new[] { new RecognizedWord(line.Text, bounds, 0) });
                characters.AddRange(fallback.Characters.Select((c, index) => index == 0 ? c with { Prefix = prefix } : c));
                continue;
            }
            foreach (var box in boxes)
            {
                characters.Add(new(box.Text, Bounds(box.X1, box.Y1, box.X2, box.Y2, box.X3, box.Y3, box.X4, box.Y4), prefix));
                prefix = "";
            }
            ExtendLineEdges(characters, first, bounds);
        }
        return OcrDocument.FromCharacters(characters, lineBounds);

        RectangleF Bounds(float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4)
        {
            var left = Math.Clamp(Math.Min(Math.Min(x1, x2), Math.Min(x3, x4)) * sx, 0, original.Width);
            var top = Math.Clamp(Math.Min(Math.Min(y1, y2), Math.Min(y3, y4)) * sy, 0, original.Height);
            var right = Math.Clamp(Math.Max(Math.Max(x1, x2), Math.Max(x3, x4)) * sx, left, original.Width);
            var bottom = Math.Clamp(Math.Max(Math.Max(y1, y2), Math.Max(y3, y4)) * sy, top, original.Height);
            return RectangleF.FromLTRB(left, top, right, bottom);
        }
    }

    private static void ExtendLineEdges(List<RecognizedCharacter> characters, int firstIndex, RectangleF line)
    {
        var first = characters[firstIndex];
        var last = characters[^1];
        if (firstIndex == characters.Count - 1)
        {
            characters[firstIndex] = first with { Bounds = RectangleF.Union(first.Bounds, line) };
            return;
        }

        // CTC estimates can leave about half a glyph outside either end of the run.
        // Use the detected line to extend the outer edges, retaining the interior boundaries.
        var dx = last.Bounds.X + last.Bounds.Width / 2 - (first.Bounds.X + first.Bounds.Width / 2);
        var dy = last.Bounds.Y + last.Bounds.Height / 2 - (first.Bounds.Y + first.Bounds.Height / 2);
        var vertical = Math.Abs(dy) > Math.Abs(dx);
        var forward = (vertical ? dy : dx) >= 0;
        characters[firstIndex] = first with { Bounds = Extend(first.Bounds, forward) };
        characters[^1] = last with { Bounds = Extend(last.Bounds, !forward) };

        RectangleF Extend(RectangleF box, bool leading) => vertical
            ? RectangleF.FromLTRB(box.Left, leading ? Math.Min(box.Top, line.Top) : box.Top,
                box.Right, leading ? box.Bottom : Math.Max(box.Bottom, line.Bottom))
            : RectangleF.FromLTRB(leading ? Math.Min(box.Left, line.Left) : box.Left, box.Top,
                leading ? box.Right : Math.Max(box.Right, line.Right), box.Bottom);
    }
}
