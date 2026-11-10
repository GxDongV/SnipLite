using System.Drawing.Drawing2D;

namespace SnipLite;

/// <summary>
/// Snipaste-shaped preferences: a tab strip sitting on a bordered white page,
/// a label column followed by its controls, and every option bound straight to
/// <see cref="AppSettings"/> and applied immediately.
///
/// Layout uses one small token set (4 px grid, one label column, one control
/// height) so pages stay aligned without per-page tweaking.
/// </summary>
internal sealed partial class PreferencesForm : Form
{
    // ---- design tokens (96 dpi units) -------------------------------------
    private const int WinW = 680;
    private const int WinH = 560;
    private const int Gutter = 8;           // window gutter around the page
    private const int StripH = 32;          // tab height
    private const int StripY = 10;          // tab top
    private const int FooterH = 56;         // footer zone below the page
    private const int PagePad = 20;         // content inset inside the page
    private const int LabelW = 170;          // label column
    private const int ControlGap = 14;      // between label and control / controls
    private const int RowH = 30;            // row pitch
    private const int CheckH = 32;          // checkbox pitch
    private const int CaptionH = 24;        // group caption
    private const int SectionGap = 22;      // between groups
    private const int HintH = 24;           // hint line
    private const int ControlH = 28;

    private static readonly Color Page = Color.FromArgb(0xFF, 0xFF, 0xFF);
    private static readonly Color Chrome = Color.FromArgb(0xFA, 0xFA, 0xFA);
    private static readonly Color TabIdle = Color.FromArgb(0xF1, 0xF1, 0xF1);
    private static readonly Color TabHover = Color.FromArgb(0xF8, 0xF8, 0xF8);
    private static readonly Color Line = Color.FromArgb(0xD2, 0xD2, 0xD2);
    private static readonly Color Hair = Color.FromArgb(0xE8, 0xE8, 0xE8);
    private static readonly Color Ink = Color.FromArgb(0x1A, 0x1A, 0x1A);
    private static readonly Color Muted = Color.FromArgb(0x5F, 0x5F, 0x5F);
    private static readonly Color Hint = Color.FromArgb(0x7A, 0x7A, 0x7A);
    private static readonly Color Field = Color.FromArgb(0xF6, 0xF6, 0xF6);
    private static readonly Color FieldEdge = Color.FromArgb(0xC8, 0xC8, 0xC8);

    private readonly string[] _tabs = { "常规", "界面", "截屏", "贴图", "输出", "控制", "更新", "关于" };
    private readonly List<(Rectangle Rect, int Index)> _tabRects = new();

    private readonly AppSettings _s;
    private sealed class BufferedPage : Panel
    {
        public BufferedPage() { DoubleBuffered = true; ResizeRedraw = true; }
    }
    private readonly Panel _page = new BufferedPage();
    private readonly Button _reset = new();

    private float _ui = 1f;
    private int _selected;
    private int _startTab;
    private int _hoverTab = -1;
    private bool _captions;
    private int _groups;

    private int _y;          // current row top, page coordinates
    private int _labelX;     // x of the label column
    private int _controlX;   // x of the first control

    private bool _ready;

    /// <summary>Raised after an option changed, so the tray can re-apply hotkeys etc.</summary>
    public event Action? Applied;

    public PreferencesForm(AppSettings settings, int startTab = 0)
    {
        _s = settings;
        _startTab = startTab;

        Text = "SnipLite 首选项";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Chrome;
        Font = settings.CreateInterfaceFont();
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = true;
        DoubleBuffered = true;
        ResizeRedraw = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

        _page.BackColor = Page;
        _page.AutoScroll = true;

        StyleButton(_reset, "恢复默认");
        _reset.Click += (_, _) => ResetAll();

        Controls.Add(_page);
        Controls.Add(_reset);
    }

    // ---------------------------------------------------------------- layout

    private int Px(int value) => (int)Math.Round(value * _ui);

    private Rectangle PageRect() => new(
        Px(Gutter),
        Px(StripY + StripH),
        ClientSize.Width - Px(Gutter * 2),
        ClientSize.Height - Px(StripY + StripH) - Px(FooterH));

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        _ui = DeviceDpi / 96f;

