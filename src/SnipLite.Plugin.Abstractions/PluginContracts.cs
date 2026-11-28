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

/// <summary>Optional settings action shown on the installed plugin row.</summary>
public interface IPluginSettings
{
    void ShowSettings(IWin32Window owner);
}

/// <summary>Optional host controls for settings dialogs. Returned controls are owned by their parent.</summary>
public interface IPluginSettingsUi
{
    void StyleSettingsWindow(Form form);
    Control CreateSettingsNavigation((string Title, string Icon, Control Page)[] pages);
    Control CreateSettingRow(string label, Control field);
    Control CreateSettingInput(TextBoxBase editor);
    Button CreateSettingsButton(string text);
    CheckBox CreateSettingsToggle(bool value);
}

public sealed record PluginCommand(string Id, PluginText Title, Func<CancellationToken, Task> ExecuteAsync)
{
    /// <summary>Optional command that stays in the active F1 screenshot editor.</summary>
    public Func<IScreenshotPluginSession, CancellationToken, Task>? ExecuteInScreenshotAsync { get; init; }

    /// <summary>Allows normal screenshots during a command that owns its own nonmodal UI.</summary>
    public bool AllowScreenshotsWhileRunning { get; init; }
}

public interface IScreenshotPluginSession
{
    Rectangle Selection { get; }
    /// <summary>Docks a nonmodal plugin panel below the active screenshot toolbar. Call on the UI thread.</summary>
    void ShowPanel(Form panel);
    /// <summary>Switches the editor to live capture mode. Call on the UI thread.</summary>
    void SetLive(bool live);
    /// <summary>Composites current screenshot annotations in place. The caller owns the bitmap.</summary>
    void ComposeFrame(Bitmap frame);
    /// <summary>Indicates whether the source pixels changed since the previous call.</summary>
    void ComposeFrame(Bitmap frame, bool sourceChanged) => ComposeFrame(frame);
}

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
    /// <summary>Keeps a nonmodal plugin form above the screenshot editor without activating it. Call on the UI thread.</summary>
    void KeepVisibleDuringScreenshot(Form form) { }
    /// <summary>Creates a themed choice button with a popup. The caller owns the button and sets its bounds.</summary>
    Button CreateChoiceButton(string[] options, int selectedIndex, Action<int> onChange) =>
        throw new NotSupportedException("This host does not provide themed choice buttons.");
    /// <summary>Saves an explicitly requested capture to the configured auto-save directory. Borrows the bitmap until completion.</summary>
    Task<string> SaveCaptureFrameAsync(Bitmap frame) =>
        throw new NotSupportedException("This host does not provide capture auto-save.");
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
