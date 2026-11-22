namespace SnipLite;

/// <summary>Read-only preview of the regular capture toolbar in Preferences.</summary>
internal sealed class ToolbarLayoutPreview : Control
{
    private readonly AppSettings _settings;
    private readonly AnnotationBar _bar = new();

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
        _bar.Scale = DeviceDpi / 96f;
        _bar.Accent = _settings.Accent;
        _bar.HiddenButtons = _settings.HiddenToolbarButtons;
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

        var bounds = _bar.Bounds;
        var width = Math.Max(1f, ClientSize.Width - 24 * _bar.Scale);
        var height = Math.Max(1f, ClientSize.Height - 16 * _bar.Scale);
        var scale = Math.Min(1f, Math.Min(width / bounds.Width, height / bounds.Height));
        var left = (ClientSize.Width - bounds.Width * scale) / 2;
        var top = (ClientSize.Height - bounds.Height * scale) / 2;
        var state = e.Graphics.Save();
        e.Graphics.TranslateTransform(left, top);
        e.Graphics.ScaleTransform(scale, scale);
        e.Graphics.TranslateTransform(-bounds.Left, -bounds.Top);
        _bar.Render(e.Graphics);
        e.Graphics.Restore(state);
    }
}
