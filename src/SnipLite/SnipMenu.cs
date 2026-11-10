using System.Drawing.Drawing2D;

namespace SnipLite;

/// <summary>
/// Snipaste's menus are plain light-grey strips with black text, no icons and a
/// one pixel border. The default WinForms menu (grey gradient, icon gutter,
/// blue selection) looks nothing like it, so both the tray menu and every
/// context menu use this renderer.
/// </summary>
internal sealed class SnipMenuRenderer : ToolStripProfessionalRenderer
{
    internal static readonly Color Back = Color.FromArgb(0xF6, 0xF6, 0xF6);
    internal static readonly Color Hover = Color.FromArgb(0xD9, 0xEC, 0xF9);

    public SnipMenuRenderer() : base(new SnipMenuColors())
    {
        RoundedEdges = false;
    }

    /// <summary>
    /// Render callbacks use item-local coordinates, even for rows below a separator.
    /// Fill the complete item so the highlight and its centered text share a row.
    /// </summary>
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled)
        {
            base.OnRenderMenuItemBackground(e);
            return;
        }

        using var brush = new SolidBrush(Hover);
        e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Color.FromArgb(26, 26, 26) : Color.FromArgb(160, 160, 160);
        e.TextRectangle = new Rectangle(e.TextRectangle.X, 0, e.TextRectangle.Width, e.Item.Height);
        e.TextFormat = (e.TextFormat & ~TextFormatFlags.Bottom) | TextFormatFlags.VerticalCenter;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        // A plain dark tick instead of the system's blue checkbox: these menus
        // are light grey strips, and the accent belongs to the annotation bar.
        var box = e.ImageRectangle;
        DrawTick(e.Graphics,
            box.Left + box.Width / 2,
            box.Top + box.Height / 2,
            Math.Max(4, box.Width / 3));
    }

    private static void DrawTick(Graphics g, int x, int y, int arm)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var pen = new Pen(Color.FromArgb(0x1A, 0x1A, 0x1A), 1.6f);
        g.DrawLines(pen, new[]
        {
            new Point(x - arm, y),
            new Point(x - arm / 3, y + arm * 2 / 3),
            new Point(x + arm, y - arm)
        });
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(Color.FromArgb(0xE2, 0xE2, 0xE2), 1f);
        var y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Color.FromArgb(0xC8, 0xC8, 0xC8), 1f);
        var bounds = new Rectangle(Point.Empty, e.ToolStrip.Size);
        e.Graphics.DrawRectangle(pen, 0, 0, bounds.Width - 1, bounds.Height - 1);
    }

    private sealed class SnipMenuColors : ProfessionalColorTable
    {
        private static readonly Color Border = Color.FromArgb(0xC8, 0xC8, 0xC8);

        public override Color ToolStripDropDownBackground => Back;
        public override Color ImageMarginGradientBegin => Back;
        public override Color ImageMarginGradientMiddle => Back;
        public override Color ImageMarginGradientEnd => Back;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Hover;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color MenuItemPressedGradientBegin => Hover;
        public override Color MenuItemPressedGradientMiddle => Hover;
        public override Color MenuItemPressedGradientEnd => Hover;
        public override Color SeparatorDark => Color.FromArgb(0xE2, 0xE2, 0xE2);
        public override Color SeparatorLight => Color.FromArgb(0xF0, 0xF0, 0xF0);
        public override Color ButtonSelectedHighlight => Hover;
        public override Color ButtonSelectedHighlightBorder => Border;
    }
}

internal static class SnipMenu
{
    public static ContextMenuStrip Create()
    {
        var menu = new ContextMenuStrip
        {
            Font = new Font("Microsoft YaHei UI", 9f),
            BackColor = Color.FromArgb(0xF6, 0xF6, 0xF6),
            ShowImageMargin = false,
            ShowCheckMargin = true,
            DropShadowEnabled = true,
            Renderer = new SnipMenuRenderer(),
            Padding = new Padding(2)
        };

        return menu;
    }

    public static ToolStripMenuItem Item(string text, Action action, string shortcut = "", bool check = false)
    {
        var item = new ToolStripMenuItem(text)
        {
            CheckOnClick = false,
            Checked = check,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(0, 5, 0, 5)
        };

        if (shortcut.Length > 0)
            item.ShortcutKeyDisplayString = shortcut;

        item.Click += (_, _) => action();
        return item;
    }
}
