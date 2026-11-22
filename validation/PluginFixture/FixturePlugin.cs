using SnipLite.Plugin.Abstractions;

namespace PluginFixture;

public sealed class FixturePlugin : IPlugin, ICaptureProvider, IImageProcessor, IOutputProvider
{
    private readonly List<IDisposable> _registrations = new();
    private string _kind = "capture";
    public string Id => _kind;
    public PluginText Title => new("Fixture", "测试插件");
    public void Initialize(IPluginContext context)
    {
        _registrations.Add(context.RegisterCommand(new("ping", Title, _ => { context.Host.Log(FixtureDependency.Dependency.Message); return Task.CompletedTask; })));
        _registrations.Add(context.RegisterCommand(new("hold", Title, token => Task.Delay(Timeout.Infinite, token))));
        _registrations.Add(context.RegisterCaptureProvider(this));
        _kind = "process"; _registrations.Add(context.RegisterImageProcessor(this));
        _kind = "export"; _registrations.Add(context.RegisterOutputProvider(this));
    }
    public Task<PluginImage?> CaptureAsync(IPluginHost host, CancellationToken token) => Task.FromResult<PluginImage?>(new(new Bitmap(64, 100), new Rectangle(-100, 20, 64, 100)));
    public Task<PluginImage?> ProcessAsync(PluginImage input, IPluginHost host, CancellationToken token) => Task.FromResult<PluginImage?>(new(new Bitmap(input.Image), input.SourceBounds));
    public Task ExportAsync(PluginImage input, IPluginHost host, CancellationToken token) { host.Log("fixture-export:" + input.Image.Width); return Task.CompletedTask; }
    public void Dispose() { foreach (var registration in _registrations) registration.Dispose(); _registrations.Clear(); }
}

public sealed class BrokenPlugin : IPlugin
{
    public void Initialize(IPluginContext context)
    {
        context.RegisterCommand(new("leftover", new("Broken", "损坏"), _ => Task.CompletedTask));
        throw new InvalidOperationException("fixture-initialization-failed");
    }
    public void Dispose() { }
}
