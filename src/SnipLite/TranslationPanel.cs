namespace SnipLite;

/// <summary>A compact child of the capture overlay; no separate result window.</summary>
internal sealed class TranslationPanel : UserControl
{
    internal static readonly Color Surface = SnipMenuRenderer.Back;
    private static readonly Color Ink = Color.FromArgb(62, 62, 62);
    private static readonly Color Muted = Color.FromArgb(112, 112, 112);
    private readonly Color _accent;
    private readonly Button _source;
    private readonly Button _target;
    private readonly Button _swap;
    private readonly Button _original;
    private readonly Button _translated;
    private readonly Button _copy;
    private readonly Button _retry;
    private readonly TextBox _body;
    private readonly Label _status;
    private readonly List<ContextMenuStrip> _menus = new();
    private readonly float _scale;
    private readonly Font _contentFont;
    private readonly Font _smallFont;
    private readonly ToolTip _tips = new();
    private string _originalText = "";
    private string _error = "";
    private bool _showOriginal;
    private bool _busy;

    public string SourceLanguage { get; private set; }
    public string TargetLanguage { get; private set; }
    public string? DetectedLanguage { get; private set; }
    public string ResultText { get; private set; } = "";
    public event Action<bool>? LanguagesChanged;
    public event Action? RetryRequested;
    public event Action<ContextMenuStrip>? MenuOpening;

    private int Px(int value) => (int)Math.Round(value * _scale);

