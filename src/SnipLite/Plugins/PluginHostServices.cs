using System.Drawing.Imaging;
using SnipLite.Plugin.Abstractions;

namespace SnipLite.Plugins;

internal static class PluginUi
{
    public static string Text(string english, string chinese, string? traditional = null) =>
        new PluginText(english, chinese, traditional).Get(Localization.LanguageService.CurrentLanguage);

    public static CancellationTokenRegistration CloseOnCancellation(Form form, CancellationToken token)
    {
        form.Shown += (_, _) => { if (token.IsCancellationRequested) form.Close(); };
        return token.Register(() =>
        {
            try
            {
                if (form.IsHandleCreated && !form.IsDisposed)
                {
                    if (form.InvokeRequired) form.BeginInvoke(() => { if (!form.IsDisposed) form.Close(); });
                    else form.Close();
                }
            }
            catch (InvalidOperationException) { }
        });
    }
}

internal sealed class PluginHostServices(AppSettings settings, Action<PluginImage, bool> publish) : IPluginHost, IPluginSettingsUi
{
    private readonly List<Form> _screenshotOverlays = [];
    public PluginManager? Manager { get; set; }
    public string Language => Localization.LanguageService.CurrentLanguage;
    internal Rectangle? PendingSelection { get; set; }

    public Task<Rectangle?> SelectRegionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (PendingSelection is Rectangle selection)
        {
            PendingSelection = null;
            return Task.FromResult<Rectangle?>(selection);
        }
        using var form = new PluginRegionForm();
        using var registration = PluginUi.CloseOnCancellation(form, cancellationToken);
        var result = form.ShowDialog() == DialogResult.OK ? form.Selection : (Rectangle?)null;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(result);
    }

    public async Task<PluginImage?> SelectImageAsync(CancellationToken cancellationToken)
    {
        var bounds = await SelectRegionAsync(cancellationToken);
        // Let DWM remove the selector before reading the live desktop.
        await Task.Delay(100, cancellationToken);
        return bounds is Rectangle region ? new PluginImage(CaptureRegion(region), region) : null;
    }

    public Bitmap CaptureRegion(Rectangle physicalBounds)
    {
        if (physicalBounds.Width < 32 || physicalBounds.Height < 32 || (long)physicalBounds.Width * physicalBounds.Height > 16_000_000 ||
            !SystemInformation.VirtualScreen.Contains(physicalBounds)) throw new ArgumentOutOfRangeException(nameof(physicalBounds), "Capture region must fit on the desktop and within 16 megapixels.");
        var bitmap = new Bitmap(physicalBounds.Width, physicalBounds.Height, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(physicalBounds.Location, Point.Empty, physicalBounds.Size, CopyPixelOperation.SourceCopy);
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }

    public Task PreviewImageAsync(PluginImage image, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var form = new PluginImagePreview(image, settings, publish, this, Manager, cancellationToken);
        using var registration = PluginUi.CloseOnCancellation(form, cancellationToken);
        form.ShowDialog();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public void StyleWindow(Form form)
    {
        form.Icon = AppIcon.Window;
        void Apply()
        {
            if (form.IsDisposed) return;
            AppTheme.Apply(form);
            if (form.IsHandleCreated) AppTheme.ApplyWindow(form);
        }
        form.HandleCreated += (_, _) => Apply();
        form.Disposed += (_, _) => AppTheme.Changed -= Apply;
        AppTheme.Changed += Apply;
        Apply();
    }

    public void KeepVisibleDuringScreenshot(Form form)
    {
        if (_screenshotOverlays.Contains(form)) return;
        _screenshotOverlays.Add(form);
        form.Disposed += (_, _) => _screenshotOverlays.Remove(form);
    }

    public Button CreateChoiceButton(string[] options, int selectedIndex, Action<int> onChange) =>
        FluentChooser.Create(options, selectedIndex, onChange);

    public void StyleSettingsWindow(Form form)
    {
        var font = settings.CreateInterfaceFont();
        form.Disposed += (_, _) => font.Dispose();
        form.Font = font;
        StyleWindow(form);
    }

    public Control CreateSettingsNavigation((string Title, string Icon, Control Page)[] pages) =>
        FluentSettingsLayout.Navigation(pages);

    public Control CreateSettingRow(string label, Control field) => FluentSettingsLayout.Row(label, field);
    public Control CreateSettingInput(TextBoxBase editor) => new FluentInputFrame(editor);
    public Button CreateSettingsButton(string text) => new FluentButton { Text = text };
    public CheckBox CreateSettingsToggle(bool value) => new FluentToggle { Checked = value };

    public Task<string> SaveCaptureFrameAsync(Bitmap frame)
    {
        var folder = settings.SaveDirectory;
        return Task.Run(() =>
        {
            var path = Services.SaveCaptureFile(frame, settings, folder, playSound: false);
            AutoSavedCaptures.Record(path);
            return path;
        });
    }

    internal void ShowScreenshot(CaptureForm screenshot)
    {
        screenshot.IsCompanionWindowActive = IsScreenshotOverlayActive;
        screenshot.Activated += (_, _) => screenshot.BeginInvoke(() =>
        {
            if (!screenshot.IsDisposed && screenshot.ContainsFocus) RaiseScreenshotOverlays();
        });
        screenshot.Shown += (_, _) => RaiseScreenshotOverlays();
        screenshot.Show();
    }

    private bool IsScreenshotOverlayActive()
    {
        for (var window = Win32.GetForegroundWindow(); window != 0; window = Win32.GetWindow(window, Win32.GW_OWNER))
        {
            if (_screenshotOverlays.Any(form => form.Visible && form.IsHandleCreated && form.Handle == window)) return true;
        }
        return false;
    }

    internal void RaiseScreenshotOverlays()
    {
        foreach (var form in _screenshotOverlays)
        {
            if (!form.Visible || !form.IsHandleCreated) continue;
            Win32.SetWindowPos(form.Handle, Win32.HWND_TOPMOST, 0, 0, 0, 0,
                Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
        }
    }

    public void Log(string message, Exception? exception = null) => AppLog.Write(message, exception);
}
