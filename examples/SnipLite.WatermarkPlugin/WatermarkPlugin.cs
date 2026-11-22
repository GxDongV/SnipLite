using SnipLite.Plugin.Abstractions;

namespace SnipLite.WatermarkPlugin;

/// <summary>References only the public SDK; never changes a borrowed input image.</summary>
public sealed class WatermarkPlugin : IPlugin, IImageProcessor
{
    private IDisposable? _registration;
    public string Id => "add-watermark";
    public PluginText Title => new("Add SnipLite watermark", "添加 SnipLite 水印", "加入 SnipLite 浮水印");
    public void Initialize(IPluginContext context) => _registration = context.RegisterImageProcessor(this);
    public void Dispose() { _registration?.Dispose(); _registration = null; }
    public Task<PluginImage?> ProcessAsync(PluginImage input, IPluginHost host, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new Bitmap(input.Image);
        try
        {
            using var graphics = Graphics.FromImage(result);
            using var font = new Font("Segoe UI", Math.Clamp(result.Width / 45f, 12, 40), FontStyle.Bold, GraphicsUnit.Pixel);
            var size = graphics.MeasureString("SnipLite", font);
            var point = new PointF(Math.Max(0, result.Width - size.Width - 12), Math.Max(0, result.Height - size.Height - 12));
            using var shadow = new SolidBrush(Color.FromArgb(190, Color.Black));
            using var ink = new SolidBrush(Color.FromArgb(225, Color.White));
            graphics.DrawString("SnipLite", font, shadow, point.X + 1, point.Y + 1);
            graphics.DrawString("SnipLite", font, ink, point);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<PluginImage?>(new(result, input.SourceBounds));
        }
        catch { result.Dispose(); throw; }
    }
}
