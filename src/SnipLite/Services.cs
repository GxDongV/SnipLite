using System.Drawing.Imaging;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using ZXing;
using ZXing.Common;
using ZXing.Windows.Compatibility;

namespace SnipLite;

internal static partial class Services
{
    /// <summary>Local OCR through the Windows engine: no extra runtime, no network.</summary>
    public static async Task<string> OcrAsync(Bitmap bitmap, string language)
    {
        try
        {
            return (await RecognizeTextAsync(bitmap, language)).Text;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static async Task<OcrDocument> RecognizeTextAsync(Bitmap bitmap, string language)
    {
            using var memory = new MemoryStream();
            bitmap.Save(memory, ImageFormat.Png);
            memory.Position = 0;

            using var stream = memory.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            // Windows OCR has a maximum image size. Keep the geometry mapped
            // back to the original crop after downscaling large selections.
            var scale = Math.Min(1d, (double)OcrEngine.MaxImageDimension / Math.Max(bitmap.Width, bitmap.Height));
            var transform = new BitmapTransform
            {
                ScaledWidth = (uint)Math.Max(1, (int)Math.Round(bitmap.Width * scale)),
                ScaledHeight = (uint)Math.Max(1, (int)Math.Round(bitmap.Height * scale))
            };
            using var software = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
                ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);

            OcrEngine? engine = null;

            if (!string.IsNullOrWhiteSpace(language))
                engine = OcrEngine.TryCreateFromLanguage(new Language(language));

            engine ??= OcrEngine.TryCreateFromUserProfileLanguages();

            if (engine is null)
                throw new InvalidOperationException("OCR 引擎不可用，请在 Windows 语言设置中安装对应的识别语言包。");

            var result = await engine.RecognizeAsync(software);
            var sx = (float)bitmap.Width / software.PixelWidth;
            var sy = (float)bitmap.Height / software.PixelHeight;
            return new OcrDocument(result.Lines.SelectMany((line, index) => line.Words.Select(word =>
                new RecognizedWord(word.Text, new RectangleF(
                    (float)word.BoundingRect.X * sx, (float)word.BoundingRect.Y * sy,
                    (float)word.BoundingRect.Width * sx, (float)word.BoundingRect.Height * sy), index))));
    }

    public static string? DecodeQr(Bitmap bitmap)
    {
        try
        {
            var reader = new BarcodeReader
            {
                AutoRotate = true,
                Options = new DecodingOptions
                {
                    TryHarder = true,
                    TryInverted = true
                }
            };

            return reader.Decode(bitmap)?.Text;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>PNG, or JPEG with the configured quality (the default encoder uses 75).</summary>
    public static void SaveImage(Bitmap image, string path, bool jpeg, int quality = 92)
    {
        if (!jpeg)
        {
            image.Save(path, ImageFormat.Png);
            return;
        }

        var encoder = ImageCodecInfo.GetImageEncoders()
            .FirstOrDefault(codec => codec.FormatID == ImageFormat.Jpeg.Guid);

        if (encoder is null)
        {
            image.Save(path, ImageFormat.Jpeg);
            return;
        }

        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)Math.Clamp(quality, 30, 100));
        image.Save(path, encoder, parameters);
    }
}
