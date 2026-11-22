using System.Runtime.InteropServices;
using SnipLite.Plugin.Abstractions;

namespace SnipLite.ScrollCapture;

internal class ScrollCaptureForm : Form
{
    private readonly IPluginHost _host;
    private readonly Rectangle _region;
    private readonly ScrollStepSize _stepSize;
    private readonly ScrollTargetWait _targetWait = new();
    private readonly IScrollTarget _scrollTarget;
    private bool _waitingForTarget;
    private readonly CancellationTokenSource _lifetime;
    private readonly CancellationTokenRegistration _cancellation;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 180 };
    private readonly ScrollStatus _status = new();
    private readonly ScrollButton _pause = new() { Glyph = "pause" };
    private readonly ScrollButton _undo = new() { Glyph = "undo" };
    private readonly ScrollButton _finish = new() { Glyph = "finish", Primary = true };
    private readonly ScrollButton _cancel = new() { Glyph = "close" };
    private readonly ScrollToggle _automatic = new() { Checked = true };
    private readonly ScrollCard _card = new();
    private readonly ToolTip _tips = new();
    private readonly Font _uiFont = new("Microsoft YaHei UI", 9f);
    private AutoScrollController? _scroller;
    private bool _awaitingScroll;
    private long _scrollIssuedAt;
    private int _unchangedAttempts;
    private int _uncertainFrames;
    private bool _completeAutomatically;
    private bool _finishRequested;
    private ScrollImageStore? _store;
    private FrameSample? _pending;
    private PluginImage? _result;
    private CaptureOutline? _outline;
    private bool _paused;
    private bool _busy;
    private bool _closeRequested;
    private int _generation;

    private string T(string en, string zh, string? tw = null) => new PluginText(en, zh, tw).Get(_host.Language);
    protected override bool ShowWithoutActivation => true;

    public ScrollCaptureForm(IPluginHost host, Rectangle region, CancellationToken cancellationToken, IScrollTarget? scrollTarget = null)
    {
        _host = host; _region = region; _stepSize = new ScrollStepSize(region.Height);
        _scrollTarget = scrollTarget ?? new WindowScrollTarget(region);
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // All bounds are computed in device pixels below. Avoid a second WinForms
        // scaling pass changing the compact toolbar's layout on first display.
        AutoScaleMode = AutoScaleMode.None;
        Text = T("Scrolling screenshot", "长截图", "捲動截圖");
        Font = _uiFont;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false; TopMost = true;
        StartPosition = FormStartPosition.Manual; KeyPreview = true;
        _card.Dock = DockStyle.Fill;
        _pause.Text = T("Pause", "暂停", "暫停");
        _undo.Text = T("Undo", "撤销", "復原");
        _finish.Text = T("Finish", "完成");
        _cancel.Text = T("Cancel", "取消");
        _automatic.Text = T("Auto scroll", "自动滚动", "自動捲動");
        _automatic.TabIndex = 0; _pause.TabIndex = 1; _undo.TabIndex = 2; _finish.TabIndex = 3; _cancel.TabIndex = 4;
        _tips.SetToolTip(_undo, T("Undo last strip", "撤销上一段", "復原上一段"));
        _tips.SetToolTip(_cancel, T("Cancel capture (Esc)", "取消截图（Esc）", "取消截圖（Esc）"));
        _card.Controls.AddRange(new Control[] { _automatic, _pause, _undo, _finish, _cancel, _status });
        Controls.Add(_card);
        SizeForDpi();
        SetStatus(T("Preparing capture…", "正在准备截图…", "正在準備截圖…"));
        UpdateButtons();
        _pause.Click += (_, _) =>
        {
            _paused = !_paused; _generation++; _pending = null; _uncertainFrames = _unchangedAttempts = 0;
            // After a pause the page may have moved. Confirm it against the last
            // accepted image before issuing another wheel message.
            _awaitingScroll = !_paused; _scrollIssuedAt = Environment.TickCount64;
            _waitingForTarget = false; _targetWait.Reset();
            if (_paused) { _scroller?.Dispose(); _scroller = null; }
            else if (_automatic.Checked) PrepareAutoScroll();
            UpdateButtons(); SetStatus(_paused ? T("Paused. Return to the last captured position before resuming.", "已暂停，恢复前请回到最后成功的位置。") : T("Capturing. The pointer can move freely; use Pause to stop scrolling.", "捕获中，鼠标可自由移动，点击暂停可停止滚动。"));
        };
        _automatic.CheckedChanged += (_, _) =>
        {
            _generation++; _pending = null; _awaitingScroll = false; _unchangedAttempts = 0;
            _waitingForTarget = false; _targetWait.Reset();
            _scroller?.Dispose(); _scroller = null;
            if (_store is not null && !_paused && _automatic.Checked)
            {
                _awaitingScroll = true; _scrollIssuedAt = Environment.TickCount64;
                PrepareAutoScroll();
            }
        };
        _undo.Click += (_, _) =>
        {
            if (_busy || _store is null) return;
            try
            {
                if (_store.Undo()) { _paused = true; _generation++; _pending = null; _awaitingScroll = false; _scroller?.Dispose(); _scroller = null; UpdateButtons(); SetStatus(T("Undone. Scroll back to the previous captured position, then resume.", "已撤销，请滚回上一段成功的位置后继续。")); }
            }
            catch (Exception ex) { Fail(ex); }
        };
        _finish.Click += async (_, _) => await FinishAsync();
        _cancel.Click += (_, _) => Close();
        _timer.Tick += async (_, _) => await SampleAsync();
        _cancellation = _lifetime.Token.Register(() =>
        {
            try
            {
                if (IsHandleCreated && !IsDisposed)
                {
                    if (InvokeRequired) BeginInvoke(() => { if (!IsDisposed) Close(); });
                    else Close();
                }
            }
            catch (InvalidOperationException) { }
        });
        host.StyleWindow(this);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SizeForDpi();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        SizeForDpi();
    }

    private void SizeForDpi()
    {
        ClientSize = new Size((int)Math.Round(584 * DeviceDpi / 96f), (int)Math.Round(126 * DeviceDpi / 96f));
        using var outline = ScrollDrawing.Rounded(ClientRectangle, 7 * DeviceDpi / 96f);
        var previous = Region;
        Region = new Region(outline);
        previous?.Dispose();
        PerformLayout();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_card?.Parent != this) return;
        int Px(int value) => (int)Math.Round(value * DeviceDpi / 96f);
        var x = Px(12); var gap = Px(8); var y = Px(12); var height = Px(32);
        var toggleWidth = Px(142);
        _automatic.Bounds = new Rectangle(x, y, toggleWidth, height); x += toggleWidth + Px(18);
        var buttonWidth = Math.Max(Px(66), (_card.Width - x - Px(12) - 3 * gap) / 4);
        foreach (var button in new[] { _pause, _undo, _finish, _cancel })
        {
            button.Bounds = new Rectangle(x, y, buttonWidth, height); x += buttonWidth + gap;
        }
        _status.Bounds = new Rectangle(Px(12), Px(54), _card.Width - Px(24), _card.Height - Px(64));
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try
        {
            var position = ToolbarBounds(_region, Size, Screen.AllScreens.Select(s => s.WorkingArea));
            if (position is null) throw new InvalidOperationException(T("Leave room above, below or beside the selection for the capture controls. Select a smaller region.", "请在选区上方、下方或侧面留出控制条空间，缩小选区后重试。"));
            Bounds = position.Value;
            _outline = new CaptureOutline(_region, ScrollPalette.For(this).Accent); _outline.Show(this);
            if (_scrollTarget is WindowScrollTarget native && native.Window != 0) SetForegroundWindow(native.Window);
            if (_automatic.Checked) PrepareAutoScroll();
            _busy = true; UpdateButtons();
            await Task.Delay(160, _lifetime.Token);
            using var first = _host.CaptureRegion(_region);
            _store = new ScrollImageStore(first);
            _host.Log($"Scroll capture started: plugin={typeof(ScrollCaptureForm).Assembly.GetName().Version}; region={_region}; automatic={_automatic.Checked}; target={(_scrollTarget as WindowScrollTarget)?.Description}");
            SetStatus(_paused ? T("Automatic scrolling could not start. Try manual mode.", "自动滚动无法启动，可切换为手动模式。") : _automatic.Checked ? T("Scrolling automatically. The pointer can move freely; pause or finish at any time.", "正在自动滚动，鼠标可自由移动，可随时暂停或完成。") : T("Scroll down slowly. Exclude fixed headers and animated areas.", "请缓慢向下滚动，选区应排除固定头尾和动画区域。"));
            _timer.Start();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { _closeRequested = true; }
        catch (Exception ex) { Fail(ex); _closeRequested = true; }
        finally { EndWork(); }
    }

    internal static Rectangle? ToolbarBounds(Rectangle region, Size size, IEnumerable<Rectangle> areas)
    {
        foreach (var area in areas)
        {
            if (size.Width > area.Width || size.Height > area.Height) continue;
            var x = Math.Clamp(region.Left, area.Left, area.Right - size.Width);
            var y = Math.Clamp(region.Top, area.Top, area.Bottom - size.Height);
            foreach (var point in new[] { new Point(x, region.Top - size.Height - 8), new Point(x, region.Bottom + 8),
                         new Point(region.Left - size.Width - 8, y), new Point(region.Right + 8, y),
                         area.Location, new Point(area.Right - size.Width, area.Bottom - size.Height) })
            {
                var rect = new Rectangle(point, size);
                if (area.Contains(rect) && !rect.IntersectsWith(region)) return rect;
            }
        }
        return null;
    }

    private async Task SampleAsync()
    {
        if ((GetAsyncKeyState(0x1B) & 0x8000) != 0) { Close(); return; }
        if (_busy || _paused || _store is null || _lifetime.IsCancellationRequested) return;
        if (_automatic.Checked)
        {
            if (_scroller?.CheckTarget() != true) { TargetUnavailable(); return; }
            if (_waitingForTarget)
            {
                _waitingForTarget = false; _targetWait.Reset(); _pending = null;
                _host.Log("Automatic scroll target recovered; confirming frame before continuing.");
                SetStatus(T("Target available again. Continuing capture.", "目标已恢复，正在继续截图。"));
            }
        }
        if (_automatic.Checked && !_awaitingScroll)
        {
            if (_scroller is null || !_scroller.Step(_stepSize.Notches))
            {
                // A timed-out message may already have scrolled. Compare pixels before
                // deciding whether another scroll is necessary, avoiding a doubled step.
                if (_scroller?.CanRetry == true) { _awaitingScroll = true; _scrollIssuedAt = Environment.TickCount64; }
                TargetUnavailable();
                return;
            }
            _awaitingScroll = true; _scrollIssuedAt = Environment.TickCount64; _pending = null;
            return;
        }
        if (_automatic.Checked && Environment.TickCount64 - _scrollIssuedAt < 420) return;
        _busy = true;
        var generation = _generation;
        try
        {
            using var frame = _host.CaptureRegion(_region);
            var previous = _store.LastSample;
            var pending = _pending;
            var token = _lifetime.Token;
            var computation = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var sample = new FrameSample(frame);
                var stable = pending is not null && FrameMatcher.Match(pending, sample, token).Kind == MatchKind.Unchanged;
                var match = stable ? FrameMatcher.Match(previous, sample, token) : new FrameMatch(MatchKind.Unchanged);
                var diagnosis = stable && match.Kind is MatchKind.Uncertain or MatchKind.Reverse ? FrameMatcher.Diagnose(previous, sample) : "";
                return (Sample: sample, Stable: stable, Match: match, Diagnosis: diagnosis);
            }, token);
            token.ThrowIfCancellationRequested();
            if (generation != _generation || IsDisposed) return;
            _pending = computation.Sample;
            if (!computation.Stable) return;
            switch (computation.Match.Kind)
            {
                case MatchKind.Forward:
                    _uncertainFrames = _unchangedAttempts = 0;
                    if (!_store.Append(frame, computation.Sample, computation.Match.Offset))
                    {
                        _paused = true;
                        SetStatus(T("Image/cache limit reached. Finish this image to continue.", "已达到图像或缓存上限，请完成当前长图。"));
                    }
                    else
                    {
                        if (_automatic.Checked) _stepSize.Observe(computation.Match.Offset);
                        _host.Log($"Scroll capture appended: offset={computation.Match.Offset}; height={_store.Height}");
                        SetStatus(T("Captured. Continue, or finish.", "已拼接，可继续或完成。"));
                    }
                    _awaitingScroll = false;
                    break;
                case MatchKind.Unchanged:
                    _uncertainFrames = 0;
                    if (_automatic.Checked)
                    {
                        if (++_unchangedAttempts >= 3)
                        {
                            _paused = true;
                            if (_store.Count > 1) _completeAutomatically = true;
                            else SetStatus(T("No scrolling detected. Disable auto scroll and resume to try manual mode.", "未检测到滚动，可取消勾选自动滚动并点击继续，尝试手动模式。"));
                        }
                        else _awaitingScroll = false;
                    }
                    break;
                case MatchKind.Reverse:
                case MatchKind.Uncertain:
                    if (++_uncertainFrames < 3) return;
                    _paused = true;
                    _host.Log($"Scroll capture alignment rejected: region={_region}; kind={computation.Match.Kind}; {computation.Diagnosis}");
                    SetStatus(T("Cannot align this frame. Scroll back to the last captured position, then resume.", "无法可靠拼接，请滚回最后成功的位置后继续。"));
                    break;
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { _paused = true; Fail(ex); }
        finally { EndWork(); }
    }

    private async Task FinishAsync()
    {
        if (_store is null) return;
        if (_busy) { _finishRequested = true; return; }
        _finishRequested = false;
        _busy = true; _timer.Stop(); UpdateButtons();
        try
        {
            var bitmap = await Task.Run(() => _store.Build(_lifetime.Token), _lifetime.Token);
            if (_lifetime.IsCancellationRequested) { bitmap.Dispose(); return; }
            _result = new PluginImage(bitmap, _region); _closeRequested = true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { _paused = true; Fail(ex); }
        finally { EndWork(); }
    }

    private void EndWork()
    {
        _busy = false;
        if (IsDisposed) return;
        UpdateButtons();
        if (_closeRequested || _lifetime.IsCancellationRequested) Close();
        else if (_completeAutomatically || _finishRequested) { _completeAutomatically = false; _ = FinishAsync(); }
    }

    private void PrepareAutoScroll()
    {
        _scroller?.Dispose();
        _stepSize.Reset();
        _unchangedAttempts = _uncertainFrames = 0;
        // Resume must keep the original target, rather than bind to a window covering it.
        _targetWait.Reset(); _waitingForTarget = false;
        if (_scrollTarget is WindowScrollTarget native && native.Window != 0 && native.IsAvailable())
            SetForegroundWindow(native.Window);
        _scroller = new AutoScrollController(_scrollTarget);
        if (!_scroller.Start()) TargetUnavailable();
    }

    private void TargetUnavailable()
    {
        _pending = null;
        if (_targetWait.ShouldWait(_scroller?.CanRetry == true, Environment.TickCount64))
        {
            if (!_waitingForTarget) _host.Log("Automatic scroll waiting: " + _scroller?.Failure);
            _waitingForTarget = true;
            SetStatus(T("Waiting for the target to become available…", "正在等待目标窗口恢复…"));
            return;
        }
        _waitingForTarget = false; _paused = true;
        _host.Log("Automatic scroll paused: " + _scroller?.Failure);
        UpdateButtons();
        SetStatus(T("Automatic scrolling paused. Restore the target, then resume; you can also use manual mode.", "自动滚动已暂停。请恢复目标窗口后继续，也可关闭自动滚动并手动滚动。"));
    }

    private void UpdateButtons()
    {
        var glyph = _paused ? "resume" : "pause";
        if (_pause.Glyph != glyph)
        {
            _pause.Text = _paused ? T("Resume", "继续", "繼續") : T("Pause", "暂停", "暫停");
            _pause.Glyph = glyph; _pause.Invalidate();
        }
        _status.Paused = _paused;
        _undo.Enabled = !_busy && _store is { Count: > 1 };
        _finish.Enabled = !_busy && _store is not null;
        _pause.Enabled = _store is not null;
    }
    private void SetStatus(string text)
    {
        var state = _paused ? T("Paused", "已暂停", "已暫停") : T("Scrolling screenshot", "长截图", "捲動截圖");
        var metrics = _store is null ? state : $"{state}  ·  {_store.Width} × {_store.Height} px  ·  {_store.Count} " + T(_store.Count == 1 ? "strip" : "strips", "段", "段");
        if (_status.Metrics != metrics) _status.Metrics = metrics;
        if (_status.Text != text)
        {
            _status.Text = text; _tips.SetToolTip(_status, text);
        }
        var accessible = metrics + ". " + text;
        if (_status.AccessibleName != accessible) _status.AccessibleName = accessible;
    }
    private void Fail(Exception ex) { _host.Log("Scroll capture failed.", ex); SetStatus(ex.GetBaseException().Message); }
    public PluginImage? TakeResult() { var result = _result; _result = null; return result; }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _timer.Stop();
        if (_busy) { e.Cancel = true; _closeRequested = true; _lifetime.Cancel(); }
        base.OnFormClosing(e);
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose(); _cancellation.Dispose(); _lifetime.Dispose(); _result?.Dispose();
            _outline?.Dispose();
            _scroller?.Dispose();
            _tips.Dispose(); _uiFont.Dispose();
            try { _store?.Dispose(); } catch (IOException ex) { _host.Log("Cannot remove scroll capture cache.", ex); }
        }
        base.Dispose(disposing);
    }
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
}
