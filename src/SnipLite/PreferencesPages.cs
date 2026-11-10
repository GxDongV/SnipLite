namespace SnipLite;

internal sealed partial class PreferencesForm
{
    private readonly int[] _subPages = new int[8];
    private readonly HashSet<string> _sectionProperties = new();
    public event Action<bool>? HotkeyEditing;

    private void SubPages(string[] names, params Action[] pages)
    {
        var selected = Math.Clamp(_subPages[_selected], 0, pages.Length - 1);
        var x = _labelX;
        for (var i = 0; i < names.Length; i++)
        {
            var index = i;
            var button = new Button(); StyleButton(button, names[i]);
            button.Location = new Point(x, _y);
            button.BackColor = i == selected ? Page : Field;
            button.FlatAppearance.BorderColor = i == selected ? _s.Accent : FieldEdge;
            button.Click += (_, _) => { _subPages[_selected] = index; ShowTab(_selected); };
            _page.Controls.Add(button); x += button.Width + Px(4);
        }
        _y += Px(44);
        _captions = false;
        pages[selected]();
    }
    private object Value(string name) { _sectionProperties.Add(name); return typeof(AppSettings).GetProperty(name)!.GetValue(_s)!; }
    private void Set(string name, object value)
    {
        Change(() => typeof(AppSettings).GetProperty(name)!.SetValue(_s, value));
        if (name == nameof(_s.DisplayLanguage)) BeginInvoke(() => ShowTab(_selected));
    }
    private void Flag(string label, string name) => Check(label, (bool)Value(name), value => Set(name, value));
    private void Number(string label, string name, int min, int max, string suffix = "")
        => Row(label, MakeNumber(min, max, Convert.ToInt32(Value(name)), 85, value => Set(name, value)), MakeStatic(suffix));
    private void Choice(string label, string name, string[] labels, object[] values)
    {
        var value = Value(name);
        Row(label, MakeChooser(labels, Math.Max(0, Array.IndexOf(values, value)), index => Set(name, values[index])));
    }
    private void ColorRow(string label, string name)
    {
        var button = new Button(); StyleButton(button, "选择颜色");
        button.BackColor = Color.FromArgb((int)Value(name));
        button.ForeColor = button.BackColor.GetBrightness() < .45 ? Color.White : Ink;
        button.Click += (_, _) =>
        {
            using var dialog = new ColorDialog { Color = button.BackColor, FullOpen = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            button.BackColor = dialog.Color;
            button.ForeColor = dialog.Color.GetBrightness() < .45 ? Color.White : Ink;
            Set(name, dialog.Color.ToArgb());
        };
        Row(label, button);
    }
    private void FontRow(string label, string family, string size)
    {
        Value(family); Value(size);
        var button = new Button(); StyleButton(button, $"{Value(family)}, {Value(size)}");
        button.Width = Math.Min(Px(320), FillWidth());
        button.Click += (_, _) =>
        {
            using var initial = new Font((string)Value(family), Convert.ToSingle(Value(size)));
            using var dialog = new FontDialog { Font = initial, MinSize = family == nameof(_s.InterfaceFont) ? 7 : 6, MaxSize = family == nameof(_s.InterfaceFont) ? 14 : 100, ShowEffects = false, FontMustExist = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            typeof(AppSettings).GetProperty(family)!.SetValue(_s, dialog.Font.Name);
            typeof(AppSettings).GetProperty(size)!.SetValue(_s, dialog.Font.SizeInPoints);
            if (family == nameof(_s.InterfaceFont)) Font = _s.CreateInterfaceFont();
            button.Text = $"{dialog.Font.Name}, {dialog.Font.SizeInPoints:0.#}";
            Persist();
        };
        Row(label, button);
    }
    private void FolderRow(string label, string name)
    {
        var text = MakeText((string)Value(name), 0); text.ReadOnly = true;
        var change = new Button(); StyleButton(change, "更改");
        change.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { SelectedPath = text.Text };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            text.Text = dialog.SelectedPath; Set(name, dialog.SelectedPath);
        };
        var open = new Button(); StyleButton(open, "打开"); open.Click += (_, _) => Open(text.Text, false);
        RowFill(label, text, open, change);
    }
    private void GeneralPage()
    {
        Choice("显示语言", nameof(_s.DisplayLanguage), new[] { "简体中文", "English" }, new object[] { "zh-CN", "en-US" });
        Choice("识别引擎", nameof(_s.OcrEngine), new[] { "本地增强（中英自动识别）", "Windows 系统识别" }, new object[] { OcrEngineKind.SimdPaddle, OcrEngineKind.Windows });
        Choice("系统识别语言", nameof(_s.OcrLanguage), new[] { "简体中文", "English", "跟随系统" }, new object[] { "zh-Hans", "en-US", "" });
        Value(nameof(_s.StartWithWindows)); Value(nameof(_s.StartAsAdministrator));
        Check("开机启动", _s.StartWithWindows, value =>
        {
            if (Startup.TryApply(value, _s.StartAsAdministrator)) Set(nameof(_s.StartWithWindows), value);
            else BeginInvoke(() => ShowTab(_selected));
        });
        Check("以管理员身份开机启动", _s.StartAsAdministrator, value =>
        {
            if (!_s.StartWithWindows || Startup.TryApply(true, value)) Set(nameof(_s.StartAsAdministrator), value);
            else BeginInvoke(() => ShowTab(_selected));
        });
        Flag("自动备份配置文件", nameof(_s.AutoBackup));
        Flag("保持快速响应", nameof(_s.KeepResponsive));
        Flag("增强版托盘菜单", nameof(_s.EnhancedTrayMenu));
        Choice("日志级别", nameof(_s.LogLevel), new[] { "关闭", "仅错误", "普通", "调试" }, new object[] { "Off", "Error", "Normal", "Debug" });
        AddButton(_controlX, "打开日志文件", () => OpenFile(AppLog.FilePath));
        var path = MakeText(AppSettings.FilePath, FillWidth()); path.ReadOnly = true;
        Row("配置文件", path);
        var openFolder = new Button(); StyleButton(openFolder, "打开所在文件夹"); openFolder.Click += (_, _) => Open(AppSettings.FilePath, true);
        var open = new Button(); StyleButton(open, "打开"); open.Click += (_, _) => OpenFile(AppSettings.FilePath);
        var change = new Button(); StyleButton(change, "更改"); change.Click += (_, _) =>
        {
            using var dialog = new SaveFileDialog { Filter = "JSON 配置|*.json", FileName = Path.GetFileName(AppSettings.FilePath), InitialDirectory = Path.GetDirectoryName(AppSettings.FilePath) };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try { _s.MoveConfiguration(dialog.FileName); path.Text = AppSettings.FilePath; }
            catch (Exception ex) { Toast.Error("配置位置修改失败：" + ex.Message); }
        };
        Row("", openFolder, open, change);
    }
    private void InterfacePage()
    {
        FontRow("界面字体", nameof(_s.InterfaceFont), nameof(_s.InterfaceFontSize));
        ColorRow("主题颜色", nameof(_s.AccentArgb));
        Choice("托盘图标", nameof(_s.TrayIconStyle), new[] { "自动", "彩色", "自定义" }, new object[] { "Auto", "Color", "Custom" });
        Value(nameof(_s.CustomTrayIcon));
        AddButton(_controlX, "选择图标文件…", () =>
        {
            using var dialog = new OpenFileDialog { Filter = "图标与图片|*.ico;*.png;*.bmp", CheckFileExists = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                using var preview = TrayIcons.LoadCustom(dialog.FileName);
                _s.CustomTrayIcon = dialog.FileName; Set(nameof(_s.TrayIconStyle), "Custom"); ShowTab(_selected);
            }
            catch (Exception ex) { Toast.Error("图标无法读取：" + ex.Message); }
        });
        HintText("自定义图标仅在本机读取，不会复制或上传。");
    }
    private void MagnifierPage()
    {
        Choice("颜色值", nameof(_s.ColorHex), new[] { "RGB", "HEX" }, new object[] { false, true });
        Flag("隐藏 #", nameof(_s.ColorHideHash)); Flag("Hex 颜色值大写", nameof(_s.ColorUppercase));
        Flag("显示操作提示", nameof(_s.MagnifierHints));
        Flag("选区前自动显示放大镜", nameof(_s.AutoMagnifier));
        Number("放大镜倍数", nameof(_s.MagnifierZoom), 2, 16, "倍");
        HintText("截屏时按住 Alt 显示放大镜，滚轮调整倍数。");
    }
    private void CaptureDisplayPage()
    {
        Number("边框宽度", nameof(_s.CaptureBorderWidth), 1, 10, "像素");
        ColorRow("遮罩颜色", nameof(_s.CaptureMaskColor));
        Number("遮罩不透明度", nameof(_s.MaskOpacity), 0, 200, "/ 255");
        Flag("显示锚点", nameof(_s.CaptureAnchors)); ColorRow("锚点描边颜色", nameof(_s.CaptureAnchorColor));
        Flag("放大镜中显示遮罩", nameof(_s.MagnifierMask)); Flag("放大镜中显示边框", nameof(_s.MagnifierBorder));
        Flag("放大镜中显示锚点", nameof(_s.MagnifierAnchors));
        Flag("显示全屏十字线", nameof(_s.ShowCrosshair)); Flag("显示快捷键提示", nameof(_s.CaptureShortcutHints));
    }
    private void CaptureBehaviorPage()
    {
        Flag("自动检测窗口", nameof(_s.DetectWindows)); Flag("隐藏 1×1 提示", nameof(_s.HideOnePixelHint));
        Flag("自动检测界面元素", nameof(_s.DetectElements));
        Flag("捕捉鼠标指针", nameof(_s.CaptureCursor)); Flag("完全禁用鼠标指针捕捉", nameof(_s.DisableCursorCapture));
        Flag("其他程序窗口激活时退出截屏", nameof(_s.ExitCaptureOnDeactivate));
        Number("最大历史截屏区域数", nameof(_s.HistoryRegionLimit), 0, 100); Flag("循环历史区域", nameof(_s.CycleHistoryRegions));
        Number("最大历史记录数", nameof(_s.HistoryImageLimit), 0, 100);
        Flag("截图后自动复制", nameof(_s.CopyAfterCapture));
        var sound = MakeText((string)Value(nameof(_s.CaptureSound)), 0);
        sound.TextChanged += (_, _) => Set(nameof(_s.CaptureSound), sound.Text);
        var choose = new Button(); StyleButton(choose, "更改"); choose.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = "WAV 音效|*.wav" };
            if (dialog.ShowDialog(this) == DialogResult.OK) sound.Text = dialog.FileName;
        };
        var play = new Button(); StyleButton(play, "试听"); play.Click += (_, _) => Services.PlaySound(sound.Text);
        RowFill("音效文件", sound, play, choose);
        Flag("复制到剪贴板时播放音效", nameof(_s.SoundOnCopy)); Flag("快速保存时播放音效", nameof(_s.SoundOnSave));
        HintText("Tab / Shift+Tab 切换历史区域；历史图像可从托盘菜单恢复。");
    }
    private void ActionRow(string label, string action, string exit)
    {
        var values = new object[] { CaptureAction.Copy, CaptureAction.Pin, CaptureAction.QuickSave, CaptureAction.None };
        var chooser = MakeChooser(new[] { "复制到剪贴板", "贴到屏幕", "快速保存", "无操作" }, Array.IndexOf(values, Value(action)), i => Set(action, values[i]));
        var check = new CheckBox { Text = "然后退出截屏", AutoSize = true, Checked = (bool)Value(exit) };
        check.CheckedChanged += (_, _) => Set(exit, check.Checked);
        Row(label, chooser, check);
    }
    private void CaptureKeysPage()
    {
        Flag("禁用鼠标滚轮功能", nameof(_s.DisableCaptureWheel)); Flag("按 Esc 时弹出确认对话框", nameof(_s.ConfirmEscape));
        Flag("忽略其他程序模拟的复制命令", nameof(_s.IgnoreInjectedCopy));
        Flag("截屏时屏蔽左 Win 键", nameof(_s.BlockLeftWindows)); Flag("截屏时屏蔽右 Win 键", nameof(_s.BlockRightWindows));
        ActionRow("左键双击", nameof(_s.DoubleClickAction), nameof(_s.DoubleClickExit));
        ActionRow("中键单击", nameof(_s.MiddleClickAction), nameof(_s.MiddleClickExit));
        ActionRow("回车键", nameof(_s.EnterAction), nameof(_s.EnterExit));
        HintText("鼠标右键直接取消截图；Esc 按当前选项决定是否确认。");
    }
    private void PinDisplayPage()
    {
        Flag("窗口阴影", nameof(_s.PinShadow)); ColorRow("阴影颜色", nameof(_s.PinShadowColor));
        Flag("在右键菜单显示助记键", nameof(_s.PinMenuShortcuts));
        Number("默认不透明度", nameof(_s.PinOpacityPercent), 10, 100, "%");
        Number("最大窗口尺寸", nameof(_s.PinMaxSize), 100, 20000, "像素");
        Number("快速缩略图宽度", nameof(_s.ThumbnailWidth), 20, 1000, "像素");
        Number("快速缩略图高度", nameof(_s.ThumbnailHeight), 20, 1000, "像素");
    }
    private void PinBehaviorPage()
    {
        Flag("使用截屏时的位置", nameof(_s.PinAtCapturePosition));
        Flag("使用鼠标滚轮缩放", nameof(_s.PinWheelZoom)); Flag("以鼠标指针为中心缩放", nameof(_s.PinZoomAtCursor));
        Flag("平滑缩放", nameof(_s.PinSmoothScaling)); Flag("缩略图接受重置操作", nameof(_s.ThumbnailAcceptReset));
        Flag("边框闪烁时使窗口半透明", nameof(_s.PinTranslucentFlash));
        Flag("切换分组时自动闪烁贴图", nameof(_s.FlashOnGroupSwitch)); Flag("闪烁时按贴图键隐藏所有贴图", nameof(_s.HidePinsDuringFlash));
        Flag("拖放操作时保留原始图", nameof(_s.KeepOriginalOnDrag));
        Flag("贴出新图片时闪烁", nameof(_s.FlashNewPin)); Flag("贴出新图片时激活", nameof(_s.ActivateNewPin));
        Number("可恢复的贴图数", nameof(_s.RecoverablePins), 0, 20);
        HintText("Alt+拖动可将图片拖到其他应用；右键可切换分组、缩略图和鼠标穿透。");
    }
    private void PinConversionPage()
    {
        Flag("文字转图片", nameof(_s.TextToImage)); Flag("总是忽略 HTML 格式", nameof(_s.IgnoreHtml));
        Flag("不把文件路径转化成贴图", nameof(_s.IgnoreFilePaths));
        Number("页边距", nameof(_s.TextImageMargin), 0, 200, "像素"); Number("图片最大宽度", nameof(_s.TextImageMaxWidth), 100, 12000, "像素");
        FontRow("字体", nameof(_s.TextImageFont), nameof(_s.TextImageFontSize));
        ColorRow("背景颜色", nameof(_s.TextImageBackground)); ColorRow("文本颜色", nameof(_s.TextImageForeground));
        AddButton(_controlX, "预览文字贴图", () =>
        {
            using var image = TextImageRenderer.Render("预览\nAaOo012", null, _s);
            using var preview = new Form { Text = "文字贴图预览", ClientSize = image.Size, StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false };
            preview.Controls.Add(new PictureBox { Image = image, Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom }); preview.ShowDialog(this);
        });
    }
    private void OutputFilePage()
    {
        Number("JPEG 图像质量", nameof(_s.JpegQuality), 30, 100);
        var pattern = MakeText((string)Value(nameof(_s.FileNamePattern)), FillWidth());
        var preview = MakeStatic(_s.ExpandFileName(DateTime.Now) + ".png");
        pattern.TextChanged += (_, _) => { Set(nameof(_s.FileNamePattern), pattern.Text); preview.Text = Services.ImageFileName(_s, ".png"); };
        Row("文件名", pattern); Row("预览", preview);
        Flag("记住上一次使用的图片扩展名", nameof(_s.RememberImageExtension));
        Flag("快速保存时显示通知", nameof(_s.QuickSaveToast)); FolderRow("快速保存路径", nameof(_s.QuickSaveDirectory));
        Flag("自动保存", nameof(_s.AutoSave)); FolderRow("自动保存路径", nameof(_s.SaveDirectory));
        HintText("命名支持 $yyyy$ $MM$ $dd$ $HH$ $mm$ $ss$ 和 $yyyy-MM-dd_HH-mm-ss$。");
    }
    private void OutputClipboardPage()
    {
        Flag("截屏：同时复制 Windows Bitmap 格式", nameof(_s.CaptureBitmapClipboard));
        Flag("贴图：同时复制 Windows Bitmap 格式", nameof(_s.PinBitmapClipboard));
        HintText("始终保留 PNG 格式；Bitmap 用于兼容只接受位图的应用。");
    }
    private void HotkeysPage()
    {
        Flag("启用全局快捷键", nameof(_s.HotkeysEnabled));
        foreach (var (label, name) in HotkeyDefinitions.Items)
        {
            var box = new HotkeyBox { Binding = (Keys)Value(name), Width = Px(240), Font = Font };
            var status = MakeStatic(""); status.ForeColor = Muted;
            box.Enter += (_, _) => HotkeyEditing?.Invoke(true);
            box.Leave += (_, _) =>
            {
                HotkeyEditing?.Invoke(false);
                status.Text = _s.HotkeyErrors.GetValueOrDefault(name, box.Binding == Keys.None ? "未设置" : "可用");
                status.ForeColor = _s.HotkeyErrors.ContainsKey(name) ? Color.Firebrick : Color.SeaGreen;
            };
            box.BindingChanged += value =>
            {
                if (value != Keys.None && HotkeyDefinitions.Items.Any(item => item.Name != name && (Keys)typeof(AppSettings).GetProperty(item.Name)!.GetValue(_s)! == value))
                { status.Text = "与其他功能重复"; status.ForeColor = Color.Firebrick; return; }
                Set(name, value); status.Text = value == Keys.None ? "未设置" : "移开焦点后生效";
            };
            Row(label, box, status);
        }
        HintText("点击输入框后按快捷键；Backspace / Delete 清除。外部占用会显示原因。");
    }
    private void ModifierRow(string label, string property)
        => Choice(label, property, new[] { "无", "Ctrl", "Alt", "Shift", "Ctrl+Shift", "Ctrl+Alt" },
            new object[] { Keys.None, Keys.Control, Keys.Alt, Keys.Shift, Keys.Control | Keys.Shift, Keys.Control | Keys.Alt });
    private void MouseRow(string label, string name)
    {
        var binding = (MouseBinding)Value(name);
        var modifiers = new[] { Keys.None, Keys.Control, Keys.Alt, Keys.Shift, Keys.Control | Keys.Shift, Keys.Control | Keys.Alt };
        var gestures = Enum.GetValues<PointerGesture>();
        var mod = MakeChooser(new[] { "无", "Ctrl", "Alt", "Shift", "Ctrl+Shift", "Ctrl+Alt" }, Math.Max(0, Array.IndexOf(modifiers, binding.Modifiers)), i =>
        { binding = binding with { Modifiers = modifiers[i] }; Set(name, binding); });
        mod.Width = Px(115);
        var action = MakeChooser(new[] { "无", "左键双击", "中键单击", "右键单击", "左键单击" }, Array.IndexOf(gestures, binding.Gesture), i =>
        { binding = binding with { Gesture = gestures[i] }; Set(name, binding); });
        Row(label, mod, MakeStatic("+"), action);
    }
    private void MousePage()
    {
        ModifierRow("贴图缩放（滚轮 / +/-）", nameof(_s.PinZoomModifiers));
        ModifierRow("调整透明度（滚轮 / +/-）", nameof(_s.PinOpacityModifiers));
        MouseRow("关闭贴图", nameof(_s.PinCloseMouse)); MouseRow("重置贴图", nameof(_s.PinResetMouse));
        MouseRow("快速缩略图", nameof(_s.PinThumbnailMouse)); MouseRow("复制文本", nameof(_s.PinCopyTextMouse));
        HintText("冲突时按上方功能优先匹配；可在任意页面恢复当前分组默认值。");
    }
    private static void OpenFile(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (!File.Exists(path)) File.WriteAllText(path, "");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { Toast.Error("打开文件失败：" + ex.Message); }
    }
}
