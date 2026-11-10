using System.Drawing.Drawing2D;

namespace SnipLite;

/// <summary>A single options row below the unchanged annotation toolbar.</summary>
internal sealed class TranslationPanel : UserControl
{
    internal static readonly Color Surface = SnipMenuRenderer.Back;
    private static readonly Color Ink = Color.FromArgb(62, 62, 62);
    private readonly Color _accent;
    private readonly Button _source;
    private readonly Button _target;
    private readonly Button _swap;
    private readonly Button _view;
    private readonly Button _copy;
    private readonly Button _retry;
    private readonly Label _status;
    private readonly List<ContextMenuStrip> _menus = new();
    private readonly float _scale;
    private readonly Font _contentFont;
    private readonly Font _smallFont;
    private readonly ToolTip _tips = new();
    private string _originalText = "";

    public string SourceLanguage { get; private set; }
    public string TargetLanguage { get; private set; }
    public string? DetectedLanguage { get; private set; }
    public string ResultText { get; private set; } = "";
    public bool ShowOriginal { get; private set; }
    public event Action<bool>? LanguagesChanged;
    public event Action? ViewChanged;
    public event Action? RetryRequested;
    public event Action<ContextMenuStrip>? MenuOpening;
    private int Px(int value) => (int)Math.Round(value * _scale);

    public TranslationPanel(float scale, string source, string target, Color? accent = null)
    {
        _scale = scale; _accent = accent ?? AnnotationBar.DefaultAccent;
        AutoScaleMode = AutoScaleMode.None; BackColor = Surface; ForeColor = Ink;
        Cursor = Cursors.Default; DoubleBuffered = true;
        _contentFont = new Font("Microsoft YaHei UI", 12 * scale, GraphicsUnit.Pixel);
        _smallFont = new Font("Microsoft YaHei UI", 11 * scale, GraphicsUnit.Pixel);
        Font = _contentFont;
        SourceLanguage = TranslationLanguages.Normalize(source, true, "en");
        TargetLanguage = TranslationLanguages.Normalize(target, false, "zh-CN");
        _source = MakeButton("", () => ShowLanguages(true));
        _target = MakeButton("", () => ShowLanguages(false));
        _swap = MakeButton("⇄", SwapLanguages);
        _tips.SetToolTip(_swap, "交换源语言和目标语言");
        _view = MakeButton("", ToggleView); _view.AccessibleName = "切换原文 / 译文";
        _view.Paint += (_, e) => DrawViewIcon(e.Graphics, _view.ClientRectangle, _view.Enabled);
        _copy = MakeButton("", CopyText); _copy.AccessibleName = "复制识别文字";
        _copy.Paint += (_, e) => DrawCopyIcon(e.Graphics, _copy.ClientRectangle, _copy.Enabled);
        _retry = MakeButton("↻", () => RetryRequested?.Invoke()); _retry.AccessibleName = "重新翻译";
        _tips.SetToolTip(_retry, "重新翻译");
        _status = new Label { ForeColor = Color.FromArgb(110, 110, 110), Font = _smallFont, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        Controls.Add(_status);
        Width = Px(680); Height = Px(44);
        UpdateLanguages(); SetBusy("正在识别…");
    }

    private Button MakeButton(string text, Action click)
    {
        var button = new Button
        {
            Text = text, FlatStyle = FlatStyle.Flat, BackColor = Surface, ForeColor = Ink,
            Cursor = Cursors.Hand, Font = _contentFont, UseVisualStyleBackColor = false,
            TextAlign = ContentAlignment.MiddleCenter, Padding = Padding.Empty, Margin = Padding.Empty
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = SnipMenuRenderer.Hover;
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(194, 222, 241);
        button.Click += (_, _) => click(); Controls.Add(button); return button;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e); if (_status is null) return;
        var x = Px(5); var y = Px(5); var button = Px(34); var gap = Px(2);
        _source.SetBounds(x, y, Px(142), button); x = _source.Right + gap;
        _swap.SetBounds(x, y, button, button); x = _swap.Right + gap;
        _target.SetBounds(x, y, Px(114), button); x = _target.Right + Px(8);
        _view.SetBounds(x, y, button, button); x = _view.Right + Px(12);
        _copy.SetBounds(Width - Px(5) - button, y, button, button);
        _retry.SetBounds(_copy.Left - gap - button, y, button, button);
        _status.SetBounds(x, y, Math.Max(1, _retry.Left - x - Px(6)), button);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var border = new Pen(_accent); e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        using var divider = new Pen(Color.FromArgb(200, 200, 200));
        e.Graphics.DrawLine(divider, _view.Right + Px(5), Px(10), _view.Right + Px(5), Height - Px(10));
    }

    private void DrawViewIcon(Graphics g, Rectangle rect, bool enabled)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var x = rect.Width / 2f; var y = rect.Height / 2f;
        using var pen = new Pen(enabled ? ShowOriginal ? _accent : Ink : Color.FromArgb(170, 170, 170), 1.5f * _scale);
        using var path = new GraphicsPath();
        path.AddBezier(x - Px(9), y, x - Px(4), y - Px(9), x + Px(4), y - Px(9), x + Px(9), y);
        path.AddBezier(x + Px(9), y, x + Px(4), y + Px(9), x - Px(4), y + Px(9), x - Px(9), y);
        g.DrawPath(pen, path); g.DrawEllipse(pen, x - Px(2), y - Px(2), Px(4), Px(4));
    }

