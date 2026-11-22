using System.Runtime.InteropServices;

namespace SnipLite;

/// <summary>A native mouse I-beam whose stem follows the text's physical line height.</summary>
internal sealed class TextIBeamCursor : IDisposable
{
    private IntPtr _handle;
    public Cursor Cursor { get; }
    public int Height { get; }
    public Size NativeSize { get; }

    public TextIBeamCursor(int height)
    {
        Height = Math.Clamp(height, 8, 1024);
        var arm = Math.Clamp(Height / 6, 3, 8);
        using var bitmap = new Bitmap(arm * 2 + 5, Height + 4);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            var center = arm + 2;
            var bottom = Height + 1;
            foreach (var (ink, width) in new[] { (Color.White, 3f), (Color.Black, 1f) })
            {
                using var pen = new Pen(ink, width);
                graphics.DrawLine(pen, center, 2, center, bottom);
                graphics.DrawLine(pen, center - arm, 2, center + arm, 2);
                graphics.DrawLine(pen, center - arm, bottom, center + arm, bottom);
            }
        }
        var icon = bitmap.GetHicon();
        try
        {
            if (!GetIconInfo(icon, out var info)) throw new System.ComponentModel.Win32Exception();
            try
            {
                info.Icon = false; info.HotspotX = bitmap.Width / 2; info.HotspotY = bitmap.Height / 2;
                _handle = CreateIconIndirect(ref info);
                if (_handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
            }
            finally { DeleteObject(info.Mask); DeleteObject(info.Color); }
        }
        finally { DestroyIcon(icon); }
        Cursor = new Cursor(_handle);
        if (GetIconInfo(_handle, out var native))
        {
            try { using var pixels = Image.FromHbitmap(native.Color); NativeSize = pixels.Size; }
            finally { DeleteObject(native.Mask); DeleteObject(native.Color); }
        }
    }

    internal void Draw(Graphics graphics, Point point)
    {
        var dc = graphics.GetHdc();
        try { DrawIconEx(dc, point.X, point.Y, _handle, NativeSize.Width, NativeSize.Height, 0, IntPtr.Zero, 3); }
        finally { graphics.ReleaseHdc(dc); }
    }

    public void Dispose()
    {
        Cursor.Dispose();
        if (_handle != IntPtr.Zero) { DestroyCursor(_handle); _handle = IntPtr.Zero; }
    }
    [StructLayout(LayoutKind.Sequential)] private struct ICONINFO
    {
        [MarshalAs(UnmanagedType.Bool)] public bool Icon;
        public int HotspotX, HotspotY;
        public IntPtr Mask, Color;
    }
    [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr icon, out ICONINFO info);
    [DllImport("user32.dll")] private static extern IntPtr CreateIconIndirect(ref ICONINFO info);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern bool DestroyCursor(IntPtr cursor);
    [DllImport("user32.dll")] private static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int width, int height, uint step, IntPtr brush, uint flags);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
}
