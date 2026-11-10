namespace SnipLite;

internal sealed class CaptureRegionForm : Form
{
    private readonly NumericUpDown[] _numbers;
    public Rectangle RegionBounds => new((int)_numbers[0].Value, (int)_numbers[1].Value, (int)_numbers[2].Value, (int)_numbers[3].Value);
    public CaptureRegionForm(AppSettings settings)
    {
        Text = "自定义截屏区域"; Font = settings.CreateInterfaceFont();
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
        ClientSize = new Size(330, 220); FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen; ShowInTaskbar = false; MinimizeBox = MaximizeBox = false;
        var screen = SystemInformation.VirtualScreen;
        var initial = settings.RecentRegions.FirstOrDefault()?.Bounds ?? new Rectangle(screen.X, screen.Y, Math.Min(800, screen.Width), Math.Min(500, screen.Height));
        var labels = new[] { "X", "Y", "宽度", "高度" }; var values = new[] { initial.X, initial.Y, initial.Width, initial.Height };
        _numbers = new NumericUpDown[4];
        for (var i = 0; i < 4; i++)
        {
            Controls.Add(new Label { Text = labels[i], Bounds = new Rectangle(24, 22 + i * 35, 70, 28) });
            var number = new NumericUpDown { Minimum = i < 2 ? -100000 : 1, Maximum = 100000, Value = values[i], Bounds = new Rectangle(110, 20 + i * 35, 180, 28) };
            Controls.Add(number); _numbers[i] = number;
        }
        var ok = new Button { Text = "开始截图", DialogResult = DialogResult.OK, Bounds = new Rectangle(110, 170, 95, 30) };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(215, 170, 75, 30) };
        Controls.AddRange(new Control[] { ok, cancel }); AcceptButton = ok; CancelButton = cancel;
    }
}
