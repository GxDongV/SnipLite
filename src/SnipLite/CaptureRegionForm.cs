namespace SnipLite;

internal sealed class CaptureRegionForm : Form
{
    private readonly NumericUpDown[] _numbers;
    public Size RegionSize => new((int)_numbers[^2].Value, (int)_numbers[^1].Value);
    public Rectangle RegionBounds => new(_numbers.Length == 4
        ? new Point((int)_numbers[0].Value, (int)_numbers[1].Value) : Point.Empty, RegionSize);

    internal static Size DefaultRegionSize(Size screen) => new(Math.Min(800, screen.Width), Math.Min(500, screen.Height));

    public CaptureRegionForm(AppSettings settings, Size? initialSize = null, Size? maximumSize = null)
    {
        var sizeOnly = initialSize.HasValue;
        Text = UiText.T("自定义截屏区域"); Font = settings.CreateInterfaceFont();
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
        ClientSize = new Size(330, sizeOnly ? 150 : 220); FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = sizeOnly ? FormStartPosition.CenterParent : FormStartPosition.CenterScreen;
        ShowInTaskbar = false; MinimizeBox = MaximizeBox = false;
        var screen = SystemInformation.VirtualScreen;
        var size = initialSize ?? DefaultRegionSize(screen.Size);
        var limit = maximumSize ?? new Size(100000, 100000);
        var labels = new[] { "X", "Y", UiText.T("宽度"), UiText.T("高度") };
        var values = new[] { screen.X, screen.Y, size.Width, size.Height };
        var maximums = new[] { 100000, 100000, limit.Width, limit.Height };
        var first = sizeOnly ? 2 : 0;
        _numbers = new NumericUpDown[4 - first];
        for (var i = first; i < 4; i++)
        {
            var y = 20 + (i - first) * 35;
            Controls.Add(new Label { Text = labels[i], Bounds = new Rectangle(24, y + 2, 70, 28) });
            var minimum = i < 2 ? -100000 : 1;
            var number = new NumericUpDown
            {
                AccessibleName = labels[i], Minimum = minimum, Maximum = maximums[i],
                Value = Math.Clamp(values[i], minimum, maximums[i]), Bounds = new Rectangle(110, y, 180, 28)
            };
            Controls.Add(number); _numbers[i - first] = number;
        }
        var buttonsY = ClientSize.Height - 50;
        var ok = new Button { Text = UiText.T(sizeOnly ? "确认" : "开始截图"), DialogResult = DialogResult.OK, Bounds = new Rectangle(110, buttonsY, 95, 30) };
        var cancel = new Button { Text = UiText.T("取消"), DialogResult = DialogResult.Cancel, Bounds = new Rectangle(215, buttonsY, 75, 30) };
        Controls.AddRange(new Control[] { ok, cancel }); AcceptButton = ok; CancelButton = cancel;
        ActiveControl = _numbers[0];
        Icon = AppIcon.Window;
        AppTheme.Apply(this);
        AppTheme.Changed += ApplyTheme;
    }
    private void ApplyTheme() { AppTheme.Apply(this); AppTheme.ApplyWindow(this); }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); AppTheme.ApplyWindow(this); }
    protected override void Dispose(bool disposing) { if (disposing) AppTheme.Changed -= ApplyTheme; base.Dispose(disposing); }
}
