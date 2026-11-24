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
    public Padding FixedEdges { get; private set; }
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

    public bool CanAppend(int height)
    {
        var total = (long)Height + height;
        return height > 0 && Count < MaxSegments && total <= MaxHeight && Width * total <= MaxPixels;
    }

    internal Bitmap ReadFirstFrame() => new(_strips[0].Path);

    public bool Append(Bitmap current, FrameSample sample, int offset, Padding? confirmedEdges = null)
    {
        if (current.Width != Width || sample.Width != Width || current.Height != sample.Height ||
            sample.Height != _lastSample.Height || offset <= 0 || offset >= current.Height) return false;

        var edges = FixedEdges;
        if (Count == 1 && confirmedEdges is { } firstEdges) edges = firstEdges;
        var bodyHeight = current.Height - edges.Vertical;
        if (offset >= bodyHeight || !CanAppend(offset)) return false;
        // Cache the footer below each strip so Undo can restore the right footer.
        using var strip = current.Clone(new Rectangle(0, current.Height - edges.Bottom - offset,
            Width, offset + edges.Bottom), PixelFormat.Format32bppArgb);
        if (!Write(strip, offset, sample)) return false;
        FixedEdges = edges;
        return true;
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
        _strips.RemoveAt(_strips.Count - 1); Height -= last.Height; _cacheBytes -= last.Bytes; _lastSample = sample;
        if (Count == 1) FixedEdges = Padding.Empty;
        return true;
    }

    public Bitmap Build(CancellationToken token = default)
    {
        var result = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(result);
            graphics.Clear(Color.White);
            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            var y = 0;
            for (var index = 0; index < Count; index++)
            {
                token.ThrowIfCancellationRequested();
                var strip = _strips[index];
                using var image = new Bitmap(strip.Path);
                var first = index == 0;
                var height = first ? strip.Height - FixedEdges.Bottom : strip.Height;
                var clip = first ? new Rectangle(0, y, Width, height) :
                    new Rectangle(FixedEdges.Left, y, Width - FixedEdges.Horizontal, height);
                graphics.SetClip(clip);
                graphics.DrawImageUnscaled(image, 0, y);
                y += height;
                if (index == Count - 1 && FixedEdges.Bottom > 0)
                {
                    graphics.SetClip(new Rectangle(0, y, Width, FixedEdges.Bottom));
                    graphics.DrawImageUnscaled(image, 0, y - height);
                }
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
