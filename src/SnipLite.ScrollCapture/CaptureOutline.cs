namespace SnipLite.ScrollCapture;

/// <summary>The window has only an outside rim; the selected pixels and input remain untouched.</summary>
internal sealed class CaptureOutline : Form
{
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var parameters = base.CreateParams; parameters.ExStyle |= 0x08000000 | 0x20 | 0x80; return parameters; }
    }
    public CaptureOutline(Rectangle selection, Color accent)
    {
        AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
        Bounds = Rectangle.Inflate(selection, 3, 3); BackColor = accent;
        var rim = new Region(ClientRectangle);
        rim.Exclude(new Rectangle(3, 3, selection.Width, selection.Height)); Region = rim;
    }
}
