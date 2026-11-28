using System.Drawing.Imaging;

namespace SnipLite;

internal static partial class Services
{
    private const string ClipboardFilePattern = "SnipLite_*.png";
    private static long _nextClipboardFileCleanup;

    public static void CopyImage(Bitmap image, bool bitmap, AppSettings? settings = null)
    {
        using var png = new MemoryStream();
        image.Save(png, ImageFormat.Png);
        png.Position = 0;
        string? file = null;
        if (settings is { CopyImageAsFile: true })
        {
            try { file = StageClipboardPng(png, settings); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Write("无法为剪贴板创建 PNG 文件", ex);
            }
        }

        // The Shell reads a DWORD with DROPEFFECT_COPY (1).
        using var dropEffect = file is null ? null : new MemoryStream([1, 0, 0, 0]);
        var data = CreateImageClipboardData(image, bitmap, png, file, dropEffect);
        Clipboard.SetDataObject(data, true, 5, 80);
        if (file is not null) CleanClipboardFiles(file);
        if (settings?.SoundOnCopy == true) _ = PlaySoundAsync(settings.CaptureSound);
        if (settings?.CopyImageAsFile == true && file is null) Toast.Error(UiText.T("图片已复制，无法同时复制 PNG 文件。"));
    }

    private static DataObject CreateImageClipboardData(Bitmap image, bool bitmap, MemoryStream png, string? file, MemoryStream? dropEffect)
    {
        var data = new DataObject();
        data.SetData("PNG", false, png);
        if (bitmap) data.SetData(DataFormats.Bitmap, true, image);
        if (file is not null)
        {
            data.SetData(DataFormats.FileDrop, false, new[] { file });
            data.SetData("Preferred DropEffect", false, dropEffect);
        }
        return data;
    }

    private static string StageClipboardPng(MemoryStream png, AppSettings settings)
    {
        var directory = Path.Combine(Path.GetTempPath(), "SnipLite", "clipboard", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, ImageFileName(settings, ".png"));
        Directory.CreateDirectory(directory);
        // Reuse the encoded bytes without moving the clipboard stream's read position.
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        png.WriteTo(file);
        return path;
    }

    private static void CleanClipboardFiles(string current)
    {
        // Clipboard contents can survive app exit. Keep the active PNG until a later
        // clipboard image replaces it, and give older files time for pending pastes.
        var now = Environment.TickCount64;
        if (now < _nextClipboardFileCleanup) return;
        _nextClipboardFileCleanup = now + (long)TimeSpan.FromHours(1).TotalMilliseconds;
        var cutoff = DateTime.UtcNow.AddDays(-1);
        var activeDirectory = Path.GetDirectoryName(current)!;
        var directory = Path.GetDirectoryName(activeDirectory)!;
        try
        {
            // Older builds staged files directly in the clipboard directory.
            foreach (var path in Directory.EnumerateFiles(directory, ClipboardFilePattern))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(path) < cutoff) File.Delete(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            foreach (var staged in new DirectoryInfo(directory).EnumerateDirectories())
            {
                if (!Guid.TryParseExact(staged.Name, "N", out _) ||
                    string.Equals(staged.FullName, activeDirectory, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    if (staged.LastWriteTimeUtc >= cutoff || (staged.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    foreach (var png in staged.EnumerateFiles("*.png")) png.Delete();
                    staged.Delete();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
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
    private static readonly object CaptureSaveLock = new();

    public static string SaveCaptureFile(Bitmap image, AppSettings settings, string folder, string extension = ".png", bool playSound = true)
    {
        Directory.CreateDirectory(folder);
        var filename = ImageFileName(settings, extension);
        var path = Path.Combine(folder, filename);
        lock (CaptureSaveLock)
        {
            for (var i = 2; File.Exists(path); i++) path = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(filename)} ({i}){extension}");
            SaveImage(image, path, extension is ".jpg" or ".jpeg", settings.JpegQuality);
        }
        if (playSound && settings.SoundOnSave) _ = PlaySoundAsync(settings.CaptureSound);
        return path;
    }
    public static Task<bool> PlaySoundAsync(string path) => Task.Run(() =>
    {
        try
        {
            using var builtIn = string.IsNullOrWhiteSpace(path)
                ? typeof(Services).Assembly.GetManifestResourceStream("SnipLite.PaperFold.wav")
                    ?? throw new InvalidOperationException(UiText.T("默认截图音效资源缺失。"))
                : null;
            using var player = builtIn is null ? new System.Media.SoundPlayer(path) : new System.Media.SoundPlayer(builtIn);
            player.LoadTimeout = 3000;
            player.Load();
            player.PlaySync();
            return true;
        }
        catch (Exception ex) { AppLog.Write("音效播放失败", ex); return false; }
    });
}
