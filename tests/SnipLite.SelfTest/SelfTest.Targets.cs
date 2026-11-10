namespace SnipLite;

internal static partial class SelfTest
{
    public static void RunTargetCheck()
    {
        try { File.WriteAllText(Path.Combine(OutputDir, "target-test.txt"), CheckCaptureTargetFlow()); }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;
            File.WriteAllText(Path.Combine(OutputDir, "target-test.txt"), "FAILED: " + ex);
        }
    }

    private static string CheckCaptureTargetFlow()
    {
        var report = new List<string>();
        void Check(string name, bool passed)
        {
            report.Add($"{name}={(passed ? "ok" : "FAILED")}");
            if (!passed) Environment.ExitCode = 1;
        }
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        object? Get(CaptureForm form, string name) => typeof(CaptureForm).GetField(name, flags)!.GetValue(form);
        void Input(CaptureForm form, string name, Point point, MouseButtons button = MouseButtons.None) =>
            typeof(CaptureForm).GetMethod(name, flags)!.Invoke(form, new object[] { new MouseEventArgs(button, 1, point.X, point.Y, 0) });
        static bool Wait(Func<bool> condition, int milliseconds = 3500)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            do { Application.DoEvents(); if (condition()) return true; Thread.Sleep(10); } while (clock.ElapsedMilliseconds < milliseconds);
            return false;
        }
        using var window = new Form { Text = "自动框选完整流程", ClientSize = new Size(570, 260), StartPosition = FormStartPosition.Manual,
            Location = new Point(80, 80), ShowInTaskbar = false, TopMost = true, AutoScaleMode = AutoScaleMode.None };
        using var panel = new Panel { Bounds = new Rectangle(20, 25, 260, 210), BackColor = Color.WhiteSmoke };
        using var button = new Button { Text = "可点击按钮", Bounds = new Rectangle(25, 25, 140, 40) };
        using var disabled = new Button { Text = "禁用按钮也是截图目标", Enabled = false, Bounds = new Rectangle(25, 90, 205, 40) };
        using var virtualPanel = new DelayedTargetControl { Bounds = new Rectangle(310, 25, 240, 210) };
        panel.Controls.AddRange(new Control[] { button, disabled }); window.Controls.AddRange(new Control[] { panel, virtualPanel });
        window.Show(); window.Hide(); window.Show(); Application.DoEvents();
        using var transparentCover = new TransparentTargetCover { Bounds = window.Bounds };
        transparentCover.Show(window); Application.DoEvents();
        var screen = SystemInformation.VirtualScreen;
        Rectangle Local(Rectangle bounds) { bounds.Offset(-screen.X, -screen.Y); return bounds; }
        Point Center(Rectangle bounds) => new(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        using var capture = new CaptureForm(screen, CaptureMode.Snip, new AppSettings { AutoMagnifier = false, ShowCrosshair = false, IgnoreInjectedCopy = false });
        capture.Show();
        using var input = new CaptureTestInput(capture);
        Application.DoEvents();
        bool Hover(Rectangle bounds) => (Rectangle)Get(capture, "_hoverTarget")! == Local(bounds);
        var buttonBounds = button.RectangleToScreen(button.ClientRectangle);
        Check("capture_ignores_transparent_overlay", ((DesktopTargets)Get(capture, "_targets")!).At(Center(buttonBounds), false) == window.Bounds);
        Input(capture, "OnMouseMove", Center(Local(buttonBounds)));
        Check("capture_overlay_button_hover", Wait(() => Hover(buttonBounds)));
        report.Add($"button_expected={Local(buttonBounds)} actual={Get(capture, "_hoverTarget")}");
        report.Add("windows=" + ((DesktopTargets)Get(capture, "_targets")!).Describe(Center(buttonBounds)));
        var panelBounds = panel.RectangleToScreen(panel.ClientRectangle);
        Input(capture, "OnMouseMove", Local(new Rectangle(panel.PointToScreen(new Point(230, 185)), Size.Empty)).Location);
        Check("capture_overlay_panel_hover", Wait(() => Hover(panelBounds)));
        var disabledBounds = disabled.RectangleToScreen(disabled.ClientRectangle);
        Input(capture, "OnMouseMove", Center(Local(disabledBounds)));
        Check("capture_overlay_disabled_button", Wait(() => Hover(disabledBounds)));
        var virtualBounds = virtualPanel.TargetBounds;
        var virtualPoint = Center(Local(virtualBounds));
        Input(capture, "OnMouseMove", virtualPoint);
        Wait(() => !(bool)Get(capture, "_targetBusy")!, 1500);
        var initiallyCoarse = !Hover(virtualBounds);
        virtualPanel.ExposeTarget();
        Check("capture_stationary_retry", initiallyCoarse && Wait(() => Hover(virtualBounds)));
        report.Add($"virtual_expected={Local(virtualBounds)} actual={Get(capture, "_hoverTarget")} busy={Get(capture, "_targetBusy")} focus={capture.ContainsFocus}");
        Input(capture, "OnMouseDown", virtualPoint, MouseButtons.Left);
        Input(capture, "OnMouseUp", virtualPoint, MouseButtons.Left);
        Check("capture_detected_target_click", (bool)Get(capture, "_hasSelection")! && (Rectangle)Get(capture, "_selection")! == Local(virtualBounds));
        capture.Close(); window.Close(); Application.DoEvents();
        return string.Join(Environment.NewLine, report) + Environment.NewLine;
    }

    private sealed class TransparentTargetCover : Form
    {
        public TransparentTargetCover()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual; TopMost = true; Opacity = .05;
        }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get { var parameters = base.CreateParams; parameters.ExStyle |= 0x20 | 0x08000000; return parameters; }
        }
    }

    private sealed class CaptureTestInput : IMessageFilter, IDisposable
    {
        private readonly IntPtr _handle;
        public CaptureTestInput(Form form) { _handle = form.Handle; Application.AddMessageFilter(this); }
        // The fixture delivers its own mouse events. Do not let unrelated real
        // pointer messages generated by Show/SetWindowPos change that test point.
        public bool PreFilterMessage(ref Message message) => message.HWnd == _handle && message.Msg is >= 0x0200 and <= 0x020E;
        public void Dispose() => Application.RemoveMessageFilter(this);
    }

    // A windowless accessible child appears after the first hit test. The pointer
    // stays still while the real CaptureForm timer must discover the new target.
    private sealed class DelayedTargetControl : Control
    {
        private bool _available;
        public Rectangle TargetBounds => RectangleToScreen(new Rectangle(25, 35, 150, 45));
        public void ExposeTarget() { _available = true; AccessibilityNotifyClients(AccessibleEvents.Reorder, -1); }
        protected override AccessibleObject CreateAccessibilityInstance() => new TargetPanelAccessible(this);
        private sealed class TargetPanelAccessible(DelayedTargetControl owner) : ControlAccessibleObject(owner)
        {
            private readonly AccessibleObject _target = new TargetAccessible(owner);
            public override int GetChildCount() => owner._available ? 1 : 0;
            public override AccessibleObject? GetChild(int index) => owner._available && index == 0 ? _target : null;
            public override AccessibleObject? HitTest(int x, int y) => owner._available && owner.TargetBounds.Contains(x, y) ? _target : base.HitTest(x, y);
        }
        private sealed class TargetAccessible(DelayedTargetControl owner) : AccessibleObject
        {
            public override Rectangle Bounds => owner.TargetBounds;
            public override AccessibleObject? Parent => owner.AccessibilityObject;
            public override AccessibleRole Role => AccessibleRole.PushButton;
            public override string? Name { get => "延迟可用的按钮"; set { } }
        }
    }
}
