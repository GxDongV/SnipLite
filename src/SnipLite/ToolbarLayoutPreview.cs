namespace SnipLite;

/// <summary>Preview and ordering control for the capture toolbar in Preferences.</summary>
internal sealed class ToolbarLayoutPreview : Control
{
    private readonly AppSettings _settings;
    private readonly AnnotationBar _bar = new();
    private string? _draggedButton;
    private Rectangle _dragThreshold;
    private bool _dragging;
    private int _insertionSlot = -1;

    public event Action<string[]>? OrderChanged;

    public ToolbarLayoutPreview(AppSettings settings, IReadOnlyList<(string Id, string Title, Image? Icon)> plugins)
    {
        _settings = settings;
        _bar.PluginButtons = plugins;
        DoubleBuffered = true;
        ResizeRedraw = true;
        TabStop = false;
        AccessibleName = "截图工具栏预览";
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RefreshToolbar();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        RefreshToolbar();
    }

    public void RefreshToolbar()
    {
        if (Capture) Capture = false;
        _bar.Scale = DeviceDpi / 96f;
        _bar.Accent = _settings.Accent;
        _bar.HiddenButtons = _settings.HiddenToolbarButtons;
        _bar.ButtonOrder = _settings.ToolbarButtonOrder;
        // Lay out the full row before fitting its drawing to the preview.
        _bar.Layout(Rectangle.Empty, new Size(int.MaxValue, int.MaxValue));
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var palette = AppTheme.Current;
        e.Graphics.Clear(palette.Sidebar);
        if (!_bar.Visible)
        {
            TextRenderer.DrawText(e.Graphics, UiText.T("所有截图功能按钮均已隐藏"), Font, ClientRectangle, palette.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            return;
        }

        var (origin, scale) = PreviewTransform();
        var state = e.Graphics.Save();
        e.Graphics.TranslateTransform(origin.X, origin.Y);
        e.Graphics.ScaleTransform(scale, scale);
        _bar.Render(e.Graphics);
        e.Graphics.Restore(state);

        if (_insertionSlot < 0) return;
        var items = _bar.Items;
        var first = items[0].Rect;
        var x = _insertionSlot switch
        {
            0 => first.Left - 2 * _bar.Scale,
            var end when end == items.Count => items[^1].Rect.Right + 2 * _bar.Scale,
            _ => (items[_insertionSlot - 1].Rect.Right + items[_insertionSlot].Rect.Left) / 2f
        };
        using var marker = new Pen(_bar.Accent, Math.Max(2, 2 * _bar.Scale));
        e.Graphics.DrawLine(marker, origin.X + x * scale, origin.Y + first.Top * scale,
            origin.X + x * scale, origin.Y + first.Bottom * scale);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !_bar.Visible) return;
        _draggedButton = _bar.HitTest(ToolbarPoint(e.Location));
        if (_draggedButton is null) return;
        var threshold = SystemInformation.DragSize;
        _dragThreshold = new Rectangle(e.X - threshold.Width / 2, e.Y - threshold.Height / 2, threshold.Width, threshold.Height);
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_draggedButton is null)
        {
            Cursor = _bar.Visible && _bar.HitTest(ToolbarPoint(e.Location)) is not null ? Cursors.Hand : Cursors.Default;
            return;
        }
        if (!_dragging && _dragThreshold.Contains(e.Location)) return;
        _dragging = true;
        _insertionSlot = InsertionSlot(e.Location);
        Cursor = _insertionSlot >= 0 ? Cursors.SizeWE : Cursors.No;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || _draggedButton is not { } id) return;
        var slot = _dragging ? InsertionSlot(e.Location) : -1;
        Capture = false;
        if (slot < 0) return;
        var items = _bar.Items;
        if ((slot < items.Count && items[slot].Id == id) || (slot > 0 && items[slot - 1].Id == id)) return;

        var order = _bar.OrderedButtons(includeUnavailable: true).ToList();
        order.Remove(id);
        var target = slot < items.Count ? order.IndexOf(items[slot].Id) : order.IndexOf(items[slot - 1].Id) + 1;
        order.Insert(target, id);
        OrderChanged?.Invoke(order.ToArray());
        if (!IsDisposed) RefreshToolbar();
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (Capture) return;
        _draggedButton = null;
        _dragging = false;
        _insertionSlot = -1;
        Cursor = Cursors.Default;
        Invalidate();
    }

    private (PointF Origin, float Scale) PreviewTransform()
    {
        var bounds = _bar.Bounds;
        var width = Math.Max(1f, ClientSize.Width - 24 * _bar.Scale);
        var height = Math.Max(1f, ClientSize.Height - 16 * _bar.Scale);
        var scale = Math.Min(1f, Math.Min(width / bounds.Width, height / bounds.Height));
        return (new PointF((ClientSize.Width - bounds.Width * scale) / 2 - bounds.Left * scale,
            (ClientSize.Height - bounds.Height * scale) / 2 - bounds.Top * scale), scale);
    }

    private Point ToolbarPoint(Point point)
    {
        var (origin, scale) = PreviewTransform();
        return new Point((int)MathF.Floor((point.X - origin.X) / scale), (int)MathF.Floor((point.Y - origin.Y) / scale));
    }

    private int InsertionSlot(Point point)
    {
        if (!ClientRectangle.Contains(point)) return -1;
        var local = ToolbarPoint(point);
        if (local.Y < _bar.MainBounds.Top || local.Y >= _bar.MainBounds.Bottom) return -1;
        var items = _bar.Items;
        for (var i = 0; i < items.Count; i++)
            if (local.X < items[i].Rect.Left + items[i].Rect.Width / 2) return i;
        return items.Count;
    }
}
