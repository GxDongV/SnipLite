using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SnipLite.Plugin.Abstractions;

namespace SnipLite.Plugins;

internal sealed class PluginImagePreview : Form
{
    private const int WmNcCalcSize = 0x0083;
    private const int WmNcHitTest = 0x0084;
    private const int CaptionHeight = 36;
    private const int CaptionButtonWidth = 34;
    private const int ResizeGrip = 6;
    private readonly string _previewTitle = PluginUi.Text("Image preview", "图像预览", "影像預覽");
    private readonly FluentToolbarButton _minimize = new() { IconId = "minimize" };
    private readonly FluentToolbarButton _maximize = new() { IconId = "maximize" };
    private readonly FluentToolbarButton _close = new() { IconId = "close" };
    private readonly PluginImage _input;
    private readonly ZoomImageViewport _viewport = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _top = new() { Minimum = 0 };
    private readonly NumericUpDown _bottom = new() { Minimum = 0 };
    private readonly FluentToolbar _tools = new() { Dock = DockStyle.Top };
    private readonly FlowLayoutPanel _cropPanel = new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Visible = false };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolTip _tips = new();
    private readonly Font _interfaceFont;
    private readonly Dictionary<string, FluentToolbarButton> _buttons = new();
    private readonly List<Control> _dividers = new();
    private readonly List<Control> _cropFrames = new();
    private readonly FluentButton _cropAction = new() { AutoSize = false };
    private Bitmap? _cropped;
    private bool _busy;
    private Bitmap Current => _cropped ?? _input.Image;

    public PluginImagePreview(PluginImage input, AppSettings settings, Action<PluginImage, bool> publish,
        IPluginHost host, PluginManager? manager, CancellationToken cancellationToken)
    {
        _input = input;
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        DoubleBuffered = true;
        ResizeRedraw = true;
        _interfaceFont = settings.CreateInterfaceFont(); Font = _interfaceFont;
        ClientSize = new Size(900, 720); MinimumSize = new Size(640, 450);
        StartPosition = FormStartPosition.CenterScreen; KeyPreview = true;

        void Button(string id, string icon, string en, string zh, Action action)
        {
            var title = PluginUi.Text(en, zh);
            var button = new FluentToolbarButton { IconId = icon, Text = title, AccessibleName = title, Tag = "localization:native" };
            _tips.SetToolTip(button, title);
            button.Click += (_, _) =>
            {
                if (_busy) return;
                try { action(); }
                catch (Exception ex) { host.Log("Image preview action failed.", ex); MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
            };
            _buttons.Add(id, button); _tools.Controls.Add(button);
        }
        void Divider()
        {
            var divider = new FluentToolbarDivider(); _dividers.Add(divider); _tools.Controls.Add(divider);
        }
        Button("pin", "pin", "Pin", "贴图", () => Publish(true));
        Button("save", "save", "Save… (Ctrl+S)", "保存…（Ctrl+S）", () =>
        {
            using var dialog = new SaveFileDialog { FileName = settings.ExpandFileName(DateTime.Now) + ".png", Filter = "PNG|*.png|JPEG|*.jpg", AddExtension = true };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                Services.SaveImage(Current, dialog.FileName, Path.GetExtension(dialog.FileName).Equals(".jpg", StringComparison.OrdinalIgnoreCase), settings.JpegQuality);
        });
        Button("copy", "copy", "Copy (Ctrl+C)", "复制（Ctrl+C）", () => Publish(false));
        Divider();
        Button("zoom-out", "zoom-out", "Zoom out (−)", "缩小（−）", () => _viewport.SetZoom(_viewport.Zoom / 1.2));
        Button("actual", "", "Actual size (Ctrl+0)", "原尺寸（Ctrl+0）", () => _viewport.SetZoom(1));
        Button("zoom-in", "zoom-in", "Zoom in (+)", "放大（+）", () => _viewport.SetZoom(_viewport.Zoom * 1.2));
        Button("fit-width", "fit-width", "Fit width", "适应宽度", () => _viewport.Fit(ImageFitMode.Width));
        Button("fit-image", "fit-image", "Fit image", "适应窗口", () => _viewport.Fit(ImageFitMode.Image));
        Divider();
        Button("crop", "crop", "Crop top / bottom", "裁剪顶部 / 底部", () =>
        {
            _cropPanel.Visible = !_cropPanel.Visible;
            _buttons["crop"].Selected = _cropPanel.Visible; _buttons["crop"].Invalidate();
        });
        void CropInput(string en, string zh, NumericUpDown number)
        {
            var title = PluginUi.Text(en, zh);
            var label = new Label { Text = title, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
            number.AccessibleName = title;
            var frame = new FluentInputFrame(number);
            _cropFrames.Add(frame); _cropPanel.Controls.Add(label); _cropPanel.Controls.Add(frame);
        }
        CropInput("Top (px)", "顶部（像素）", _top);
        CropInput("Bottom (px)", "底部（像素）", _bottom);
        var crop = _cropAction; crop.Text = PluginUi.Text("Apply crop", "应用裁剪");
        crop.Click += (_, _) =>
        {
            if (_busy) return;
            try { Crop(); }
            catch (Exception ex) { host.Log("Preview crop failed.", ex); MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        _cropPanel.Controls.Add(crop);

        if (manager is not null && (manager.Processors.Count > 0 || manager.Outputs.Count > 0))
        {
            var menu = SnipMenu.Create(); Disposed += (_, _) => menu.Dispose();
            foreach (var (_, processor) in manager.Processors)
                menu.Items.Add(SnipMenu.Item(processor.Title.Get(host.Language), () => Run(async () =>
                {
                    using var borrowed = new PluginImage(new Bitmap(Current), input.SourceBounds);
                    using var result = await processor.ProcessAsync(borrowed, host, cancellationToken);
                    if (result is not null)
                    {
                        if (ReferenceEquals(borrowed.Image, result.Image)) throw new InvalidOperationException("Processor returned its borrowed input.");
                        await host.PreviewImageAsync(result, cancellationToken);
                    }
                }), iconId: "image"));
            foreach (var (_, output) in manager.Outputs)
                menu.Items.Add(SnipMenu.Item(output.Title.Get(host.Language), () => Run(async () =>
                {
                    using var borrowed = new PluginImage(new Bitmap(Current), input.SourceBounds);
                    await output.ExportAsync(borrowed, host, cancellationToken);
                }), iconId: "output"));
            Button("extensions", "plugin", "Extensions…", "扩展工具…", () => menu.Show(_buttons["extensions"], new Point(0, _buttons["extensions"].Height)));
        }
        Controls.Add(_viewport); Controls.Add(_cropPanel); Controls.Add(_tools); Controls.Add(_status);
        AddCaptionButton(_minimize, PluginUi.Text("Minimize", "最小化", "最小化"), () => WindowState = FormWindowState.Minimized);
        AddCaptionButton(_maximize, PluginUi.Text("Maximize", "最大化", "最大化"), ToggleMaximize);
        AddCaptionButton(_close, PluginUi.Text("Close (Esc)", "关闭（Esc）", "關閉（Esc）"), Close);
        _viewport.ViewChanged += UpdateViewStatus;
        FormClosing += (_, e) => { if (_busy && !cancellationToken.IsCancellationRequested) e.Cancel = true; };
        host.StyleWindow(this);
        _viewport.AccessibleName = _previewTitle;
        UpdateCropLimits(); _viewport.SetImage(Current);
        ApplyDpiLayout();

        void Publish(bool pin)
        {
            using var borrowed = new PluginImage(new Bitmap(Current), input.SourceBounds);
            publish(borrowed, pin);
        }
        async void Run(Func<Task> action)
        {
            if (_busy) return;
            _busy = true; _tools.Enabled = _cropPanel.Enabled = _viewport.Enabled = false;
            try { await action(); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception ex) { host.Log("Preview extension failed.", ex); if (!IsDisposed) MessageBox.Show(this, ex.Message, Text); }
            finally { _busy = false; if (!IsDisposed) _tools.Enabled = _cropPanel.Enabled = _viewport.Enabled = true; }
        }
    }

    private void AddCaptionButton(FluentToolbarButton button, string title, Action action)
    {
        button.Text = button.AccessibleName = title;
        _tips.SetToolTip(button, title);
        button.Click += (_, _) => action();
        Controls.Add(button);
    }

    private void ToggleMaximize() =>
        WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;

    private int Px(int value) => (int)Math.Round(value * DeviceDpi / 96f);

    protected override CreateParams CreateParams
    {
        get
        {
            // Keep native resizing and Alt+Space while drawing the whole frame ourselves.
            const int thickFrame = 0x00040000, systemMenu = 0x00080000;
            const int minimizeBox = 0x00020000, maximizeBox = 0x00010000;
            var parameters = base.CreateParams;
            parameters.Style |= thickFrame | systemMenu | minimizeBox | maximizeBox;
            return parameters;
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmNcCalcSize)
        {
            if (IsZoomed(message.HWnd))
            {
                var rect = Marshal.PtrToStructure<WindowRect>(message.LParam);
                var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                var client = Rectangle.Intersect(bounds, Screen.FromRectangle(bounds).WorkingArea);
                if (!client.IsEmpty)
                    Marshal.StructureToPtr(new WindowRect(client.Left, client.Top, client.Right, client.Bottom), message.LParam, false);
            }
            message.Result = 0;
            return;
        }
        base.WndProc(ref message);
        const int clientHit = 1;
        if (message.Msg == WmNcHitTest && message.Result == clientHit)
        {
            var coordinates = message.LParam.ToInt64();
            var point = PointToClient(new Point(unchecked((short)coordinates), unchecked((short)(coordinates >> 16))));
            message.Result = FrameHitTest(point);
        }
    }

    private int FrameHitTest(Point point)
    {
        const int client = 1, caption = 2, left = 10, right = 11, top = 12, bottom = 15;
        if (WindowState == FormWindowState.Normal)
        {
            var grip = Px(ResizeGrip);
            var horizontal = point.X < grip ? -1 : point.X >= ClientSize.Width - grip ? 1 : 0;
            if (point.Y < grip) return horizontal == 0 ? top : top + (horizontal < 0 ? 1 : 2);
            if (point.Y >= ClientSize.Height - grip) return horizontal == 0 ? bottom : bottom + (horizontal < 0 ? 1 : 2);
            if (horizontal != 0) return horizontal < 0 ? left : right;
        }
        return point.Y < Px(CaptionHeight) && point.X < ClientSize.Width - Px(ResizeGrip + 3 * CaptionButtonWidth)
            ? caption : client;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct WindowRect(int Left, int Top, int Right, int Bottom);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(nint window);

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_close?.Parent != this) return;
        var right = ClientSize.Width - Px(ResizeGrip);
        var width = Px(CaptionButtonWidth);
        var y = Px(ResizeGrip);
        var height = Px(CaptionHeight) - y;
        _close.SetBounds(right - width, y, width, height);
        _maximize.SetBounds(right - width * 2, y, width, height);
        _minimize.SetBounds(right - width * 3, y, width, height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var theme = AppTheme.Current;
        var title = new Rectangle(Px(14), Px(4), Math.Max(0, _minimize.Left - Px(24)), Px(30));
        TextRenderer.DrawText(e.Graphics, _previewTitle, Font, title, theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        using var border = new Pen(theme.Border);
        if (WindowState == FormWindowState.Normal)
            e.Graphics.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        e.Graphics.DrawLine(border, Px(7), Px(CaptionHeight), ClientSize.Width - Px(7), Px(CaptionHeight));
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_maximize is null) return;
        var maximized = WindowState == FormWindowState.Maximized;
        var icon = maximized ? "restore" : "maximize";
        if (_maximize.IconId == icon) return;
        var title = maximized ? PluginUi.Text("Restore", "还原", "還原") : PluginUi.Text("Maximize", "最大化", "最大化");
        _maximize.IconId = icon;
        _maximize.Text = _maximize.AccessibleName = title;
        _tips.SetToolTip(_maximize, title);
        _maximize.Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ClientSize = new Size((int)Math.Round(900 * DeviceDpi / 96f), (int)Math.Round(720 * DeviceDpi / 96f));
        ApplyDpiLayout();
    }
    protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); ApplyDpiLayout(); }
    private void ApplyDpiLayout()
    {
        if (_buttons is null) return;
        SuspendLayout(); _tools.SuspendLayout(); _cropPanel.SuspendLayout();
        try
        {
            MinimumSize = new Size(Px(640), Px(450));
            Padding = new Padding(Px(7), Px(CaptionHeight + 1), Px(7), Px(7));
            _tools.Padding = new Padding(Px(4), Px(2), Px(4), Px(2));
            foreach (var (id, button) in _buttons)
            {
                button.Size = new Size(Px(id == "actual" ? 68 : 34), Px(34)); button.Margin = new Padding(0, 0, Px(2), 0);
            }
            foreach (var divider in _dividers) { divider.Size = new Size(Px(17), Px(34)); divider.Margin = Padding.Empty; }
            _cropPanel.Padding = new Padding(Px(8));
            foreach (Control control in _cropPanel.Controls)
                control.Margin = new Padding(Px(4), Px(control is Label ? 7 : 3), Px(8), Px(3));
            foreach (var frame in _cropFrames) frame.Size = new Size(Px(94), Px(32));
            _cropAction.Size = new Size(TextRenderer.MeasureText(_cropAction.Text, _cropAction.Font).Width + Px(24), Px(32));
            _status.Height = Px(26);
        }
        finally { _cropPanel.ResumeLayout(true); _tools.ResumeLayout(true); ResumeLayout(true); }
    }

    private void UpdateViewStatus()
    {
        if (!_buttons.TryGetValue("actual", out var percent)) return;
        percent.Text = _viewport.Zoom.ToString("0.#%", System.Globalization.CultureInfo.InvariantCulture); percent.Invalidate();
        foreach (var (id, mode) in new[] { ("fit-width", ImageFitMode.Width), ("fit-image", ImageFitMode.Image), ("actual", ImageFitMode.Manual) })
        {
            _buttons[id].Selected = _viewport.FitMode == mode && (mode != ImageFitMode.Manual || Math.Abs(_viewport.Zoom - 1) < .0001);
            _buttons[id].Invalidate();
        }
        _buttons["zoom-in"].Enabled = _viewport.Zoom < ZoomImageViewport.MaxZoom - .00001;
        _buttons["zoom-out"].Enabled = _viewport.Zoom > ZoomImageViewport.MinZoom + .00001;
        _status.Text = $"{Current.Width} × {Current.Height} px  ·  {percent.Text}  ·  " + PluginUi.Text("Ctrl + wheel to zoom · Drag to pan", "Ctrl＋滚轮缩放 · 拖动平移");
    }
    private void UpdateCropLimits()
    {
        _top.Value = _bottom.Value = 0;
        _top.Maximum = _bottom.Maximum = Math.Max(0, Current.Height - 1);
        Text = _previewTitle + $" — {Current.Width} × {Current.Height}";
    }
    private void Crop()
    {
        var top = (int)_top.Value; var bottom = (int)_bottom.Value;
        if (top + bottom == 0 || top + bottom >= Current.Height) return;
        var next = Current.Clone(new Rectangle(0, top, Current.Width, Current.Height - top - bottom), PixelFormat.Format32bppArgb);
        _viewport.ClearImage(); _cropped?.Dispose(); _cropped = next;
        UpdateCropLimits(); _viewport.SetImage(Current);
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_busy) return base.ProcessCmdKey(ref msg, keyData);
        if (keyData == Keys.Escape) { Close(); return true; }
        var action = keyData switch { Keys.Control | Keys.C => "copy", Keys.Control | Keys.S => "save", _ => null };
        if (action is not null) { _buttons[action].PerformClick(); return true; }
        if (keyData == (Keys.Control | Keys.D0) || keyData == (Keys.Control | Keys.NumPad0)) { _viewport.SetZoom(1); return true; }
        if (!_top.ContainsFocus && !_bottom.ContainsFocus)
        {
            var key = keyData & Keys.KeyCode;
            if ((key is Keys.Add or Keys.Oemplus or Keys.Subtract or Keys.OemMinus) &&
                (keyData & Keys.Modifiers) is Keys.None or Keys.Control or Keys.Shift)
            {
                _viewport.SetZoom(_viewport.Zoom * (key is Keys.Add or Keys.Oemplus ? 1.2 : 1 / 1.2)); return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _viewport.ClearImage(); _cropped?.Dispose(); _cropped = null; _tips.Dispose(); }
        base.Dispose(disposing);
        if (disposing) _interfaceFont.Dispose();
    }
}
