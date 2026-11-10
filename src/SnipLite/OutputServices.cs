using System.Drawing.Imaging;

namespace SnipLite;

internal static partial class Services
{
    public static DataObject ImageClipboardData(Bitmap image, bool bitmap)
    {
        var data = new DataObject();
        var png = new MemoryStream(); image.Save(png, ImageFormat.Png); png.Position = 0;
        data.SetData("PNG", false, png);
        if (bitmap) data.SetData(DataFormats.Bitmap, true, image);
        return data;
    }
    public static void CopyImage(Bitmap image, bool bitmap)
    {
        var data = ImageClipboardData(image, bitmap);
        try { Clipboard.SetDataObject(data, true, 5, 80); }
        finally { if (data.TryGetData<MemoryStream>("PNG", false, out var png)) png?.Dispose(); }
    }
    public static Bitmap? ClipboardImage()
    {
        if (Clipboard.TryGetData<MemoryStream>("PNG", out var png) && png is not null)
        { using (png) using (var image = new Bitmap(png)) return new Bitmap(image); }
        if (!Clipboard.ContainsImage()) return null;
        using var bitmap = Clipboard.GetImage(); return bitmap is null ? null : new Bitmap(bitmap);
    }
    public static string ImageFileName(AppSettings settings, string extension)
    {
        var stem = settings.ExpandFileName(DateTime.Now);
        return new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(stem).ToLowerInvariant())
            ? Path.ChangeExtension(stem, extension) : stem + extension;
    }
    public static string SaveCaptureFile(Bitmap image, AppSettings settings, string folder, string extension = ".png")
    {
        Directory.CreateDirectory(folder);
        var filename = ImageFileName(settings, extension);
        var path = Path.Combine(folder, filename);
        for (var i = 2; File.Exists(path); i++) path = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(filename)} ({i}){extension}");
        SaveImage(image, path, extension is ".jpg" or ".jpeg", settings.JpegQuality);
        return path;
    }
    public static void PlaySound(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        _ = Task.Run(() =>
        {
            try { using var player = new System.Media.SoundPlayer(path); player.PlaySync(); }
            catch (Exception ex) { AppLog.Write("音效播放失败", ex); }
        });
    }
}
