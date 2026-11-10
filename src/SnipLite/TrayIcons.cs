namespace SnipLite;

internal static class TrayIcons
{
    public static Icon LoadCustom(string path)
    {
        if (Path.GetExtension(path).Equals(".ico", StringComparison.OrdinalIgnoreCase)) return new Icon(path, 32, 32);
        using var image = new Bitmap(path);
        using var bitmap = new Bitmap(image, new Size(32, 32));
        var handle = bitmap.GetHicon();
        try { using var borrowed = Icon.FromHandle(handle); return (Icon)borrowed.Clone(); }
        finally { DestroyIcon(handle); }
    }
    public static Icon Create(AppSettings settings)
    {
        if (settings.TrayIconStyle == "Custom" && File.Exists(settings.CustomTrayIcon))
        { try { return LoadCustom(settings.CustomTrayIcon); } catch (Exception ex) { AppLog.Write("自定义托盘图标读取失败", ex); } }
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var background = new SolidBrush(settings.TrayIconStyle == "Color" ? settings.Accent : Color.FromArgb(0, 174, 255));
            g.FillRectangle(background, 3, 3, 26, 26);
            using var pen = new Pen(Color.White, 3); g.DrawRectangle(pen, 10, 10, 12, 12);
        }
        var handle = bitmap.GetHicon();
        try { using var borrowed = Icon.FromHandle(handle); return (Icon)borrowed.Clone(); }
        finally { DestroyIcon(handle); }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
}
