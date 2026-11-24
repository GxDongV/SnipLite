using System.Runtime.InteropServices;

namespace SnipLite.ScrollCapture;

/// <summary>Sends wheel messages to the selected control without moving or capturing the pointer.</summary>
internal sealed class AutoScrollController : IDisposable
{
    private readonly IScrollTarget _target;
    private bool _started;
    public string Failure { get; private set; } = "";
    public bool CanRetry => _target.CanRetry;

    public AutoScrollController(Rectangle region) : this(new WindowScrollTarget(region)) { }
    internal AutoScrollController(IScrollTarget target) => _target = target;

    public bool Start()
    {
        _started = true;
        return CheckTarget();
    }

    public bool Step(int notches = 1)
    {
        if (notches is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(notches));
        if (!CheckTarget()) return false;
        if (!_target.SendWheel(notches)) return Reject(_target.Failure);
        Failure = "";
        return true;
    }

    public bool CheckTarget()
    {
        if (!_started) return Reject("Automatic scrolling is not started.");
        if (!_target.IsAvailable()) return Reject(_target.Failure);
        Failure = "";
        return true;
    }

    private bool Reject(string reason) { Failure = reason; return false; }
    public void Dispose() => _started = false;
}

internal interface IScrollTarget
{
    bool IsAvailable();
    bool SendWheel(int notches);
    string Failure => "The target window is unavailable.";
    bool CanRetry => false;
}

/// <summary>No scrolling or sampling during temporary occlusion; a bounded wait avoids false pauses.</summary>
internal sealed class ScrollTargetWait
{
    internal const int GraceMilliseconds = 2000;
    private long? _failedAt;
    internal bool ShouldWait(bool retryable, long now)
    {
        if (!retryable) { Reset(); return false; }
        _failedAt ??= now;
        return now - _failedAt.Value < GraceMilliseconds;
    }
    internal void Reset() => _failedAt = null;
}

/// <summary>Calibrates one small step, then targets half a viewport of overlap-preserving movement.</summary>
internal sealed class ScrollStepSize(int viewportHeight)
{
    private double _pixelsPerNotch;
    private double _upperPixelsPerNotch;
    private int _pendingNotches;
    public int Notches { get; private set; } = 1;
    public bool CanProbe => _pendingNotches > 0 && _upperPixelsPerNotch > 0 &&
        EstimatedOverlapAfterProbe >= FrameMatcher.MinimumOverlap(viewportHeight);
    public int EstimatedOffset => (int)Math.Round(_pendingNotches * _pixelsPerNotch);
    public int EstimatedOverlapAfterProbe => viewportHeight - (int)Math.Ceiling((_pendingNotches + 1) * _upperPixelsPerNotch);

    public void Reset()
    {
        Notches = 1;
        _pixelsPerNotch = _upperPixelsPerNotch = 0;
        ConfirmPosition();
    }

    public void Issued(int notches) => _pendingNotches += notches;
    public void ConfirmPosition() => _pendingNotches = 0;

    public void Observe(int offset)
    {
        if (offset <= 0 || _pendingNotches == 0) return;
        if (_pendingNotches == Notches || _pixelsPerNotch == 0 || offset > EstimatedOffset)
        {
            _pixelsPerNotch = (double)offset / _pendingNotches;
            // Keep a margin for variable wheel distances. Measure longer steps over
            // all their notches and keep earlier high estimates for this session.
            _upperPixelsPerNotch = Math.Max(_upperPixelsPerNotch, _pixelsPerNotch * 1.1);
        }
        ConfirmPosition();
        var desired = Math.Clamp((int)(viewportHeight / (2 * _pixelsPerNotch)), 1, 6);
        Notches = Math.Min(Notches + 2, desired);
    }
}

internal interface IScrollWindowApi
{
    nint At(Point point);
    nint Root(nint window);
    bool Exists(nint window);
    bool Visible(nint window);
    bool Minimized(nint window);
    Rectangle? ClientBounds(nint window);
    bool Wheel(nint control, Point point, int notches, out int error);
}

