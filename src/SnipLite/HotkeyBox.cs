namespace SnipLite;

internal static class HotkeyDefinitions
{
    public static readonly (string Label, string Name)[] Items =
    {
        ("截屏", nameof(AppSettings.CaptureHotkey)), ("截屏并自动复制", nameof(AppSettings.CopyCaptureHotkey)),
        ("自定义截屏", nameof(AppSettings.CustomCaptureHotkey)), ("贴图", nameof(AppSettings.PinHotkey)),
        ("隐藏 / 显示所有贴图", nameof(AppSettings.TogglePinsHotkey)), ("切换到另一贴图分组", nameof(AppSettings.SwitchGroupHotkey)),
        ("鼠标穿透开关", nameof(AppSettings.MouseThroughHotkey)), ("文字识别", nameof(AppSettings.OcrHotkey)), ("二维码识别", nameof(AppSettings.QrHotkey))
    };
    public static string Format(Keys key) => key == Keys.None ? "未设置" : new KeysConverter().ConvertToString(key) ?? key.ToString();
}

internal sealed class HotkeyBox : TextBox
{
    private Keys _binding;
    public event Action<Keys>? BindingChanged;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Keys Binding { get => _binding; set { _binding = value; Text = HotkeyDefinitions.Format(value); } }
    public HotkeyBox() { ReadOnly = true; ShortcutsEnabled = false; TextAlign = HorizontalAlignment.Center; }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        if (key == Keys.Tab) return base.ProcessCmdKey(ref msg, keyData);
        if (key is Keys.ShiftKey or Keys.ControlKey or Keys.Menu or Keys.LWin or Keys.RWin) return true;
        Binding = key is Keys.Back or Keys.Delete ? Keys.None : keyData;
        BindingChanged?.Invoke(Binding);
        return true;
    }
}
