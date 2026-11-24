using System.Drawing.Drawing2D;
using System.ComponentModel;

namespace SnipLite.ScrollCapture;

// The controls and artwork belong to this plugin. The host supplies window theme
// colors through StyleWindow; no reference to the host implementation is needed.
internal readonly record struct ScrollPalette(Color Window, Color Text, bool Dark, bool HighContrast)
{
    public static ScrollPalette For(Control control)
    {
        var form = control.FindForm();
        var window = form?.BackColor ?? SystemColors.Control;
        return new(window, form?.ForeColor ?? SystemColors.ControlText, window.GetBrightness() < .5f, SystemInformation.HighContrast);
    }
    public Color Card => HighContrast ? SystemColors.Window : Dark ? Color.FromArgb(43, 43, 46) : Color.White;
    public Color Border => HighContrast ? SystemColors.WindowText : Dark ? Color.FromArgb(65, 65, 70) : Color.FromArgb(225, 225, 231);
    public Color Muted => HighContrast ? SystemColors.WindowText : Dark ? Color.FromArgb(184, 184, 190) : Color.FromArgb(100, 100, 108);
    public Color Hover => HighContrast ? SystemColors.Highlight : Dark ? Color.FromArgb(58, 58, 63) : Color.FromArgb(232, 233, 237);
    public Color Accent => HighContrast ? SystemColors.Highlight : Dark ? Color.FromArgb(117, 185, 255) : Color.FromArgb(0, 103, 192);
    public Color OnAccent => HighContrast ? SystemColors.HighlightText : Dark ? Color.FromArgb(12, 33, 52) : Color.White;
    public Color Disabled => HighContrast ? SystemColors.GrayText : Dark ? Color.FromArgb(126, 126, 134) : Color.FromArgb(156, 156, 164);
}

internal static class ScrollDrawing
{
    internal static GraphicsPath Rounded(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(1, Math.Min(radius * 2, Math.Min(rect.Width, rect.Height)));
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    internal static Bitmap LoadIcon()
    {
        using var stream = typeof(ScrollDrawing).Assembly.GetManifestResourceStream("SnipLite.ScrollCapture.Icon.png")
            ?? throw new InvalidDataException("Missing scroll capture artwork.");
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }

    internal static void Glyph(Graphics graphics, string id, Rectangle bounds, Color color)
    {
        var state = graphics.Save();
        try
        {
            graphics.TranslateTransform(bounds.X, bounds.Y); graphics.ScaleTransform(bounds.Width / 24f, bounds.Height / 24f);
            using var pen = new Pen(color, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            void Line(float x1, float y1, float x2, float y2) => graphics.DrawLine(pen, x1, y1, x2, y2);
            void Lines(params PointF[] points) => graphics.DrawLines(pen, points);
            void Box(float x, float y, float width, float height)
            {
                using var path = Rounded(new RectangleF(x, y, width, height), 1);
                graphics.DrawPath(pen, path);
            }
            void Corners()
            {
                Lines(new(3, 8), new(3, 3), new(8, 3));
                Lines(new(16, 3), new(21, 3), new(21, 8));
                Lines(new(3, 16), new(3, 21), new(8, 21));
                Lines(new(16, 21), new(21, 21), new(21, 16));
            }

            switch (id)
            {
                case "pause": graphics.DrawLine(pen, 8, 5, 8, 19); graphics.DrawLine(pen, 16, 5, 16, 19); break;
                case "resume": graphics.DrawPolygon(pen, new PointF[] { new(8, 4), new(19, 12), new(8, 20) }); break;
                case "undo":
                    graphics.DrawLines(pen, new PointF[] { new(8, 4), new(3, 9), new(8, 14) });
                    graphics.DrawLines(pen, new PointF[] { new(3, 9), new(14, 9), new(19, 13), new(19, 17), new(15, 20), new(10, 20) }); break;
                case "finish": graphics.DrawLines(pen, new PointF[] { new(4, 12), new(10, 18), new(20, 6) }); break;
                case "close": graphics.DrawLine(pen, 6, 6, 18, 18); graphics.DrawLine(pen, 18, 6, 6, 18); break;
                case "copy":
                    Box(8, 8, 13, 13);
                    Lines(new(16, 4), new(16, 3), new(3, 3), new(3, 16), new(4, 16));
                    break;
                case "save":
                    Lines(new(4, 3), new(17, 3), new(21, 7), new(21, 21), new(3, 21), new(3, 4), new(4, 3));
                    Box(7, 3, 9, 6);
                    Box(7, 14, 10, 7);
                    break;
                case "fit-width":
                    Line(3, 4, 3, 20);
                    Line(21, 4, 21, 20);
                    Line(5, 12, 19, 12);
                    Lines(new(8, 9), new(5, 12), new(8, 15));
                    Lines(new(16, 9), new(19, 12), new(16, 15));
                    break;
                case "fit-image":
                    Corners();
                    Box(8, 6, 8, 12);
                    break;
                case "actual":
                    Corners();
                    Box(8, 8, 8, 8);
                    break;
                case "zoom-in":
                case "zoom-out":
                    graphics.DrawEllipse(pen, 3, 3, 13, 13);
                    Line(14, 14, 21, 21);
                    Line(6, 9.5f, 13, 9.5f);
                    if (id == "zoom-in") Line(9.5f, 6, 9.5f, 13);
                    break;
                case "extensions":
                    Box(6, 9, 12, 10);
                    Line(9, 4, 9, 9);
                    Line(15, 4, 15, 9);
                    Line(12, 19, 12, 22);
                    break;
            }
        }
        finally { graphics.Restore(state); }
    }
}

internal sealed class ScrollPreviewButton : ToolStripButton
{
    private readonly string _glyph;

    public ScrollPreviewButton(string glyph, string label) : base(label)
    {
        _glyph = Name = glyph;
        DisplayStyle = ToolStripItemDisplayStyle.Image;
        ToolTipText = AccessibleName = label;
    }

    private int Px(int value) => (int)Math.Round(value * (Owner?.DeviceDpi ?? 96) / 96f);

    public override Size GetPreferredSize(Size constrainingSize) => new(Px(34), Px(32));

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Owner is not { } owner) return;
        var palette = ScrollPalette.For(owner);
        var color = palette.HighContrast && Selected && Enabled ? SystemColors.HighlightText
            : Enabled ? palette.Text : palette.Disabled;
        var size = Px(18);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        ScrollDrawing.Glyph(e.Graphics, _glyph, new Rectangle((Width - size) / 2, (Height - size) / 2, size, size), color);
    }
}

