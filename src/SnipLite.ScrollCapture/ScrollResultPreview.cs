using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SnipLite.Plugin.Abstractions;

namespace SnipLite.ScrollCapture;

internal sealed class ScrollResultPreview : Form
{
    private readonly PluginImage _result;
    private readonly IPluginHost _host;
    private readonly CancellationToken _token;
    private readonly ScrollResultViewport _view;
    private bool _shown;
    private bool _usingHostPreview;

    internal ScrollResultPreview(PluginImage result, IPluginHost host, CancellationToken token)
    {
        _result = result;
        _host = host;
        _token = token;
        Text = T("Scrolling screenshot", "长截图预览", "捲動截圖預覽");
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(900, 720);
        MinimumSize = new Size(550, 360);
        TopMost = true;
        KeyPreview = true;
        _view = new ScrollResultViewport(result.Image) { Dock = DockStyle.Fill };
        var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, AutoSize = true, Dock = DockStyle.Top, ShowItemToolTips = true };
        void Add(string icon, string text, Action action)
        {
            var button = new ScrollPreviewButton(icon, text);
            button.Click += (_, _) => Run(action);
            tools.Items.Add(button);
        }
        Add("copy", T("Copy (Ctrl+C)", "复制（Ctrl+C）", "複製（Ctrl+C）"), CopyResult);
        Add("save", T("Save… (Ctrl+S)", "保存…（Ctrl+S）", "儲存…（Ctrl+S）"), SaveResult);
        tools.Items.Add(new ToolStripSeparator());
        Add("fit-width", T("Fit width", "适应宽度", "適應寬度"), _view.FitWidth);
        Add("fit-image", T("Fit image", "适应窗口", "適應視窗"), _view.FitImage);
        Add("actual", T("Actual size (Ctrl+0)", "原尺寸（Ctrl+0）", "原尺寸（Ctrl+0）"), () => _view.Zoom(1));
        Add("zoom-out", T("Zoom out", "缩小", "縮小"), () => _view.Zoom(_view.ScaleFactor / 1.2));
        Add("zoom-in", T("Zoom in", "放大", "放大"), () => _view.Zoom(_view.ScaleFactor * 1.2));
        tools.Items.Add(new ToolStripSeparator());
        var more = new ScrollPreviewButton("extensions", T("Pin, crop and extensions…", "贴图、裁剪与扩展…", "貼圖、裁剪與擴充…"));
        more.Click += async (_, _) =>
        {
            more.Enabled = false;
            _usingHostPreview = true;
            try { await _host.PreviewImageAsync(_result, _token); }
            catch (OperationCanceledException) when (_token.IsCancellationRequested) { }
            catch (Exception ex) { Report(ex); }
            finally
            {
                _usingHostPreview = false;
                if (_token.IsCancellationRequested) Close();
                else more.Enabled = true;
            }
        };
        tools.Items.Add(more);
        Controls.Add(_view);
        Controls.Add(tools);
        host.StyleWindow(this);
    }

    private string T(string en, string zh, string tw) => new PluginText(en, zh, tw).Get(_host.Language);

    internal void ShowResult()
    {
        _token.ThrowIfCancellationRequested();
        _ = Handle;
        using var registration = _token.Register(() =>
        {
            try { BeginInvoke(Close); }
            catch (InvalidOperationException) { }
        });
        _token.ThrowIfCancellationRequested();
        ShowDialog();
        _token.ThrowIfCancellationRequested();
    }

    protected override void OnShown(EventArgs e)
    {
        _shown = true;
        base.OnShown(e);
        Refresh();
        Activate();
        SetForegroundWindow(Handle);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            const int borderColor = 34;
            uint noBorder = 0xfffffffe;
            DwmSetWindowAttribute(Handle, borderColor, in noBorder, sizeof(uint));
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (_usingHostPreview) e.Cancel = true;
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (_shown) TopMost = false;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Escape: Close(); return true;
            case Keys.Control | Keys.C: Run(CopyResult); return true;
            case Keys.Control | Keys.S: Run(SaveResult); return true;
            case Keys.Control | Keys.D0: _view.Zoom(1); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void CopyResult() => Clipboard.SetImage(_result.Image);

    private void SaveResult()
    {
        using var dialog = new SaveFileDialog
        {
            FileName = $"SnipLite-{DateTime.Now:yyyyMMdd-HHmmss}.png",
            Filter = "PNG|*.png|JPEG|*.jpg;*.jpeg",
            DefaultExt = "png"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var extension = Path.GetExtension(dialog.FileName);
        var jpeg = extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
        _result.Image.Save(dialog.FileName, jpeg ? ImageFormat.Jpeg : ImageFormat.Png);
    }

    private void Run(Action action)
    {
        try { action(); }
        catch (Exception ex) { Report(ex); }
    }

    private void Report(Exception exception)
    {
        _host.Log("Scrolling screenshot preview action failed.", exception);
        if (!IsDisposed) MessageBox.Show(this, exception.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, in uint value, int size);
}

internal sealed class ScrollResultViewport : ScrollableControl
{
    private readonly Bitmap _image;
    private bool _fit = true;
    private bool _fitHeight;
    private bool _layingOut;
    private Point? _dragStart;
    private Point _scrollStart;
    private int Gap => (int)Math.Round(12 * DeviceDpi / 96f);
    internal double ScaleFactor { get; private set; } = 1;
    private Size ImageSize => new(Math.Max(1, (int)Math.Round(_image.Width * ScaleFactor)),
        Math.Max(1, (int)Math.Round(_image.Height * ScaleFactor)));

    public ScrollResultViewport(Bitmap image)
    {
        _image = image;
        AutoScroll = true;
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    internal void FitWidth() => Fit(false);
    internal void FitImage() => Fit(true);
    private void Fit(bool height)
    {
        _fit = true;
        _fitHeight = height;
        AutoScrollPosition = Point.Empty;
        Reflow();
    }

    internal void Zoom(double scale)
    {
        _fit = false;
        ScaleFactor = Math.Clamp(scale, .01, 8);
        Reflow();
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); Reflow(); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); Reflow(); }

    private void Reflow()
    {
        if (_layingOut || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        _layingOut = true;
        try
        {
            if (_fit)
            {
                var width = ClientSize.Width + (VScroll ? SystemInformation.VerticalScrollBarWidth : 0) - 2 * Gap;
                var height = ClientSize.Height + (HScroll ? SystemInformation.HorizontalScrollBarHeight : 0) - 2 * Gap;
                var scale = Math.Min(1, width / (double)_image.Width);
                if (_fitHeight) scale = Math.Min(scale, height / (double)_image.Height);
                else if (_image.Height * scale > height) scale = Math.Min(1, (width - SystemInformation.VerticalScrollBarWidth) / (double)_image.Width);
                ScaleFactor = Math.Max(.01, scale);
            }
            var size = ImageSize;
            AutoScrollMinSize = new Size(size.Width + 2 * Gap, size.Height + 2 * Gap);
        }
        finally { _layingOut = false; }
        Invalidate();
    }

    protected override void OnScroll(ScrollEventArgs e) { base.OnScroll(e); Invalidate(); }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if ((ModifierKeys & Keys.Control) == 0)
        {
            base.OnMouseWheel(e);
            Invalidate();
            return;
        }
        Zoom(ScaleFactor * Math.Pow(1.2, e.Delta / 120d));
        if (e is HandledMouseEventArgs handled) handled.Handled = true;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        _dragStart = e.Location;
        _scrollStart = AutoScrollPosition;
        Capture = true;
        Cursor = Cursors.SizeAll;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragStart is not { } start) return;
        AutoScrollPosition = new Point(Math.Max(0, -_scrollStart.X + start.X - e.X),
            Math.Max(0, -_scrollStart.Y + start.Y - e.Y));
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left) Capture = false;
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (Capture) return;
        _dragStart = null;
        Cursor = Cursors.Default;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var size = ImageSize;
        var bounds = new Rectangle(Math.Max(Gap, (ClientSize.Width - size.Width) / 2) + AutoScrollPosition.X,
            Math.Max(Gap, (ClientSize.Height - size.Height) / 2) + AutoScrollPosition.Y, size.Width, size.Height);
        var visible = Rectangle.Intersect(Rectangle.Intersect(bounds, ClientRectangle), e.ClipRectangle);
        if (visible.IsEmpty) return;
        e.Graphics.FillRectangle(Brushes.White, visible);
        e.Graphics.InterpolationMode = ScaleFactor < 1 ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        var sx = _image.Width / (float)bounds.Width;
        var sy = _image.Height / (float)bounds.Height;
        e.Graphics.DrawImage(_image, visible, (visible.X - bounds.X) * sx, (visible.Y - bounds.Y) * sy,
            visible.Width * sx, visible.Height * sy, GraphicsUnit.Pixel);
    }
}
