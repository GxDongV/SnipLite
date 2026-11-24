namespace SnipLite.ScrollCapture;

/// <summary>Draws outside the capture area while sampling, and across it on an alignment failure.</summary>
internal sealed class CaptureOutline : Form
{
    private const int Rim = 3;
    private readonly Size _selection;
    private Rectangle[] _bestMarks = [];
    private Rectangle[] _rivalMarks = [];
    private bool _rejected;

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var parameters = base.CreateParams; parameters.ExStyle |= 0x08000000 | 0x20 | 0x80; return parameters; }
    }
    public CaptureOutline(Rectangle selection, Color accent)
    {
        _selection = selection.Size;
        AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
        Bounds = Rectangle.Inflate(selection, Rim, Rim); BackColor = accent;
        ShowAlignment(null);
    }

    internal void ShowAlignment(FrameAlignment? alignment)
    {
        _rejected = alignment?.Match.Kind is MatchKind.Uncertain or MatchKind.Reverse;
        var search = alignment?.Search;
        bool reverse = alignment?.Match.Kind == MatchKind.Reverse;
        int Row(int offset) => reverse ? offset : _selection.Height - offset;
        _bestMarks = search?.Best is { } best ? Marks(Row(best.Offset), _rejected, false) : [];
        _rivalMarks = _rejected && search?.Rival is { } rival ? Marks(Row(rival.Offset), true, true) : [];

        var rim = new Region(ClientRectangle);
        rim.Exclude(new Rectangle(Rim, Rim, _selection.Width, _selection.Height));
        foreach (var mark in _bestMarks.Concat(_rivalMarks)) rim.Union(mark);
        var previous = Region;
        Region = rim;
        previous?.Dispose();
        Invalidate();
    }

    private Rectangle[] Marks(int row, bool acrossSelection, bool dashed)
    {
        var y = Rim + row;
        if (!acrossSelection)
            return [new(0, y - Rim, Rim, Rim * 2), new(Rim + _selection.Width, y - Rim, Rim, Rim * 2)];
        if (!dashed) return [new(0, y, ClientSize.Width, 2)];

        const int dash = 12;
        return Enumerable.Range(0, (ClientSize.Width + 2 * dash - 1) / (2 * dash))
            .Select(i => i * 2 * dash)
            .Select(x => new Rectangle(x, y, Math.Min(dash, ClientSize.Width - x), 2)).ToArray();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_bestMarks.Length > 0)
        {
            using var best = new SolidBrush(_rejected ? Color.OrangeRed : Color.LimeGreen);
            e.Graphics.FillRectangles(best, _bestMarks);
        }
        if (_rivalMarks.Length > 0)
        {
            using var rival = new SolidBrush(Color.Gold);
            e.Graphics.FillRectangles(rival, _rivalMarks);
        }
    }
}
