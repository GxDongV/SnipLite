using System.Drawing.Drawing2D;

namespace SnipLite.Plugins;

internal enum ImageFitMode { Width, Image, Manual }

/// <summary>Draws only the visible source pixels. Zooming never allocates a scaled long bitmap.</summary>
internal sealed class ZoomImageViewport : ScrollableControl
{
    internal const double MinZoom = .01;
    internal const double MaxZoom = 8;
    private Bitmap? _image;
    private double _zoom = 1;
    private ImageFitMode _fitMode = ImageFitMode.Width;
    private bool _layingOut;
    private Point? _grab;
    private Point _grabScroll;
    internal event Action? ViewChanged;
    internal double Zoom => _zoom;
    internal ImageFitMode FitMode => _fitMode;
    private int Gap => (int)Math.Round(12 * DeviceDpi / 96f);
    internal Rectangle ImageBounds
    {
        get
        {
            var size = ScaledSize;
            return new Rectangle(Math.Max(Gap, (ClientSize.Width - size.Width) / 2) + AutoScrollPosition.X,
                Math.Max(Gap, (ClientSize.Height - size.Height) / 2) + AutoScrollPosition.Y, size.Width, size.Height);
        }
    }
    private Size ScaledSize => _image is null ? Size.Empty : new Size(Math.Max(1, (int)Math.Round(_image.Width * _zoom)), Math.Max(1, (int)Math.Round(_image.Height * _zoom)));

    public ZoomImageViewport()
    {
        AutoScroll = true; TabStop = true; DoubleBuffered = true; ResizeRedraw = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    internal void SetImage(Bitmap image)
    {
        _image = image; AutoScrollPosition = Point.Empty; Reflow();
    }
    internal void ClearImage() { _image = null; AutoScrollMinSize = Size.Empty; Invalidate(); }
    internal PointF ImagePoint(Point viewportPoint)
    {
        var bounds = ImageBounds;
        return new PointF((float)((viewportPoint.X - bounds.X) / _zoom), (float)((viewportPoint.Y - bounds.Y) / _zoom));
    }
    internal void Fit(ImageFitMode mode)
    {
        if (mode == ImageFitMode.Manual) { SetZoom(1); return; }
        _fitMode = mode; AutoScrollPosition = Point.Empty; Reflow();
    }
    internal void SetZoom(double zoom, Point? anchor = null)
    {
        if (_image is null || !double.IsFinite(zoom)) return;
        var pivot = anchor ?? new Point(ClientSize.Width / 2, ClientSize.Height / 2);
        var imagePoint = ImagePoint(pivot);
        _fitMode = ImageFitMode.Manual; _zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        Reflow();
        var size = ScaledSize;
        var left = Math.Max(Gap, (ClientSize.Width - size.Width) / 2);
        var top = Math.Max(Gap, (ClientSize.Height - size.Height) / 2);
        AutoScrollPosition = new Point(Math.Max(0, (int)Math.Round(imagePoint.X * _zoom + left - pivot.X)),
            Math.Max(0, (int)Math.Round(imagePoint.Y * _zoom + top - pivot.Y)));
        Invalidate();
    }

    private void Reflow()
    {
        if (_layingOut || _image is null || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        _layingOut = true;
        try
        {
            // Work from the viewport without bars, then reserve a vertical bar for width-fit long images.
            var width = ClientSize.Width + (VScroll ? SystemInformation.VerticalScrollBarWidth : 0);
            var height = ClientSize.Height + (HScroll ? SystemInformation.HorizontalScrollBarHeight : 0);
            if (_fitMode == ImageFitMode.Width)
            {
                var available = width - 2 * Gap;
                var candidate = Math.Min(1, available / (double)_image.Width);
                if (_image.Height * candidate + 2 * Gap > height) available -= SystemInformation.VerticalScrollBarWidth;
                _zoom = Math.Clamp(Math.Min(1, available / (double)_image.Width), MinZoom, MaxZoom);
            }
            else if (_fitMode == ImageFitMode.Image)
                _zoom = Math.Clamp(Math.Min(1, Math.Min((width - 2 * Gap) / (double)_image.Width, (height - 2 * Gap) / (double)_image.Height)), MinZoom, MaxZoom);
            var scaled = ScaledSize;
            AutoScrollMinSize = new Size(scaled.Width + 2 * Gap, scaled.Height + 2 * Gap);
            Invalidate();
            ViewChanged?.Invoke();
        }
        finally { _layingOut = false; }
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); Reflow(); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); Reflow(); }
    protected override void OnScroll(ScrollEventArgs e) { base.OnScroll(e); Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Focus(); }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if ((ModifierKeys & Keys.Control) != 0)
        {
            SetZoom(_zoom * Math.Pow(1.2, e.Delta / 120d), e.Location);
            if (e is HandledMouseEventArgs handled) handled.Handled = true;
            return;
        }
        base.OnMouseWheel(e); Invalidate();
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || _image is null) return;
        Focus(); _grab = e.Location; _grabScroll = AutoScrollPosition; Capture = true; Cursor = Cursors.SizeAll;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_grab is not Point start) return;
        AutoScrollPosition = new Point(Math.Max(0, -_grabScroll.X + start.X - e.X), Math.Max(0, -_grabScroll.Y + start.Y - e.Y));
        Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left) { _grab = null; Capture = false; Cursor = Cursors.Default; }
    }
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture) { _grab = null; Cursor = Cursors.Default; }
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(AppTheme.Current.Sidebar);
        if (_image is null) return;
        var bounds = ImageBounds;
        var visible = Rectangle.Intersect(Rectangle.Intersect(bounds, ClientRectangle), e.ClipRectangle);
        if (visible.IsEmpty) return;
        using var white = new SolidBrush(Color.White); e.Graphics.FillRectangle(white, visible);
        e.Graphics.InterpolationMode = _zoom < 1 ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        // Use the rounded destination size for the source mapping to avoid seams at fractional zoom.
        var sx = _image.Width / (double)bounds.Width; var sy = _image.Height / (double)bounds.Height;
        e.Graphics.DrawImage(_image, visible, (float)((visible.X - bounds.X) * sx), (float)((visible.Y - bounds.Y) * sy),
            (float)(visible.Width * sx), (float)(visible.Height * sy), GraphicsUnit.Pixel);
    }
}
