using System.Runtime.InteropServices;

namespace SnipLite;

internal static class Win32
{
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint GW_OWNER = 4;

    public static readonly IntPtr HWND_TOPMOST = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint command);

    /// <summary>Raw cursor position in physical screen pixels, no DPI translation.</summary>
    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT point);

    public static Point CursorPosition()
    {
        GetCursorPos(out var p);
        return new Point(p.X, p.Y);
    }

    [StructLayout(LayoutKind.Sequential)] private struct CURSORINFO { public int Size, Flags; public IntPtr Icon; public POINT Position; }
    [StructLayout(LayoutKind.Sequential)] private struct ICONINFO { [MarshalAs(UnmanagedType.Bool)] public bool Icon; public uint XHotspot, YHotspot; public IntPtr Mask, Color; }
    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CURSORINFO info);
    [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr icon, out ICONINFO info);
    [DllImport("user32.dll")] private static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int width, int height, uint step, IntPtr brush, uint flags);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("user32.dll")] private static extern IntPtr CopyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);

    public static void DrawCursor(Graphics graphics, Point screenOrigin)
    {
        var cursor = new CURSORINFO { Size = Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(ref cursor) || cursor.Flags != 1) return;
        DrawCursorIcon(graphics, cursor.Icon, new Point(cursor.Position.X, cursor.Position.Y), screenOrigin);
    }

    internal static CursorSnapshot? CaptureCursor()
    {
        var cursor = new CURSORINFO { Size = Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(ref cursor) || cursor.Flags != 1) return null;
        var icon = CopyIcon(cursor.Icon);
        return icon == IntPtr.Zero ? null : new CursorSnapshot(icon, new Point(cursor.Position.X, cursor.Position.Y));
    }

    private static void DrawCursorIcon(Graphics graphics, IntPtr cursor, Point position, Point screenOrigin)
    {
        if (!GetIconInfo(cursor, out var icon)) return;
        try
        {
            var dc = graphics.GetHdc();
            try
            {
                DrawIconEx(dc, position.X - screenOrigin.X - (int)icon.XHotspot,
                    position.Y - screenOrigin.Y - (int)icon.YHotspot, cursor, 0, 0, 0, IntPtr.Zero, 3);
            }
            finally { graphics.ReleaseHdc(dc); }
        }
        finally
        {
            if (icon.Mask != IntPtr.Zero) DeleteObject(icon.Mask);
            if (icon.Color != IntPtr.Zero) DeleteObject(icon.Color);
        }
    }

    internal sealed class CursorSnapshot(IntPtr icon, Point position) : IDisposable
    {
        private IntPtr _icon = icon;

        internal void Draw(Graphics graphics, Point screenOrigin) => DrawCursorIcon(graphics, _icon, position, screenOrigin);

        public void Dispose()
        {
            if (_icon == IntPtr.Zero) return;
            DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }
    }
}