internal sealed class ScrollCard : Panel
{
    public ScrollCard() { DoubleBuffered = true; ResizeRedraw = true; }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var palette = ScrollPalette.For(this); var scale = DeviceDpi / 96f;
        e.Graphics.Clear(palette.Window); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = ScrollDrawing.Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1), 7 * scale);
        using var fill = new SolidBrush(palette.Card); e.Graphics.FillPath(fill, path);
        using var edge = new Pen(palette.Border); e.Graphics.DrawPath(edge, path);
    }
}

internal sealed class ScrollButton : Button
{
    private bool _hover;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal string Glyph { get; set; } = "";
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Primary { get; init; }
    public ScrollButton()
    {
        AutoSize = false; FlatStyle = FlatStyle.Flat; UseVisualStyleBackColor = false; Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var palette = ScrollPalette.For(this); var scale = DeviceDpi / 96f; var g = e.Graphics;
        g.Clear(palette.Card); g.SmoothingMode = SmoothingMode.AntiAlias;
        var primary = Primary && Enabled;
        var background = primary ? palette.Accent : _hover && Enabled ? palette.Hover : palette.Card;
        var foreground = !Enabled ? palette.Disabled : primary ? palette.OnAccent : palette.Text;
        using var path = ScrollDrawing.Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1), 5 * scale);
        using var fill = new SolidBrush(background); g.FillPath(fill, path);
        using var edge = new Pen(primary ? palette.Accent : palette.Border); g.DrawPath(edge, path);
        var iconSize = (int)Math.Round(16 * scale);
        var labelWidth = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
        var start = Math.Max((int)(8 * scale), (Width - labelWidth - iconSize - (int)(6 * scale)) / 2);
        ScrollDrawing.Glyph(g, Glyph, new Rectangle(start, (Height - iconSize) / 2, iconSize, iconSize), foreground);
        var textStart = start + iconSize + (int)(6 * scale);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(textStart, 0, Math.Max(1, Width - textStart - (int)(6 * scale)), Height), foreground,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -(int)(4 * scale), -(int)(4 * scale)), foreground, background);
    }
}

internal sealed class ScrollToggle : CheckBox
{
    public ScrollToggle()
    {
        AutoSize = false; Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.CheckButton;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }
    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var palette = ScrollPalette.For(this); var scale = DeviceDpi / 96f; var g = e.Graphics;
        g.Clear(palette.Card); g.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new RectangleF(Width - 39 * scale, (Height - 20 * scale) / 2, 36 * scale, 20 * scale);
        using var path = ScrollDrawing.Rounded(track, 10 * scale);
        using var fill = new SolidBrush(Checked ? palette.Accent : palette.Card); g.FillPath(fill, path);
        using var edge = new Pen(Checked ? palette.Accent : palette.Muted); g.DrawPath(edge, path);
        using var knob = new SolidBrush(Checked ? palette.OnAccent : palette.Muted);
        g.FillEllipse(knob, track.X + (Checked ? 20 : 4) * scale, track.Y + 4 * scale, 12 * scale, 12 * scale);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Math.Max(1, Width - (int)(48 * scale)), Height), palette.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -2, -2), palette.Text, palette.Card);
    }
}

internal sealed class ScrollStatus : Control
{
    private readonly Bitmap _icon = ScrollDrawing.LoadIcon();
    public ScrollStatus() { DoubleBuffered = true; ResizeRedraw = true; TabStop = false; AccessibleRole = AccessibleRole.StaticText; }
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var palette = ScrollPalette.For(this); var scale = DeviceDpi / 96f; var g = e.Graphics;
        g.Clear(palette.Card);
        var iconSize = (int)(20 * scale); var gap = (int)(10 * scale);
        g.DrawImage(_icon, new Rectangle(0, (Height - iconSize) / 2, iconSize, iconSize));
        var left = iconSize + gap;
        TextRenderer.DrawText(g, Text, Font, new Rectangle(left, 0, Width - left, Height), palette.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
    protected override void Dispose(bool disposing) { if (disposing) _icon.Dispose(); base.Dispose(disposing); }
}
