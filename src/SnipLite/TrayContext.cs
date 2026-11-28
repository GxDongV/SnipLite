using SnipLite.Plugins;
using SnipLite.Plugin.Abstractions;

namespace SnipLite;

internal sealed class TrayContext : ApplicationContext
{
    private readonly AppSettings _settings;
    private readonly NotifyIcon _tray;
    private readonly HotkeyHost _hotkeys = new();
    private readonly ThemeMonitor _themeMonitor = new();
    private readonly Localization.LanguageMonitor _languageMonitor;
    private readonly CaptureHistory _history = new();
    private readonly System.Windows.Forms.Timer _autoSaveCleanupTimer = new() { Interval = 120_000 };
    private readonly PluginManager _plugins;
    private readonly PluginHostServices _pluginHost;
    private bool _exiting;
    private CaptureForm? _overlay;
    private PreferencesForm? _prefs;
    private bool _editingHotkey;
    private string _hotkeySignature = "";
    private string _menuSignature = "";
    private uint? _lastPasteSequence;

    public TrayContext(bool captureOnStart = false, int openPreferencesTab = -1, bool openMenu = false, bool scrollCapture = false)
    {
        _settings = AppSettings.Load();
        Localization.LanguageService.Configure(_settings.DisplayLanguage);
        _tray = new NotifyIcon { Icon = TrayIcons.Create(_settings), Text = "SnipLite", Visible = true };
        _pluginHost = new PluginHostServices(_settings, (image, pin) => HandleResult(new CaptureResult
        {
            Image = new Bitmap(image.Image), Bounds = image.SourceBounds ?? new Rectangle(Point.Empty, image.Image.Size),
            Pin = pin, Action = pin ? CaptureAction.Pin : CaptureAction.Copy
        }, forceCopy: !pin));
        _plugins = new PluginManager(_pluginHost, () => !_exiting && _overlay is not { IsDisposed: false, Visible: true });
        _pluginHost.Manager = _plugins;
        _plugins.DiscoverBundled(BundledScrollPlugin.CacheRoot);
        _plugins.DiscoverInstalled();
        _plugins.Changed += PluginsChanged;
        AppTheme.Changed += RefreshTheme;
        _tray.DoubleClick += (_, _) => StartCapture(CaptureMode.Snip);
        ApplySettings();
        _languageMonitor = new Localization.LanguageMonitor(ApplySettings);
        _languageMonitor.Start();
        _autoSaveCleanupTimer.Tick += CleanAutoSavesAfterStartup;
        _autoSaveCleanupTimer.Start();
        void Once(Action action)
        {
            var timer = new System.Windows.Forms.Timer { Interval = 250 };
            timer.Tick += (_, _) => { timer.Stop(); timer.Dispose(); action(); }; timer.Start();
        }
        if (captureOnStart) Once(() => StartCapture(CaptureMode.Snip));
        if (openPreferencesTab >= 0) Once(() => ShowPreferences(openPreferencesTab));
        if (openMenu) Once(() => _tray.ContextMenuStrip?.Show(200, 200));
        if (scrollCapture) Once(() => _ = _plugins.ExecuteAsync(BundledScrollPlugin.Id + "/capture"));
        AppLog.Write($"SnipLite 已启动 version={AppInfo.Version} exe={Application.ExecutablePath}");
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = SnipMenu.Create(_settings.CreateInterfaceFont());
        menu.Items.Add(SnipMenu.Item("截屏", () => StartCapture(CaptureMode.Snip), HotkeyDefinitions.Format(_settings.CaptureHotkey)));
        foreach (var command in _plugins.Commands)
            menu.Items.Add(SnipMenu.Item(command.Title.Get(_pluginHost.Language), () => _ = _plugins.ExecuteAsync(command.Id), HotkeyDefinitions.Format(_plugins.Hotkey(command.Id)), pluginIcon: command.Icon, iconId: "plugin"));
        if (_settings.EnhancedTrayMenu)
        {
            menu.Items.Add(SnipMenu.Item("截屏并自动复制", () => StartCapture(CaptureMode.Snip, true), HotkeyDefinitions.Format(_settings.CopyCaptureHotkey)));
            menu.Items.Add(SnipMenu.Item("自定义截屏…", CustomCapture, HotkeyDefinitions.Format(_settings.CustomCaptureHotkey)));
        }
        menu.Items.Add(SnipMenu.Item("贴图 / 恢复贴图", PasteFromClipboard, HotkeyDefinitions.Format(_settings.PinHotkey)));
        menu.Items.Add(SnipMenu.Item("隐藏 / 显示所有贴图", PinForm.ToggleAll, HotkeyDefinitions.Format(_settings.TogglePinsHotkey)));
        if (_settings.EnhancedTrayMenu)
        {
            menu.Items.Add(SnipMenu.Item("切换到另一贴图分组", PinForm.SwitchGroup, HotkeyDefinitions.Format(_settings.SwitchGroupHotkey)));
            menu.Items.Add(SnipMenu.Item("恢复历史贴图", () => PinForm.RestoreLast(_settings)));
            menu.Items.Add(SnipMenu.Item("关闭全部贴图", PinForm.CloseAll));
            var history = SnipMenu.Item("截屏历史");
            history.DropDownOpening += (_, _) =>
            {
                foreach (ToolStripItem item in history.DropDownItems) item.Dispose();
                history.DropDownItems.Clear();
                foreach (var entry in _history.Entries)
                    history.DropDownItems.Add(SnipMenu.Item(entry.Label, () => PinImage(entry.Image(), entry.Bounds), iconId: "image"));
                if (history.DropDownItems.Count == 0) history.DropDownItems.Add(new ToolStripMenuItem(UiText.T("暂无截屏历史")) { Enabled = false });
            };
            menu.Items.Add(history);
            menu.Items.Add(SnipMenu.Item("清空截屏历史", _history.Clear));
        }
        menu.Items.Add(new ToolStripSeparator());
        var disable = SnipMenu.Item("禁用快捷键", () => { _settings.HotkeysEnabled = !_settings.HotkeysEnabled; _settings.Save(); ApplySettings(); });
        menu.Items.Add(disable);
        menu.Opening += (_, _) => disable.Checked = !_settings.HotkeysEnabled;
        menu.Items.Add(SnipMenu.Item("文字识别", () => StartCapture(CaptureMode.Ocr), HotkeyDefinitions.Format(_settings.OcrHotkey)));
        menu.Items.Add(SnipMenu.Item("二维码识别", () => StartCapture(CaptureMode.Qr), HotkeyDefinitions.Format(_settings.QrHotkey)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(SnipMenu.Item("首选项…", () => ShowPreferences(0)));
        menu.Items.Add(SnipMenu.Item("管理插件…", () => ShowPreferences(PreferencesForm.PluginsTab), iconId: "plugin"));
        menu.Items.Add(SnipMenu.Item("帮助", () => ShowPreferences(7)));
        menu.Items.Add(SnipMenu.Item("重新启动", () => Restart()));
        menu.Items.Add(SnipMenu.Item("退出", Exit));
        UiText.Apply(menu, _settings.DisplayLanguage);
        return menu;
    }

    private void ShowPreferences(int tab)
    {
        if (_prefs is { IsDisposed: false }) { _prefs.Activate(); _prefs.SelectTab(tab); return; }
        _prefs = new PreferencesForm(_settings, tab, _plugins);
        _prefs.PluginRestartRequested += () => Restart(PreferencesForm.PluginsTab);
        _prefs.AdministratorRestartReady += Exit;
        _prefs.Applied += ApplySettings;
        _prefs.HotkeyAvailable = _hotkeys.CanRegister;
        _prefs.HotkeyEditing += editing => { _editingHotkey = editing; _hotkeySignature = ""; ApplyHotkeys(); };
        _prefs.FormClosed += (_, _) => { _prefs = null; _editingHotkey = false; _hotkeySignature = ""; ApplyHotkeys(); };
        _prefs.Show();
    }
    private void ApplySettings()
    {
        Localization.LanguageService.Configure(_settings.DisplayLanguage);
        Toast.Notifications = _settings.ShowToast; AppLog.Level = _settings.LogLevel;
        var icon = TrayIcons.Create(_settings); var oldIcon = _tray.Icon; _tray.Icon = icon; oldIcon?.Dispose();
        var tip = Localization.LanguageService.Format("SnipLite {0} | {1} 截图 | {2} 贴图", AppInfo.Version, HotkeyDefinitions.Format(_settings.CaptureHotkey), HotkeyDefinitions.Format(_settings.PinHotkey));
        _tray.Text = tip[..Math.Min(tip.Length, 63)];
        var signature = $"{_settings.EnhancedTrayMenu}|{Localization.LanguageService.CurrentLanguage}|{_settings.InterfaceFont}|{_settings.InterfaceFontSize}|" + string.Join(',', HotkeyDefinitions.Items.Select(item => typeof(AppSettings).GetProperty(item.Name)!.GetValue(_settings)));
        if (_menuSignature != signature)
        {
            var oldMenu = _tray.ContextMenuStrip; _tray.ContextMenuStrip = BuildMenu(); _menuSignature = signature;
            // Wait until ToolStrip's closing stack has unwound before disposal.
            if (oldMenu is not null) SynchronizationContext.Current?.Post(_ => oldMenu.Dispose(), null);
        }
        _history.Trim(_settings.HistoryImageLimit);
        PinForm.TrimHistory(_settings.HistoryImageLimit);
        PinForm.ApplySettingsToAll(); _overlay?.Invalidate(); ApplyHotkeys();
    }
    private void ApplyHotkeys()
    {
        var signature = $"{_settings.HotkeysEnabled}|{_editingHotkey}|" + string.Join(',', HotkeyDefinitions.Items.Select(item => typeof(AppSettings).GetProperty(item.Name)!.GetValue(_settings)))
            + "|" + string.Join(',', _plugins.Commands.Select(c => $"{c.Id}:{_plugins.Hotkey(c.Id)}"));
        if (signature == _hotkeySignature) return;
        _hotkeySignature = signature; _hotkeys.UnregisterAll(); _settings.HotkeyErrors.Clear();
        if (!_settings.HotkeysEnabled || _editingHotkey)
        {
            AppLog.Write($"Global hotkeys inactive: enabled={_settings.HotkeysEnabled} editing={_editingHotkey}", debug: true);
            return;
        }
        var handlers = new Dictionary<string, Action>
        {
            [nameof(_settings.CaptureHotkey)] = () => StartCapture(CaptureMode.Snip),
            [nameof(_settings.CopyCaptureHotkey)] = () => StartCapture(CaptureMode.Snip, true),
            [nameof(_settings.CustomCaptureHotkey)] = CustomCapture,
            [nameof(_settings.PinHotkey)] = PasteFromClipboard,
            [nameof(_settings.TogglePinsHotkey)] = PinForm.ToggleAll,
            [nameof(_settings.SwitchGroupHotkey)] = PinForm.SwitchGroup,
            [nameof(_settings.MouseThroughHotkey)] = PinForm.ToggleMouseThroughActive,
            [nameof(_settings.TranslationHotkey)] = () => StartCapture(CaptureMode.Translation),
            [nameof(_settings.OcrHotkey)] = () => StartCapture(CaptureMode.Ocr),
            [nameof(_settings.QrHotkey)] = () => StartCapture(CaptureMode.Qr)
        };
        void Register(string id, string title, Keys binding, Action handler)
        {
            if (binding == Keys.None || _hotkeys.TryRegister(binding, id, handler, out var error)) return;
            var bindingText = HotkeyDefinitions.Format(binding);
            if (error.NativeErrorCode == HotkeyHost.ErrorHotkeyAlreadyRegistered)
            {
                _settings.HotkeyErrors[id] = "已被其他程序占用";
                Toast.Error(Localization.LanguageService.Format("{0}快捷键 {1} 已被占用", title, bindingText));
            }
            else
            {
                _settings.HotkeyErrors[id] = error.Message;
                Toast.Error($"{title} ({bindingText}) · {error.Message}");
            }
        }
        foreach (var (label, name) in HotkeyDefinitions.Items)
        {
            if (name == nameof(AppSettings.CopyCoordinatesHotkey)) continue;
            var binding = (Keys)typeof(AppSettings).GetProperty(name)!.GetValue(_settings)!;
            Register(name, UiText.T(label), binding, handlers[name]);
        }
        foreach (var command in _plugins.Commands)
            Register(command.Id, command.Title.Get(_pluginHost.Language), _plugins.Hotkey(command.Id), () => _ = _plugins.ExecuteAsync(command.Id));
    }
    private void PluginsChanged()
    {
        _menuSignature = _hotkeySignature = ""; ApplySettings();
    }
    private void CustomCapture()
    {
        using var dialog = new CaptureRegionForm(_settings);
        if (dialog.ShowDialog() != DialogResult.OK) return;
        var bounds = Rectangle.Intersect(dialog.RegionBounds, SystemInformation.VirtualScreen);
        if (bounds.Width < 1 || bounds.Height < 1) { Toast.Error(UiText.T("区域不在屏幕范围内")); return; }
        StartCapture(CaptureMode.Snip, preset: bounds);
    }
    private void StartCapture(CaptureMode mode, bool forceCopy = false, Rectangle? preset = null)
    {
        if (_plugins.BlocksScreenshots || _exiting)
        {
            AppLog.Write($"Capture skipped: mode={mode} pluginRunning={_plugins.IsRunning} exiting={_exiting}", debug: true);
            return;
        }
        if (_overlay is { IsDisposed: false, Visible: true })
        {
            _overlay.Activate();
            _pluginHost.RaiseScreenshotOverlays();
            return;
        }
        _overlay?.Dispose();
        try
        {
            IReadOnlyList<RegisteredPluginCommand> commands = _plugins.IsRunning ? [] : _plugins.Commands;
            var overlay = new CaptureForm(SystemInformation.VirtualScreen, mode, _settings, preset, showFrozenCursor: !_plugins.IsRunning)
            {
                PluginButtons = commands.Select(c => (c.Id, c.Title.Get(_pluginHost.Language), c.Icon)).ToArray(),
                ScreenshotPluginIds = commands.Where(c => c.ExecuteInScreenshot is not null).Select(c => c.Id).ToHashSet()
            }; _overlay = overlay;
            overlay.ScreenshotPluginRequested += async id =>
            {
                if (_exiting || _plugins.IsRunning) return;
                await _plugins.ExecuteFromScreenshotAsync(overlay, id);
            };
            overlay.PluginRequested += async (id, bounds) =>
            {
                if (!_exiting) await _plugins.ExecuteFromSelectionAsync(id, bounds, _pluginHost);
            };
            overlay.Done += result =>
            {
                if (result is null || !result.KeepOpen) if (ReferenceEquals(_overlay, overlay)) _overlay = null;
                if (result is not null) HandleResult(result, forceCopy);
            };
            overlay.FormClosed += (_, _) => { if (ReferenceEquals(_overlay, overlay)) _overlay = null; };
            _pluginHost.ShowScreenshot(overlay);
        }
        catch (Exception ex) { _overlay?.Dispose(); _overlay = null; Program.Report(ex); }
    }
    private void HandleResult(CaptureResult result, bool forceCopy)
    {
        var transferred = false;
        try
        {
            _history.Add(result.Image, result.Bounds, _settings.HistoryImageLimit);
            var copiesToClipboard = forceCopy || result.Action == CaptureAction.Copy ||
                result.Action == CaptureAction.None && _settings.CopyAfterCapture;
            if (_settings.AutoSave)
            {
                var path = Services.SaveCaptureFile(result.Image, _settings, _settings.SaveDirectory, playSound: !copiesToClipboard);
                AutoSavedCaptures.Record(path);
            }
            if (result.Pin || result.Action == CaptureAction.Pin)
            { PinImage(result.Image, result.Bounds); transferred = true; }
            else if (result.Action == CaptureAction.QuickSave)
            {
                var path = Services.SaveCaptureFile(result.Image, _settings, _settings.QuickSaveDirectory);
                if (_settings.QuickSaveToast) Toast.Show(UiText.T("已保存：") + Path.GetFileName(path));

            }
            else if (forceCopy || result.Action == CaptureAction.Copy || _settings.CopyAfterCapture)
            {
                Services.CopyImage(result.Image, _settings.CaptureBitmapClipboard, _settings);
                if (_history.Entries.Count > 0) _lastPasteSequence = GetClipboardSequenceNumber();

                Toast.Show(Localization.LanguageService.Format("已复制  {0} × {1}", result.Image.Width, result.Image.Height));
            }
        }
        catch (Exception ex) { AppLog.Write("截图输出失败", ex); Toast.Error(UiText.T("截图输出失败：") + ex.Message); }
        finally { if (!transferred) result.Image.Dispose(); }
    }
    private void PinImage(Bitmap image, Rectangle? bounds = null, bool stagger = false)
    {
        Point? point = _settings.PinAtCapturePosition && bounds is Rectangle r ? new Point(r.X + r.Width / 2, r.Y + r.Height / 2) : null;
        var pin = new PinForm(image, point, _settings.PinOpacityPercent, _settings);
        if (stagger) pin.ShowBesideExisting(); else pin.Show();
    }
    private void PasteFromClipboard()
    {
        if (TryPinActiveCapture(_overlay)) return;
        if (PinForm.RestoreHidden()) return;
        var sequence = GetClipboardSequenceNumber();
        var newClipboard = _lastPasteSequence != sequence;
        _lastPasteSequence = sequence;
        try
        {
            var image = newClipboard ? Services.ClipboardImage() : null;
            if (image is not null) { PinImage(image, stagger: true); return; }
            if (newClipboard && _settings.TextToImage && Clipboard.ContainsText())
            {
                var text = Clipboard.GetText();
                if (!_settings.IgnoreFilePaths || !TextImageRenderer.IsFilePath(text))
                {
                    var html = !_settings.IgnoreHtml && Clipboard.ContainsText(TextDataFormat.Html) ? Clipboard.GetText(TextDataFormat.Html) : null;
                    PinImage(TextImageRenderer.Render(text, html, _settings), stagger: true); return;
                }
            }
            if (PinForm.RestoreLast(_settings)) return;
            if (PinForm.ActivateExisting()) return;
            Toast.Show(UiText.T("剪贴板中没有可贴出的内容，也没有可恢复的贴图"));
        }
        catch (Exception ex) { AppLog.Write("贴图失败", ex); Toast.Error(UiText.T("贴图失败：") + ex.Message); }
    }

    internal static bool TryPinActiveCapture(CaptureForm? overlay)
    {
        if (overlay is not { IsDisposed: false, Visible: true }) return false;
        // An active capture owns the key even before a selection is complete.
        // Never paste old clipboard contents over the capture overlay.
        overlay.TryPinSelection();
        return true;
    }
    private void Restart(int preferencesTab = -1)
    {
        var arguments = preferencesTab < 0 ? "--wait-for-instance" : $"--wait-for-instance --prefs:{preferencesTab}";
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Application.ExecutablePath) { Arguments = arguments, UseShellExecute = true });
        Exit();
    }
    private async void Exit()
    {
        if (_exiting) return;
        _exiting = true;
        _autoSaveCleanupTimer.Dispose();
        _plugins.Cancel();
        await _plugins.WhenIdle;
        _plugins.Changed -= PluginsChanged;
        _plugins.Dispose();
        AppTheme.Changed -= RefreshTheme;
        _themeMonitor.Dispose();
        _languageMonitor.Dispose();
        _tray.Visible = false; var icon = _tray.Icon; _tray.Dispose(); icon?.Dispose();
        _overlay?.Close(); _prefs?.Close(); _hotkeys.Dispose(); PinForm.CloseAllForExit(); ExitThread();
    }

    private void CleanAutoSavesAfterStartup(object? sender, EventArgs e)
    {
        _autoSaveCleanupTimer.Stop();
        var mode = _settings.AutoSaveRetention;
        if (!_settings.AutoSave || mode == AutoSaveRetentionMode.KeepAll) return;
        var folder = _settings.SaveDirectory;
        var limit = mode == AutoSaveRetentionMode.Count ? _settings.AutoSaveKeepCount : _settings.AutoSaveKeepDays;
        _ = Task.Run(() => AutoSavedCaptures.Clean(folder, mode, limit, DateTime.UtcNow));
    }
    private void RefreshTheme()
    {
        var old = _tray.Icon;
        _tray.Icon = TrayIcons.Create(_settings);
        old?.Dispose();
        _overlay?.Invalidate();
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
}
