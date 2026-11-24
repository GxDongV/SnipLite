using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SnipLite.Plugin.Abstractions;

namespace SnipLite.Plugins;

internal sealed class PluginImagePreview : Form
{
    private readonly string _previewTitle = PluginUi.Text("Image preview", "图像预览", "影像預覽");
    private readonly PluginImage _input;
    private readonly ZoomImageViewport _viewport = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _top = new() { Minimum = 0 };
    private readonly NumericUpDown _bottom = new() { Minimum = 0 };
    private readonly ToolStrip _tools = new()
    {
        Dock = DockStyle.Top,
        GripStyle = ToolStripGripStyle.Hidden,
        ShowItemToolTips = true,
        CanOverflow = false
    };
    private readonly FlowLayoutPanel _cropPanel = new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Visible = false };
    private readonly FluentButton _cropAction = new() { AutoSize = false };
    private readonly ContextMenuStrip _imageMenu;
    private readonly Font _interfaceFont;
    private Bitmap? _cropped;
    private bool _shown;
    private bool _busy;

    private Bitmap Current => _cropped ?? _input.Image;

    public PluginImagePreview(PluginImage input, AppSettings settings, Action<PluginImage, bool> publish,
        IPluginHost host, PluginManager? manager, CancellationToken cancellationToken)
    {
        _input = input;
        AutoScaleMode = AutoScaleMode.None;
        _interfaceFont = settings.CreateInterfaceFont();
        Font = _interfaceFont;
        ClientSize = new Size(900, 720);
        MinimumSize = new Size(550, 360);
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        KeyPreview = true;

        void AddButton(string icon, string text, Action action)
        {
            var button = new PreviewButton(icon)
            {
                Name = icon,
                Text = text,
                AccessibleName = text,
                ToolTipText = text,
                DisplayStyle = ToolStripItemDisplayStyle.Image
            };
            button.Click += (_, _) => RunAction(action);
            _tools.Items.Add(button);
        }

        AddButton("copy", PluginUi.Text("Copy (Ctrl+C)", "复制（Ctrl+C）", "複製（Ctrl+C）"), () => Publish(false));
        AddButton("save", PluginUi.Text("Save… (Ctrl+S)", "保存…（Ctrl+S）", "儲存…（Ctrl+S）"), () =>
        {
            using var dialog = new SaveFileDialog
            {
                FileName = settings.ExpandFileName(DateTime.Now) + ".png",
                Filter = "PNG|*.png|JPEG|*.jpg;*.jpeg",
                AddExtension = true,
                DefaultExt = "png"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            var extension = Path.GetExtension(dialog.FileName);
            var jpeg = extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
            Services.SaveImage(Current, dialog.FileName, jpeg, settings.JpegQuality);
        });
        _tools.Items.Add(new ToolStripSeparator());
        AddButton("fit-width", PluginUi.Text("Fit width", "适应宽度", "適應寬度"), () => _viewport.Fit(ImageFitMode.Width));
        AddButton("fit-image", PluginUi.Text("Fit image", "适应窗口", "適應視窗"), () => _viewport.Fit(ImageFitMode.Image));
        AddButton("actual", PluginUi.Text("Actual size (Ctrl+0)", "原尺寸（Ctrl+0）", "原尺寸（Ctrl+0）"), () => _viewport.SetZoom(1));
        AddButton("zoom-out", PluginUi.Text("Zoom out", "缩小", "縮小"), () => _viewport.SetZoom(_viewport.Zoom / 1.2));
        AddButton("zoom-in", PluginUi.Text("Zoom in", "放大", "放大"), () => _viewport.SetZoom(_viewport.Zoom * 1.2));

        void CropInput(string title, NumericUpDown number)
        {
            var label = new Label { Text = title, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
            number.AccessibleName = title;
            _cropPanel.Controls.Add(label);
            _cropPanel.Controls.Add(new FluentInputFrame(number));
        }

        CropInput(PluginUi.Text("Top (px)", "顶部（像素）", "頂部（像素）"), _top);
        CropInput(PluginUi.Text("Bottom (px)", "底部（像素）"), _bottom);
        _cropAction.Text = PluginUi.Text("Apply crop", "应用裁剪", "套用裁剪");
        _cropAction.Click += (_, _) => RunAction(Crop);
        _cropPanel.Controls.Add(_cropAction);

        _imageMenu = SnipMenu.Create(settings.CreateInterfaceFont());
        _imageMenu.Items.Add(SnipMenu.Item(PluginUi.Text("Pin", "贴图", "釘選"), () => RunAction(() => Publish(true)), iconId: "pin"));
        _imageMenu.Items.Add(SnipMenu.Item(PluginUi.Text("Crop top / bottom", "裁剪顶部 / 底部", "裁剪頂部 / 底部"),
            () => _cropPanel.Visible = !_cropPanel.Visible, iconId: "crop"));
        if (manager is not null && (manager.Processors.Count > 0 || manager.Outputs.Count > 0))
        {
            _imageMenu.Items.Add(new ToolStripSeparator());
            foreach (var (_, processor) in manager.Processors)
                _imageMenu.Items.Add(SnipMenu.Item(processor.Title.Get(host.Language), () => RunExtension(async () =>
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
                _imageMenu.Items.Add(SnipMenu.Item(output.Title.Get(host.Language), () => RunExtension(async () =>
                {
                    using var borrowed = new PluginImage(new Bitmap(Current), input.SourceBounds);
                    await output.ExportAsync(borrowed, host, cancellationToken);
                }), iconId: "output"));
        }

        _viewport.ContextMenuStrip = _imageMenu;
        Controls.Add(_viewport);
        Controls.Add(_cropPanel);
        Controls.Add(_tools);
        _viewport.ViewChanged += UpdateZoomButtons;
        FormClosing += (_, e) => { if (_busy && !cancellationToken.IsCancellationRequested) e.Cancel = true; };
        host.StyleWindow(this);
        _viewport.AccessibleName = _previewTitle;
        UpdateCropLimits();
        _viewport.SetImage(Current);
        ApplyDpiLayout();

        void Publish(bool pin)
        {
            using var borrowed = new PluginImage(new Bitmap(Current), input.SourceBounds);
            publish(borrowed, pin);
        }

        void RunAction(Action action)
        {
            if (_busy) return;
            try { action(); }
            catch (Exception ex)
            {
                host.Log("Image preview action failed.", ex);
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        async void RunExtension(Func<Task> action)
        {
            if (_busy) return;
            _busy = true;
            _tools.Enabled = _cropPanel.Enabled = _viewport.Enabled = false;
            try { await action(); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                host.Log("Preview extension failed.", ex);
                if (!IsDisposed) MessageBox.Show(this, ex.Message, Text);
            }
            finally
            {
                _busy = false;
                if (!IsDisposed) _tools.Enabled = _cropPanel.Enabled = _viewport.Enabled = true;
            }
        }
    }

    private int Px(int value) => (int)Math.Round(value * DeviceDpi / 96f);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!_shown)
        {
            ClientSize = new Size(Px(900), Px(720));
            ApplyDpiLayout();
        }

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            const int borderColor = 34;
            uint noBorder = 0xfffffffe;
            DwmSetWindowAttribute(Handle, borderColor, in noBorder, sizeof(uint));
        }
    }

    protected override void OnShown(EventArgs e)
    {
        _shown = true;
        base.OnShown(e);
        Refresh();
        Activate();
        SetForegroundWindow(Handle);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (_shown) TopMost = false;
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyDpiLayout();
    }

    private void ApplyDpiLayout()
    {
        MinimumSize = new Size(Px(550), Px(360));
        _tools.PerformLayout();
        _cropPanel.SuspendLayout();
        try
        {
            _cropPanel.Padding = new Padding(Px(8));
            foreach (Control control in _cropPanel.Controls)
                control.Margin = new Padding(Px(4), Px(control is Label ? 7 : 3), Px(8), Px(3));
            foreach (var frame in _cropPanel.Controls.OfType<FluentInputFrame>())
                frame.Size = new Size(Px(94), Px(32));
            _cropAction.Size = new Size(TextRenderer.MeasureText(_cropAction.Text, _cropAction.Font).Width + Px(24), Px(32));
        }
        finally { _cropPanel.ResumeLayout(true); }
    }

    private void UpdateZoomButtons()
    {
        _tools.Items["zoom-in"]!.Enabled = _viewport.Zoom < ZoomImageViewport.MaxZoom - .00001;
        _tools.Items["zoom-out"]!.Enabled = _viewport.Zoom > ZoomImageViewport.MinZoom + .00001;
    }

    private void UpdateCropLimits()
    {
        _top.Value = _bottom.Value = 0;
        _top.Maximum = _bottom.Maximum = Math.Max(0, Current.Height - 1);
        Text = $"{_previewTitle} — {Current.Width} × {Current.Height}";
    }

    private void Crop()
    {
        var top = (int)_top.Value;
        var bottom = (int)_bottom.Value;
        if (top + bottom == 0 || top + bottom >= Current.Height) return;
        var next = Current.Clone(new Rectangle(0, top, Current.Width, Current.Height - top - bottom), PixelFormat.Format32bppArgb);
        _viewport.ClearImage();
        _cropped?.Dispose();
        _cropped = next;
        UpdateCropLimits();
        _viewport.SetImage(Current);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_busy) return base.ProcessCmdKey(ref msg, keyData);
        switch (keyData)
        {
            case Keys.Escape:
                Close();
                return true;
            case Keys.Control | Keys.C:
                _tools.Items["copy"]!.PerformClick();
                return true;
            case Keys.Control | Keys.S:
                _tools.Items["save"]!.PerformClick();
                return true;
            case Keys.Control | Keys.D0:
            case Keys.Control | Keys.NumPad0:
                _viewport.SetZoom(1);
                return true;
        }

        if (!_top.ContainsFocus && !_bottom.ContainsFocus)
        {
            var key = keyData & Keys.KeyCode;
            if ((key is Keys.Add or Keys.Oemplus or Keys.Subtract or Keys.OemMinus) &&
                (keyData & Keys.Modifiers) is Keys.None or Keys.Control or Keys.Shift)
            {
                _viewport.SetZoom(_viewport.Zoom * (key is Keys.Add or Keys.Oemplus ? 1.2 : 1 / 1.2));
                return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _viewport.ClearImage();
            _cropped?.Dispose();
            _cropped = null;
            _imageMenu.Dispose();
        }
        base.Dispose(disposing);
        if (disposing) _interfaceFont.Dispose();
    }

    private sealed class PreviewButton(string icon) : ToolStripButton
    {
        private int Px(int value) => (int)Math.Round(value * (Owner?.DeviceDpi ?? 96) / 96f);

        public override Size GetPreferredSize(Size constrainingSize) => new(Px(34), Px(32));

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var theme = AppTheme.Current;
            var color = theme.HighContrast && Selected && Enabled ? SystemColors.HighlightText
                : Enabled ? theme.Text : theme.Disabled;
            var size = Px(18);
            FluentIcons.Draw(e.Graphics, icon, new Rectangle((Width - size) / 2, (Height - size) / 2, size, size), color);
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, in uint value, int size);
}