        ClientSize = new Size(Px(WinW), Px(WinH));
        PlaceChrome();

        _ready = true;
        ShowTab(Math.Clamp(_startTab, 0, _tabs.Length - 1));
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // No control should wear a focus highlight when the dialog appears:
        // the page is a read-then-adjust surface, not a form to be typed into.
        ActiveControl = null;
    }

    private void PlaceChrome()
    {
        _page.Bounds = PageRect();

        var footer = ClientSize.Height - Px(FooterH);
        _reset.Bounds = new Rectangle(
            ClientSize.Width - Px(Gutter + PagePad) - _reset.Width,
            footer + Px(8),
            _reset.Width,
            Px(32));
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        if (!_ready) return;

        PlaceChrome();
        _tabRects.Clear();
        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            Close();
            return;
        }

        base.OnKeyDown(e);
    }

    // ----------------------------------------------------------------- paint

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (!_ready) return;

        var g = e.Graphics;
        using (var chrome = new SolidBrush(Chrome)) g.FillRectangle(chrome, e.ClipRectangle);

        var page = PageRect();

        using (var back = new SolidBrush(Page))
            g.FillRectangle(back, page);

        using (var border = new Pen(Line))
            g.DrawRectangle(border, page.X, page.Y, page.Width - 1, page.Height - 1);

        if (_tabRects.Count == 0) LayoutTabs(g);

        foreach (var (rect, index) in _tabRects)
        {
            var active = index == _selected;

            // The active tab merges into the page: one extra pixel of white
            // paints over the page's top border.
            var tab = active
                ? new Rectangle(rect.X, rect.Y, rect.Width, rect.Height + 1)
                : rect;

            using (var fill = new SolidBrush(active ? Page : index == _hoverTab ? TabHover : TabIdle))
                g.FillRectangle(fill, tab);

            using (var pen = new Pen(Line))
            {
                g.DrawLine(pen, tab.Left, tab.Top, tab.Right - 1, tab.Top);
                g.DrawLine(pen, tab.Left, tab.Top, tab.Left, tab.Bottom - 1);
                g.DrawLine(pen, tab.Right - 1, tab.Top, tab.Right - 1, tab.Bottom - 1);
            }

            TextRenderer.DrawText(g, UiText.T(_tabs[index], _s.DisplayLanguage), Font, tab,
                active ? Ink : Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        // Footer: a hairline, then identity on the left and actions on the right.
        using (var hair = new Pen(Hair))
        {
            var y = ClientSize.Height - Px(FooterH) + Px(4);
            g.DrawLine(hair, Px(Gutter), y, ClientSize.Width - Px(Gutter), y);
        }

        using var versionFont = new Font("Microsoft YaHei UI", 8f);
        TextRenderer.DrawText(g, "SnipLite " + AppInfo.Version, versionFont,
            new Rectangle(Px(Gutter + PagePad), ClientSize.Height - Px(FooterH) + Px(14), Px(240), Px(20)),
            Hint,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }

    private void LayoutTabs(Graphics g)
    {
        _tabRects.Clear();

        var x = PageRect().X;

        for (var i = 0; i < _tabs.Length; i++)
        {
            var width = TextRenderer.MeasureText(g, UiText.T(_tabs[i], _s.DisplayLanguage), Font).Width + Px(26);
            _tabRects.Add((new Rectangle(x, Px(StripY), width, Px(StripH)), i));
            x += width;
        }
    }

    // ------------------------------------------------------------ hit testing

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var hover = _tabRects.FindIndex(t => t.Rect.Contains(e.Location));

        if (hover == _hoverTab) return;

        var previous = _hoverTab;
        _hoverTab = hover;
        Cursor = hover >= 0 ? Cursors.Hand : Cursors.Default;
        InvalidateTab(previous);
        InvalidateTab(hover);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);

        if (_hoverTab < 0) return;

        var previous = _hoverTab;
        _hoverTab = -1;
        InvalidateTab(previous);
    }

    private void InvalidateTab(int index)
    {
        if (index < 0 || index >= _tabRects.Count) return;
        var rect = _tabRects[index].Rect; rect.Inflate(2, 2); rect.Height++;
        Invalidate(rect);
    }
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        if (!_ready) return;
        _tabRects.Clear(); Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != MouseButtons.Left) return;

        foreach (var (rect, index) in _tabRects)
        {
            if (!rect.Contains(e.Location)) continue;

            if (index != _selected) ShowTab(index);
            return;
        }
    }

    /// <summary>Switch pages from outside (the tray "帮助" entry opens 关于).</summary>
    public void SelectTab(int index)
    {
        if (!_ready)
        {
            _startTab = index;
            return;
        }

        index = Math.Clamp(index, 0, _tabs.Length - 1);
        if (index != _selected) ShowTab(index);
    }
    internal void SelectSubPage(int tab, int sub) { _subPages[tab] = sub; ShowTab(tab); }

    // ----------------------------------------------------------------- pages

    private void ShowTab(int index)
    {
        if (!_ready) return;

        _selected = index;
        _tabRects.Clear();
        _sectionProperties.Clear();
        _page.AutoScrollPosition = Point.Empty;

        _page.SuspendLayout();

        while (_page.Controls.Count > 0)
            _page.Controls[0].Dispose();

        _y = Px(PagePad);
        _labelX = Px(PagePad);
        _controlX = _labelX + Px(LabelW + ControlGap);
        _groups = 0;

        // Pages with a single group need no caption repeating the tab name.
        _captions = index is 0 or 1 or 4;

        switch (index)
        {
            case 0: BuildGeneral(); break;
            case 1: BuildInterface(); break;
            case 2: BuildCapture(); break;
            case 3: BuildPin(); break;
            case 4: BuildOutput(); break;
            case 5: BuildControl(); break;
            case 6: BuildUpdate(); break;
            default: BuildAbout(); break;
        }

        _page.AutoScrollMinSize = new Size(0, _y + Px(PagePad));
        _page.ResumeLayout();
        UiText.Apply(_page, _s.DisplayLanguage);
        _reset.Visible = index <= 5;
        ActiveControl = null;
        Invalidate();
    }

    private void BuildGeneral() => GeneralPage();
    private void BuildInterface() => SubPages(new[] { "常规", "放大镜" }, InterfacePage, MagnifierPage);
    private void BuildCapture() => SubPages(new[] { "显示", "行为", "按键" }, CaptureDisplayPage, CaptureBehaviorPage, CaptureKeysPage);
    private void BuildPin() => SubPages(new[] { "显示", "行为", "转换" }, PinDisplayPage, PinBehaviorPage, PinConversionPage);
    private void BuildOutput() => SubPages(new[] { "文件", "剪贴板" }, OutputFilePage, OutputClipboardPage);
    private void BuildControl() => SubPages(new[] { "全局快捷键", "鼠标" }, HotkeysPage, MousePage);
    private void BuildUpdate()
    {
        Row("当前版本", MakeStatic(AppInfo.Version));
        Row("发布版本", MakeWebLink(AppInfo.ReleasesUrl));
        HintText("目前通过本地程序文件更新，设置会自动保留。");
    }
    private void BuildAbout()
    {
        Row("程序", MakeStatic("SnipLite " + AppInfo.Version));
        var executable = MakeText(Application.ExecutablePath, FillWidth()); executable.ReadOnly = true; Row("程序文件", executable);
        Row("运行环境", MakeStatic(".NET 10 · WinForms")); Row("识别引擎", MakeStatic("Windows OCR · ZXing.Net"));
        Row("GitHub 项目", MakeWebLink(AppInfo.RepositoryUrl));
        Row("问题反馈", MakeWebLink(AppInfo.IssuesUrl));
        var license = new LinkLabel { Text = "MIT License / Third-party notices", AutoSize = true,
            LinkColor = _s.Accent, BackColor = Page, Cursor = Cursors.Hand, TabStop = true };
        license.LinkClicked += (_, _) => LicenseNotices.Show(this);
        Row("开源许可证", license);
        HintText("截图、贴图、标注、文字与二维码识别均在本机处理。");
    }

    private LinkLabel MakeWebLink(string url)
    {
        var link = new LinkLabel
        {
            Text = url, AutoSize = false, AutoEllipsis = true,
            Size = new Size(FillWidth(), Px(24)), TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Page, LinkColor = _s.Accent, ActiveLinkColor = _s.Accent,
            VisitedLinkColor = _s.Accent, LinkBehavior = LinkBehavior.HoverUnderline,
            Cursor = Cursors.Hand, TabStop = true
        };
        link.LinkClicked += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { AppLog.Write("打开项目网址失败", ex); Toast.Error("无法打开浏览器，请手动访问：" + url); }
        };
        return link;
    }
    // ------------------------------------------------------------- sections

    /// <summary>Width of the page's content column.</summary>
    private int ContentWidth() => PageRect().Width - Px(PagePad * 2) - SystemInformation.VerticalScrollBarWidth;

    /// <summary>Width left for a field that starts in the control column.</summary>
    private int FillWidth() => ContentWidth() - Px(LabelW + ControlGap);

    /// <summary>
    /// A named group of rows. Pages with more than one group show a caption and
    /// breathe a little; single-group pages start straight at the page padding.
    /// </summary>
    private void Section(string title, Action content)
    {
        if (_groups++ > 0) _y += Px(SectionGap);

        if (_captions)
        {
            _page.Controls.Add(new Label
            {
                Text = title,
                AutoSize = false,
                Bounds = new Rectangle(_labelX, _y, ContentWidth(), Px(18)),
                ForeColor = Muted,
                BackColor = Page,
                TextAlign = ContentAlignment.MiddleLeft
            });

            _y += Px(CaptionH);
        }

        content();
    }

    // --------------------------------------------------------------- helpers

    private void Change(Action apply)
    {
        apply();
        Persist();
    }

    private void Persist()
    {
        _s.Save();
        Applied?.Invoke();
    }

    private void ResetAll()
    {
        var defaults = AppSettings.Reset();
        foreach (var name in _sectionProperties)
        {
            var property = typeof(AppSettings).GetProperty(name)!;
            property.SetValue(_s, property.GetValue(defaults));
        }
        if (_selected == 0 && !Startup.TryApply(_s.StartWithWindows, _s.StartAsAdministrator))
            Toast.Error("开机启动设置未修改，请重试。");
        Font = _s.CreateInterfaceFont();
        Toast.Notifications = _s.ShowToast;
        Persist();
        ShowTab(_selected);
    }
    private void Row(string label, params Control[] controls)
    {
        AddLabel(label, _labelX, _y, LabelW);

        var x = _controlX;

        foreach (var control in controls)
        {
            control.Location = new Point(x, _y + Math.Max(0, (Px(RowH) - control.Height) / 2));
            _page.Controls.Add(control);
            x += control.Width + Px(ControlGap);
        }

        _y += Px(RowH);
    }

    /// <summary>A row whose first control stretches to fill the remaining width.</summary>
    private void RowFill(string label, Control fill, params Control[] trailing)
    {
        var used = 0;

        foreach (var control in trailing)
            used += control.Width + Px(ControlGap);

        fill.Width = Math.Max(Px(120), FillWidth() - used);

        var all = new Control[trailing.Length + 1];
        all[0] = fill;
        trailing.CopyTo(all, 1);

        Row(label, all);
    }

    private void Check(string text, bool value, Action<bool> onChange)
    {
        var box = new CheckBox
        {
            Text = text,
            AutoSize = false,
            Bounds = new Rectangle(_labelX, _y, ContentWidth(), Px(24)),
            BackColor = Page,
            ForeColor = Ink,
            Checked = value
        };

        box.CheckedChanged += (_, _) => onChange(box.Checked);

        _page.Controls.Add(box);
        _y += Px(CheckH);
    }

    private void AddLabel(string text, int x, int y, int width)
    {
        _page.Controls.Add(new Label
        {
            Text = text,
            AutoSize = false,
            Bounds = new Rectangle(x, y, Px(width), Px(RowH)),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Ink,
            BackColor = Page
        });
    }

    private void HintText(string text)
    {
        _y += Px(4);

        _page.Controls.Add(new Label
        {
            Text = text,
            AutoSize = false,
            Bounds = new Rectangle(_labelX, _y, ContentWidth(), Px(22)),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Hint,
            BackColor = Page
        });

        _y += Px(HintH);
    }

    private static Control MakeStatic(string text) => new Label
    {
        Text = text,
        AutoSize = true,
        ForeColor = Ink,
        BackColor = Page,
        Margin = Padding.Empty
    };

    /// <summary>
    /// A combo box drawn the way the rest of the app looks: a flat grey field
    /// that opens the same menu the tray uses. The native flat ComboBox loses
    /// its text while unfocused, and its blue selection fights the palette.
    /// </summary>
    private Button MakeChooser(string[] options, int selected, Action<int> onChange)
    {
        var button = new Button
        {
            Text = options[Math.Clamp(selected, 0, options.Length - 1)],
            AutoSize = false,
            FlatStyle = FlatStyle.Flat,
            BackColor = Field,
            ForeColor = Ink,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(Px(8), 0, Px(26), 0),
            Width = Px(168),
            Height = Px(ControlH),
            TabStop = false
        };

        button.FlatAppearance.BorderColor = FieldEdge;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(0xEC, 0xEC, 0xEC);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(0xE0, 0xE0, 0xE0);

        button.Paint += (_, e) =>
        {
            var x = button.Width - Px(14);
            var y = button.Height / 2 + Px(1);
            var arm = Px(4);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using var pen = new Pen(Muted, 1.4f);
            e.Graphics.DrawLines(pen, new[]
            {
                new Point(x - arm, y - Px(2)),
                new Point(x, y + Px(2)),
                new Point(x + arm, y - Px(2))
            });
        };

        // One menu for the life of the chooser, owned by the button so it is
        // disposed together with it. Re-creating (and disposing) the menu on
        // every click upsets ToolStripManager's message filter.
        var menu = SnipMenu.Create();
        var items = new ToolStripMenuItem[options.Length];

        button.ContextMenuStrip = menu;

        for (var i = 0; i < options.Length; i++)
        {
            var index = i;

            items[i] = SnipMenu.Item(options[i], () =>
            {
                button.Text = options[index];
                onChange(index);
            });

            menu.Items.Add(items[i]);
        }

        menu.Opening += (_, _) =>
        {
            for (var i = 0; i < options.Length; i++)
                items[i].Checked = options[i] == button.Text;

            menu.MinimumSize = new Size(button.Width, 0);
        };

        button.Click += (_, _) => menu.Show(button, new Point(0, button.Height + Px(2)));
        return button;
    }

    private NumericUpDown MakeNumber(int min, int max, int value, int width, Action<int> onChange)
    {
        var number = new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            Width = Px(width),
            BackColor = Page,
            ForeColor = Ink,
            TextAlign = HorizontalAlignment.Right
        };

        number.ValueChanged += (_, _) => onChange((int)number.Value);

        return number;
    }

    private TextBox MakeText(string value, int width) => new()
    {
        Text = value,
        Width = width,
        BackColor = Page,
        ForeColor = Ink,
        BorderStyle = BorderStyle.FixedSingle
    };

    private void StyleButton(Button button, string text)
    {
        button.Text = text;
        button.AutoSize = false;
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = Field;
        button.ForeColor = Ink;
        button.FlatAppearance.BorderColor = FieldEdge;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(0xEC, 0xEC, 0xEC);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(0xE0, 0xE0, 0xE0);
        button.Height = Px(28);
        button.Width = TextRenderer.MeasureText(text, Font).Width + Px(24);
    }

    private void AddButton(int x, string text, Action action)
    {
        var button = new Button();
        StyleButton(button, text);
        button.Bounds = new Rectangle(x, _y, button.Width, Px(28));
        button.Click += (_, _) => action();

        _page.Controls.Add(button);
        _y += Px(RowH + 2);
    }

    private static string Hex(Color color) =>
        "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");

    private void Open(string path, bool revealFile)
    {
        try
        {
            if (revealFile)
            {
                System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\"");
                return;
            }

            Directory.CreateDirectory(path);
            System.Diagnostics.Process.Start("explorer.exe", path);
        }
        catch
        {
        }
    }
}
