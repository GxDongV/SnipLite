namespace SnipLite;

internal sealed class TrayContext : ApplicationContext
{
    private readonly AppSettings _settings;
    private readonly NotifyIcon _tray;
    private readonly HotkeyHost _hotkeys = new();
    private readonly CaptureHistory _history = new();
    private CaptureForm? _overlay;
    private PreferencesForm? _prefs;
    private bool _editingHotkey;
    private string _hotkeySignature = "";
    private string _menuSignature = "";
    private uint _lastPasteSequence;

    public TrayContext(bool captureOnStart = false, int openPreferencesTab = -1, bool openMenu = false)
    {
        _settings = AppSettings.Load();
        _tray = new NotifyIcon { Icon = TrayIcons.Create(_settings), Text = "SnipLite", Visible = true };
        _tray.DoubleClick += (_, _) => StartCapture(CaptureMode.Snip);
        ApplySettings();
        void Once(Action action)
        {
            var timer = new System.Windows.Forms.Timer { Interval = 250 };
            timer.Tick += (_, _) => { timer.Stop(); timer.Dispose(); action(); }; timer.Start();
        }
        if (captureOnStart) Once(() => StartCapture(CaptureMode.Snip));
        if (openPreferencesTab >= 0) Once(() => ShowPreferences(openPreferencesTab));
        if (openMenu) Once(() => _tray.ContextMenuStrip?.Show(200, 200));
        if (_settings.KeepResponsive) _ = Task.Run(() =>
        {
            try { Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages(); } catch { }
        });
        AppLog.Write($"SnipLite 已启动 version={AppInfo.Version} exe={Application.ExecutablePath}");
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = SnipMenu.Create(); menu.Font = _settings.CreateInterfaceFont();
        menu.Items.Add(SnipMenu.Item("截屏", () => StartCapture(CaptureMode.Snip), HotkeyDefinitions.Format(_settings.CaptureHotkey)));
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
            menu.Items.Add(SnipMenu.Item("恢复最近关闭的贴图", () => PinForm.RestoreLast(_settings)));
            menu.Items.Add(SnipMenu.Item("关闭全部贴图", PinForm.CloseAll));
            var history = new ToolStripMenuItem("截屏历史");
            history.DropDownOpening += (_, _) =>
            {
                foreach (ToolStripItem item in history.DropDownItems) item.Dispose();
                history.DropDownItems.Clear();
                foreach (var entry in _history.Entries)
                    history.DropDownItems.Add(SnipMenu.Item(entry.Label, () => PinImage(entry.Image(), entry.Bounds)));
                if (history.DropDownItems.Count == 0) history.DropDownItems.Add(new ToolStripMenuItem("暂无截屏历史") { Enabled = false });
            };
            menu.Items.Add(history);
            menu.Items.Add(SnipMenu.Item("清空截屏历史", () => { _history.Clear(); _settings.RecentRegions.Clear(); _settings.Save(); }));
        }
        menu.Items.Add(new ToolStripSeparator());
        var disable = SnipMenu.Item("禁用快捷键", () => { _settings.HotkeysEnabled = !_settings.HotkeysEnabled; _settings.Save(); ApplySettings(); });
        menu.Items.Add(disable);
        menu.Opening += (_, _) => disable.Checked = !_settings.HotkeysEnabled;
        menu.Items.Add(SnipMenu.Item("文字识别", () => StartCapture(CaptureMode.Ocr), HotkeyDefinitions.Format(_settings.OcrHotkey)));
        menu.Items.Add(SnipMenu.Item("二维码识别", () => StartCapture(CaptureMode.Qr), HotkeyDefinitions.Format(_settings.QrHotkey)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(SnipMenu.Item("首选项…", () => ShowPreferences(0)));
        menu.Items.Add(SnipMenu.Item("帮助", () => ShowPreferences(7)));
        menu.Items.Add(SnipMenu.Item("重新启动", Restart));
        menu.Items.Add(SnipMenu.Item("退出", Exit));
        UiText.Apply(menu, _settings.DisplayLanguage);
        return menu;
    }

    private void ShowPreferences(int tab)
    {
        if (_prefs is { IsDisposed: false }) { _prefs.Activate(); _prefs.SelectTab(tab); return; }
        _prefs = new PreferencesForm(_settings, tab);
        _prefs.Applied += ApplySettings;
        _prefs.HotkeyEditing += editing => { _editingHotkey = editing; _hotkeySignature = ""; ApplyHotkeys(); };
        _prefs.FormClosed += (_, _) => { _prefs = null; _editingHotkey = false; _hotkeySignature = ""; ApplyHotkeys(); };
        _prefs.Show();
    }
    private void ApplySettings()
    {
        Toast.Notifications = _settings.ShowToast; AppLog.Level = _settings.LogLevel;
        var icon = TrayIcons.Create(_settings); var oldIcon = _tray.Icon; _tray.Icon = icon; oldIcon?.Dispose();
        var tip = $"SnipLite {AppInfo.Version} | {HotkeyDefinitions.Format(_settings.CaptureHotkey)} 截图 | {HotkeyDefinitions.Format(_settings.PinHotkey)} 贴图";
        _tray.Text = tip[..Math.Min(tip.Length, 63)];
        var signature = $"{_settings.EnhancedTrayMenu}|{_settings.DisplayLanguage}|{_settings.InterfaceFont}|{_settings.InterfaceFontSize}|" + string.Join(',', HotkeyDefinitions.Items.Select(item => typeof(AppSettings).GetProperty(item.Name)!.GetValue(_settings)));
        if (_menuSignature != signature)
        {
            var oldMenu = _tray.ContextMenuStrip; _tray.ContextMenuStrip = BuildMenu(); _menuSignature = signature;
            // Wait until ToolStrip's closing stack has unwound before disposal.
            if (oldMenu is not null) SynchronizationContext.Current?.Post(_ => oldMenu.Dispose(), null);
        }
        _history.Trim(_settings.HistoryImageLimit);
        PinForm.ApplySettingsToAll(); ApplyHotkeys();
    }
    private void ApplyHotkeys()
    {
        var signature = $"{_settings.HotkeysEnabled}|{_editingHotkey}|" + string.Join(',', HotkeyDefinitions.Items.Select(item => typeof(AppSettings).GetProperty(item.Name)!.GetValue(_settings)));
        if (signature == _hotkeySignature) return;
        _hotkeySignature = signature; _hotkeys.UnregisterAll(); _settings.HotkeyErrors.Clear();
        if (!_settings.HotkeysEnabled || _editingHotkey) return;
        var handlers = new Dictionary<string, Action>
        {
            [nameof(_settings.CaptureHotkey)] = () => StartCapture(CaptureMode.Snip),
            [nameof(_settings.CopyCaptureHotkey)] = () => StartCapture(CaptureMode.Snip, true),
            [nameof(_settings.CustomCaptureHotkey)] = CustomCapture,
            [nameof(_settings.PinHotkey)] = PasteFromClipboard,
            [nameof(_settings.TogglePinsHotkey)] = PinForm.ToggleAll,
            [nameof(_settings.SwitchGroupHotkey)] = PinForm.SwitchGroup,
            [nameof(_settings.MouseThroughHotkey)] = PinForm.ToggleMouseThroughActive,
            [nameof(_settings.OcrHotkey)] = () => StartCapture(CaptureMode.Ocr),
            [nameof(_settings.QrHotkey)] = () => StartCapture(CaptureMode.Qr)
        };
        foreach (var (label, name) in HotkeyDefinitions.Items)
        {
            var binding = (Keys)typeof(AppSettings).GetProperty(name)!.GetValue(_settings)!;
            if (binding == Keys.None) continue;
            uint mods = 0;
            if (binding.HasFlag(Keys.Control)) mods |= HotkeyHost.MOD_CONTROL;
            if (binding.HasFlag(Keys.Alt)) mods |= HotkeyHost.MOD_ALT;
            if (binding.HasFlag(Keys.Shift)) mods |= HotkeyHost.MOD_SHIFT;
            if (_hotkeys.Register(mods, binding & Keys.KeyCode, handlers[name])) continue;
            _settings.HotkeyErrors[name] = "已被其他程序占用";
            Toast.Error($"{label}快捷键 {HotkeyDefinitions.Format(binding)} 已被占用");
        }
    }
    private void CustomCapture()
    {
        using var dialog = new CaptureRegionForm(_settings);
        if (dialog.ShowDialog() != DialogResult.OK) return;
        var bounds = Rectangle.Intersect(dialog.RegionBounds, SystemInformation.VirtualScreen);
        if (bounds.Width < 1 || bounds.Height < 1) { Toast.Error("区域不在屏幕范围内"); return; }
        StartCapture(CaptureMode.Snip, preset: bounds);
    }
    private void StartCapture(CaptureMode mode, bool forceCopy = false, Rectangle? preset = null)
    {
        if (_overlay is { IsDisposed: false, Visible: true }) { _overlay.Activate(); return; }
        _overlay?.Dispose();
        try
        {
            var overlay = new CaptureForm(SystemInformation.VirtualScreen, mode, _settings, preset); _overlay = overlay;
            overlay.Done += result =>
            {
                if (result is null || !result.KeepOpen) if (ReferenceEquals(_overlay, overlay)) _overlay = null;
                if (result is not null) HandleResult(result, mode, forceCopy);
            };
            overlay.FormClosed += (_, _) => { if (ReferenceEquals(_overlay, overlay)) _overlay = null; };
            overlay.Show();
        }
        catch (Exception ex) { _overlay?.Dispose(); _overlay = null; Program.Report(ex); }
    }
    private void HandleResult(CaptureResult result, CaptureMode mode, bool forceCopy)
    {
        if (mode == CaptureMode.Ocr) { _ = RunOcrAsync(result.Image); return; }
        var transferred = false;
        try
        {
            _history.Add(result.Image, result.Bounds, _settings.HistoryImageLimit);
            _settings.RecentRegions.RemoveAll(region => region.Bounds == result.Bounds);
            _settings.RecentRegions.Insert(0, new(result.Bounds.X, result.Bounds.Y, result.Bounds.Width, result.Bounds.Height));
            _settings.RecentRegions = _settings.RecentRegions.Take(_settings.HistoryRegionLimit).ToList(); _settings.Save();
            if (_settings.AutoSave) Services.SaveCaptureFile(result.Image, _settings, _settings.SaveDirectory);
            if (result.Pin || result.Action == CaptureAction.Pin)
            { PinImage(result.Image, result.Bounds); transferred = true; }
            else if (result.Action == CaptureAction.QuickSave)
            {
                var path = Services.SaveCaptureFile(result.Image, _settings, _settings.QuickSaveDirectory);
                if (_settings.QuickSaveToast) Toast.Show("已保存：" + Path.GetFileName(path));
                if (_settings.SoundOnSave) Services.PlaySound(_settings.CaptureSound);
            }
            else if (forceCopy || result.Action == CaptureAction.Copy || _settings.CopyAfterCapture)
            {
                Services.CopyImage(result.Image, _settings.CaptureBitmapClipboard);
                if (_settings.SoundOnCopy) Services.PlaySound(_settings.CaptureSound);
                Toast.Show($"已复制  {result.Image.Width} × {result.Image.Height}");
            }
        }
        catch (Exception ex) { AppLog.Write("截图输出失败", ex); Toast.Error("截图输出失败：" + ex.Message); }
        finally { if (!transferred) result.Image.Dispose(); }
    }
    private void PinImage(Bitmap image, Rectangle? bounds = null)
    {
        Point? point = _settings.PinAtCapturePosition && bounds is Rectangle r ? new Point(r.X + r.Width / 2, r.Y + r.Height / 2) : null;
        var pin = new PinForm(image, point, _settings.PinOpacityPercent, _settings); pin.Show();
    }
    private async Task RunOcrAsync(Bitmap image)
    {
        using (image)
        {
            var text = await Task.Run(() => Services.OcrAsync(image, _settings.OcrLanguage, _settings.OcrEngine));
            if (string.IsNullOrWhiteSpace(text)) { Toast.Show("没有识别到文字"); return; }
            try { Clipboard.SetText(text); Toast.Show("文字已复制"); } catch (Exception ex) { Toast.Error("复制失败：" + ex.Message); }
        }
    }
    private void PasteFromClipboard()
    {
        if (_overlay is { IsDisposed: false, Visible: true }) return;
        if (PinForm.RestoreHiddenOrFlashing(_settings)) return;
        var sequence = GetClipboardSequenceNumber();
        if (_lastPasteSequence == sequence)
        {
            if (PinForm.RestoreLast(_settings) || PinForm.ActivateExisting()) return;
        }
        _lastPasteSequence = sequence;
        try
        {
            var image = Services.ClipboardImage();
            if (image is not null) { PinImage(image); return; }
            if (_settings.TextToImage && Clipboard.ContainsText())
            {
                var text = Clipboard.GetText();
                if (!_settings.IgnoreFilePaths || !TextImageRenderer.IsFilePath(text))
                {
                    var html = !_settings.IgnoreHtml && Clipboard.ContainsText(TextDataFormat.Html) ? Clipboard.GetText(TextDataFormat.Html) : null;
                    PinImage(TextImageRenderer.Render(text, html, _settings)); return;
                }
            }
            if (PinForm.RestoreLast(_settings)) return;
            if (_history.Entries.FirstOrDefault() is { } entry) { PinImage(entry.Image(), entry.Bounds); return; }
            Toast.Show("剪贴板中没有可贴出的内容，也没有可恢复的贴图");
        }
        catch (Exception ex) { AppLog.Write("贴图失败", ex); Toast.Error("贴图失败：" + ex.Message); }
    }
    private void Restart()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Application.ExecutablePath) { Arguments = "--wait-for-instance", UseShellExecute = true });
        Exit();
    }
    private void Exit()
    {
        _tray.Visible = false; var icon = _tray.Icon; _tray.Dispose(); icon?.Dispose();
        _overlay?.Close(); _prefs?.Close(); _hotkeys.Dispose(); PinForm.CloseAllForExit(); ExitThread();
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
}
