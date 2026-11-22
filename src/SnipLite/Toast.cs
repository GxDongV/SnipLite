using System.Drawing.Drawing2D;

namespace SnipLite;

/// <summary>Small non-activating status card anchored to the active screen.</summary>
internal sealed class Toast : Form
{
    private static Toast? _current;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Label _label;
    private readonly bool _error;

    public static bool Notifications { get; set; } = true;

    private Toast(string text, int milliseconds, bool error)
    {
        _error = error;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Font = new Font("Microsoft YaHei UI", 9f);

        var measured = TextRenderer.MeasureText(text, Font, new Size(400, 1000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        ClientSize = new Size(Math.Clamp(measured.Width + 78, 240, 480), Math.Max(48, measured.Height + 22));
        _label = new Label
        {
            Text = text,
            AutoSize = false,
            Bounds = new Rectangle(54, 9, ClientSize.Width - 68, ClientSize.Height - 18),
            ForeColor = AppTheme.Current.Text,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        Controls.Add(_label);

        _timer = new System.Windows.Forms.Timer { Interval = milliseconds };
        _timer.Tick += (_, _) => Close();
        ApplyTheme();
        AppTheme.Changed += ApplyTheme;
    }

    private void ApplyTheme()
    {
        BackColor = AppTheme.Current.Window;
        _label.ForeColor = AppTheme.Current.Text;
        Invalidate();
        AppTheme.ApplyWindow(this);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
            cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
            cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
            return cp;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        UpdateShape();
        AppTheme.ApplyWindow(this);

        var area = Screen.FromPoint(Win32.CursorPosition()).WorkingArea;
        Location = new Point(area.Right - Width - 24, area.Bottom - Height - 24);
        _timer.Start();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateShape();
    }

    private void UpdateShape()
    {
        if (Width < 2 || Height < 2) return;
        using var path = FluentDrawing.Rounded(new RectangleF(0, 0, Width, Height), 12);
        Region = new Region(path);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // The rounded region and custom paint provide the complete background.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var palette = AppTheme.Current;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = FluentDrawing.Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1), 12);
        using var fill = new SolidBrush(palette.Card);
        using var border = new Pen(palette.Border);
        g.FillPath(fill, path);
        g.DrawPath(border, path);

        var icon = new Rectangle(14, (Height - 28) / 2, 28, 28);
        var color = _error ? Color.FromArgb(210, 72, 72) : AppTheme.AdaptAccent(palette.Accent);
        using var iconFill = new SolidBrush(Color.FromArgb(28, color));
        using var iconPen = new Pen(color, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.FillEllipse(iconFill, icon);
        g.DrawEllipse(iconPen, icon.X + 1, icon.Y + 1, icon.Width - 2, icon.Height - 2);
        if (_error)
        {
            var x = icon.X + icon.Width / 2f;
            g.DrawLine(iconPen, x, icon.Y + 7, x, icon.Bottom - 10);
            using var dot = new SolidBrush(color);
            g.FillEllipse(dot, x - 1.5f, icon.Bottom - 7, 3, 3);
        }
        else
        {
            g.DrawLine(iconPen, icon.X + 8, icon.Y + 14, icon.X + 12, icon.Y + 18);
            g.DrawLine(iconPen, icon.X + 12, icon.Y + 18, icon.X + 20, icon.Y + 9);
        }
    }

    protected override void OnClick(EventArgs e) => Close();

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        AppTheme.Changed -= ApplyTheme;
        _timer.Stop();
        _timer.Dispose();
        Region?.Dispose();
        if (ReferenceEquals(_current, this)) _current = null;
        base.OnFormClosed(e);
    }

    public static void Show(string text, int milliseconds = 2200)
    {
        if (!Notifications) return;
        ShowCore(text, milliseconds, error: false);
    }

    public static void Error(string text)
    {
        if (!Notifications) return;
        ShowCore(text, 4000, error: true);
    }

    private static void ShowCore(string text, int milliseconds, bool error)
    {
        _current?.Close();
        var toast = new Toast(text, milliseconds, error);
        _current = toast;
        toast.Show();
    }
}
