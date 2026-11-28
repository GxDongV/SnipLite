using System.Drawing.Drawing2D;

namespace SnipLite;

internal static class FluentSettingsLayout
{
    internal static void DrawNavigationItem(Graphics graphics, Rectangle bounds, string title, string icon,
        Font font, float scale, bool selected, bool hovered)
    {
        int Px(int value) => (int)Math.Round(value * scale);
        var palette = AppTheme.Current;
        if (selected || hovered)
        {
            using var shape = FluentDrawing.Rounded(bounds, Px(5));
            using var fill = new SolidBrush(selected ? palette.Card : palette.Hover);
            graphics.FillPath(fill, shape);
        }
        if (selected)
        {
            using var indicator = new SolidBrush(palette.Accent);
            using var shape = FluentDrawing.Rounded(
                new Rectangle(bounds.Left, bounds.Top + Px(10), Px(3), bounds.Height - Px(20)), Px(2));
            graphics.FillPath(indicator, shape);
        }
        FluentIcons.Draw(graphics, icon, new Rectangle(bounds.X + Px(12), bounds.Y + Px(8), Px(20), Px(20)), palette.Text);
        TextRenderer.DrawText(graphics, title, font,
            new Rectangle(bounds.X + Px(43), bounds.Y, bounds.Width - Px(47), bounds.Height), palette.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }

    internal static Control Navigation((string Title, string Icon, Control Page)[] pages)
    {
        var layout = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Dock = DockStyle.Fill, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 164));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var sidebar = new SettingsSidebar { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(10, 18, 10, 0) };
        var content = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        layout.Controls.Add(sidebar, 0, 0);
        layout.Controls.Add(content, 1, 0);

        var items = new NavigationButton[pages.Length];
        for (var i = 0; i < pages.Length; i++)
        {
            var index = i;
            var (title, icon, page) = pages[i];
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            content.Controls.Add(page);
            var item = items[i] = new NavigationButton(icon) { Text = title, AccessibleName = title, TabIndex = i };
            item.Click += (_, _) => SelectPage(index);
            sidebar.Controls.Add(item);
        }

        void Arrange()
        {
            var scale = sidebar.DeviceDpi / 96f;
            var sidebarWidth = (int)Math.Round(164 * scale);
            if (layout.ColumnStyles[0].Width != sidebarWidth) layout.ColumnStyles[0].Width = sidebarWidth;
            for (var i = 0; i < items.Length; i++)
                items[i].Bounds = new Rectangle((int)Math.Round(10 * scale), (int)Math.Round((18 + 40 * i) * scale),
                    Math.Max(1, sidebar.ClientSize.Width - (int)Math.Round(20 * scale)), (int)Math.Round(36 * scale));
        }
        sidebar.SizeChanged += (_, _) => Arrange();
        sidebar.HandleCreated += (_, _) => Arrange();
        SelectPage(0);
        return layout;

        void SelectPage(int index)
        {
            for (var i = 0; i < items.Length; i++)
            {
                items[i].Selected = i == index;
                items[i].Invalidate();
                pages[i].Page.Visible = i == index;
            }
        }
    }

    internal static Control Row(string title, Control field)
    {
        var card = new FluentSettingRow { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6) };
        var row = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Dock = DockStyle.Fill,
            Padding = new Padding(14, 9, 14, 9), Margin = Padding.Empty };
        var labelWidth = field is FluentToggle ? 72 : 45;
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, labelWidth));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100 - labelWidth));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        row.Controls.Add(new Label { Text = title, AutoEllipsis = true, Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 0, 8, 0) }, 0, 0);
        field.Dock = DockStyle.Fill;
        field.Margin = Padding.Empty;
        field.AccessibleName = title;
        row.Controls.Add(field, 1, 0);
        card.Controls.Add(row);
        card.Padding = new Padding(3);
        return card;
    }

    private sealed class SettingsSidebar : Panel
    {
        protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(AppTheme.Current.Sidebar);
    }

    private sealed class NavigationButton : Button
    {
        private readonly string _icon;
        private bool _hovered;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal bool Selected { get; set; }

        internal NavigationButton(string icon)
        {
            _icon = icon;
            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hovered = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(AppTheme.Current.Sidebar);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            DrawNavigationItem(e.Graphics, ClientRectangle, Text, _icon, Font, DeviceDpi / 96f, Selected, _hovered);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -4, -4));
        }
    }
}
