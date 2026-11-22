namespace SnipLite.Plugin.Abstractions;

public static class PluginApi
{
    public const int MajorVersion = 1;
}

/// <summary>Plugins supply their own text. Other languages fall back to English.</summary>
public sealed record PluginText(string English, string SimplifiedChinese, string? TraditionalChinese = null)
{
    public string Get(string language) => language switch
    {
        "zh-CN" => SimplifiedChinese,
        "zh-TW" => TraditionalChinese ?? English,
        _ => English
    };
}

/// <summary>Owns Image. SourceBounds is physical screen provenance, not the output size.</summary>
public sealed class PluginImage(Bitmap image, Rectangle? sourceBounds = null) : IDisposable
{
    public Bitmap Image { get; } = image;
    public Rectangle? SourceBounds { get; } = sourceBounds;
    public void Dispose() => Image.Dispose();
}

public interface IPlugin : IDisposable
{
    void Initialize(IPluginContext context);
}

public sealed record PluginCommand(string Id, PluginText Title, Func<CancellationToken, Task> ExecuteAsync);

public interface IPluginContext
{
    IPluginHost Host { get; }
    string DataDirectory { get; }
    IDisposable RegisterCommand(PluginCommand command);
    IDisposable RegisterCaptureProvider(ICaptureProvider provider);
    IDisposable RegisterImageProcessor(IImageProcessor processor);
    IDisposable RegisterOutputProvider(IOutputProvider provider);
}

/// <summary>Interactive calls start on the UI thread. CPU work must not block it.</summary>
public interface IPluginHost
{
    string Language { get; }
    Task<Rectangle?> SelectRegionAsync(CancellationToken cancellationToken);
    Task<PluginImage?> SelectImageAsync(CancellationToken cancellationToken);
    /// <summary>Returns an owned bitmap. Must be called on the UI thread.</summary>
    Bitmap CaptureRegion(Rectangle physicalBounds);
    /// <summary>Borrows image until completion; the caller keeps ownership.</summary>
    Task PreviewImageAsync(PluginImage image, CancellationToken cancellationToken);
    /// <summary>Styles a plugin form and releases theme subscriptions when it is disposed.</summary>
    void StyleWindow(Form form);
    void Log(string message, Exception? exception = null);
}

public interface ICaptureProvider
{
    string Id { get; }
    PluginText Title { get; }
    /// <summary>Transfers ownership of the returned result to the host.</summary>
    Task<PluginImage?> CaptureAsync(IPluginHost host, CancellationToken cancellationToken);
}

public interface IImageProcessor
{
    string Id { get; }
    PluginText Title { get; }
    /// <summary>Input is borrowed and immutable. Return a newly owned image or null.</summary>
    Task<PluginImage?> ProcessAsync(PluginImage input, IPluginHost host, CancellationToken cancellationToken);
}

public interface IOutputProvider
{
    string Id { get; }
    PluginText Title { get; }
    /// <summary>Input is borrowed and must not be retained after completion.</summary>
    Task ExportAsync(PluginImage input, IPluginHost host, CancellationToken cancellationToken);
}
