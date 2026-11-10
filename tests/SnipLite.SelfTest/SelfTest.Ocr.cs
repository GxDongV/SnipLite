using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text;
using System.Text.Json;

namespace SnipLite;

internal static partial class SelfTest
{
    // A practical smoke check, not a representative accuracy benchmark.
    public static void RunOcrSmoke(string? imageDirectory)
    {
        var results = new List<object>();
        try
        {
            CheckOcrGeometry();
            if (imageDirectory is not null)
            {
                Check("public-chinese", "text_rec.jpg", "韩国小馆", "zh-Hans");
                Check("public-english", "en_rec.jpg", "To facilitate the shot type analysis in videos, we collect MovieShots, a large-scale", "en-US");
                Check("public-small-text", "short.png", "中国网络空间安全", "zh-Hans");
                Check("public-upside-down", "text_cls.jpg", "怪我咯", "zh-Hans");
            }
            const string mixed = "SnipLite 截图与文字识别\n按 Ctrl+C 复制文字，按 Esc 返回。\nVersion 0.3.8  Email: demo@example.com";
            foreach (var dark in new[] { false, true })
            {
                using var bitmap = new Bitmap(680, 145, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bitmap))
                using (var font = new Font("Microsoft YaHei UI", 20, GraphicsUnit.Pixel))
                using (var brush = new SolidBrush(dark ? Color.White : Color.FromArgb(24, 24, 24)))
                {
                    g.Clear(dark ? Color.FromArgb(32, 32, 32) : Color.White);
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    g.DrawString(mixed, font, brush, new PointF(16, 16));
                }
                bitmap.Save(Path.Combine(OutputDir, $"ocr-mixed-{dark}.png"));
                Run(dark ? "generated-dark-ui" : "generated-light-ui", bitmap, mixed, "zh-Hans");
            }
            using var blank = new Bitmap(640, 120);
            using (var g = Graphics.FromImage(blank)) g.Clear(Color.White);
            Run("blank", blank, "", "zh-Hans");
        }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;
            results.Add(new { Error = ex.ToString() });
            Console.Error.WriteLine(ex);
        }
        File.WriteAllText(Path.Combine(OutputDir, "ocr-smoke.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));

        void Check(string name, string file, string expected, string language)
        {
            using var image = new Bitmap(Path.Combine(imageDirectory!, file));
            Run(name, image, expected, language);
        }
        void Run(string name, Bitmap image, string expected, string language)
        {
            foreach (var engine in new[] { OcrEngineKind.SimdPaddle, OcrEngineKind.Windows })
            {
                var timer = Stopwatch.StartNew();
                try
                {
                    // Call the enhanced engine directly so fallback cannot conceal a failed integration.
                    OcrDocument Recognize() => engine == OcrEngineKind.SimdPaddle
                        ? SimdOcr.RecognizeAsync(image).GetAwaiter().GetResult()
                        : Services.RecognizeWindowsTextAsync(image, language).GetAwaiter().GetResult();
                    var document = Task.Run(Recognize).GetAwaiter().GetResult();
                    var firstMs = timer.Elapsed.TotalMilliseconds;
                    timer.Restart();
                    // Also exercise the common service path used by capture, pin and hotkey OCR.
                    var warm = Task.Run(() => Services.RecognizeTextAsync(image, language, engine)).GetAwaiter().GetResult();
                    var warmMs = timer.Elapsed.TotalMilliseconds;
                    var matches = Normalize(document.Text) == Normalize(expected);
                    var stable = document.Text == warm.Text;
                    var geometry = document.Characters.All(c => !c.Bounds.IsEmpty && c.Bounds.Left >= 0 && c.Bounds.Top >= 0
                        && c.Bounds.Right <= image.Width + .1f && c.Bounds.Bottom <= image.Height + .1f);
                    if (!stable || !geometry || engine == OcrEngineKind.SimdPaddle && !matches)
                        Environment.ExitCode = 1;
                    results.Add(new { Name = name, Engine = engine.ToString(), Expected = expected, Actual = document.Text,
                        Matches = matches, Stable = stable, Geometry = geometry, FirstMs = firstMs, WarmMs = warmMs,
                        Characters = document.Characters.Count, Width = image.Width, Height = image.Height });
                    Console.WriteLine($"{name} {engine}: match={matches} geometry={geometry} first={firstMs:0}ms warm={warmMs:0}ms text={document.Text.ReplaceLineEndings(" / ")}");
                }
                catch (Exception ex)
                {
                    results.Add(new { Name = name, Engine = engine.ToString(), Error = ex.ToString() });
                    Console.Error.WriteLine($"{name} {engine}: {ex.Message}");
                    if (engine == OcrEngineKind.SimdPaddle) Environment.ExitCode = 1;
                }
            }
        }
        static string Normalize(string text) => string.Concat(text.Normalize(NormalizationForm.FormKC).Where(c => !char.IsWhiteSpace(c)));
    }

    private static void CheckOcrGeometry()
    {
        void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        var characters = OcrDocument.FromCharacters(new[]
        {
            new RecognizedCharacter("中", new(10, 10, 20, 20), ""),
            new RecognizedCharacter("文", new(30, 10, 20, 20), ""),
            new RecognizedCharacter("A", new(10, 40, 10, 20), Environment.NewLine),
            new RecognizedCharacter(" ", new(20, 40, 5, 20), ""),
            new RecognizedCharacter("B", new(25, 40, 10, 20), "")
        });
        Require(characters.Text == "中文" + Environment.NewLine + "A B", "OCR copy lost spaces or line breaks");
        Require(characters.GetText(2, 1) == "文" + Environment.NewLine + "A", "Reverse selection failed");
        Require(characters.GetText(3, 4) == " B", "Partial selection failed");
        Require(characters.HitTest(new(31, 15)) == 1, "Character hit testing failed");
        var settings = new AppSettings { OcrEngine = OcrEngineKind.Windows };
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Require(restored.OcrEngine == OcrEngineKind.Windows, "Engine preference round trip failed");
        Require(new AppSettings().OcrEngine == OcrEngineKind.SimdPaddle, "Enhanced OCR is not the default");
        Console.WriteLine("OCR selection, copying and engine settings: passed");
    }
}
