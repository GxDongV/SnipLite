using SnipLite.Plugin.Abstractions;

namespace SnipLite.ScrollCapture;

public sealed class ScrollCapturePlugin : IPlugin, ICaptureProvider
{
    private IDisposable? _registration;
    public string Id => "capture";
    public PluginText Title => new("Scrolling screenshot…", "长截图…", "捲動截圖…");

    public void Initialize(IPluginContext context) =>
        _registration = context.RegisterCaptureProvider(this);
    public void Dispose() { _registration?.Dispose(); _registration = null; }

    public async Task<PluginImage?> CaptureAsync(IPluginHost host, CancellationToken cancellationToken)
    {
        var bounds = await host.SelectRegionAsync(cancellationToken);
        if (bounds is not Rectangle region) return null;
        await Task.Delay(180, cancellationToken);
        using var session = new ScrollCaptureForm(host, region, cancellationToken);
        // A modal form disables the target when it belongs to this process.
        // Keep capturing asynchronously without disabling any desktop window.
        var completion = ObserveCompletion(session);
        session.Show();
        var result = await completion;
        if (cancellationToken.IsCancellationRequested)
        {
            result?.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
        }
        return result;
    }

    internal static Task<PluginImage?> ObserveCompletion(ScrollCaptureForm session)
    {
        var completion = new TaskCompletionSource<PluginImage?>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Modeless Close disposes the form after FormClosed. Transfer ownership before disposal.
        session.FormClosed += (_, _) => completion.TrySetResult(session.TakeResult());
        return completion.Task;
    }
}
