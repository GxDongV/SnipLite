using System.Diagnostics;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using SnipLite;
using SnipLite.Plugins;

namespace SnipLiteChecks;

internal static class RecorderIntegration
{
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        Console.WriteLine("PASS " + name);
    }

    private static async Task WaitFor(Func<bool> done, string error)
    {
        var clock = Stopwatch.StartNew();
        while (!done() && clock.Elapsed < TimeSpan.FromSeconds(8)) await Task.Delay(25);
        if (!done()) throw new TimeoutException(error);
    }

    private static object PrivateField(object instance, string name) =>
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;

    private static void ClickTool(CaptureForm capture, Rectangle tool)
    {
        var position = tool.Location + new Size(tool.Width / 2, tool.Height / 2);
        SendMessage(capture.Handle, 0x201, 1, MakePoint(position));
        if (!capture.IsDisposed) SendMessage(capture.Handle, 0x202, 0, MakePoint(position));
    }

    private static nint MakePoint(Point point) => (point.Y << 16) | (point.X & 0xffff);

    private static void DrawStroke(CaptureForm capture, Point from, Point to)
    {
        SendMessage(capture.Handle, 0x201, 1, MakePoint(from));
        SendMessage(capture.Handle, 0x200, 1, MakePoint(to));
        SendMessage(capture.Handle, 0x202, 0, MakePoint(to));
    }

    [DllImport("user32.dll")] private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out int processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, System.Text.StringBuilder buffer, int capacity);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(nint window, out uint affinity);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(nint window, uint affinity);
    [DllImport("user32.dll")] private static extern bool GetLayeredWindowAttributes(nint window, out uint key, out byte alpha, out uint flags);
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(nint destination, int x, int y, int width, int height,
        nint source, int sourceX, int sourceY, uint operation);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);

    private static Form VisibleOverlay(string name) =>
        Application.OpenForms.Cast<Form>().Single(window => window.Visible && window.GetType().Name == name);

    public static int Run(string package, string? preview = null, bool benchmark = false)
    {
        var root = Path.Combine(Path.GetTempPath(), "SnipLite-RecorderIntegration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var screen = Screen.PrimaryScreen!.Bounds;
        var selection = benchmark ? new Rectangle(screen.Left + 40, screen.Top + 40, 1920, 1080) : new Rectangle(screen.Left + 200, screen.Top + 180, 640, 320);
        var renderColor = Color.FromArgb(230, 241, 246);
        using var target = new Form
        {
            FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
            Bounds = screen, BackColor = renderColor
        };
        var exit = 0;

        target.Shown += async (_, _) =>
        {
            try
            {
                var settings = new AppSettings
                {
                    ExitCaptureOnDeactivate = true, ShowCrosshair = false, KeepResponsive = true,
                    SaveDirectory = Path.Combine(root, "auto-save"), AutoSave = false
                };
                var host = new PluginHostServices(settings, (_, _) => { });
                using var manager = new PluginManager(host, () => true, Path.Combine(root, "settings.json"), Path.Combine(root, "plugins"));
                manager.ImportPackage(package);
                manager.SetEnabled(manager.Records.Single(), true);
                Check(manager.Records.Single().Settings is not null, "enabled recorder exposes save-folder settings to the host");
                var command = manager.Commands.Single();
                Check(command.ExecuteInScreenshot is null, "recording command returns the F1 editor to the desktop before opening its controls");
                using var capture = new CaptureForm(SystemInformation.VirtualScreen, CaptureMode.Snip, settings, selection)
                {
                    PluginButtons = [(command.Id, command.Title.Get("zh-CN"), command.Icon)]
                };
                Task? commandTask = null;
                capture.PluginRequested += (id, bounds) =>
                    commandTask = manager.ExecuteFromSelectionAsync(id, bounds, host);
                capture.Show();
                var bar = (AnnotationBar)PrivateField(capture, "_bar");
                await WaitFor(() => !bar.ItemBounds(command.Id).IsEmpty, "F1 toolbar did not appear.");
                ClickTool(capture, bar.ItemBounds(command.Id));
                await WaitFor(() => capture.IsDisposed && Application.OpenForms.Cast<Form>().Any(IsRecorder),
                    "Recorder did not replace the frozen screenshot with a standalone panel.");
                var panel = Application.OpenForms.Cast<Form>().Single(IsRecorder);
                var recorderSettings = PrivateField(panel, "_settings");
                Field(recorderSettings, "_directory").SetValue(recorderSettings, Path.Combine(root, "recorder-settings"));
                Check(panel.Owner is null, "the recording controls are independent of the screenshot window");
                ActivateTarget(target);
                var readyProbe = new Point(selection.Left + 200, selection.Top + 160);
                try
                {
                    await WaitFor(() => WindowFromPoint(readyProbe) == target.Handle, "The desktop target could not be reached in ready mode.");
                }
                catch (TimeoutException)
                {
                    var window = WindowFromPoint(readyProbe);
                    GetWindowThreadProcessId(window, out var pid);
                    var name = new System.Text.StringBuilder(128);
                    GetClassName(window, name, name.Capacity);
                    Console.Error.WriteLine($"Desktop probe hit {name}, PID {pid}. Target PID {Environment.ProcessId}. Forms: " +
                        string.Join(", ", Application.OpenForms.Cast<Form>().Select(form => $"{form.GetType().Name}/{form.Handle:x}/{form.Bounds}")));
                    throw;
                }
                Check(true, "preparing a recording leaves the desktop available");
                Check(PrivateField(panel, "_session") is null && manager.IsRunning && !manager.BlocksScreenshots,
                    "ready mode waits for Start and permits a new screenshot");
                if (Environment.GetEnvironmentVariable("SNIPLITE_CHECK_SETTINGS_ONLY") == "1")
                {
                    VerifyRecorderSettingsDialog(panel);
                    VerifyRecorderSettingsDialog(panel, () => manager.Records.Single().Settings!.ShowSettings(target));
                    panel.Close();
                    if (commandTask is not null) await commandTask;
                    await VerifyCaptureSettings();
                    return;
                }
                var cursorBeforeHover = Cursor.Position;
                try
                {
                    var startButton = (Button)PrivateField(panel, "_start");
                    var point = startButton.PointToScreen(new Point(startButton.Width / 2, startButton.Height / 2));
                    var hit = WindowFromPoint(point);
                    GetWindowThreadProcessId(hit, out var hitPid);
                    Check(GetAncestor(hit, 2) == panel.Handle, $"the hover position reaches the toolbar (PID {hitPid})");
                    Cursor.Position = readyProbe;
                    await Task.Delay(80);
                    Cursor.Position = point;
                    try
                    {
                        await WaitFor(() => Field(panel, "_controlTip").GetValue(panel) is Form { Visible: true },
                            "The first hover did not display its tooltip before recording.");
                    }
                    catch (TimeoutException)
                    {
                        Console.Error.WriteLine($"Hover: cursor={Cursor.Position} clip={Cursor.Clip} requested={point}, active={Form.ActiveForm}, " +
                            $"hovered={Field(panel, "_hoveredControl").GetValue(panel)}");
                        throw;
                    }
                    var tip = (Form)PrivateField(panel, "_controlTip");
                    var hoverExcluded = GetWindowDisplayAffinity(tip.Handle, out var hoverAffinity) && hoverAffinity == 0x11;
                    Check(!panel.ContainsFocus && hoverExcluded,
                        $"the first hover works without focusing the toolbar and excludes its tooltip (focus={panel.ContainsFocus}, affinity={hoverAffinity:x})");
                }
                finally { Cursor.Position = cursorBeforeHover; }
                await WaitFor(() => Application.OpenForms.Cast<Form>().Any(window => window.Visible && window.GetType().Name == "SelectionOutline"),
                    "the selection has no visible outline in ready mode: " + panel.AccessibleDescription);
                foreach (var window in new[] { panel, VisibleOverlay("SelectionOutline"), VisibleOverlay("RecorderStatusWindow") })
                    Check(DwmGetWindowAttribute(window.Handle, 1, out var rendering, sizeof(int)) == 0 && rendering == 0,
                        window.GetType().Name + " has no DWM non-client rendering");

                static bool IsRecorder(Form window) => window.GetType().Name == "RecorderForm";
                var format = (Button)PrivateField(panel, "_format");
                var formatMenu = format.ContextMenuStrip!;
                var start = (Button)PrivateField(panel, "_start");
                var stop = (Button)PrivateField(panel, "_stop");
                var pause = (Button)PrivateField(panel, "_pause");
                var copy = (Button)PrivateField(panel, "_copy");
                Field(panel, "_saveFolder").SetValue(panel, root);
                Check(panel.Controls.OfType<Button>().Count() == 15 && formatMenu.Items.Cast<ToolStripMenuItem>()
                    .Select(item => item.Text).SequenceEqual(["MP4", "GIF", "APNG", "WebP"]),
                    "panel includes independent audio and input-effect switches");

                var buttons = panel.Controls.OfType<Button>().Where(button => button != format).ToArray();
                settings.ShowCrosshair = true;
                using (var fresh = new CaptureForm(SystemInformation.VirtualScreen, CaptureMode.Snip, settings,
                    showFrozenCursor: false))
                {
                    var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    fresh.Shown += (_, _) => shown.TrySetResult();
                    host.ShowScreenshot(fresh);
                    await shown.Task.WaitAsync(TimeSpan.FromSeconds(8));
                    var startPoint = start.PointToScreen(new Point(start.Width / 2, start.Height / 2));
                    Check(GetAncestor(WindowFromPoint(startPoint), 2) == panel.Handle,
                        "the recorder toolbar stays visible and clickable when a new screenshot opens");
                    await MoveScreenshotCrosshair(fresh, new Point(selection.Left + 270, selection.Top + 167));
                    Check(IsAbove(panel, fresh) && IsAbove(VisibleOverlay("SelectionOutline"), fresh) &&
                        IsAbove(VisibleOverlay("RecorderStatusWindow"), fresh), "the recording controls and region stay visible with the screenshot crosshair active");
                    fresh.Close();
                }
                settings.ShowCrosshair = false;
                var fitsAbove = selection.Top - panel.Height - 8 >= Screen.FromRectangle(selection).WorkingArea.Top;
                Check((fitsAbove ? panel.Bottom <= selection.Top : panel.Top >= selection.Top) &&
                    Math.Abs(panel.Left + panel.Width / 2 - (selection.Left + selection.Width / 2)) <= 1,
                    "the default toolbar is centered at the top of the selection");
                Check(buttons.Select(button => button.Top).Distinct().Count() == 1 && buttons.All(button => button.Width == button.Height),
                    "recording toolbar uses one compact row of square icon buttons");
                var tips = (ToolTip)PrivateField(panel, "_tips");
                Check(buttons.All(button => !string.IsNullOrWhiteSpace(tips.GetToolTip(button)) &&
                    !string.IsNullOrWhiteSpace(button.AccessibleName)), "each icon has a tooltip and accessible name");
                var keyboard = Feature(panel, "Keyboard");
                var offTip = tips.GetToolTip(keyboard);
                keyboard.PerformClick();
                var active = keyboard.GetType().GetProperty("Selected", BindingFlags.Instance | BindingFlags.NonPublic)!;
                Check(Equals(active.GetValue(keyboard), true) && tips.GetToolTip(keyboard) != offTip,
                    "enabled effect has an active indicator and a different tooltip");
                keyboard.PerformClick();
                Check(Equals(active.GetValue(keyboard), false) && tips.GetToolTip(keyboard) == offTip,
                    "disabled effect has the default indicator and tooltip");

                if (benchmark)
                {
                    await BenchmarkRecording();
                    panel.Close();
                    if (commandTask is not null) await commandTask;
                    return;
                }
                VerifyKeyLifetime(panel);
                VerifyStatusPreferences(panel, root, host);
                VerifyStatusIcons(panel, format);
                ActivateTarget(target);
                await VerifyAudioMenus(panel);
                VerifyRecorderSettingsDialog(panel);
                VerifyRecorderSettingsDialog(panel, () => manager.Records.Single().Settings!.ShowSettings(target));
                Field(panel, "_saveFolder").SetValue(panel, root);
                SelectFormat(format, "GIF");
                format.PerformClick();
                try
                {
                    Check(formatMenu.Visible && formatMenu.Region is not null &&
                        formatMenu.Items.OfType<ToolStripMenuItem>().Single(item => item.Checked).Text == "GIF",
                        "the rounded format popup marks the current selection");
                    Check(GetWindowDisplayAffinity(formatMenu.Handle, out var menuAffinity) && menuAffinity == 0x11,
                        "the format popup is excluded from capture");
                }
                finally { formatMenu.Close(); }
                SelectFormat(format, "MP4");
                if (preview is not null)
                {
                    keyboard.PerformClick();
                    SavePreview(panel, target, selection, preview);
                    keyboard.PerformClick();
                }
                await RecordFormat("MP4", true);
                await RecordFormat("GIF", false);
                await RecordFormat("APNG", false);
                await RecordFormat("WebP", false);
                var expectedFolder = Path.Combine(root, "recordings");
                Field(panel, "_saveFolder").SetValue(panel, expectedFolder);
                ((Button)PrivateField(panel, "_folder")).PerformClick();
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
                object? explorer = null;
                await WaitFor(() =>
                {
                    foreach (var window in shell.Windows())
                    {
                        if (Uri.TryCreate((string)window.LocationURL, UriKind.Absolute, out var location)
                            && location.IsFile && Path.TrimEndingDirectorySeparator(location.LocalPath)
                                .Equals(expectedFolder, StringComparison.OrdinalIgnoreCase))
                        {
                            explorer = window;
                            return true;
                        }
                    }
                    return false;
                }, "Explorer did not open the active recordings folder.");
                Check(Directory.Exists(expectedFolder), "folder icon opens the configured recording directory in Explorer");
                if (explorer is not null) ((dynamic)explorer).Quit();
                Field(panel, "_saveFolder").SetValue(panel, root);
                var beforeClose = Directory.GetFiles(root, "*.mp4").Length;
                SelectFormat(format, "MP4");
                start.PerformClick();
                await WaitFor(() => pause.Enabled, "Recorder did not start before close test.");
                await Task.Delay(200);
                pause.PerformClick();
                panel.Close();
                await WaitFor(() => panel.IsDisposed && !manager.IsRunning,
                    "Closing the paused recording panel did not exit the recording session.");
                Check(Directory.GetFiles(root, "*.mp4").Length == beforeClose + 1,
                    "closing the paused panel saves the recorded frames");
                if (commandTask is not null) await commandTask;
                var fullScreenTask = manager.ExecuteFromSelectionAsync(command.Id, screen, host);
                await WaitFor(() => Application.OpenForms.Cast<Form>().Any(IsRecorder), "Full-screen recording controls did not open.");
                var fullScreenPanel = Application.OpenForms.Cast<Form>().Single(IsRecorder);
                await WaitFor(() => Application.OpenForms.Cast<Form>().Any(window => window.Visible && window.GetType().Name == "RecorderStatusWindow"),
                    "Full-screen overlays did not open.");
                var fullScreenStart = (Button)PrivateField(fullScreenPanel, "_start");
                var fullScreenStop = (Button)PrivateField(fullScreenPanel, "_stop");
                Check(screen.Contains(fullScreenPanel.Bounds)
                    && Math.Abs(fullScreenPanel.Left + fullScreenPanel.Width / 2 - (screen.Left + screen.Width / 2)) <= 1
                    && fullScreenPanel.Top < screen.Top + screen.Height / 4,
                    "the default full-screen toolbar is inside the selection at the top center");
                var fullScreenButton = fullScreenStart.PointToScreen(new Point(fullScreenStart.Width / 2, fullScreenStart.Height / 2));
                Check(GetAncestor(WindowFromPoint(fullScreenButton), 2) == fullScreenPanel.Handle,
                    "the full-screen toolbar is on screen and available");
                var fullScreenFolder = Directory.CreateDirectory(Path.Combine(root, "full-screen")).FullName;
                Field(fullScreenPanel, "_saveFolder").SetValue(fullScreenPanel, fullScreenFolder);
                target.BackColor = renderColor;
                ActivateTarget(target);
                var edge = new Rectangle(screen.Left + screen.Width / 4, screen.Top, 32, 8);
                using var idleEdge = ReadDesktop(edge);
                var fullScreenOptions = PrivateField(fullScreenPanel, "_settings");
                var options = fullScreenOptions.GetType().GetProperty("Current")!.GetValue(fullScreenOptions)!;
                var videoFps = options.GetType().GetProperty("VideoFps")!;
                var originalFps = videoFps.GetValue(options);
                videoFps.SetValue(options, 60);
                fullScreenStart.PerformClick();
                videoFps.SetValue(options, originalFps);
                await WaitFor(() => ((Button)PrivateField(fullScreenPanel, "_pause")).Enabled,
                    "Full-screen recording did not start: " + fullScreenPanel.AccessibleDescription);
                await Task.Delay(250);
                if (Environment.GetEnvironmentVariable("SNIPLITE_CHECK_BORDERLESS") == "1")
                {
                    using var recordingEdge = ReadDesktop(edge);
                    var changed = 0;
                    for (var y = 0; y < edge.Height; y++)
                        for (var x = 0; x < edge.Width; x++)
                            if (idleEdge.GetPixel(x, y) != recordingEdge.GetPixel(x, y)) changed++;
                    Check(changed == 0, $"recording leaves the monitor edge unchanged ({changed} changed pixels)");
                }
                var fullScreenSession = PrivateField(fullScreenPanel, "_session");
                var latest = fullScreenSession.GetType().GetMethod("CopyLatest")!;
                fullScreenButton.Offset(-screen.X, -screen.Y);
                using (var frame = (Bitmap)latest.Invoke(fullScreenSession, null)!)
                    Check(frame.GetPixel(fullScreenButton.X, fullScreenButton.Y).ToArgb() == renderColor.ToArgb(),
                        "the captured frame shows the desktop beneath the visible full-screen toolbar");
                var fullScreenFormat = (Button)PrivateField(fullScreenPanel, "_format");
                fullScreenFormat.PerformClick();
                Check(!fullScreenFormat.Enabled && !fullScreenFormat.ContextMenuStrip!.Visible,
                    "full-screen recording locks the format popup");
                await VerifyRapidTrail(fullScreenPanel, fullScreenSession, target, screen);
                await VerifyStatusDrag(fullScreenSession, screen, renderColor);
                await VerifyStatusDrag(fullScreenSession, screen, renderColor, fullScreenPanel);
                var fullScreenCopy = (Button)PrivateField(fullScreenPanel, "_copy");
                var previousClipboard = Clipboard.GetDataObject();
                try
                {
                    var previousSaves = Directory.Exists(settings.SaveDirectory) ? Directory.GetFiles(settings.SaveDirectory, "*.png").Length : 0;
                    var recordingMessage = fullScreenPanel.AccessibleDescription;
                    fullScreenCopy.PerformClick();
                    await WaitFor(() => Directory.Exists(settings.SaveDirectory) &&
                        Directory.GetFiles(settings.SaveDirectory, "*.png").Length > previousSaves &&
                        fullScreenPanel.AccessibleDescription != recordingMessage, "The frame was not saved to the auto-save folder.");
                    var savedFile = Directory.GetFiles(settings.SaveDirectory, "*.png").OrderBy(File.GetLastWriteTimeUtc).Last();
                    using var saved = new Bitmap(savedFile);
                    using var copied = Clipboard.GetImage();
                    Check(saved.Size == screen.Size && copied?.Size == saved.Size, "Copy Frame writes both the clipboard and the auto-save PNG");
                }
                finally
                {
                    await RestoreClipboard(previousClipboard);
                }
                var feedback = (ToolTip)PrivateField(fullScreenPanel, "_feedback");
                fullScreenPanel.Activate();
                await WaitFor(() => fullScreenPanel.ContainsFocus, "The full-screen toolbar did not receive focus.");
                feedback.Show("Recording feedback", fullScreenPanel, 0, fullScreenPanel.Height, 2000);
                var tooltipWindow = ((NativeWindow)PrivateField(feedback, "_window")).Handle;
                await WaitFor(() => IsWindowVisible(tooltipWindow), "The recording feedback tooltip did not open.");
                Check(GetWindowDisplayAffinity(tooltipWindow, out var affinity) && affinity == 0x11,
                    "the toolbar feedback tooltip is also excluded from capture");
                feedback.Hide(fullScreenPanel);
                using (var fresh = new CaptureForm(SystemInformation.VirtualScreen, CaptureMode.Snip, settings, screen, showFrozenCursor: false))
                {
                    var revealed = (Bitmap)PrivateField(fresh, "_screenshot");
                    var virtualScreen = SystemInformation.VirtualScreen;
                    Check(revealed.GetPixel(screen.X - virtualScreen.X + fullScreenButton.X, screen.Y - virtualScreen.Y + fullScreenButton.Y).ToArgb() == renderColor.ToArgb(),
                        "the frozen screenshot source also excludes the toolbar");
                }
                fullScreenStop.PerformClick();
                await WaitFor(() => fullScreenStart.Enabled, "Full-screen recording did not finalize.");
                Console.WriteLine($"FULLSCREEN={Directory.GetFiles(fullScreenFolder, "*.mp4").Single()}");
                Console.WriteLine($"FULLSCREEN_TOOLBAR={fullScreenButton.X},{fullScreenButton.Y}");
                fullScreenPanel.Close();
                await fullScreenTask;
                await VerifyCaptureSettings();

                async Task VerifyCaptureSettings()
                {
                    var captureRegion = new Rectangle(screen.Left + 200, screen.Top, 900, 600);
                    if (!screen.Contains(captureRegion)) return;
                    var captureTask = manager.ExecuteFromSelectionAsync(command.Id, captureRegion, host);
                    await WaitFor(() => Application.OpenForms.Cast<Form>().Any(IsRecorder), "Settings verification recorder did not open.");
                    var recorder = Application.OpenForms.Cast<Form>().Single(IsRecorder);
                    await WaitFor(() => Field(recorder, "_status").GetValue(recorder) is Form,
                        "The configured recorder did not initialize its overlays.");
                    var recorderSettings = PrivateField(recorder, "_settings");
                    Field(recorderSettings, "_directory").SetValue(recorderSettings, Path.Combine(root, "configured-session"));
                    var update = recorderSettings.GetType().GetMethod("Save", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var optionsType = recorderSettings.GetType().GetProperty("Current")!.PropertyType;
                    var initial = recorderSettings.GetType().GetProperty("Current")!.GetValue(recorderSettings)!;
                    var configuredFolder = Directory.CreateDirectory(Path.Combine(root, "configured-output")).FullName;
                    var chooser = (Button)PrivateField(recorder, "_format");
                    var begin = (Button)PrivateField(recorder, "_start");
                    var finish = (Button)PrivateField(recorder, "_stop");
                    var pauseRecording = (Button)PrivateField(recorder, "_pause");
                    try
                    {
                        foreach (var (api, formatName, autoHide, status, captureToolbar) in
                            new[] { ("Dxgi", "MP4", true, true, false), ("Gdi", "GIF", false, false, true),
                                ("Wgc", "APNG", false, true, false), ("Wgc", "WebP", false, true, false) })
                        {
                            var options = Activator.CreateInstance(optionsType, configuredFolder,
                                optionsType.GetProperty("StatusPosition")!.GetValue(initial),
                                optionsType.GetProperty("ToolbarPosition")!.GetValue(initial))!;
                            Set("CaptureApi", Enum.Parse(optionsType.GetProperty("CaptureApi")!.PropertyType, api));
                            Set("VideoResolution", 480);
                            Set("VideoFps", 15);
                            Set("SeparateAudio", true);
                            Set("AnimationResolution", 480);
                            Set("AnimationFps", 10);
                            Set("LoopAnimation", false);
                            Set("AutoHideToolbar", autoHide);
                            Set("ShowStatus", status);
                            Set("CaptureToolbar", captureToolbar);
                            update.Invoke(recorderSettings, [options]);
                            Check(Application.OpenForms.Cast<Form>().Any(form => form.Visible && form.GetType().Name == "RecorderStatusWindow") == status,
                                "recording status visibility setting is applied");
                            SelectFormat(chooser, formatName);
                            ActivateTarget(target);
                            begin.PerformClick();
                            await WaitFor(() => Field(recorder, "_ready").GetValue(recorder) is true ||
                                Field(recorder, "_session").GetValue(recorder) is null, $"The {api} recorder did not start.");
                            if (autoHide && Field(recorder, "_ready").GetValue(recorder) is true)
                                ((Button)PrivateField(recorder, "_toolbarHandle")).PerformClick();
                            Check(pauseRecording.Enabled, $"{api} starts recording: {recorder.AccessibleDescription}");
                            var session = PrivateField(recorder, "_session");
                            using var tone = MakeTone();
                            using var player = new System.Media.SoundPlayer(tone);
                            if (formatName == "MP4")
                            {
                                Feature(recorder, "SystemAudio").PerformClick();
                                player.Play();
                                var peak = session.GetType().GetProperty("SystemPeak")!;
                                await WaitFor(() => Convert.ToUInt32(peak.GetValue(session)) > 0,
                                    "The configured MP4 did not pick up the playback test tone.");
                            }
                            Check((bool)PrivateField(recorder, "_autoCollapsed") == autoHide, "auto-hide follows the saved setting");
                            Check(GetWindowDisplayAffinity(recorder.Handle, out var toolbarAffinity) &&
                                toolbarAffinity == (captureToolbar ? 0u : 0x11u), "capture-toolbar setting changes window exclusion");
                            await Task.Delay(300);
                            target.BackColor = Color.FromArgb(207, 221, 234);
                            await Task.Delay(350);
                            target.BackColor = renderColor;
                            await Task.Delay(650);
                            if (status)
                            {
                                var statusWindow = VisibleOverlay("RecorderStatusWindow");
                                Check(!Equals(PrivateField(statusWindow, "_elapsed"), "00:00:00"),
                                    "the status bar shows elapsed recording time");
                            }
                            var copyFrame = session.GetType().GetMethod("CopyLatest")!;
                            var desktopPixel = new Point(captureRegion.Width / 2, captureRegion.Height / 2);
                            await WaitFor(() =>
                            {
                                using var sampled = (Bitmap?)copyFrame.Invoke(session, null);
                                return sampled?.GetPixel(desktopPixel.X, desktopPixel.Y).ToArgb() == renderColor.ToArgb();
                            }, $"{api} did not capture the test background.");
                            using (var frame = (Bitmap)copyFrame.Invoke(session, null)!)
                            {
                                Check(true, $"{api} captures desktop pixels");
                                var probe = new Point(recorder.Left + recorder.Width / 2, recorder.Top + 4);
                                probe.Offset(-captureRegion.Left, -captureRegion.Top);
                                if (new Rectangle(Point.Empty, frame.Size).Contains(probe))
                                    Check((frame.GetPixel(probe.X, probe.Y).ToArgb() != renderColor.ToArgb()) == captureToolbar,
                                        $"{api} applies the capture-toolbar setting to frame pixels");
                            }
                            finish.PerformClick();
                            await WaitFor(() => begin.Enabled, "The configured recording did not finish.");
                            var file = Directory.GetFiles(configuredFolder, "*." + formatName.ToLowerInvariant()).Single();
                            if (formatName == "GIF")
                            {
                                using var image = Image.FromFile(file);
                                Check(image.Size == new Size(720, 480), "the animation resolution scales the exported image");
                                var bytes = File.ReadAllBytes(file);
                                Check(bytes.AsSpan().IndexOf("NETSCAPE2.0"u8) < 0, "the GIF loop extension is omitted when looping is disabled");
                                Check(image.GetFrameCount(FrameDimension.Time) >= 5, "the configured GIF contains multiple frames");
                            }
                            else if (formatName == "MP4")
                            {
                                Check(Directory.GetFiles(configuredFolder, "*.wav").Length == 2, "both independent WAV tracks are exported with the MP4");
                                foreach (var track in Directory.GetFiles(configuredFolder, "*.wav"))
                                {
                                    var data = File.ReadAllBytes(track);
                                    Check(data.AsSpan(0, 4).SequenceEqual("RIFF"u8) &&
                                        BitConverter.ToUInt32(data, 40) == data.Length - 44, "separate WAV has a finalized PCM header");
                                    Check(data.AsSpan(44).IndexOfAnyExcept((byte)0) >= 0 ==
                                        track.EndsWith(".system.wav", StringComparison.OrdinalIgnoreCase),
                                        "the playback tone appears only in the independent system track");
                                }
                            }
                            else
                            {
                                var data = File.ReadAllBytes(file);
                                var marker = formatName == "APNG" ? "acTL"u8.ToArray() : "ANIM"u8.ToArray();
                                var position = data.AsSpan().IndexOf(marker);
                                Check(position >= 0, $"the {formatName} includes animation control");
                                var plays = formatName == "APNG"
                                    ? System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position + 8))
                                    : BitConverter.ToUInt16(data, position + 12);
                                Check(plays == 1, $"the {formatName} exports a single play when looping is disabled");
                            }
                            Console.WriteLine($"CONFIGURED_{api}={file}");
                            void Set(string property, object value) => optionsType.GetProperty(property)!.SetValue(options, value);
                        }
                    }
                    finally
                    {
                        recorder.Close();
                        await captureTask;
                    }
                }

                async Task DrawWithFreshScreenshot(object session)
                {
                    Check(!manager.BlocksScreenshots, "a regular screenshot can start during recording");
                    var outline = VisibleOverlay("SelectionOutline");
                    var status = VisibleOverlay("RecorderStatusWindow");
                    var features = new[] { Feature(panel, "Trail"), Feature(panel, "Clicks"), Feature(panel, "Keyboard") };
                    foreach (var button in features) button.PerformClick();
                    // Put the cursor away from the drawing area so its pixels can be compared independently.
                    var originalCursor = Cursor.Position;
                    var cursorPoint = new Point(selection.Left + 485, selection.Top + 160);
                    Cursor.Position = cursorPoint;
                    var oldCaptureCursor = settings.CaptureCursor;
                    var oldDisableCursor = settings.DisableCursorCapture;
                    settings.CaptureCursor = true;
                    settings.DisableCursorCapture = false;
                    CaptureForm? opened = null;
                    using var hotkeys = new HotkeyHost();
                    const Keys testShortcut = Keys.Control | Keys.Shift | Keys.F1;
                    settings.ShowCrosshair = true;
                    try
                    {
                        if (!hotkeys.TryRegister(testShortcut, "Recorder regression screenshot", () =>
                            {
                                opened = new CaptureForm(SystemInformation.VirtualScreen, CaptureMode.Snip, settings,
                                    showFrozenCursor: false);
                                host.ShowScreenshot(opened);
                            }, out var error)) throw error;
                        keybd_event((byte)Keys.LControlKey, 0, 0, 0);
                        keybd_event((byte)Keys.LShiftKey, 0, 0, 0);
                        try
                        {
                            keybd_event((byte)Keys.F1, 0, 0, 0);
                            keybd_event((byte)Keys.F1, 0, 2, 0);
                        }
                        finally
                        {
                            keybd_event((byte)Keys.LShiftKey, 0, 2, 0);
                            keybd_event((byte)Keys.LControlKey, 0, 2, 0);
                        }
                        await WaitFor(() => opened is { Visible: true }, "The registered screenshot hotkey did not open an editor.");
                    }
                    finally
                    {
                        settings.CaptureCursor = oldCaptureCursor;
                        settings.DisableCursorCapture = oldDisableCursor;
                        hotkeys.UnregisterAll();
                    }
                    using var drawing = opened!;
                    var virtualDesktop = SystemInformation.VirtualScreen;
                    var localCrop = new Rectangle(selection.X - virtualDesktop.X, selection.Y - virtualDesktop.Y, selection.Width, selection.Height);
                    var screenshot = (Bitmap)PrivateField(drawing, "_screenshot");
                    var render = typeof(CaptureForm).GetMethod("RenderComposite", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    using (var exported = (Bitmap)render.Invoke(drawing, [localCrop])!)
                    {
                        var changed = 0;
                        for (var y = 155; y < 185; y++)
                            for (var x = 481; x < 518; x++)
                                if (exported.GetPixel(x, y).ToArgb() != screenshot.GetPixel(localCrop.X + x, localCrop.Y + y).ToArgb()) changed++;
                        Check(changed > 0, "the captured cursor appears only in screenshot output, not the frozen editor");
                    }
                    var drawingBar = (AnnotationBar)PrivateField(drawing, "_bar");
                    await MoveScreenshotCrosshair(drawing, new Point(selection.Left + 270, selection.Top + 162));
                    await MoveScreenshotCrosshair(drawing, new Point(selection.Left + 300, selection.Top + 167));
                    await WaitFor(() => panel.OwnedForms.Any(window => window.Visible && window.GetType().Name == "EffectWindow"),
                        "Moving the mouse in the screenshot did not produce a live trail.");
                    Check(!drawing.IsDisposed, "the screenshot stays open when the mouse trail appears");
                    var effects = panel.OwnedForms.Single(window => window.GetType().Name == "EffectWindow");
                    var windows = new[] { panel, status, outline, effects };
                    var covered = windows.Where(window => !IsAbove(window, drawing)).Select(window => window.GetType().Name).ToArray();
                    Check(covered.Length == 0, "the new screenshot leaves all recording windows above it" +
                        (covered.Length == 0 ? "" : "; covered windows: " + string.Join(", ", covered)));
                    Check(IsAbove(panel, status) && IsAbove(panel, outline), "the toolbar remains above the recording status and outline");
                    Check(drawing.ContainsFocus, "the new screenshot retains keyboard focus with recording windows visible");
                    Check(GetAncestor(WindowFromPoint(Cursor.Position), 2) == drawing.Handle,
                        "the live mouse trail lets pointer input reach the screenshot editor");
                    var selectionOrigin = drawing.PointToClient(selection.Location);
                    DrawStroke(drawing, selectionOrigin, selectionOrigin + selection.Size);
                    await WaitFor(() => !drawingBar.ItemBounds("pen").IsEmpty, "The new screenshot toolbar did not appear after selecting a region.");
                    var copyFrame = session.GetType().GetMethod("CopyLatest")!;
                    keybd_event((byte)Keys.J, 0, 0, 0);
                    keybd_event((byte)Keys.J, 0, 2, 0);
                    mouse_event(0x0002, 0, 0, 0, 0);
                    mouse_event(0x0004, 0, 0, 0, 0);
                    await WaitFor(() =>
                    {
                        using var frame = (Bitmap?)copyFrame.Invoke(session, null);
                        return frame is not null && effects.Visible && IsAbove(effects, drawing) && drawing.ContainsFocus &&
                            HasColor(frame, new Rectangle(frame.Width - 160, frame.Height - 90, 155, 84), c => c.R < 70 && c.G < 75 && c.B < 90) &&
                            HasColor(frame, new Rectangle(274, 142, 52, 52), c => c.R > 220 && c.G > 160 && c.B < 100);
                    }, "The editor did not keep the input overlay visible while recording the key popup and click ring.");
                    Check(true, "keyboard and click effects stay above the editor and appear in the recording");
                    await OpenSettingsOverScreenshot(drawing);
                    ClickTool(drawing, drawingBar.ItemBounds("pen"));
                    var origin = drawing.PointToClient(selection.Location);
                    Point Local(int x, int y) => origin + new Size(x, y);
                    var drawingProbe = new Rectangle(selection.Left + 28, selection.Top + 58, 410, 145);
                    await VerifyRapidScreenshotInput(drawing, session, drawingProbe, selection.Location, "effects on", effects);
                    foreach (var button in features) button.PerformClick();
                    await VerifyRapidScreenshotInput(drawing, session, drawingProbe, selection.Location, "effects off");
                    for (var i = 0; i < 12; i++)
                    {
                        DrawStroke(drawing, Local(40, 80 + i * 5), Local(220, 80 + i * 5));
                        drawing.Update();
                    }
                    await WaitFor(() =>
                    {
                        using var frame = (Bitmap?)copyFrame.Invoke(session, null);
                        if (frame is null) return false;
                        for (var y = 76; y < 148; y += 2)
                            for (var x = 34; x < 425; x += 2)
                                if (frame.GetPixel(x, y) is { R: > 175, G: < 125, B: < 125 }) return true;
                        return false;
                    }, "The new screenshot's annotations did not appear in the captured frame.");
                    using (var frame = (Bitmap)copyFrame.Invoke(session, null)!)
                    {
                        VerifyBrightArea(frame, new Rectangle(34, 76, 391, 72), "screenshot recording after fast strokes");
                    }
                    drawing.Close();
                    settings.ShowCrosshair = false;
                    Cursor.Position = originalCursor;
                    await WaitFor(() => drawing.IsDisposed && !panel.IsDisposed && manager.IsRunning,
                        "Closing the new screenshot also closed or interrupted the recorder.");
                    Check(true, "the recording captures a fresh screenshot's drawing and continues after that editor closes");
                    await Task.Delay(180);
                }

                static async Task MoveScreenshotCrosshair(CaptureForm screenshot, Point point)
                {
                    Cursor.Position = point;
                    await WaitFor(() => screenshot.OwnedForms.Any(window => window.Visible && window.Left == point.X) &&
                        screenshot.OwnedForms.Any(window => window.Visible && window.Top == point.Y),
                        "The screenshot crosshair did not follow the mouse before selection.");
                }

                async Task OpenSettingsOverScreenshot(CaptureForm drawing)
                {
                    var gear = (Button)PrivateField(panel, "_openSettings");
                    Check(gear.Enabled && !format.Enabled, "recording settings remain accessible while the active format is locked");
                    Cursor.Position = gear.PointToScreen(new Point(gear.Width / 2, gear.Height / 2));
                    Check(GetAncestor(WindowFromPoint(Cursor.Position), 2) == panel.Handle, "the recording settings button can receive a click above the screenshot");
                    mouse_event(0x0002, 0, 0, 0, 0);
                    mouse_event(0x0004, 0, 0, 0, 0);
                    await WaitFor(() => Application.OpenForms.Cast<Form>().Any(form => form.Visible && form.GetType().Name == "RecorderSettingsForm"),
                        "The recording settings did not open from the toolbar above the screenshot.");
                    var dialog = VisibleOverlay("RecorderSettingsForm");
                    try
                    {
                        Check(!drawing.IsDisposed, "opening settings keeps the screenshot open");
                        Check(IsAbove(dialog, drawing), "the recording settings dialog stays above the screenshot");
                        Check(dialog.AcceptButton is null && GetWindowDisplayAffinity(dialog.Handle, out var affinity) && affinity == 0x11,
                            "recording settings are read-only and excluded from capture during recording");
                    }
                    finally { dialog.DialogResult = DialogResult.Cancel; }
                    await WaitFor(() => dialog.IsDisposed, "The recording settings dialog did not close.");
                    drawing.Activate();
                    await WaitFor(() => drawing.ContainsFocus && IsAbove(panel, drawing), "The editor did not regain focus below the recording toolbar.");
                    Check(IsAbove(VisibleOverlay("SelectionOutline"), drawing) && IsAbove(VisibleOverlay("RecorderStatusWindow"), drawing),
                        "reactivating the editor keeps the recording outline and status above it");
                }

                static bool IsAbove(Form window, Form reference)
                {
                    const uint previousWindow = 3;
                    for (var handle = GetWindow(reference.Handle, previousWindow); handle != 0; handle = GetWindow(handle, previousWindow))
                        if (handle == window.Handle) return true;
                    return false;
                }

                async Task BenchmarkRecording()
                {
                    using var process = Process.GetCurrentProcess();
                    var cpu = process.TotalProcessorTime;
                    var memory = GC.GetTotalAllocatedBytes(precise: true);
                    var stopwatch = Stopwatch.StartNew();
                    start.PerformClick();
                    await WaitFor(() => pause.Enabled, "1080p recording did not start.");
                    var session = PrivateField(panel, "_session");
                    var duration = session.GetType().GetProperty("Duration")!;
                    stopwatch.Restart();
                    await DrawWithFreshScreenshot(session);
                    var remaining = TimeSpan.FromSeconds(5) - stopwatch.Elapsed;
                    if (remaining > TimeSpan.Zero) await Task.Delay(remaining);
                    var recorded = (TimeSpan)duration.GetValue(session)!;
                    var elapsed = stopwatch.Elapsed;
                    stop.PerformClick();
                    await WaitFor(() => start.Enabled, "1080p recording did not finalize.");
                    var frames = recorded.TotalSeconds * 20;
                    var fps = frames / elapsed.TotalSeconds;
                    var cpuSeconds = (process.TotalProcessorTime - cpu).TotalSeconds;
                    var allocatedMiB = (GC.GetTotalAllocatedBytes(precise: true) - memory) / 1048576.0;
                    Check(fps >= 18, $"1080p sustained {fps:F1} fps over {elapsed.TotalSeconds:F1}s");
                    Console.WriteLine($"CPU seconds (process): {cpuSeconds:F2}. Managed allocations: {allocatedMiB:F1} MiB.");
                }
                async Task RecordFormat(string name, bool addStroke)
                {
                    target.BackColor = renderColor;
                    SelectFormat(format, name);
                    if (addStroke)
                    {
                        Feature(panel, "SystemAudio").PerformClick();
                        Feature(panel, "Microphone").PerformClick();
                    }
                    start.PerformClick();
                    Check(!format.Enabled, "starting immediately locks the output format");
                    await WaitFor(() => pause.Enabled || start.Enabled, "Capture did not start.");
                    Check(pause.Enabled && copy.Enabled, "recording started before exporting " + name + " " + panel.AccessibleDescription);
                    await Task.Delay(370);
                    var session = PrivateField(panel, "_session");
                    var duration = session.GetType().GetProperty("Duration")!;
                    if (addStroke) await DrawWithFreshScreenshot(session);
                    pause.PerformClick();
                    await Task.Delay(90);
                    var pausedAt = duration.GetValue(session);
                    Check(!format.Enabled, "pausing keeps the output format locked");
                    await Task.Delay(160);
                    Check(Equals(pausedAt, duration.GetValue(session)), name + " can pause");
                    using (var frame = (Bitmap)session.GetType().GetMethod("CopyLatest")!.Invoke(session, null)!)
                    {
                        var backdrop = frame.GetPixel(12, frame.Height - 15);
                        Check(backdrop.ToArgb() == renderColor.ToArgb(), name + " frame excludes lower-left UI");
                    }

                    if (addStroke)
                    {
                        var activeAudio = session.GetType().GetProperty("ActiveAudio")!;
                        var sources = activeAudio.GetValue(session)!.ToString()!;
                        Console.WriteLine("AUDIO BEFORE RESUME=" + sources);
                        Check(sources == "None", "pause releases audio sources");
                    }

                    pause.PerformClick();
                    target.BackColor = Color.FromArgb(213, 233, 210);
                    if (addStroke)
                    {
                        var activeAudio = session.GetType().GetProperty("ActiveAudio")!;
                        await WaitFor(() => (Convert.ToInt32(activeAudio.GetValue(session)) & 2) != 0,
                            "Default system audio endpoint did not start.");
                        var active = Convert.ToInt32(activeAudio.GetValue(session));
                        Console.WriteLine("AUDIO ACTIVE=" + activeAudio.GetValue(session));
                        if ((active & 1) != 0)
                        {
                            Feature(panel, "Microphone").PerformClick();
                            await WaitFor(() => Convert.ToInt32(activeAudio.GetValue(session)) == 2,
                                "Disabling microphone should leave system audio active.");
                            Check(true, "microphone can stop independently of system audio");
                        }
                        Feature(panel, "Keyboard").PerformClick();
                        Feature(panel, "Trail").PerformClick();
                        Feature(panel, "Clicks").PerformClick();
                        using var tone = MakeTone();
                        using var player = new System.Media.SoundPlayer(tone);
                        player.Play();
                        var systemPeak = session.GetType().GetProperty("SystemPeak")!;
                        await WaitFor(() => Convert.ToUInt32(systemPeak.GetValue(session)) > 0,
                            "The playback capture meter did not react to the test tone.");
                        await WaitFor(() => HasGreenMeter(Feature(panel, "SystemAudio")),
                            "The playback button did not show the live input level.");
                        Check(true, "the system audio button reacts to actual recorded PCM");
                        await VerifyLiveEffects(panel, target, selection, copy, root);
                        await Task.Delay(500);
                        player.Stop();
                        Feature(panel, "SystemAudio").PerformClick();
                        await WaitFor(() => Convert.ToInt32(activeAudio.GetValue(session)) == 0,
                            "Disabling system audio should stop loopback.");
                        await Task.Delay(250);
                        Check(true, "system audio can stop during recording");
                        if ((active & 1) == 0) Feature(panel, "Microphone").PerformClick();
                    }
                    else
                    {
                        Feature(panel, "Keyboard").PerformClick();
                        Feature(panel, "Trail").PerformClick();
                        Feature(panel, "Clicks").PerformClick();
                        await VerifyLiveEffects(panel, target, selection, copy, root);
                    }
                    Check(!format.Enabled, "format picker remains locked during recording");
                    SelectFormat(format, name == "MP4" ? "GIF" : "MP4");
                    Check(format.Text == name, "the disabled chooser preserves the selected format");
                    var status = VisibleOverlay("RecorderStatusWindow");
                    var label = status.GetType().GetProperty("Format", BindingFlags.Instance | BindingFlags.NonPublic);
                    Check(Equals(label!.GetValue(status), name), "lower-left status follows " + name);
                    stop.PerformClick();
                    await WaitFor(() => start.Enabled, "Recording did not finish.");
                    Check(format.Enabled, "the output format unlocks after saving");
                    var file = Directory.GetFiles(root, "*." + name.ToLowerInvariant()).Single();
                    using var probe = File.OpenRead(file);
                    Check(probe.Length > 500, name + " file saved and finalized");
                    if (name == "GIF")
                    {
                        using var gif = Image.FromStream(probe);
                        Check(gif.GetFrameCount(FrameDimension.Time) >= 5, "GIF decodes as a real multi-frame animation");
                    }
                    Console.WriteLine(name + "=" + file);
                }
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                exit = 1;
            }
            finally { target.Close(); }
        };
        Application.Run(target);
        return exit;
    }


    private static void VerifyRecorderSettingsDialog(Form panel, Action? openSettings = null)
    {
        var settingsButton = (Button)PrivateField(panel, "_openSettings");
        using var watcher = new System.Windows.Forms.Timer { Interval = 50 };
        Form? opened = null;
        Exception? failure = null;
        using var theme = AppTheme.Override(dark: openSettings is not null);
        watcher.Tick += async (_, _) =>
        {
            opened = Application.OpenForms.Cast<Form>().FirstOrDefault(form => form.GetType().Name == "RecorderSettingsForm");
            if (opened is null) return;
            watcher.Stop();
            try
            {
                var navigation = Descendants(opened).OfType<Button>().Where(button => button.GetType().Name == "NavigationButton").ToArray();
                Check(navigation.Length == 3, "the dialog uses the shared sidebar with three pages");
                foreach (var item in navigation)
                {
                    item.PerformClick();
                    Check(Equals(item.GetType().GetProperty("Selected", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(item), true),
                        $"the settings sidebar selects {item.Text}");
                    Check(Descendants(opened).OfType<FluentSettingRow>().Any(row => row.Visible), "the selected page displays shared setting cards");
                }
                Check(Descendants(opened).OfType<FluentToggle>().Count() == 5, "all recording options use the host toggle");
                Check(Descendants(opened).OfType<FluentInputFrame>().Count() == 1, "the save path uses the host input frame");
                Check(opened.AcceptButton is FluentButton && opened.CancelButton is FluentButton, "dialog actions use host buttons");
                var preview = Environment.GetEnvironmentVariable("SNIPLITE_SETTINGS_PREVIEW");
                if (!string.IsNullOrEmpty(preview))
                {
                    navigation[openSettings is null ? 0 : 2].PerformClick();
                    opened.Update();
                    await Task.Delay(350);
                    using var screenshot = new Bitmap(opened.Width, opened.Height);
                    opened.DrawToBitmap(screenshot, new Rectangle(Point.Empty, screenshot.Size));
                    screenshot.Save(preview + (openSettings is null ? "-light.png" : "-dark.png"), ImageFormat.Png);
                }
            }
            catch (Exception error) { failure = error; }
            finally { opened.DialogResult = DialogResult.Cancel; }
        };
        watcher.Start();
        (openSettings ?? settingsButton.PerformClick)();
        if (failure is not null) throw new InvalidOperationException("Recording settings UI validation failed.", failure);
        Check(opened is not null, openSettings is null
            ? "the toolbar settings button opens the shared recording settings"
            : "the plugin preferences entry opens the shared recording settings");

        static IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;
                foreach (var descendant in Descendants(child)) yield return descendant;
            }
        }
    }

    private static void ActivateTarget(Form target)
    {
        target.Activate();
        const uint flags = Win32.SWP_NOSIZE | Win32.SWP_NOMOVE | Win32.SWP_NOACTIVATE;
        Win32.SetWindowPos(target.Handle, Win32.HWND_TOPMOST, 0, 0, 0, 0, flags);
        foreach (var window in Application.OpenForms.Cast<Form>().Where(form => form.Visible && form != target)
            .OrderBy(form => form.GetType().Name == "RecorderForm"))
            Win32.SetWindowPos(window.Handle, Win32.HWND_TOPMOST, 0, 0, 0, 0, flags);
    }

    private static bool HasGreenMeter(Button button)
    {
        using var bitmap = new Bitmap(button.Width, button.Height);
        button.DrawToBitmap(bitmap, button.ClientRectangle);
        for (var y = bitmap.Height / 3; y < bitmap.Height * 2 / 3; y++)
                for (var x = bitmap.Width / 4; x < bitmap.Width * 2 / 3; x++)
                if (bitmap.GetPixel(x, y) is { R: < 125, G: > 130, B: < 100 }) return true;
        return false;
    }

    private static async Task VerifyAudioMenus(Form panel)
    {
        var controls = new[] { (Key: "Microphone", Setting: "MicrophoneDevice"), (Key: "SystemAudio", Setting: "SystemAudioDevice") };
        var settings = PrivateField(panel, "_settings");
        var current = settings.GetType().GetProperty("Current")!;
        var optionsType = current.PropertyType;
        var initial = current.GetValue(settings)!;
        var save = settings.GetType().GetMethod("Save", BindingFlags.Instance | BindingFlags.NonPublic)!;
        save.Invoke(settings, [Activator.CreateInstance(optionsType, optionsType.GetProperty("Folder")!.GetValue(initial),
            optionsType.GetProperty("StatusPosition")!.GetValue(initial), optionsType.GetProperty("ToolbarPosition")!.GetValue(initial))]);
        var cursor = Cursor.Position;
        try
        {
            foreach (var (key, setting) in controls)
            {
                var button = Feature(panel, key);
                var menu = await OpenMenu(button);
                var defaultDevice = menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Tag is null);
                Check(defaultDevice.Checked && GetWindowDisplayAffinity(menu.Handle, out var affinity) && affinity == 0x11,
                    $"the {key} right-click menu checks the default and is excluded from capture");
                var endpoint = menu.Items.OfType<ToolStripMenuItem>().FirstOrDefault(item => item.Tag is string && item.Enabled);
                if (endpoint is null) { menu.Close(); continue; }
                var id = (string)endpoint.Tag!;
                endpoint.PerformClick();
                if (menu.Visible) menu.Close();
                Check(Equals(current.PropertyType.GetProperty(setting)!.GetValue(current.GetValue(settings)), id),
                    $"the {key} choice is saved by endpoint ID");
                await WaitFor(() => AudioState(button) is "Preview" or "Unavailable",
                    $"Selecting the {key} endpoint did not check it before recording.");
                Check(PrivateField(panel, "_session") is null, $"the {key} selection is checked before recording starts");
                menu = await OpenMenu(button);
                Check(menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Checked).Tag as string == id,
                    $"the {key} menu remembers the selected endpoint");
                menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Tag is null).PerformClick();
                if (menu.Visible) menu.Close();
            }

            var system = Feature(panel, "SystemAudio");
            using (var tone = MakeTone())
            using (var player = new System.Media.SoundPlayer(tone))
            {
                player.Play();
                await WaitFor(() => AudioState(system) == "Preview" && HasGreenMeter(system),
                    "The default playback device did not show its level before recording.");
                Check(PrivateField(panel, "_session") is null, "actual playback updates the system audio meter before recording");
            }
            foreach (var (key, field) in new[] { ("Microphone", "_micTestUntil"), ("SystemAudio", "_systemTestUntil") })
            {
                Field(panel, field).SetValue(panel, 0L);
                await WaitFor(() => AudioState(Feature(panel, key)) == "Off",
                    $"The {key} test did not return to its previous toggle state.");
            }

            var folder = (Button)PrivateField(panel, "_folder");
            Cursor.Position = new Point(panel.Right + 16, panel.Bottom + 16);
            panel.Activate();
            await WaitFor(() => panel.ContainsFocus && Field(panel, "_hoveredControl").GetValue(panel) is null,
                "The toolbar did not receive keyboard focus after the menus closed.");
            var another = (Button)PrivateField(panel, "_start");
            another.Focus();
            folder.Focus();
            var expected = ((ToolTip)PrivateField(panel, "_tips")).GetToolTip(folder);
            var focusTip = (ToolTip)PrivateField(panel, "_focusTip");
            await WaitFor(() => !string.IsNullOrEmpty(expected) && focusTip.GetToolTip(panel) == expected,
                "The focused toolbar button did not show its label.");
            var focusWindow = ((NativeWindow)PrivateField(focusTip, "_window")).Handle;
            Check(IsWindowVisible(focusWindow) && GetWindowDisplayAffinity(focusWindow, out var focusAffinity) && focusAffinity == 0x11,
                "the toolbar focus tooltip is visible and excluded from capture");
            focusTip.Hide(panel);
        }
        finally { Cursor.Position = cursor; }

        static string AudioState(Button button)
        {
            var indicator = PrivateField(button, "_audio");
            return indicator.GetType().GetProperty("State")!.GetValue(indicator)!.ToString()!;
        }

        async Task<ContextMenuStrip> OpenMenu(Button button)
        {
            panel.Activate();
            await WaitFor(() => panel.ContainsFocus, "The recorder did not receive focus for the audio endpoint menu.");
            Cursor.Position = button.PointToScreen(new Point(button.Width / 2, button.Height / 2));
            await Task.Delay(40);
            var hit = WindowFromPoint(Cursor.Position);
            GetWindowThreadProcessId(hit, out var pid);
            Check(GetAncestor(hit, 2) == panel.Handle, $"The audio button can receive a right-click at {Cursor.Position}. Hit: {Control.FromHandle(hit)?.GetType().Name} (PID {pid})");
            mouse_event(0x0008, 0, 0, 0, 0);
            mouse_event(0x0010, 0, 0, 0, 0);
            await WaitFor(() => Field(panel, "_audioMenu").GetValue(panel) is ContextMenuStrip { Visible: true },
                "Right-click did not open the audio endpoint menu.");
            return (ContextMenuStrip)PrivateField(panel, "_audioMenu");
        }
    }

    private static void SelectFormat(Button format, string name) =>
        format.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == name).PerformClick();

    private static async Task VerifyStatusDrag(object session, Rectangle screen, Color backdrop, Form? toolbar = null)
    {
        var status = toolbar ?? VisibleOverlay("RecorderStatusWindow");
        var label = toolbar is null ? "status bar" : "recording toolbar";
        var handle = (Button)PrivateField(status, toolbar is null ? "_handle" : "_toolbarHandle");
        var collapse = (Button)PrivateField(status, toolbar is null ? "_collapse" : "_collapseToolbar");
        var original = status.Bounds;
        var cursor = Cursor.Position;
        try
        {
            await UseMouse(collapse, Size.Empty);
            Check(status.Width < original.Width / 2 && handle.Visible && !collapse.Visible,
                $"collapsing the live {label} leaves only the left handle");

            var offset = new Size(116, toolbar is null ? -70 : 70);
            await UseMouse(handle, offset);
            Check(status.Location == original.Location + offset && status.Width < original.Width / 2,
                $"dragging the collapsed {label} moves it without expanding");

            var sample = Center(handle);
            Cursor.Position = new Point(screen.Left + screen.Width / 2, screen.Top + screen.Height / 2);
            await Task.Delay(200);
            using (var frame = (Bitmap)session.GetType().GetMethod("CopyLatest")!.Invoke(session, null)!)
                Check(frame.GetPixel(sample.X - screen.Left, sample.Y - screen.Top).ToArgb() == backdrop.ToArgb(),
                    $"recording excludes the {label} handle after dragging");

            await UseMouse(handle, Size.Empty);
            Check(status.Width == original.Width && collapse.Visible && status.Location == original.Location + offset,
                $"clicking the handle expands the {label} at the new location");

            var secondOffset = new Size(28, -35);
            await UseMouse(handle, secondOffset);
            Check(status.Location == original.Location + offset + secondOffset && status.Width == original.Width,
                $"dragging the expanded {label} preserves its layout");
        }
        finally { Cursor.Position = cursor; }

        static Point Center(Control control) =>
            control.PointToScreen(new Point(control.Width / 2, control.Height / 2));

        static async Task UseMouse(Control control, Size offset)
        {
            var start = Center(control);
            Cursor.Position = start;
            await Task.Delay(50);
            mouse_event(0x0002, 0, 0, 0, 0);
            try
            {
                if (!offset.IsEmpty)
                {
                    await Task.Delay(50);
                    Cursor.Position = start + offset;
                    await Task.Delay(100);
                }
            }
            finally { mouse_event(0x0004, 0, 0, 0, 0); }
            await Task.Delay(100);
        }
    }

    private static void VerifyStatusIcons(Form panel, Button format)
    {
        var status = VisibleOverlay("RecorderStatusWindow");
        var bounds = status.Bounds;
        Check(status.Width * 96.0 / status.DeviceDpi <= 215, "recording status uses a compact row");

        Bitmap Render()
        {
            var image = new Bitmap(status.Width, status.Height);
            status.DrawToBitmap(image, status.ClientRectangle);
            return image;
        }

        var distinctFormats = new HashSet<string>();
        foreach (var name in new[] { "MP4", "GIF", "APNG", "WebP" })
        {
            SelectFormat(format, name);
            using var image = Render();
            using var bytes = new MemoryStream();
            image.Save(bytes, ImageFormat.Png);
            distinctFormats.Add(Convert.ToBase64String(bytes.ToArray()));
        }
        Check(distinctFormats.Count == 4, "each output format has a distinct rendered status");
        SelectFormat(format, "MP4");

        int ChangedRegions(Bitmap before, Bitmap after)
        {
            var changed = 0;
            for (var y = 0; y < before.Height; y++)
                for (var x = 0; x < before.Width; x++)
                    if (before.GetPixel(x, y) != after.GetPixel(x, y))
                        changed |= x * 96 < status.DeviceDpi * 69 ? 1 : x * 96 < status.DeviceDpi * 95 ? 2 : 4;
            return changed;
        }

        using var off = Render();
        var microphone = Feature(panel, "Microphone");
        var system = Feature(panel, "SystemAudio");
        microphone.PerformClick();
        using var microphoneOn = Render();
        Check(ChangedRegions(off, microphoneOn) == 2, "enabling microphone changes only its status icon");
        system.PerformClick();
        using var bothOn = Render();
        Check(ChangedRegions(microphoneOn, bothOn) == 4, "enabling system sound changes only its status icon");

        SelectFormat(format, "GIF");
        using var animation = Render();
        Check(ChangedRegions(off, animation) == 1, "silent animation updates the format label and shows both audio icons as off");
        Check(status.Bounds == bounds, "status row keeps its bounds when audio or file format changes");
        SelectFormat(format, "MP4");
        system.PerformClick();
        microphone.PerformClick();
        using var restored = Render();
        Check(ChangedRegions(off, restored) == 0, "turning the sources off restores both original icons");
    }

    private static Bitmap ReadDesktop(Rectangle bounds)
    {
        var image = new Bitmap(bounds.Width, bounds.Height);
        using var graphics = Graphics.FromImage(image);
        graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        return image;
    }

    private static void VerifyStatusPreferences(Form panel, string root, PluginHostServices host)
    {
        var type = PrivateField(panel, "_settings").GetType();
        var isolated = Path.Combine(root, "recording-settings");
        Directory.CreateDirectory(isolated);
        var json = Path.Combine(isolated, "recorder-settings.json");
        File.WriteAllText(json, System.Text.Json.JsonSerializer.Serialize(root));
        object OpenSettings() => Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null,
            [isolated, host], null)!;
        var isolatedSettings = OpenSettings();
        var optionsType = type.GetProperty("Current")!.PropertyType;
        var positionType = optionsType.GetProperty("StatusPosition")!.PropertyType;
        object Read(object instance) => type.GetProperty("Current")!.GetValue(instance)!;
        Check((string)optionsType.GetProperty("Folder")!.GetValue(Read(isolatedSettings))! == root,
            "existing string-only recording settings keep the configured directory");
        var save = type.GetMethod("Save", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var status = VisibleOverlay("RecorderStatusWindow");
        var setPosition = status.GetType().GetMethod("SetPosition", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var names = new[] { "BottomLeft", "Bottom", "BottomRight", "Left", "Right", "TopLeft", "Top", "TopRight" };
        var bounds = new HashSet<Rectangle>();
        foreach (var name in names)
        {
            setPosition.Invoke(status, [Enum.Parse(positionType, name)]);
            bounds.Add(status.Bounds);
        }
        Check(bounds.Count == names.Length, "all eight anchor positions move the actual status window");
        var selectedPosition = Enum.Parse(positionType, "TopRight");
        var toolbarProperty = optionsType.GetProperty("ToolbarPosition")!;
        var top = Enum.Parse(toolbarProperty.PropertyType, "Top");
        var bottom = Enum.Parse(toolbarProperty.PropertyType, "Bottom");
        Check(Equals(toolbarProperty.GetValue(Read(isolatedSettings)), top), "legacy settings default to the top toolbar position");
        var options = Activator.CreateInstance(optionsType, root, selectedPosition, bottom)!;
        save.Invoke(isolatedSettings, [options]);
        var reloaded = Read(OpenSettings());
        Check(Equals(optionsType.GetProperty("StatusPosition")!.GetValue(reloaded), selectedPosition),
            "status position persists across plugin settings reload");
        Check(Equals(toolbarProperty.GetValue(reloaded), bottom), "toolbar position persists independently of status position");
        setPosition.Invoke(status, [Enum.Parse(positionType, "BottomLeft")]);

        var settings = PrivateField(panel, "_settings");
        var original = Read(settings);
        var selection = (Rectangle)PrivateField(panel, "_region");
        var workArea = Screen.FromRectangle(selection).WorkingArea;
        try
        {
            foreach (var (name, correctlyPlaced) in new (string, Func<Rectangle, bool>)[]
            {
                ("Bottom", toolbar => toolbar.Top > selection.Bottom),
                ("Left", toolbar => toolbar.Left >= selection.Left && toolbar.Left - selection.Left < 32
                    && workArea.Contains(toolbar) && toolbar.Top + toolbar.Height / 2 == selection.Top + selection.Height / 2),
                ("Right", toolbar => toolbar.Left > selection.Right && toolbar.Top + toolbar.Height / 2 == selection.Top + selection.Height / 2)
            })
            {
                save.Invoke(settings, [Activator.CreateInstance(optionsType, root, Enum.Parse(positionType, "BottomLeft"),
                    Enum.Parse(toolbarProperty.PropertyType, name))!]);
                Check(correctlyPlaced(panel.Bounds), $"changing settings moves the toolbar to {name} immediately");
            }
        }
        finally
        {
            save.Invoke(settings, [original]);
            Field(panel, "_saveFolder").SetValue(panel, root);
        }
    }

    private static Button Feature(Form panel, string key)
    {
        var buttons = (System.Collections.IDictionary)PrivateField(panel, "_featureButtons");
        return (Button)buttons[buttons.Keys.Cast<object>().Single(item => item.ToString() == key)]!;
    }

    private static void VerifyKeyLifetime(Form panel)
    {
        var type = PrivateField(panel, "_effects").GetType();
        using var effects = (IDisposable)Activator.CreateInstance(type, new Size(640, 320))!;
        var add = type.GetMethod("RecordKey", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var active = type.GetMethod("ActiveKeys", BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (var (key, time) in new[] { ("W", 0L), ("A", 50L), ("S", 75L), ("D", 100L) })
            add.Invoke(effects, [key, time]);
        string[] KeysAt(long time) => (string[])active.Invoke(effects, [time])!;
        Check(KeysAt(100).SequenceEqual(["A", "S", "D"]), "input model keeps the newest three keys in order");
        Check(KeysAt(349).Length == 3 && KeysAt(350).SequenceEqual(["S", "D"]) && KeysAt(400).Length == 0,
            "every key expires after exactly 300 ms");
    }

    private static MemoryStream MakeTone()
    {
        const int rate = 48000;
        const int length = rate * 3;
        var stream = new MemoryStream();
        using (var wave = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            wave.Write("RIFF"u8); wave.Write(36 + length * 2); wave.Write("WAVEfmt "u8);
            wave.Write(16); wave.Write((short)1); wave.Write((short)1); wave.Write(rate);
            wave.Write(rate * 2); wave.Write((short)2); wave.Write((short)16);
            wave.Write("data"u8); wave.Write(length * 2);
            for (var i = 0; i < length; i++) wave.Write((short)(780 * Math.Sin(i * 2 * Math.PI * 997 / rate)));
        }
        stream.Position = 0;
        return stream;
    }

    private static async Task VerifyLiveEffects(Form panel, Form target, Rectangle selection, Button copy, string root)
    {
        var session = PrivateField(panel, "_session");
        var model = PrivateField(panel, "_effects");
        var active = model.GetType().GetMethod("ActiveKeys", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var clock = model.GetType().GetMethod("Clock", BindingFlags.NonPublic | BindingFlags.Instance)!;
        string[] CurrentKeys() => (string[])active.Invoke(model, [clock.Invoke(model, null)])!;
        var previousCursor = Cursor.Position;
        try
        {
            panel.Activate();
            Feature(panel, "Keyboard").Focus();
            await WaitFor(() => panel.ContainsFocus, "The recording controls did not receive keyboard focus.");
            Press(Keys.J);
            await WaitFor(() => CurrentKeys().Contains("J"), "Single J was dropped while the recording controls had focus.");
            await WaitFor(() => panel.OwnedForms.Any(window => window.Visible && window.GetType().Name == "EffectWindow"),
                "Single key did not open the keyboard popup.");
            Check(true, "single key appears while the recording controls have focus");

            keybd_event((byte)Keys.LShiftKey, 0, 0, 0);
            try
            {
                await WaitFor(() => CurrentKeys().Contains("Shift"), "Standalone Shift did not appear at key-down.");
            }
            finally { keybd_event((byte)Keys.LShiftKey, 0, 2, 0); }
            Check(true, "standalone modifier appears on key-down");

            keybd_event((byte)Keys.LControlKey, 0, 0, 0);
            keybd_event((byte)Keys.LShiftKey, 0, 0, 0);
            try { Press(Keys.J); }
            finally
            {
                keybd_event((byte)Keys.LShiftKey, 0, 2, 0);
                keybd_event((byte)Keys.LControlKey, 0, 2, 0);
            }
            await WaitFor(() => CurrentKeys().Contains("Ctrl+Shift+J"), "Ctrl+Shift+J was not shown as a combination.");
            Check(true, "Ctrl+Shift+J appears while the recording controls have focus");
            await Task.Delay(320);
            ActivateTarget(target);
            await Task.Delay(90);
            if (panel.ContainsFocus) throw new InvalidOperationException("Test target did not receive focus.");
            Cursor.Position = new Point(selection.Left + 265, selection.Top + 186);
            await Task.Delay(45);
            Cursor.Position = new Point(selection.Left + 290, selection.Top + 186);
            await Task.Delay(45);
            Cursor.Position = new Point(selection.Left + 310, selection.Top + 186);
            await Task.Delay(45);
            foreach (var key in new[] { Keys.W, Keys.A, Keys.S, Keys.D }) Press(key);
            await WaitFor(() => CurrentKeys().SequenceEqual(["A", "S", "D"]), "Low-level keyboard hook did not receive WASD.");
            var gotWASD = CurrentKeys().SequenceEqual(["A", "S", "D"]);
            Check(gotWASD, "real WASD input appears in the right order");
            keybd_event((byte)Keys.LControlKey, 0, 0, 0);
            try { Press(Keys.K); }
            finally { keybd_event((byte)Keys.LControlKey, 0, 2, 0); }
            await WaitFor(() => CurrentKeys().Contains("Ctrl+K"), "Keyboard hook did not render the Ctrl+K combination.");
            Check(CurrentKeys()[^1] == "Ctrl+K" && !CurrentKeys().Contains("Ctrl"), "combination is shown as one key label");
            mouse_event(0x0002, 0, 0, 0, 0);
            mouse_event(0x0004, 0, 0, 0, 0);
            var copyFrame = session.GetType().GetMethod("CopyLatest")!;
            Bitmap? matching = null;
            try
            {
                await WaitFor(() =>
                {
                    var frame = (Bitmap)copyFrame.Invoke(session, null)!;
                    if (HasColor(frame, new Rectangle(480, 215, 160, 98), c => c.R < 70 && c.G < 75 && c.B < 90)
                        && HasColor(frame, new Rectangle(283, 158, 61, 60), c => c.R > 220 && c.G > 160 && c.B < 100)
                        && HasColor(frame, new Rectangle(264, 181, 42, 11), c => c.R < 175 && c.B > 220 && c.B - c.G > 12))
                    {
                        matching = frame;
                        return true;
                    }
                    frame.Dispose();
                    return false;
                }, "No captured frame showed the keyboard popup, click ring, and mouse trail together.");
                Check(true, "captured frame contains the keyboard popup, click ring and mouse trail");
                matching!.Save(Path.Combine(root, "effects-frame.png"), ImageFormat.Png);
            }
            finally { matching?.Dispose(); }
            var oldClipboard = Clipboard.GetDataObject();
            try
            {
                copy.PerformClick();
                using var pasted = Clipboard.GetImage();
                Check(pasted?.Size == selection.Size, "the copy button writes a pasteable bitmap to the clipboard");
            }
            finally
            {
                await RestoreClipboard(oldClipboard);
            }
            await Task.Delay(330);
            Check(CurrentKeys().Length == 0, "keyboard popup data expires in the live session");

            Feature(panel, "Keyboard").PerformClick();
            Feature(panel, "Trail").PerformClick();
            Feature(panel, "Clicks").PerformClick();
            Press(Keys.J);
            await Task.Delay(80);
            Check(CurrentKeys().Length == 0, "disabled keyboard effect ignores subsequent input");
        }
        finally { Cursor.Position = previousCursor; }

        static void Press(Keys key)
        {
            keybd_event((byte)key, 0, 0, 0);
            keybd_event((byte)key, 0, 2, 0);
        }
    }

    private static async Task VerifyRapidTrail(Form panel, object session, Form target, Rectangle screen)
    {
        var trail = Feature(panel, "Trail");
        var pointer = Cursor.Position;
        var area = new Rectangle(screen.Left + screen.Width / 2 - 180, screen.Top + screen.Height / 2 - 120, 360, 240);
        var origin = area.Location - new Size(screen.Location);
        var latest = session.GetType().GetMethod("CopyLatest")!;
        trail.PerformClick();
        try
        {
            ActivateTarget(target);
            Cursor.Position = area.Location + new Size(30, 40);
            await Task.Delay(60);
            Cursor.Position = area.Location + new Size(220, 140);
            await WaitFor(() => panel.OwnedForms.Any(window => window.Visible && window.GetType().Name == "EffectWindow"),
                "Fast cursor movement did not display a mouse trail.");
            var overlay = panel.OwnedForms.Single(window => window.GetType().Name == "EffectWindow");
            Check(GetWindowDisplayAffinity(overlay.Handle, out var affinity) && affinity == 0x11,
                "the live trail is excluded from the recording capture source");
            for (var sample = 0; sample < 24; sample++)
            {
                Move(sample);
                await Task.Delay(25);
                if (sample % 3 != 0) continue;
                using var frame = (Bitmap?)latest.Invoke(session, null);
                if (frame is null) throw new InvalidOperationException("No recorded frame during fast cursor movement.");
                VerifyBrightArea(frame, new Rectangle(origin, area.Size), "full-screen recording");
            }
            Check(true, "full-screen recording samples remain free of black patches during fast cursor movement");

            using var preview = new Bitmap(area.Width, area.Height);
            // Temporarily let the test sample the otherwise excluded preview over its solid-color test window.
            Check(SetWindowDisplayAffinity(overlay.Handle, 0), "the desktop probe can sample the trail preview");
            try
            {
                var visibleSamples = 0;
                using var graphics = Graphics.FromImage(preview);
                for (var sample = 0; sample < 36; sample++)
                {
                    Move(sample);
                    await Task.Delay(20);
                    CopyLayeredDesktop(graphics, area);
                    VerifyBrightArea(preview, new Rectangle(Point.Empty, area.Size), "desktop trail preview");
                    if (HasColor(preview, new Rectangle(Point.Empty, area.Size), c => c.R < 175 && c.B > 220 && c.B > c.G + 12))
                        visibleSamples++;
                }
                Check(visibleSamples >= 18, $"desktop preview stays visible without black patches ({visibleSamples}/36 samples contain the trail)");
            }
            finally { SetWindowDisplayAffinity(overlay.Handle, affinity); }
            Check(!GetLayeredWindowAttributes(overlay.Handle, out _, out _, out var attributes) || (attributes & 1) == 0,
                "live effects publish premultiplied alpha without color-keyed repaints");
        }
        finally
        {
            trail.PerformClick();
            Cursor.Position = pointer;
        }

        void Move(int sample)
        {
            var x = sample % 2 == 0 ? 45 + sample * 11 % 55 : 235 + sample * 7 % 70;
            var y = 38 + sample * 29 % 150;
            Cursor.Position = area.Location + new Size(x, y);
        }

    }

    private static async Task VerifyRapidScreenshotInput(CaptureForm drawing, object session, Rectangle probe, Point origin,
        string effects, Form? preview = null)
    {
        var copyFrame = session.GetType().GetMethod("CopyLatest")!;
        using var desktopFrame = new Bitmap(probe.Width, probe.Height);
        using var graphics = Graphics.FromImage(desktopFrame);
        var captureProbe = probe;
        captureProbe.Offset(-origin.X, -origin.Y);
        var start = probe.Location + new Size(22, 20);
        Cursor.Position = start;
        await Task.Delay(45);
        Check(GetAncestor(WindowFromPoint(start), 2) == drawing.Handle,
            "the real mouse can reach the screenshot's drawing area");
        await MoveAndSample($"F1 editor mouse movement ({effects})");
        Cursor.Position = start;
        await Task.Delay(45);
        mouse_event(0x0002, 0, 0, 0, 0);
        try { await MoveAndSample($"F1 editor pen stroke ({effects})"); }
        finally { mouse_event(0x0004, 0, 0, 0, 0); }

        async Task MoveAndSample(string description)
        {
            for (var sample = 0; sample < 36; sample++)
            {
                var x = sample % 2 == 0 ? 24 + sample * 11 % 40 : 250 + sample * 7 % 44;
                var y = 20 + sample * 13 % 100;
                Cursor.Position = probe.Location + new Size(x, y);
                await Task.Delay(15);
                if (preview is { Visible: true })
                {
                    if (!SetWindowDisplayAffinity(preview.Handle, 0)) throw new InvalidOperationException("The F1 probe could not read the visible trail.");
                    try { CopyLayeredDesktop(graphics, probe); }
                    finally { SetWindowDisplayAffinity(preview.Handle, 0x11); }
                }
                else graphics.CopyFromScreen(probe.Location, Point.Empty, probe.Size);
                VerifyBrightArea(desktopFrame, new Rectangle(Point.Empty, probe.Size), description + " desktop");
                if (sample % 3 != 0) continue;
                using var captured = (Bitmap?)copyFrame.Invoke(session, null);
                if (captured is null) throw new InvalidOperationException("No recording frame during " + description);
                VerifyBrightArea(captured, captureProbe, description + " recording");
            }
            Check(true, description + " shows no black patches in sampled editor and recording pixels");
        }
    }

    private static void CopyLayeredDesktop(Graphics graphics, Rectangle area)
    {
        const uint operation = (uint)CopyPixelOperation.SourceCopy | (uint)CopyPixelOperation.CaptureBlt;
        var desktop = GetDC(0);
        if (desktop == 0) throw new InvalidOperationException("The desktop probe could not open a screen DC.");
        try
        {
            var dc = graphics.GetHdc();
            try
            {
                if (!BitBlt(dc, 0, 0, area.Width, area.Height, desktop, area.Left, area.Top, operation))
                    throw new InvalidOperationException("The desktop probe could not read the preview pixels.");
            }
            finally { graphics.ReleaseHdc(dc); }
        }
        finally { ReleaseDC(0, desktop); }
    }

    private static void VerifyBrightArea(Bitmap bitmap, Rectangle bounds, string description)
    {
        for (var y = bounds.Top; y < bounds.Bottom - 6; y += 6)
            for (var x = bounds.Left; x < bounds.Right - 6; x += 6)
                if (IsDark(bitmap.GetPixel(x, y)) && IsDark(bitmap.GetPixel(x + 6, y)) &&
                    IsDark(bitmap.GetPixel(x, y + 6)) && IsDark(bitmap.GetPixel(x + 6, y + 6)))
                    throw new InvalidOperationException($"A black patch appeared during {description} at ({x}, {y}).");
        static bool IsDark(Color color) => color.R < 30 && color.G < 30 && color.B < 30;
    }

    private static bool HasColor(Bitmap frame, Rectangle area, Func<Color, bool> match)
    {
        for (var y = area.Top; y < area.Bottom; y++)
            for (var x = area.Left; x < area.Right; x++)
                if (match(frame.GetPixel(x, y))) return true;
        return false;
    }

    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scanCode, uint flags, nint extra);
    private static async Task RestoreClipboard(IDataObject? previous)
    {
        for (var retries = 0; ; retries++)
        {
            try
            {
                if (previous is not null) Clipboard.SetDataObject(previous, true, 1, 0);
                else Clipboard.Clear();
                return;
            }
            catch (ExternalException error) when (error.HResult == unchecked((int)0x800401D0) && retries < 15)
            {
                await Task.Delay(100);
            }
        }
    }

    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, nint extra);

    private static void SavePreview(Form panel, Form target, Rectangle selection, string path)
    {
        var status = VisibleOverlay("RecorderStatusWindow");
        var bounds = Rectangle.Inflate(Rectangle.Union(selection, panel.Bounds), 16, 16);
        using var editor = new Bitmap(target.Width, target.Height, PixelFormat.Format32bppRgb);
        target.DrawToBitmap(editor, target.ClientRectangle);
        using var preview = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
        using (var graphics = Graphics.FromImage(preview))
        {
            graphics.DrawImage(editor, new Rectangle(Point.Empty, bounds.Size), target.RectangleToClient(bounds), GraphicsUnit.Pixel);
            foreach (var window in new[] { panel, status })
            {
                using var snapshot = new Bitmap(window.Width, window.Height, PixelFormat.Format32bppRgb);
                window.DrawToBitmap(snapshot, window.ClientRectangle);
                var state = graphics.Save();
                using var clip = window.Region?.Clone();
                if (clip is not null)
                {
                    clip.Translate(window.Left - bounds.Left, window.Top - bounds.Top);
                    graphics.SetClip(clip, System.Drawing.Drawing2D.CombineMode.Intersect);
                }
                graphics.DrawImageUnscaled(snapshot, window.Left - bounds.Left, window.Top - bounds.Top);
                graphics.Restore(state);
            }
        }
        preview.Save(path, ImageFormat.Png);
    }
    private static FieldInfo Field(object instance, string name) =>
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
}
