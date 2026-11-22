using System.Drawing.Imaging;

namespace SnipLite.Plugins;

internal sealed class PluginRegionForm : Form
{
    private readonly Bitmap _desktop;
    private Point? _anchor;
    private Rectangle _local;
    public Rectangle Selection => new(PointToScreen(_local.Location), _local.Size);

    public PluginRegionForm()
    {
        Text = PluginUi.Text("Select capture region", "选择截图区域", "選擇截圖區域");
        var desktop = SystemInformation.VirtualScreen;
        _desktop = new Bitmap(desktop.Width, desktop.Height, PixelFormat.Format32bppPArgb);
        try { using var g = Graphics.FromImage(_desktop); g.CopyFromScreen(desktop.Location, Point.Empty, desktop.Size); }
        catch { _desktop.Dispose(); throw; }
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual; Bounds = desktop;
        AutoScaleMode = AutoScaleMode.None; TopMost = true; DoubleBuffered = true; KeyPreview = true; Cursor = Cursors.Cross;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.DrawImageUnscaled(_desktop, Point.Empty);
        using var dim = new SolidBrush(Color.FromArgb(125, Color.Black));
        using var outside = new Region(ClientRectangle);
        if (!_local.IsEmpty) outside.Exclude(_local);
        e.Graphics.FillRegion(dim, outside);
        if (!_local.IsEmpty)
        {
            using var pen = new Pen(Color.DodgerBlue, 2);
            e.Graphics.DrawRectangle(pen, _local);
        }
        var text = PluginUi.Text("Drag to select content. Exclude fixed headers, footers and scrollbars. Esc: cancel.",
            "拖动选择内容区域，请排除固定头尾和滚动条。Esc：取消。", "拖曳選擇內容區域，請排除固定頭尾和捲軸。Esc：取消。");
        var location = PointToClient(Screen.FromPoint(Cursor.Position).WorkingArea.Location);
        TextRenderer.DrawText(e.Graphics, text, SystemFonts.MessageBoxFont,
            new Rectangle(location.X + 20, location.Y + 20, Math.Min(850, ClientSize.Width - 40), 55), Color.White, Color.FromArgb(40, 40, 40),
            TextFormatFlags.WordBreak | TextFormatFlags.VerticalCenter);
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right) { Close(); return; }
        if (e.Button == MouseButtons.Left) { _anchor = e.Location; Capture = true; }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_anchor is not Point anchor) return;
        _local = Rectangle.Intersect(ClientRectangle, Rectangle.FromLTRB(Math.Min(anchor.X, e.X), Math.Min(anchor.Y, e.Y), Math.Max(anchor.X, e.X), Math.Max(anchor.Y, e.Y)));
        Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _anchor is null) return;
        OnMouseMove(e); _anchor = null; Capture = false;
        if (_local.Width >= 64 && _local.Height >= 80 && (long)_local.Width * _local.Height <= 16_000_000)
        { DialogResult = DialogResult.OK; Close(); }
        else { _local = Rectangle.Empty; Invalidate(); }
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    protected override void Dispose(bool disposing) { if (disposing) _desktop.Dispose(); base.Dispose(disposing); }
}