    public TranslationPanel(float scale, string source, string target, Color? accent = null)
    {
        _scale = scale;
        _accent = accent ?? AnnotationBar.DefaultAccent;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Surface;
        ForeColor = Ink;
        Cursor = Cursors.Default;
        DoubleBuffered = true;
        _contentFont = new Font("Microsoft YaHei UI", 14 * scale, GraphicsUnit.Pixel);
        _smallFont = new Font("Microsoft YaHei UI", 11 * scale, GraphicsUnit.Pixel);
        Font = _contentFont;
        SourceLanguage = TranslationLanguages.Normalize(source, true, "en");
        TargetLanguage = TranslationLanguages.Normalize(target, false, "zh-CN");
        _source = MakeButton("", () => ShowLanguages(true));
        _target = MakeButton("", () => ShowLanguages(false));
        _swap = MakeButton("⇄", SwapLanguages);
        _tips.SetToolTip(_swap, "交换语言和原文 / 译文");
        _original = MakeButton("原文", () => { _showOriginal = true; RefreshBody(); });
        _translated = MakeButton("译文", () => { _showOriginal = false; RefreshBody(); });
        _copy = MakeButton("复制", CopyText);
        _retry = MakeButton("重试", () => RetryRequested?.Invoke());
        _body = new TextBox
        {
            Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.None,
            BackColor = Surface, ForeColor = Ink, Font = _contentFont, TabStop = true,
            ShortcutsEnabled = true, Name = "TranslationText", AccessibleName = "翻译结果"
        };
        _status = new Label { ForeColor = Muted, Font = _smallFont, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        Controls.Add(_body); Controls.Add(_status);
        Height = Px(232); Width = Px(410);
        UpdateLanguages();
        SetBusy("正在识别选区文字…");
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
        base.OnLayout(e);
        if (_status is null) return;
        var pad = Px(14);
        var space = ClientSize.Width - pad * 2;
        var swapWidth = Px(36);
        var languageWidth = (space - swapWidth) / 2;
        _source.SetBounds(pad, Px(9), languageWidth, Px(38));
        _swap.SetBounds(pad + languageWidth, Px(9), swapWidth, Px(38));
        _target.SetBounds(pad + languageWidth + swapWidth, Px(9), languageWidth, Px(38));
        _original.SetBounds(pad, Px(61), Px(52), Px(26));
        _translated.SetBounds(pad + Px(58), Px(61), Px(52), Px(26));
        _copy.SetBounds(Width - pad - Px(58), Px(61), Px(58), Px(26));
        _body.SetBounds(pad + Px(3), Px(100), space - Px(6), Math.Max(Px(30), Height - Px(138)));
        _status.SetBounds(pad + Px(3), Height - Px(30), space - Px(62), Px(20));
        _retry.SetBounds(Width - pad - Px(54), Height - Px(33), Px(54), Px(26));
        UpdateScrollBars();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var line = new Pen(Color.FromArgb(210, 210, 210));
        using var border = new Pen(_accent);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        e.Graphics.DrawLine(line, Px(14), Px(55), Width - Px(14), Px(55));
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
        // Keep dropdowns alive until the panel is disposed, after ToolStrip's closing filter unwinds.
        var menu = SnipMenu.Create();
        menu.Font = _contentFont;
        menu.MaximumSize = new Size(Px(218), Px(284));
        foreach (var language in TranslationLanguages.All.Where(l => source || l.Code != "auto"))
        {
            var item = new ToolStripMenuItem(language.Name)
            {
                ForeColor = Ink, AutoSize = false, Size = new Size(Px(198), Px(36)),
                Checked = language.Code == (source ? SourceLanguage : TargetLanguage), Padding = Padding.Empty
            };
            item.Click += (_, _) => SelectLanguage(source, language.Code);
            menu.Items.Add(item);
        }
        _menus.Add(menu); MenuOpening?.Invoke(menu);
        var owner = source ? _source : _target;
        menu.Show(owner, new Point(0, owner.Height + Px(5)));
    }

    public void SetOriginal(string text) { _originalText = text; RefreshBody(); }
    public void SetBusy(string status)
    {
        _busy = true; _error = ""; ResultText = ""; _showOriginal = false;
        _status.Text = status; _retry.Enabled = false; RefreshBody();
    }
    public void SetResult(TranslationResult result)
    {
        _busy = false; _error = ""; ResultText = result.Text; DetectedLanguage = result.DetectedLanguage;
        _status.Text = "Google 在线翻译 · Esc 返回"; _retry.Text = "重译"; _retry.Enabled = true;
        UpdateLanguages(); RefreshBody();
    }
    public void SetError(string error)
    {
        _busy = false; _error = error; ResultText = "";
        _status.Text = "Google 在线翻译 · Esc 返回"; _retry.Text = "重试"; _retry.Enabled = true; RefreshBody();
    }
    private void RefreshBody()
    {
        var text = _showOriginal ? _originalText : _error.Length > 0 ? _error : ResultText;
        if (_body.Text != text) _body.Text = text.ReplaceLineEndings(Environment.NewLine);
        _body.ForeColor = !_showOriginal && _error.Length > 0 ? Color.FromArgb(178, 48, 42) : Ink;
        _copy.Text = "复制";
        _copy.Enabled = _showOriginal ? _originalText.Length > 0 : !_busy && _error.Length == 0 && ResultText.Length > 0;
        _original.ForeColor = _showOriginal ? _accent : Muted;
        _translated.ForeColor = !_showOriginal ? _accent : Muted;
        UpdateScrollBars();
    }
    private void UpdateScrollBars()
    {
        if (_body is null || _body.Width < 1) return;
        var height = TextRenderer.MeasureText(_body.Text, _contentFont, new Size(Math.Max(1, _body.Width - Px(20)), int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl).Height;
        var scroll = height > _body.Height ? ScrollBars.Vertical : ScrollBars.None;
        if (_body.ScrollBars != scroll) _body.ScrollBars = scroll;
    }
    public void CopyText()
    {
        var text = _body.SelectionLength > 0 ? _body.SelectedText : _showOriginal ? _originalText : _error.Length == 0 ? ResultText : "";
        if (string.IsNullOrEmpty(text)) return;
        try { Clipboard.SetText(text); _copy.Text = "已复制"; }
        catch { _status.Text = "复制失败，请重试"; }
    }
    public void CloseMenus() { foreach (var menu in _menus) if (menu.Visible) menu.Close(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CloseMenus();
            var menus = _menus.ToArray(); _menus.Clear();
            var context = SynchronizationContext.Current;
            if (context is not null) context.Post(_ => { foreach (var menu in menus) menu.Dispose(); }, null);
            else foreach (var menu in menus) menu.Dispose();
            _tips.Dispose(); _contentFont.Dispose(); _smallFont.Dispose();
        }
        base.Dispose(disposing);
    }

}