internal sealed class WindowScrollTarget : IScrollTarget
{
    private readonly Point _point;
    private nint _control;
    private readonly nint _window;
    private readonly Rectangle? _originalClient;
    private readonly IScrollWindowApi _api;
    public nint Window => _window;
    public string Failure { get; private set; } = "";
    public bool CanRetry { get; private set; }
    internal string Description => $"window=0x{_window:X}; control=0x{_control:X}; point={_point}; client={_originalClient}";

    public WindowScrollTarget(Rectangle region) : this(region, new NativeScrollWindowApi()) { }
    internal WindowScrollTarget(Rectangle region, IScrollWindowApi api)
    {
        _api = api;
        // A point near the right edge can hit a scrollbar/sidebar instead of the content.
        // Bind before the capture toolbar is shown, and keep this root for the whole session.
        _point = new(region.Left + region.Width / 2, region.Top + region.Height / 2);
        _control = _api.At(_point);
        _window = _api.Root(_control);
        _originalClient = _api.ClientBounds(_window);
    }

    public bool IsAvailable()
    {
        if (_window == 0 || !_api.Exists(_window) || !_api.Visible(_window)) return Reject("Target window closed or hidden.");
        if (_api.Minimized(_window)) return Reject("Target window minimized.");
        var bounds = _api.ClientBounds(_window);
        if (_originalClient is null || bounds is null || bounds != _originalClient)
            return Reject($"Target content moved or resized: original={_originalClient}; current={bounds}.");
        var current = _api.At(_point);
        if (current == 0 || _api.Root(current) != _window)
            return Reject($"Target scroll point temporarily covered: hit=0x{current:X}; expected=0x{_window:X}.", true);
        // Browsers and other applications can recreate native content children during scrolling.
        // Re-resolve the child, but never switch to another root window under the selection.
        _control = current;
        Failure = ""; CanRetry = false; return true;
    }

    public bool SendWheel(int notches)
    {
        if (!IsAvailable()) return false;
        if (_api.Wheel(_control, _point, notches, out var error)) { Failure = ""; CanRetry = false; return true; }
        // Access denied cannot be fixed by retrying; a busy target gets the same bounded grace as occlusion.
        return Reject($"Scroll message failed: win32={error}; {Description}.", error is 0 or 1460);
    }

    private bool Reject(string reason, bool retryable = false) { Failure = reason; CanRetry = retryable; return false; }
}

internal sealed class NativeScrollWindowApi : IScrollWindowApi
{
    public nint At(Point point) => WindowFromPoint(new(point.X, point.Y));
    public nint Root(nint window) => GetAncestor(window, 2);
    public bool Exists(nint window) => IsWindow(window);
    public bool Visible(nint window) => IsWindowVisible(window);
    public bool Minimized(nint window) => IsIconic(window);
    public Rectangle? ClientBounds(nint window)
    {
        var origin = new NativePoint();
        return GetClientRect(window, out var rect) && ClientToScreen(window, ref origin)
            ? new Rectangle(origin.X, origin.Y, rect.Right - rect.Left, rect.Bottom - rect.Top) : null;
    }
    public bool Wheel(nint control, Point point, int notches, out int error)
    {
        // WM_MOUSEWHEEL carries screen coordinates, including signed monitor positions.
        var coordinates = unchecked((int)((uint)(ushort)point.X | ((uint)(ushort)point.Y << 16)));
        var delta = unchecked((nuint)(uint)((-120 * notches) << 16));
        Marshal.SetLastPInvokeError(0);
        var sent = SendMessageTimeout(control, 0x020A, delta, coordinates, 0x0001 | 0x0002 | 0x0020, 250, out _) != 0;
        error = sent ? 0 : Marshal.GetLastPInvokeError(); return sent;
    }

    [StructLayout(LayoutKind.Sequential)] private readonly record struct NativePoint(int X, int Y);
    [StructLayout(LayoutKind.Sequential)] private readonly record struct NativeRect(int Left, int Top, int Right, int Bottom);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint window, ref NativePoint point);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    private static extern nint SendMessageTimeout(nint window, uint message, nuint wParam, nint lParam, uint flags, uint timeout, out nuint result);
}
