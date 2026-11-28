using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SnipLite.Plugin.Abstractions;

namespace SnipLite;

internal sealed partial class CaptureForm
{
    private static readonly Color LiveTransparency = Color.FromArgb(4, 1, 7);
    private Form? _pluginPanel;
    private Bitmap? _liveSource;
    private readonly object _liveGate = new();
    private System.Windows.Forms.Timer? _liveEditorTimer;
    private int _liveFramePending;
    private bool _liveNeedsUiComposition;
    private bool _liveHasAnnotations;
    private volatile bool _recordingLive;
    private bool _closeAfterRecording;
    private bool _dockedCommandActive;

    internal IScreenshotPluginSession CreatePluginSession() => new ScreenshotPluginSession(this);

    private void PositionPluginPanel()
    {
        if (_pluginPanel is not { IsDisposed: false } panel) return;
        var bar = RectangleToScreen(_bar.Bounds);
        var work = Screen.FromRectangle(bar).WorkingArea;
        panel.Location = new Point(
            Math.Clamp(bar.Left, work.Left, Math.Max(work.Left, work.Right - panel.Width)),
            Math.Clamp(bar.Bottom + Math.Max(4, DeviceDpi / 24), work.Top, Math.Max(work.Top, work.Bottom - panel.Height)));
    }

    private void SetPluginLive(bool live)
    {
        if (IsDisposed || _resourcesReleased || _recordingLive == live) return;
        CommitText();
        if (live)
        {
            TransparencyKey = LiveTransparency;
            if (!SetWindowDisplayAffinity(Handle, 0x11))
            {
                var failure = Marshal.GetLastWin32Error();
                TransparencyKey = Color.Empty;
                throw new System.ComponentModel.Win32Exception(failure);
            }
        }
        else
        {
            SetWindowDisplayAffinity(Handle, 0);
            TransparencyKey = Color.Empty;
            _recordingLive = false;
            _liveEditorTimer?.Dispose();
            _liveEditorTimer = null;
            ReleaseLiveSource();
        }
        _recordingLive = live;
        if (live)
        {
            _liveEditorTimer = new System.Windows.Forms.Timer { Interval = 80 };
            _liveEditorTimer.Tick += (_, _) => RefreshLiveEditor();
            RefreshLiveEditor();
            _liveEditorTimer.Start();
        }
        InvalidateAnnotations();
    }

    private bool CloseRecordingPanel(Action complete)
    {
        if (_pluginPanel is not { IsDisposed: false } panel) return false;
        if (_closeAfterRecording) return true;
        _closeAfterRecording = true;
        panel.FormClosed += (_, _) =>
        {
            if (!IsDisposed) BeginInvoke(() =>
            {
                _closeAfterRecording = false;
                if (!IsDisposed) complete();
            });
        };
        panel.Close();
        return true;
    }

    private void ReleaseLiveSource()
    {
        lock (_liveGate)
        {
            _liveSource?.Dispose();
            _liveSource = null;
            _liveHasAnnotations = false;
        }
    }

    private bool DrawLiveSource(Graphics graphics, Point destination)
    {
        lock (_liveGate)
        {
            if (_liveSource is null) return false;
            graphics.DrawImageUnscaled(_liveSource, destination);
            return true;
        }
    }

    private void DrawPluginFrame(Graphics graphics, Rectangle dirty)
    {
        using var background = new SolidBrush(LiveTransparency);
        graphics.FillRectangle(background, dirty);
        if (_bar.Tool == ToolKind.None) return;
        if (!DrawLiveSource(graphics, Crop.Location))
            graphics.DrawImage(_screenshot, Crop, Crop, GraphicsUnit.Pixel);
    }

    private void DrawCaptureAnnotations(Graphics graphics, Rectangle crop, Point destination)
    {
        lock (_liveGate)
        {
            var dynamic = _recordingLive && (_current is MosaicShape || _shapes.Exists(shape => shape is MosaicShape));
            if (dynamic) _annotationDirty = true;
            graphics.DrawImageUnscaled(GetAnnotationLayer(crop), destination);
            _liveHasAnnotations = _shapes.Count > 0 || _current is not null;
        }
    }

    private void RefreshLiveEditor()
    {
        if (!_recordingLive || IsDisposed) return;
        var dynamic = _ime is not null || _current is MosaicShape || _shapes.Exists(shape => shape is MosaicShape);
        Volatile.Write(ref _liveNeedsUiComposition, dynamic);
        lock (_liveGate)
            _liveHasAnnotations = _shapes.Count > 0 || _current is not null;
        if (Interlocked.Exchange(ref _liveFramePending, 0) != 0 && (_bar.Tool != ToolKind.None || dynamic))
            Invalidate(_selection);
    }

    private void ComposeLiveFrame(Bitmap frame, bool sourceChanged)
    {
        var dynamic = Volatile.Read(ref _liveNeedsUiComposition);
        lock (_liveGate)
        {
            if (!_recordingLive) return;
            if (sourceChanged || _liveSource is null)
            {
                _liveSource ??= new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppPArgb);
                using var raw = Graphics.FromImage(_liveSource);
                raw.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                raw.DrawImageUnscaled(frame, Point.Empty);
                Volatile.Write(ref _liveFramePending, 1);
            }

            if (!dynamic && _liveHasAnnotations && _annotationLayer is { } layer)
            {
                using var target = Graphics.FromImage(frame);
                target.DrawImageUnscaled(layer, Point.Empty);
            }
        }

        if (!dynamic) return;
        if (InvokeRequired) Invoke(() => ComposeLiveEdits(frame));
        else ComposeLiveEdits(frame);
    }

    private void ComposeLiveEdits(Bitmap frame)
    {
        if (_resourcesReleased || !_recordingLive) return;
        var crop = Crop;
        using var graphics = Graphics.FromImage(frame);
        if (_shapes.Count > 0 || _current is not null) DrawCaptureAnnotations(graphics, crop, Point.Empty);
        if (_ime is not null)
        {
            graphics.TranslateTransform(-crop.X, -crop.Y);
            DrawTextPreview(graphics);
        }
    }

    private sealed class ScreenshotPluginSession(CaptureForm owner) : IScreenshotPluginSession, IDisposable
    {
        public Rectangle Selection => owner.RectangleToScreen(owner.Crop);

        public void ShowPanel(Form panel)
        {
            if (owner._pluginPanel is { IsDisposed: false })
                throw new InvalidOperationException("A screenshot plugin panel is already open.");
            owner._pluginPanel = panel;
            panel.FormClosed += (_, _) => ClearPanel(panel);
            owner.Invalidate();
            owner.Update();
            owner.PositionPluginPanel();
            panel.Show(owner);
            owner.Invalidate();
        }

        public void SetLive(bool live) => owner.SetPluginLive(live);

        public void ComposeFrame(Bitmap frame) => ComposeFrame(frame, true);

        public void ComposeFrame(Bitmap frame, bool sourceChanged)
        {
            if (owner.IsDisposed || owner.Disposing)
                throw new OperationCanceledException("The screenshot window closed.");
            owner.ComposeLiveFrame(frame, sourceChanged);
        }
        public void Dispose()
        {
            if (owner.IsDisposed || owner.Disposing) return;
            owner.SetPluginLive(false);
            if (owner._pluginPanel is { IsDisposed: false } panel)
            {
                ClearPanel(panel);
                panel.Close();
            }
        }

        private void ClearPanel(Form panel)
        {
            if (!ReferenceEquals(owner._pluginPanel, panel)) return;
            owner._pluginPanel = null;
            if (!owner.IsDisposed) owner.Invalidate();
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(nint window, uint affinity);
}