    private void DrawCopyIcon(Graphics g, Rectangle rect, bool enabled)
    {
        using var pen = new Pen(enabled ? Ink : Color.FromArgb(170, 170, 170), 1.5f * _scale);
        var x = rect.Width / 2; var y = rect.Height / 2;
        g.DrawRectangle(pen, x - Px(7), y - Px(7), Px(10), Px(10));
        g.DrawRectangle(pen, x - Px(2), y - Px(2), Px(10), Px(10));
    }

    internal void SelectLanguage(bool source, string code)
    {
        code = TranslationLanguages.Normalize(code, source, source ? "en" : "zh-CN");
        if (source ? SourceLanguage == code : TargetLanguage == code) return;
        if (source) SourceLanguage = code; else TargetLanguage = code;
        DetectedLanguage = null; UpdateLanguages(); LanguagesChanged?.Invoke(false);
    }

    internal void SwapLanguages()
    {
        var source = SourceLanguage == "auto" ? TranslationLanguages.Normalize(DetectedLanguage, false, "en") : SourceLanguage;
        (SourceLanguage, TargetLanguage) = (TargetLanguage, source);
        DetectedLanguage = null; UpdateLanguages(); LanguagesChanged?.Invoke(true);
    }

    private void UpdateLanguages()
    {
        _source.Text = (SourceLanguage == "auto" && DetectedLanguage is not null
            ? "检测到" + TranslationLanguages.Name(DetectedLanguage) : TranslationLanguages.Name(SourceLanguage)) + " ⌄";
        _target.Text = TranslationLanguages.Name(TargetLanguage) + " ⌄";
    }

    internal void ShowLanguages(bool source)
    {
        var menu = SnipMenu.Create(); menu.Font = _contentFont; menu.MaximumSize = new Size(Px(218), Px(284));
        foreach (var language in TranslationLanguages.All.Where(l => source || l.Code != "auto"))
        {
            var item = new ToolStripMenuItem(language.Name)
            {
                AutoSize = false, Size = new Size(Px(198), Px(34)),
                Checked = language.Code == (source ? SourceLanguage : TargetLanguage), Padding = Padding.Empty
            };
            item.Click += (_, _) => SelectLanguage(source, language.Code); menu.Items.Add(item);
        }
        _menus.Add(menu); MenuOpening?.Invoke(menu);
        var owner = source ? _source : _target; menu.Show(owner, new Point(0, owner.Height + Px(3)));
    }

    public void SetOriginal(string text) => _originalText = text;
    public void SetBusy(string status)
    {
        ResultText = ""; ShowOriginal = false;
        _view.Enabled = _copy.Enabled = _retry.Enabled = false;
        _status.ForeColor = Color.FromArgb(110, 110, 110); SetStatus(status); RefreshView();
    }
    public void SetProgress(int completed, int total) => SetStatus($"翻译中 {completed}/{total}");
    public void SetResult(TranslationResult result)
    {
        ResultText = result.Text; DetectedLanguage = result.DetectedLanguage;
        _view.Enabled = _copy.Enabled = _retry.Enabled = true;
        SetStatus("已翻译"); _tips.SetToolTip(_retry, "重新翻译");
        UpdateLanguages(); RefreshView();
    }
    public void SetError(string error)
    {
        ResultText = ""; _view.Enabled = _copy.Enabled = false; _retry.Enabled = true;
        _status.ForeColor = Color.FromArgb(178, 48, 42); SetStatus(error);
        _tips.SetToolTip(_retry, "重试翻译"); RefreshView();
    }
    private void SetStatus(string status)
    {
        _status.Text = "Google · " + status; _tips.SetToolTip(_status, _status.Text);
    }
    internal void ToggleView()
    {
        if (!_view.Enabled) return;
        ShowOriginal = !ShowOriginal; SetStatus(ShowOriginal ? "显示原文" : "已翻译");
        RefreshView(); ViewChanged?.Invoke();
    }
    private void RefreshView()
    {
        _view.BackColor = ShowOriginal ? SnipMenuRenderer.Hover : Surface;
        _tips.SetToolTip(_view, ShowOriginal ? "查看译文（当前显示原文）" : "查看原文（当前显示译文）");
        _tips.SetToolTip(_copy, ShowOriginal ? "复制原文" : "复制译文");
        _view.Invalidate(); _copy.Invalidate();
    }
    public void CopyText()
    {
        var text = ShowOriginal ? _originalText : ResultText; if (string.IsNullOrEmpty(text)) return;
        try { Clipboard.SetText(text); SetStatus(ShowOriginal ? "原文已复制" : "译文已复制"); }
        catch { SetStatus("复制失败，请重试"); }
    }
    public void CloseMenus() { foreach (var menu in _menus) if (menu.Visible) menu.Close(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CloseMenus(); var menus = _menus.ToArray(); _menus.Clear();
            var context = SynchronizationContext.Current;
            if (context is not null) context.Post(_ => { foreach (var menu in menus) menu.Dispose(); }, null);
            else foreach (var menu in menus) menu.Dispose();
            _tips.Dispose(); _contentFont.Dispose(); _smallFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
