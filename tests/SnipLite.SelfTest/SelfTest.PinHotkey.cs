using System.Reflection;

namespace SnipLite;

internal static partial class SelfTest
{
    public static void RunPinHotkeyCheck()
    {
        var report = new List<string>();
        void Check(string name, bool passed)
        {
            report.Add($"{name}={(passed ? "ok" : "FAILED")}");
            if (!passed) Environment.ExitCode = 1;
        }
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(CaptureForm form, string name, object value) => typeof(CaptureForm).GetField(name, flags)!.SetValue(form, value);
        var crop = new Rectangle(40, 40, 240, 160);
        CaptureForm Open(bool selected, Keys binding = Keys.F3)
        {
            var screen = SystemInformation.VirtualScreen;
            var preset = crop; preset.Offset(screen.Location);
            var form = new CaptureForm(screen, CaptureMode.Snip, new AppSettings
            {
                DetectWindows = false, DetectElements = false, ShowCrosshair = false,
                AutoMagnifier = false, ExitCaptureOnDeactivate = false, PinHotkey = binding, HotkeysEnabled = false
            }, selected ? preset : null);
            form.Show(); Application.DoEvents();
            var image = (Bitmap)typeof(CaptureForm).GetField("_screenshot", flags)!.GetValue(form)!;
            using (var g = Graphics.FromImage(image)) g.FillRectangle(Brushes.RoyalBlue, crop);
            var shapes = (List<Shape>)typeof(CaptureForm).GetField("_shapes", flags)!.GetValue(form)!;
            shapes.Add(new RectShape { Rect = new(crop.X + 50, crop.Y + 50, 30, 30), Fill = true, Color = Color.Red });
            return form;
        }
        void Validate(CaptureResult? result, string name)
        {
            try
            {
                var screen = SystemInformation.VirtualScreen;
                var expected = crop; expected.Offset(screen.Location);
                Check(name, result is { Pin: true } && result.Bounds == expected && result.Image.Size == crop.Size
                    && result.Image.GetPixel(10, 10).ToArgb() == Color.RoyalBlue.ToArgb()
                    && result.Image.GetPixel(60, 60).R > 240 && result.Image.GetPixel(60, 60).G < 20);
            }
            finally { result?.Image.Dispose(); }
        }
        try
        {
            Check("no_overlay_keeps_clipboard_restore_route", !TrayContext.TryPinActiveCapture(null));
            using (var empty = Open(false))
            {
                CaptureResult? result = null; empty.Done += value => result = value;
                Check("unselected_capture_consumes_hotkey_without_pasting", TrayContext.TryPinActiveCapture(empty) && result is null && empty.Visible);
                empty.Close();
                Check("closed_overlay_keeps_clipboard_restore_route", !TrayContext.TryPinActiveCapture(empty));
            }
            using (var capture = Open(true))
            using (var host = new HotkeyHost())
            {
                CaptureResult? result = null; capture.Done += value => result = value;
                var handlers = (Dictionary<int, Action>)typeof(HotkeyHost).GetField("_handlers", flags)!.GetValue(host)!;
                // Dispatch WM_HOTKEY through the real global handler, without registering or stealing a system key.
                handlers[1] = () => { TrayContext.TryPinActiveCapture(capture); };
                var args = new object[] { Message.Create(host.Handle, 0x0312, new IntPtr(1), IntPtr.Zero) };
                typeof(HotkeyHost).GetMethod("WndProc", flags)!.Invoke(host, args);
                Validate(result, "global_f3_pins_current_crop_and_annotations");
            }
            foreach (var (key, binding, recognition) in new[]
            {
                (Keys.F3, Keys.F3, false), (Keys.Control | Keys.T, Keys.F3, false),
                (Keys.Control | Keys.F7, Keys.Control | Keys.F7, false), (Keys.F3, Keys.F3, true)
            })
            {
                using var capture = Open(true, binding);
                if (recognition) Set(capture, "_ocrMode", true);
                CaptureResult? result = null; capture.Done += value => result = value;
                var args = new object[] { Message.Create(capture.Handle, 0, IntPtr.Zero, IntPtr.Zero), key };
                typeof(CaptureForm).GetMethod("ProcessCmdKey", flags)!.Invoke(capture, args);
                Validate(result, $"local_pin_{key}_recognition_{recognition}");
            }
            using (var dragging = Open(true))
            {
                Set(dragging, "_dragging", true);
                Check("unfinished_selection_does_not_pin", !dragging.TryPinSelection() && dragging.Visible);
                dragging.Close();
            }
        }
        catch (Exception ex) { Environment.ExitCode = 1; report.Add("FAILED: " + ex); }
        File.WriteAllLines(Path.Combine(OutputDir, "pin-hotkey-test.txt"), report);
        foreach (var line in report) Console.WriteLine(line);
    }
}
