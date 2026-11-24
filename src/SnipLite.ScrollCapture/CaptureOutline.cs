namespace SnipLite.ScrollCapture;

/// <summary>Draws a border outside the capture area.</summary>
internal sealed class CaptureOutline : Form
{
    private const int Rim = 3;

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var parameters = base.CreateParams; parameters.ExStyle |= 0x08000000 | 0x20 | 0x80; return parameters; }
    }
    public CaptureOutline(Rectangle selection, Color accent)
    {
        AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
        Bounds = Rectangle.Inflate(selection, Rim, Rim); BackColor = accent;
        var rim = new Region(ClientRectangle);
        rim.Exclude(new Rectangle(Rim, Rim, selection.Width, selection.Height));
        Region = rim;
    }
}
