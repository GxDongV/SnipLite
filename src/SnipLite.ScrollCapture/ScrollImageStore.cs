using System.Drawing.Imaging;

namespace SnipLite.ScrollCapture;

/// <summary>Stores appended strips on disk. Full-size history frames are not retained.</summary>
internal sealed class ScrollImageStore : IDisposable
{
    public const long MaxPixels = 32_000_000;
    public const int MaxHeight = 30_000;
    public const long MaxCacheBytes = 256 * 1024 * 1024;
    public const int MaxSegments = 512;
    private readonly string _directory;
    private readonly List<(string Path, int Height, int FrameHeight, long Bytes)> _strips = new();
    private FrameSample _lastSample = null!;
    private long _cacheBytes;
    public int Width { get; }
    public int Height { get; private set; }
    public int Count => _strips.Count;
    public FrameSample LastSample => _lastSample;
    public string DirectoryPath => _directory;

    public ScrollImageStore(Bitmap first)
    {
        if (first.Width < 32 || first.Height < 80 || (long)first.Width * first.Height > 16_000_000 || first.Height > MaxHeight)
            throw new ArgumentOutOfRangeException(nameof(first));
        Width = first.Width;
        _directory = Path.Combine(Path.GetTempPath(), "SnipLite", "ScrollCapture", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        try { if (!Write(first, first.Height, new FrameSample(first))) throw new IOException("Capture cache budget exceeded."); }
        catch { Dispose(); throw; }
    }

    public bool CanAppend(int height) => height > 0 && _strips.Count < MaxSegments && (long)Height + height <= MaxHeight && (long)Width * (Height + (long)height) <= MaxPixels;

    public bool Append(Bitmap current, FrameSample sample, int offset)
    {
        if (current.Width != Width || sample.Width != Width || current.Height != sample.Height || offset >= current.Height || !CanAppend(offset)) return false;
        using var strip = current.Clone(new Rectangle(0, current.Height - offset, Width, offset), PixelFormat.Format32bppArgb);
        return Write(strip, offset, sample);
    }

    private bool Write(Bitmap strip, int height, FrameSample sample)
    {
        var path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".png");
        try
        {
            strip.Save(path, ImageFormat.Png);
            var bytes = new FileInfo(path).Length + sample.Gray.Length;
            if (_cacheBytes + bytes > MaxCacheBytes) { File.Delete(path); return false; }
            File.WriteAllBytes(path + ".sample", sample.Gray);
            _strips.Add((path, height, sample.Height, bytes)); Height += height; _cacheBytes += bytes; _lastSample = sample;
            return true;
        }
        catch { File.Delete(path); File.Delete(path + ".sample"); throw; }
    }

    public bool Undo()
    {
        if (_strips.Count <= 1) return false;
        var last = _strips[^1]; var previous = _strips[^2];
        var sample = new FrameSample(Width, previous.FrameHeight, File.ReadAllBytes(previous.Path + ".sample"));
        File.Delete(last.Path); File.Delete(last.Path + ".sample");
        _strips.RemoveAt(_strips.Count - 1); Height -= last.Height; _cacheBytes -= last.Bytes; _lastSample = sample; return true;
    }

    public Bitmap Build(CancellationToken token = default)
    {
        var result = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(result);
            var y = 0;
            foreach (var strip in _strips)
            {
                token.ThrowIfCancellationRequested();
                using var image = new Bitmap(strip.Path);
                graphics.DrawImageUnscaled(image, 0, y); y += strip.Height;
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    public void Dispose()
    {
        _strips.Clear();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
