using System.Drawing.Imaging;
using ZXing;
using ZXing.Common;
using ZXing.Windows.Compatibility;

namespace SnipLite;

/// <summary>
/// Head-less checks. <c>--selftest</c> validates capture + OCR + barcode,
/// <c>--demo</c> renders every annotation tool and the toolbar to a PNG.
/// </summary>
internal static partial class SelfTest
{
    internal static string OutputDir
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("SNIPLITE_TEST_OUTPUT");
            var dir = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(Path.GetTempPath(), "SnipLite.SelfTest") : Path.GetFullPath(configured);
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static void Run()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        var output = OutputDir;
        var progress = Path.Combine(output, "selftest-progress.txt");
        File.WriteAllText(progress, "start\r\n");
        string Stage(string name, Func<string> check)
        {
            File.AppendAllText(progress, name + "\r\n"); return check();
        }

        try
        {
            var screen = SystemInformation.VirtualScreen;

            using var shot = new Bitmap(screen.Width, screen.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(shot))
                g.CopyFromScreen(screen.Location, Point.Empty, screen.Size, CopyPixelOperation.SourceCopy);

            // draw a known QR code into the frame so the decode path is exercised
            var qrResult = QrRoundTrip(shot);

            shot.Save(Path.Combine(output, "selftest.png"), ImageFormat.Png);

            var text = Task.Run(() => Services.OcrAsync(shot, "zh-Hans")).GetAwaiter().GetResult();
            var code = Services.DecodeQr(shot);
            var recognition = Stage("recognition", CheckRecognition);

            var report =
                $"virtual={screen}\r\n" +
                $"bitmap={shot.Width}x{shot.Height}\r\n" +
                $"dpi={GetDpi():0}\r\n" +
                $"ocr_chars={text.Length}\r\n" +
                $"ocr={(text.Length > 120 ? text[..120].Replace("\r", " ").Replace("\n", " ") : text)}\r\n" +
                $"qr_roundtrip={qrResult}\r\n" +
                $"qr_screen={code}\r\n" + recognition + Stage("annotations", CheckAnnotations) + Stage("preferences", CheckPreferences) + Stage("capture interactions", CheckCaptureInteractions) + Stage("automatic targets", CheckAutomaticTargets) + Stage("capture target flow", CheckCaptureTargetFlow) + Stage("text editing", CheckTextEditing) + Stage("preference hover", CheckPreferenceHover) + Stage("menu hover", CheckMenuHover);

            File.WriteAllText(Path.Combine(output, "selftest.txt"), report);
        }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;
            File.WriteAllText(Path.Combine(output, "selftest.txt"), "FAILED: " + ex);
        }
    }

    private static string CheckAnnotations()
    {
        var report = new List<string>();
        void Check(string name, bool passed)
        {
            report.Add($"{name}={(passed ? "ok" : "FAILED")}");
            if (!passed) Environment.ExitCode = 1;
        }
        using var screenshot = new Bitmap(240, 180, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(screenshot)) g.Clear(Color.FromArgb(30, 80, 140));
        using var layer = new Bitmap(240, 180, PixelFormat.Format32bppPArgb);
        var crop = new Rectangle(0, 0, 240, 180);
        var rectangle = new RectShape { Rect = new Rectangle(10, 10, 180, 130), Fill = true, Color = Color.Red };
        var eraser = new EraserShape { Rect = new Rectangle(30, 30, 40, 40), Rectangular = true };
        AnnotationRenderer.RenderLayer(layer, crop, screenshot, new Shape[] { rectangle, eraser });
        Check("eraser_rectangle_alpha", layer.GetPixel(50, 50).A == 0 && layer.GetPixel(15, 15).R == 255);
        using var composite = new Bitmap(screenshot);
        using (var g = Graphics.FromImage(composite)) g.DrawImageUnscaled(layer, 0, 0);
        Check("eraser_preserves_screenshot", composite.GetPixel(50, 50).ToArgb() == screenshot.GetPixel(50, 50).ToArgb());
        var brush = new EraserShape { Rectangular = false, Width = 20 };
        brush.SetStart(new Point(40, 50)); brush.DragTo(new Point(120, 50));
        AnnotationRenderer.RenderLayer(layer, crop, screenshot, new Shape[] { rectangle, brush });
        Check("eraser_brush_alpha", layer.GetPixel(80, 50).A == 0 && layer.GetPixel(80, 80).A == 255);
        AnnotationRenderer.RenderLayer(layer, crop, screenshot, new Shape[] { rectangle });
        Check("eraser_undo_replay", layer.GetPixel(80, 50).A == 255);

        var marker = new MarkerShape { Rectangular = true, Color = Color.Yellow, Opacity = 50 };
        marker.SetStart(new Point(15, 15)); marker.DragTo(new Point(130, 60));
        AnnotationRenderer.RenderLayer(layer, crop, screenshot, new[] { marker });
        Check("marker_half_opacity", Math.Abs(layer.GetPixel(40, 30).A - 128) <= 1);
        marker.Rectangular = false; marker.Width = 20;
        marker.SetStart(new Point(15, 50)); marker.DragTo(new Point(130, 50));
        AnnotationRenderer.RenderLayer(layer, crop, screenshot, new[] { marker });
        Check("marker_line_mode", layer.GetPixel(70, 50).A is >= 127 and <= 128 && layer.GetPixel(70, 80).A == 0);

        var mosaic = new MosaicShape { Rectangular = false, Width = 20, Block = 8 };
        mosaic.SetStart(new Point(40, 50)); mosaic.DragTo(new Point(130, 50));
        AnnotationRenderer.RenderLayer(layer, crop, screenshot, new[] { mosaic });
        Check("mosaic_brush_clip", layer.GetPixel(80, 50).A == 255 && layer.GetPixel(80, 80).A == 0);
        var clone = (MosaicShape)mosaic.Clone();
        mosaic.DragTo(new Point(200, 90));
        Check("history_deep_copy", clone.Points.Count == 2 && mosaic.Points.Count == 3);

        var rect = new RectShape();
        rect.SetStart(new Point(100, 100)); rect.DragTo(new Point(70, 70)); rect.DragTo(new Point(20, 30));
        Check("rectangle_reverse_anchor", rect.Rect == new Rectangle(20, 30, 80, 70));
        var text = new TextShape { Text = "字体大小 Test", At = new Point(15, 20), FontSize = 12 };
        using var graphics = Graphics.FromImage(layer);
        var small = text.Bounds(graphics);
        text.FontSize = 24;
        var large = text.Bounds(graphics);
        var original = (TextShape)text.Clone();
        text.DragTo(new Point(80, 90));
        Check("text_size_independent", large.Height > small.Height * 1.8 && large.Width > small.Width * 1.8);
        Check("text_move_snapshot", text.At == new Point(80, 90) && original.At == new Point(15, 20));

        foreach (var tool in new[] { ToolKind.Text, ToolKind.Marker, ToolKind.Mosaic, ToolKind.Eraser, ToolKind.Arrow })
        {
            var bar = new AnnotationBar { Tool = tool, Scale = 1.5f };
            bar.Layout(new Rectangle(100, 100, 1200, 600), new Size(1920, 1080));
            var id = tool switch { ToolKind.Text => "fontsize", ToolKind.Marker => "opacity", ToolKind.Mosaic => "strength", ToolKind.Eraser => "modebrush", _ => "arrowstyle" };
            var item = bar.ItemBounds(id);
            Check($"toolbar_{tool.ToString().ToLowerInvariant()}", item.Width > 0 && bar.HitTest(new Point(item.Left + 2, item.Top + 2)) == id);
            Check($"toolbar_height_{tool.ToString().ToLowerInvariant()}", item.Height == bar.ItemBounds("copy").Height
                && bar.OptionsBounds.Height == bar.MainBounds.Height);
        }
        var largePen = new PenShape { Width = 150, Color = Color.Green };
        largePen.SetStart(new Point(1, 1)); largePen.DragTo(new Point(5, 5));
        using var tiny = new Bitmap(8, 8, PixelFormat.Format32bppPArgb);
        AnnotationRenderer.RenderLayer(tiny, new Rectangle(0, 0, 8, 8), screenshot, new[] { largePen });
        Check("oversize_pen_clip", tiny.GetPixel(4, 4).G > 0);
        var freeMarker = new MarkerShape { Width = 150, Color = Color.Yellow };
        freeMarker.SetStart(new Point(1, 1)); freeMarker.DragTo(new Point(3, 5)); freeMarker.DragTo(new Point(5, 2));
        AnnotationRenderer.RenderLayer(tiny, new Rectangle(0, 0, 8, 8), screenshot, new[] { freeMarker });
        Check("oversize_marker_brush", tiny.GetPixel(4, 4).A is >= 127 and <= 128 && freeMarker.Points.Count == 3);
        using var pattern = new Bitmap(32, 16, PixelFormat.Format32bppArgb);
        using (var painter = Graphics.FromImage(pattern))
        {
            painter.Clear(Color.White);
            painter.FillRectangle(Brushes.Black, 0, 0, 2, 16);
            painter.FillRectangle(Brushes.Red, 8, 0, 4, 16);
        }
        using var tiles = new Bitmap(32, 16, PixelFormat.Format32bppPArgb);
        var pixelation = new MosaicShape { Rect = new Rectangle(0, 0, 32, 16), Block = 8 };
        AnnotationRenderer.RenderLayer(tiles, new Rectangle(0, 0, 32, 16), pattern, new[] { pixelation });
        Check("mosaic_averages_text_detail", tiles.GetPixel(4, 4).R is > 150 and < 220);
        Check("mosaic_preserves_colour_structure", tiles.GetPixel(4, 4).ToArgb() != tiles.GetPixel(12, 4).ToArgb() && tiles.GetPixel(20, 4).R == 255);
        Check("mosaic_tile_constant", tiles.GetPixel(0, 0).ToArgb() == tiles.GetPixel(7, 7).ToArgb());
        foreach (var style in new[] { ArrowStyle.TaperedSolid, ArrowStyle.TaperedOutline })
        {
            using var arrowImage = new Bitmap(340, 100, PixelFormat.Format32bppPArgb);
            using (var painter = Graphics.FromImage(arrowImage))
            {
                painter.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                new ArrowShape { Start = new Point(20, 50), End = new Point(320, 50), Width = 5, Color = Color.Red, Style = style }.Render(painter, screenshot);
            }
            var filled = arrowImage.GetPixel(200, 50).A;
            Check($"arrow_{style.ToString().ToLowerInvariant()}_body", style == ArrowStyle.TaperedSolid ? filled == 255 : filled == 0);
            Check($"arrow_{style.ToString().ToLowerInvariant()}_head", Enumerable.Range(35, 31).Count(y => arrowImage.GetPixel(298, y).A > 0) > 3);
        }
        foreach (var scale in new[] { 1f, 1.5f, 2f })
        {
            var ocrBar = new AnnotationBar { Scale = scale, OcrMode = true, Status = "未识别到文字，按 Esc 返回后调整选区" };
            ocrBar.Layout(new Rectangle(100, 100, 1100, 500), new Size(1920, 1080));
            Check($"ocr_status_same_width_{scale}", ocrBar.MainBounds.Width == ocrBar.MessageBounds.Width && ocrBar.MainBounds.Left == ocrBar.MessageBounds.Left);
            var qrBar = new AnnotationBar { Scale = scale, Status = "https://example.com/path?key=hello&code=12345678901234567890", ResultCopyVisible = true };
            qrBar.Layout(new Rectangle(100, 100, 1100, 500), new Size(1920, 1080));
            var copy = qrBar.ItemBounds("resultcopy");
            Check($"qr_inline_copy_layout_{scale}", qrBar.MainBounds.Width == qrBar.MessageBounds.Width && qrBar.MessageBounds.Contains(copy)
                && qrBar.HitTest(new Point(copy.Left + 2, copy.Top + 2)) == "resultcopy");
        }
        foreach (var scale in new[] { 1f, 1.25f, 1.5f, 2f, 3f })
        {
            using var reticle = new Bitmap(81, 81);
            var theme = scale == 2f ? Color.DarkOrange : AnnotationBar.DefaultAccent;
            using (var painter = Graphics.FromImage(reticle))
            {
                painter.Clear(Color.Gray);
                painter.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                MagnifierReticle.Draw(painter, new Rectangle(0, 0, 81, 81), new RectangleF(36, 36, 9, 9), scale, theme);
            }
            var edge = Color.FromArgb(theme.R / 4, theme.G / 4, theme.B / 4).ToArgb();
            var expected = new[] { Color.Gray.ToArgb(), edge, theme.ToArgb(), edge, Color.Gray.ToArgb() };
            var horizontal = new[] { 10, 70 }.All(x => Enumerable.Range(0, 5).All(i => reticle.GetPixel(x, 38 + i).ToArgb() == expected[i]));
            var vertical = new[] { 10, 70 }.All(y => Enumerable.Range(0, 5).All(i => reticle.GetPixel(38 + i, y).ToArgb() == expected[i]));
            Check($"reticle_symmetric_3px_{scale:0.##}", horizontal && vertical && reticle.GetPixel(40, 40).ToArgb() == Color.Gray.ToArgb());
        }
        using (var owner = new Form())
        using (var crosshair = new CaptureCrosshair(owner, new Rectangle(20, 30, 320, 240), Color.DeepSkyBlue))
        {
            crosshair.Move(new Point(80, 90));
            report.Add($"native_crosshair_bounds=h:{crosshair.HorizontalBounds} v:{crosshair.VerticalBounds}");
            Check("native_crosshair_geometry", crosshair.HorizontalBounds.Location == new Point(20, 120) && crosshair.VerticalBounds.Location == new Point(100, 30)
                && crosshair.HorizontalInkBounds == new RectangleF(0, 0, 320, 1) && crosshair.VerticalInkBounds == new RectangleF(0, 0, 1, 240));
            crosshair.Move(new Point(120, 140));
            Check("native_crosshair_move", crosshair.HorizontalBounds.Location == new Point(20, 170) && crosshair.VerticalBounds.Location == new Point(140, 30)
                && crosshair.HorizontalInkBounds.Height == 1 && crosshair.VerticalInkBounds.Width == 1);
            crosshair.Hide();
        }
        return string.Join(Environment.NewLine, report) + Environment.NewLine;
    }

    public static void RenderFeedbackDemo()
    {
        using var source = new Bitmap(720, 220, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(source))
        {
            g.Clear(Color.White);
            using var font = new Font("Microsoft YaHei UI", 22);
            for (var y = 10; y < 220; y += 48) g.DrawString("文字隐私内容 / Password A9x3 / 彩色图形", font, Brushes.Black, 10, y);
            using var color = new SolidBrush(Color.FromArgb(220, 50, 70)); g.FillEllipse(color, 580, 20, 120, 170);
        }
        using var canvas = new Bitmap(780, 720);
        using var output = Graphics.FromImage(canvas); output.Clear(Color.FromArgb(240, 243, 247));
        output.DrawImageUnscaled(source, 30, 25);
        using var layer = new Bitmap(720, 220, PixelFormat.Format32bppPArgb);
        AnnotationRenderer.RenderLayer(layer, new Rectangle(0, 0, 720, 220), source, new[] { new MosaicShape { Rect = new Rectangle(0, 0, 720, 220), Block = 12 } });
        output.DrawImageUnscaled(layer, 30, 265);
        using var gallery = new Bitmap(330, 300);
        using (var g = Graphics.FromImage(gallery))
        {
            g.Clear(SnipMenuRenderer.Back);
            var styles = new[] { ArrowStyle.Line, ArrowStyle.Solid, ArrowStyle.Open, ArrowStyle.Outline, ArrowStyle.Double };
            for (var i = 0; i < styles.Length; i++) ArrowStyleGallery.DrawPreview(g, new Rectangle(0, i * 60, 330, 60), styles[i], false, false, 1.5f);
        }
        gallery.Save(Path.Combine(OutputDir, "arrow-gallery.png"));
        output.FillRectangle(Brushes.Gray, 300, 510, 180, 180);
        MagnifierReticle.Draw(output, new Rectangle(300, 510, 180, 180), new RectangleF(384, 594, 12, 12), 1.5f);
        canvas.Save(Path.Combine(OutputDir, "mosaic-reticle.png"));
    }

    private static string CheckPreferences()
    {
        var report = new List<string>();
        void Check(string name, bool passed) { report.Add($"{name}={(passed ? "ok" : "FAILED")}"); if (!passed) Environment.ExitCode = 1; }
        var settings = new AppSettings { ColorHex = true, ColorHideHash = true, ColorUppercase = false, PinOpacityPercent = 70, CaptureHotkey = Keys.Control | Keys.F2 };
        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        var copy = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!;
        Check("preferences_roundtrip", copy.CaptureHotkey == settings.CaptureHotkey && copy.PinOpacityPercent == 70 && copy.ColorHideHash);
        Check("color_format", settings.FormatColor(Color.FromArgb(171, 205, 239)) == "abcdef");
        var other = new AppSettings(); other.CopyFrom(settings);
        Check("preferences_reset_copy", other.CaptureHotkey == settings.CaptureHotkey && other.ColorHideHash);
        var history = new CaptureHistory(); using var image = new Bitmap(40, 30);
        history.Add(image, new Rectangle(5, 8, 40, 30), 1); history.Add(image, new Rectangle(8, 10, 40, 30), 1);
        using var restored = history.Entries[0].Image();
        Check("capture_history_limit", history.Entries.Count == 1 && restored.Size == image.Size && history.Entries[0].Bounds.X == 8);
        using var text = TextImageRenderer.Render("中文 test\nAa012", null, settings);
        Check("text_pin_render", text.Width > 30 && text.Height > 30);
        Check("clipboard_path_filter", TextImageRenderer.IsFilePath(@"C:\example\file.txt") && !TextImageRenderer.IsFilePath("普通文字"));
        using var shadow = PinShadowWindow.RenderShadow(new Size(100, 100), new Rectangle(20, 20, 60, 60), Color.Black);
        Check("shadow_transparent_center", shadow.GetPixel(50, 50).A == 0);
        Check("shadow_soft_edges", shadow.GetPixel(82, 50).A > shadow.GetPixel(95, 50).A && shadow.GetPixel(82, 50).A > shadow.GetPixel(17, 50).A);
        var pinSettings = new AppSettings { PinShadow = false, FlashNewPin = false, ActivateNewPin = false, RecoverablePins = 1 };
        var pin = new PinForm(new Bitmap(30, 20), settings: pinSettings); _ = pin.Handle; pin.Close();
        Check("closed_pin_recoverable", PinForm.RecoverableCount == 1);
        var recovered = PinForm.RestoreLast(pinSettings);
        Check("restore_closed_pin", recovered && PinForm.OpenCount == 1);
        PinForm.HideAll(); Check("restore_hidden_pins", PinForm.RestoreHiddenOrFlashing(pinSettings) && PinForm.AnyVisible);
        PinForm.CloseAllForExit();
        return string.Join(Environment.NewLine, report) + Environment.NewLine;
    }

    private static string CheckCaptureInteractions()
    {
        var report = new List<string>(); var errors = new List<Exception>();
        System.Threading.ThreadExceptionEventHandler handler = (_, e) => errors.Add(e.Exception);
        Application.ThreadException += handler;
        void Check(string name, bool passed) { report.Add($"{name}={(passed ? "ok" : "FAILED")}"); if (!passed) Environment.ExitCode = 1; }
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var type = typeof(CaptureForm);
        object? Get(CaptureForm form, string name) => type.GetField(name, flags)!.GetValue(form);
        void Set(CaptureForm form, string name, object value) => type.GetField(name, flags)!.SetValue(form, value);
        object? Call(CaptureForm form, string name, params object[] args) => type.GetMethod(name, flags)!.Invoke(form, args);
        try
        {
            using var form = new CaptureForm(SystemInformation.VirtualScreen, CaptureMode.Snip,
                new AppSettings { DetectWindows = false, DetectElements = false, IgnoreInjectedCopy = false, AutoMagnifier = false });
            _ = form.Handle;
            Set(form, "_selection", new Rectangle(0, 0, 220, 160)); Set(form, "_hasSelection", true);
            var bar = (AnnotationBar)Get(form, "_bar")!;
            bar.Layout(new Rectangle(0, 0, 220, 160), SystemInformation.VirtualScreen.Size);
            void CloseMenu()
            {
                var menu = ((List<ContextMenuStrip>)Get(form, "_optionMenus")!)[^1];
                menu.Close(); Application.DoEvents();
                Check("popup_close_keeps_native_alive", !menu.IsDisposed);
            }
            Call(form, "Command", "pen"); Call(form, "ShowWidthPicker");
            var menus = (List<ContextMenuStrip>)Get(form, "_optionMenus")!;
            ((TrackBar)((ToolStripControlHost)menus[^1].Items[0]).Control).Value = 100;
            CloseMenu();
            Call(form, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
            Call(form, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 30, 30, 0));
            Call(form, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 30, 30, 0));
            using (var rendered = (Bitmap)Call(form, "RenderComposite", new Rectangle(0, 0, 220, 160))!) Check("large_brush_after_popup", rendered.Size == new Size(220, 160));
            Call(form, "BeginText", new Point(40, 40));
            ((ImeSink)Get(form, "_ime")!).Text = "Aa"; Call(form, "CommitText");
            Call(form, "ShowSizePicker");
            ((NumericUpDown)((ToolStripControlHost)menus[^1].Items[0]).Control).Value = 32;
            CloseMenu();
            var text = (TextShape)Get(form, "_activeText")!;
            Check("text_size_applies_to_selected", text.FontSize == 32);
            Call(form, "ShowFontPicker"); ((ToolStripComboBox)menus[^1].Items[0]).SelectedItem = "Arial"; CloseMenu();
            Check("font_switch_applies", text.FontFamily == "Arial");
            Call(form, "ShowArrowPicker");
            var arrowMenu = menus[^1];
            var arrowHost = (ToolStripControlHost)arrowMenu.Items[0];
            report.Add($"arrow_gallery_geometry=dpi:{form.DeviceDpi} menu:{arrowMenu.ClientSize} host:{arrowHost.Bounds} control:{arrowHost.Control.Size}");
            Check("arrow_gallery_not_clipped", arrowHost.Width >= (int)(220 * form.DeviceDpi / 96f) && arrowHost.Height >= (int)(200 * form.DeviceDpi / 96f)
                && arrowHost.Bounds.Right <= arrowMenu.ClientSize.Width && arrowHost.Bounds.Bottom <= arrowMenu.ClientSize.Height
                && arrowHost.Control.Width <= arrowHost.Width && arrowHost.Control.Height <= arrowHost.Height);
            CloseMenu();
            using (var graphics = form.CreateGraphics())
            {
                var handle = (Rectangle)Call(form, "TextResizeHandle", graphics, text)!;
                var bounds = text.Bounds(graphics);
                Call(form, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, handle.Left + handle.Width / 2, handle.Top + handle.Height / 2, 0));
                Call(form, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, (int)(bounds.Left + bounds.Width * 1.4), (int)(bounds.Top + bounds.Height * 1.4), 0));
                Call(form, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
            }
            Check("text_resize_handle", text.FontSize > 40);
            Call(form, "Undo"); Check("text_resize_undo", ((List<Shape>)Get(form, "_shapes")!).OfType<TextShape>().Last().FontSize == 32);
            const string payload = "https://example.com/?text=中文&value=A\nsecond line";
            Call(form, "ShowInlineQr", payload);
            Check("qr_result_inline_no_dialog", (string)Get(form, "_qrText")! == payload && bar.ResultCopyVisible && form.OwnedForms.Length == 0);
            Call(form, "ClearQrResult");
            Check("qr_result_clear", Get(form, "_qrText") is null && !bar.ResultCopyVisible);
            var cancelled = false; form.Done += result => cancelled = result is null;
            Call(form, "OnMouseDown", new MouseEventArgs(MouseButtons.Right, 1, 0, 0, 0));
            Check("right_click_cancels", cancelled);
            Application.DoEvents(); Check("native_menu_regression", errors.Count == 0);
            using var picker = new CaptureForm(SystemInformation.VirtualScreen, CaptureMode.Snip,
                new AppSettings { DetectWindows = false, DetectElements = false, IgnoreInjectedCopy = false, AutoMagnifier = false });
            _ = picker.Handle;
            var target = new Rectangle(30, 30, 150, 80);
            Set(picker, "_hoverTarget", target);
            Call(picker, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 60, 50, 0));
            Call(picker, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 60, 50, 0));
            Check("click_uses_highlighted_target", (Rectangle)Get(picker, "_selection")! == target && (bool)Get(picker, "_hasSelection")!);
            Call(picker, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 300, 200, 0));
            Call(picker, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 430, 300, 0));
            Call(picker, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 430, 300, 0));
            Check("drag_overrides_automatic_target", (Rectangle)Get(picker, "_selection")! == new Rectangle(300, 200, 130, 100));
            picker.Close();
        }
        finally { Application.ThreadException -= handler; }
        return string.Join(Environment.NewLine, report) + Environment.NewLine;
    }

    private static string CheckAutomaticTargets()
    {
        var report = new List<string>();
        void Check(string name, bool passed) { report.Add($"{name}={(passed ? "ok" : "FAILED")}"); if (!passed) Environment.ExitCode = 1; }
        using var window = new Form { Text = "SnipLite 自动选区验证", ClientSize = new Size(320, 200), StartPosition = FormStartPosition.Manual, Location = new Point(80, 80), ShowInTaskbar = false, TopMost = true, AutoScaleMode = AutoScaleMode.None };
        using var panel = new Panel { Bounds = new Rectangle(25, 30, 260, 140), BackColor = Color.WhiteSmoke };
        using var button = new Button { Text = "检测目标", Bounds = new Rectangle(30, 25, 110, 38) };
        panel.Controls.Add(button); window.Controls.Add(panel); window.Show(); Application.DoEvents();
        var buttonBounds = button.RectangleToScreen(button.ClientRectangle);
        var buttonPoint = new Point(buttonBounds.Left + buttonBounds.Width / 2, buttonBounds.Top + buttonBounds.Height / 2);
        var panelPoint = panel.PointToScreen(new Point(230, 110));
        var targets = new DesktopTargets();
        // An actual opaque overlay must not become the hit-test target.
        using var cover = new Form { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Bounds = window.Bounds, TopMost = true, ShowInTaskbar = false, BackColor = Color.Gray };
        cover.Show(); Application.DoEvents();
        var immediate = targets.At(buttonPoint, true, includeAutomation: false);
        Check("native_child_target_under_overlay", immediate.Contains(buttonPoint) && immediate.Width <= buttonBounds.Width + 4 && immediate.Height <= buttonBounds.Height + 4);
        var job = Task.Run(() => targets.At(buttonPoint, true));
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!job.IsCompleted && timeout.ElapsedMilliseconds < 5000) { Application.DoEvents(); Thread.Sleep(10); }
        var detailed = job.IsCompletedSuccessfully ? job.Result : Rectangle.Empty;
        Check("automation_target_under_overlay", detailed.Contains(buttonPoint) && detailed.Width <= buttonBounds.Width + 4 && detailed.Height <= buttonBounds.Height + 4);
        var content = targets.At(panelPoint, true, includeAutomation: false);
        Check("native_content_panel_target", content.Contains(panelPoint) && content.Width == panel.Width && content.Height == panel.Height);
        cover.Close(); window.Close();
        return string.Join(Environment.NewLine, report) + Environment.NewLine;
    }

    private static string CheckPreferenceHover()
    {
        using var form = new PreferencesForm(new AppSettings());
        form.Show(); Application.DoEvents();
        using var bitmap = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var rects = (List<(Rectangle Rect, int Index)>)typeof(PreferencesForm).GetField("_tabRects", flags)!.GetValue(form)!;
        var moves = rects.Select(tab => new Point(tab.Rect.Left + tab.Rect.Width / 2, tab.Rect.Top + tab.Rect.Height / 2)).ToArray();
        var dirty = new List<Rectangle>();
        form.Invalidated += (_, e) => dirty.Add(e.InvalidRect);
        var move = typeof(PreferencesForm).GetMethod("OnMouseMove", flags)!;
        foreach (var point in moves) { move.Invoke(form, new object[] { new MouseEventArgs(MouseButtons.None, 0, point.X, point.Y, 0) }); form.Update(); }
        var local = dirty.Count > 0 && dirty.All(rect => rect.Height <= (int)(32 * form.DeviceDpi / 96f) + 8 && rect.Width < form.ClientSize.Width / 2);
        dirty.Clear();
        for (var i = 0; i < 20; i++) move.Invoke(form, new object[] { new MouseEventArgs(MouseButtons.None, 0, moves[^1].X, moves[^1].Y, 0) });
        var stable = dirty.Count == 0;
        form.Close();
        if (!local || !stable) Environment.ExitCode = 1;
        return $"preferences_hover_local_repaint={(local ? "ok" : "FAILED")}\r\npreferences_hover_stable={(stable ? "ok" : "FAILED")}\r\n";
    }

    public static void RunMenuCheck()
    {
        try { File.WriteAllText(Path.Combine(OutputDir, "menu-test.txt"), CheckMenuHover()); }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;
            File.WriteAllText(Path.Combine(OutputDir, "menu-test.txt"), "FAILED: " + ex);
        }
    }

    private static string CheckMenuHover()
    {
        var report = new List<string>();
        void Check(string name, bool passed)
        {
            report.Add($"{name}={(passed ? "ok" : "FAILED")}");
            if (!passed) Environment.ExitCode = 1;
        }
        using var menu = SnipMenu.Create();
        menu.Items.Add(SnipMenu.Item("截屏", () => { }, "F1"));
        menu.Items.Add(SnipMenu.Item("贴图 / 恢复贴图", () => { }, "F3"));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(SnipMenu.Item("禁用快捷键", () => { }, check: true));
        menu.Items.Add(SnipMenu.Item("首选项…", () => { }));
        menu.Items.Add(SnipMenu.Item("退出", () => { }));
        menu.Show(new Point(Screen.PrimaryScreen!.WorkingArea.Left + 30, Screen.PrimaryScreen.WorkingArea.Top + 30));
        Application.DoEvents();
        var items = menu.Items.OfType<ToolStripMenuItem>().ToArray();
        foreach (var (item, index) in items.Select((item, index) => (item, index)))
        {
            item.Select(); menu.Refresh();
            using var bitmap = new Bitmap(menu.Width, menu.Height);
            menu.DrawToBitmap(bitmap, menu.ClientRectangle);
            bitmap.Save(Path.Combine(OutputDir, $"menu-hover-{index}.png"));
            var bounds = item.Bounds;
            // Sample empty space at both ends of a row, including its top and bottom.
            var highlight = new[] { bounds.Left + 2, bounds.Right - 3 }.All(x =>
                new[] { bounds.Top + 1, bounds.Top + bounds.Height / 2, bounds.Bottom - 2 }
                    .All(y => bitmap.GetPixel(x, y).ToArgb() == SnipMenuRenderer.Hover.ToArgb()));
            Check($"menu_hover_row_{index}", item.Selected && highlight);
            var othersClear = items.Where(other => other != item).All(other =>
                bitmap.GetPixel(other.Bounds.Right - 3, other.Bounds.Top + other.Height / 2).ToArgb() == SnipMenuRenderer.Back.ToArgb());
            Check($"menu_hover_no_bleed_{index}", othersClear);
            // Find the actual ink bounds of the label and shortcut, outside the check gutter.
            var inkRows = Enumerable.Range(bounds.Top, bounds.Height).Where(y =>
                Enumerable.Range(bounds.Left + 35, bounds.Width - 48).Any(x =>
                {
                    var pixel = bitmap.GetPixel(x, y);
                    return pixel.R < 100 && pixel.G < 100 && pixel.B < 100;
                })).ToArray();
            Check($"menu_text_centered_{index}", inkRows.Length > 0 &&
                Math.Abs((inkRows[0] + inkRows[^1]) - (bounds.Top + bounds.Bottom - 1)) <= 4);
        }
        menu.Close(); Application.DoEvents();
        return string.Join(Environment.NewLine, report) + Environment.NewLine;
    }

    public static void RenderInlineDemo()
    {
        using var image = new Bitmap(1120, 790);
        using var g = Graphics.FromImage(image); g.Clear(Color.FromArgb(240, 243, 247));
        using var white = new SolidBrush(Color.White); g.FillRectangle(white, 20, 20, 1080, 290);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        foreach (var (y, length, style) in new[] { (70, 310, ArrowStyle.TaperedSolid), (120, 560, ArrowStyle.TaperedSolid), (180, 110, ArrowStyle.TaperedOutline), (245, 110, ArrowStyle.TaperedSolid) })
            new ArrowShape { Start = new Point(80, y), End = new Point(80 + length, y), Width = 4, Color = Color.FromArgb(245, 25, 35), Style = style }.Render(g, image);
        var examples = new[]
        {
            new AnnotationBar { Status = "https://example.com/result?value=完整识别内容&source=二维码", ResultCopyVisible = true, Scale = 1.5f },
            new AnnotationBar { OcrMode = true, Status = "未识别到文字，按 Esc 返回后调整选区", Scale = 1.5f },
            new AnnotationBar { OcrMode = true, Status = "拖动选字 · Ctrl+A 全选 · Ctrl+C 复制 · Esc 返回", Scale = 1.5f }
        };
        for (var i = 0; i < examples.Length; i++)
        {
            examples[i].Layout(new Rectangle(20, 335 + i * 145, 1040, 1), image.Size); examples[i].Render(g);
        }
        image.Save(Path.Combine(OutputDir, "inline-results-arrows.png"));
    }

    public static void RenderPreferencesDemo()
    {
        using var form = new PreferencesForm(new AppSettings());
        form.Show();
        foreach (var (tab, count) in new[] { (0, 1), (1, 2), (2, 3), (3, 3), (4, 2), (5, 2), (6, 1), (7, 1) })
        for (var sub = 0; sub < count; sub++)
        {
            form.SelectSubPage(tab, sub); form.PerformLayout();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(Path.Combine(OutputDir, $"preferences-{tab}-{sub}.png"));
        }
        form.Close();
    }

    public static void RenderAnnotationDemo()
    {
        foreach (var scale in new[] { 1f, 1.5f })
        {
            using var canvas = new Bitmap((int)(1280 * scale), (int)(920 * scale));
            using var g = Graphics.FromImage(canvas);
            g.Clear(Color.FromArgb(242, 245, 249));
            using var font = new Font("Microsoft YaHei UI", 15 * scale, GraphicsUnit.Pixel);
            var tools = new[] { ToolKind.Text, ToolKind.Marker, ToolKind.Mosaic, ToolKind.Eraser, ToolKind.Arrow };
            for (var i = 0; i < tools.Length; i++)
            {
                var y = (int)((20 + i * 180) * scale);
                g.DrawString(tools[i].ToString(), font, Brushes.DimGray, 20 * scale, y);
                var bar = new AnnotationBar { Tool = tools[i], Scale = scale };
                bar.Layout(new Rectangle((int)(20 * scale), y + (int)(22 * scale), (int)(1200 * scale), 1), canvas.Size);
                bar.Render(g);
            }
            canvas.Save(Path.Combine(OutputDir, $"annotation-tools-{scale:0.0}.png"), ImageFormat.Png);
        }
    }

    private static string CheckRecognition()
    {
        var report = new List<string>();
        void Check(string name, bool passed)
        {
            report.Add($"{name}={(passed ? "ok" : "FAILED")}");
            if (!passed) Environment.ExitCode = 1;
        }
        var document = new OcrDocument(new[]
        {
            new RecognizedWord("中文", new RectangleF(10, 10, 40, 20), 0),
            new RecognizedWord("测试", new RectangleF(50, 10, 40, 20), 0),
            new RecognizedWord("Hello", new RectangleF(10, 50, 70, 20), 1),
            new RecognizedWord("OCR", new RectangleF(90, 50, 50, 20), 1)
        });
        Check("ocr_line_spacing", document.Text == "中文测试" + Environment.NewLine + "Hello OCR");
        Check("ocr_reverse_selection", document.GetText(3, 1) == "文测试");
        Check("ocr_cross_line_selection", document.GetText(2, 8) == "测试" + Environment.NewLine + "Hello");
        Check("ocr_unselected", document.GetText(-1, -1) == "");
        Check("ocr_hit_test", document.HitTest(new PointF(12, 15)) == 0 && document.HitTest(new PointF(500, 500)) == -1);
        Check("ocr_nearest_edge", document.HitTest(new PointF(-5, 15), true) == 0);

        using var fixture = RecognitionFixture();
        fixture.Save(Path.Combine(OutputDir, "recognition-fixture.png"));
        var actual = Task.Run(() => Services.RecognizeTextAsync(fixture, "zh-Hans")).GetAwaiter().GetResult();
        Check("ocr_fixture_text", actual.Text.Contains("SnipLite OCR") && actual.Text.Contains("中文识别测试"));
        Check("ocr_fixture_geometry", actual.Characters.Count > 0 && actual.Characters.All(c =>
            c.Bounds.Left >= 0 && c.Bounds.Top >= 0 && c.Bounds.Right <= fixture.Width + 1 && c.Bounds.Bottom <= fixture.Height + 1));
        Check("qr_fixture", Services.DecodeQr(fixture) == RecognitionPayload);
        using var blank = new Bitmap(200, 200);
        using (var g = Graphics.FromImage(blank)) g.Clear(Color.White);
        Check("qr_empty", Services.DecodeQr(blank) is null);
        var empty = Task.Run(() => Services.RecognizeTextAsync(blank, "zh-Hans")).GetAwaiter().GetResult();
        Check("ocr_empty", empty.Characters.Count == 0);

        // Exercise Windows OCR's image-size limit and original-pixel mapping.
        using var large = new Bitmap(6000, 1600);
        using (var g = Graphics.FromImage(large))
        {
            g.Clear(Color.White);
            using var font = new Font("Segoe UI", 96, GraphicsUnit.Pixel);
            g.DrawString("LARGE OCR SAMPLE", font, Brushes.Black, 3000, 600);
        }
        var scaled = Task.Run(() => Services.RecognizeTextAsync(large, "en-US")).GetAwaiter().GetResult();
        Check("ocr_large_image", scaled.Text.Contains("LARGE OCR SAMPLE"));
        Check("ocr_scaled_geometry", scaled.Characters.Count > 0 && scaled.Characters[0].Bounds.Left > 2900 && scaled.Characters[0].Bounds.Top > 580);
        return string.Join(Environment.NewLine, report) + Environment.NewLine;
    }

    private const string RecognitionPayload = "https://example.com/snip-lite?text=hello&source=qr#copy";

    /// <summary>Render short and long result payloads for visual layout checks.</summary>
    public static void RenderResultDemo()
    {
        var samples = new[]
        {
            (Name: "result-link.png", Text: RecognitionPayload),
            (Name: "result-text.png", Text: string.Join(Environment.NewLine,
                Enumerable.Range(1, 16).Select(i => $"第 {i} 行：二维码识别结果，支持中文、换行与完整复制。")))
        };
        foreach (var sample in samples)
        {
            using var form = new RecognitionResultForm(sample.Text);
            form.Show();
            form.PerformLayout();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(Path.Combine(OutputDir, sample.Name), ImageFormat.Png);
            form.Close();
        }
    }

    private static Bitmap RecognitionFixture()
    {
        var image = new Bitmap(1000, 560);
        using var g = Graphics.FromImage(image);
        g.Clear(Color.White);
        using var english = new Font("Segoe UI", 38, GraphicsUnit.Pixel);
        using var chinese = new Font("Microsoft YaHei UI", 36, GraphicsUnit.Pixel);
        g.DrawString("SnipLite OCR selection test", english, Brushes.Black, 35, 45);
        g.DrawString("中文识别测试  复制选中文字", chinese, Brushes.Black, 35, 125);
        g.DrawString("Hello world 12345", english, Brushes.Black, 35, 210);
        g.DrawString("Second line for selection", english, Brushes.Black, 35, 285);
        var writer = new BarcodeWriter
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new EncodingOptions { Width = 300, Height = 300, Margin = 4 }
        };
        using var qr = writer.Write(RecognitionPayload);
        g.DrawImageUnscaled(qr, 680, 240);
        return image;
    }

    /// <summary>A fixed local canvas for checking real capture interactions.</summary>
    public static void ShowRecognitionDemo()
    {
        using var image = RecognitionFixture();
        using var form = new Form
        {
            Text = "SnipLite 识别交互验证", ClientSize = new Size(1000, 650),
            StartPosition = FormStartPosition.CenterScreen, AutoScaleMode = AutoScaleMode.Dpi
        };
        var picture = new PictureBox { Image = image, SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Fill };
        var controls = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 70, Padding = new Padding(12) };
        var capture = new Button { Text = "开始截图", AutoSize = true };
        var paste = new TextBox { Width = 600, Name = "PasteVerification", PlaceholderText = "复制后在此粘贴，核对结果" };
        capture.Click += (_, _) =>
        {
            var overlay = new CaptureForm(SystemInformation.VirtualScreen, CaptureMode.Snip, new AppSettings());
            overlay.Done += result => result?.Image.Dispose();
            overlay.Show();
        };
        controls.Controls.AddRange(new Control[] { capture, paste });
        form.Controls.Add(picture);
        form.Controls.Add(controls);
        Application.Run(form);
    }

    /// <summary>Encodes a known payload, paints it into the frame, decodes a crop back.</summary>
    private static string QrRoundTrip(Bitmap frame)
    {
        const string payload = "SnipLite QR roundtrip OK";

        try
        {
            var writer = new BarcodeWriter
            {
                Format = BarcodeFormat.QR_CODE,
                Options = new EncodingOptions { Width = 320, Height = 320, Margin = 4 }
            };

            using var qr = writer.Write(payload);

            using (var g = Graphics.FromImage(frame))
                g.DrawImage(qr, 80, 80);

            using var crop = frame.Clone(new Rectangle(60, 60, 360, 360), PixelFormat.Format32bppArgb);
            var decoded = Services.DecodeQr(crop);

            return decoded == payload ? "ok" : "FAILED: " + (decoded ?? "null");
        }
        catch (Exception ex)
        {
            return "FAILED: " + ex.Message;
        }
    }

    /// <summary>
    /// Renders every annotation tool plus the toolbar onto a colourful canvas so
    /// the drawing code can be checked without clicking through the overlay.
    /// </summary>
    public static void RenderDemo()
    {
        var screen = SystemInformation.VirtualScreen;

        using var shot = new Bitmap(screen.Width, screen.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(shot))
            g.CopyFromScreen(screen.Location, Point.Empty, screen.Size, CopyPixelOperation.SourceCopy);

        using var canvas = new Bitmap(shot);
        using (var g = Graphics.FromImage(canvas))
        {
            using (var gradient = new System.Drawing.Drawing2D.LinearGradientBrush(
                new Rectangle(0, 0, canvas.Width, canvas.Height),
                Color.FromArgb(24, 90, 160),
                Color.FromArgb(250, 170, 60),
                30f))
            {
                g.FillRectangle(gradient, 0, 0, canvas.Width, canvas.Height);
            }

            using (var vivid = new SolidBrush(Color.FromArgb(90, 220, 60, 130)))
                g.FillEllipse(vivid, 700, 200, 900, 700);

            using (var cool = new SolidBrush(Color.FromArgb(70, 40, 220, 190)))
                g.FillEllipse(cool, 1800, 900, 1100, 800);

            // fine detail right under the mosaic, so the pixelation is obvious
            using var detailFont = new Font("Consolas", 10.5f);
            using var detailBrush = new SolidBrush(Color.FromArgb(240, 255, 255, 255));

            for (var i = 0; i < 12; i++)
                g.DrawString($"password: A9f3-K2xQ-{i:00}  token=8bd1e0f7", detailFont, detailBrush, 1020, 288 + i * 15);
        }

        var crop = new Rectangle(200, 200, 1200, 700);
        var scale = GetDpi() / 96f;

        var scribble = new PenShape
        {
            Color = Color.FromArgb(253, 216, 53),
            Width = 3f
        };

        for (var i = 0; i < 40; i++)
            scribble.Points.Add(new Point(300 + i * 8, 760 + (int)(Math.Sin(i * 0.4) * 40)));

        var shapes = new List<Shape>
        {
            new RectShape
            {
                Color = Color.FromArgb(229, 57, 53),
                Width = 3f,
                Rect = new Rectangle(260, 260, 420, 240)
            },
            new EllipseShape
            {
                Color = Color.FromArgb(67, 160, 71),
                Width = 3f,
                Rect = new Rectangle(700, 270, 300, 200)
            },
            new ArrowShape
            {
                Color = Color.FromArgb(30, 136, 229),
                Width = 4f,
                Start = new Point(260, 560),
                End = new Point(640, 620)
            },
            scribble,
            new TextShape
            {
                Color = Color.FromArgb(255, 255, 255),
                FontSize = 16f,
                At = new Point(700, 540),
                Text = "SnipLite 标注：文字 / 中文"
            },
            new MosaicShape
            {
                Rect = new Rectangle(1010, 270, 300, 200)
            }
        };

        using var image = new Bitmap(crop.Width, crop.Height, PixelFormat.Format32bppArgb);

        using (var g = Graphics.FromImage(image))
        {
            g.DrawImage(canvas, new Rectangle(0, 0, crop.Width, crop.Height), crop, GraphicsUnit.Pixel);
            g.TranslateTransform(-crop.X, -crop.Y);
            g.SetClip(crop);

            foreach (var shape in shapes)
                shape.Render(g, canvas);

            g.ResetClip();
            g.ResetTransform();

            var bar = new AnnotationBar
            {
                Scale = scale,
                Tool = ToolKind.Rectangle,
                Color = AnnotationBar.Palette[0],
                StrokeWidth = 3f
            };

            bar.Layout(new Rectangle(20, 20, crop.Width - 40, crop.Height - 40), crop.Size);

            bar.Render(g);
        }

        image.Save(Path.Combine(OutputDir, "demo.png"), ImageFormat.Png);

        // zoomed crop of the mosaic area, so blocking quality is visible
        using var zoom = new Bitmap(600, 400, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(zoom))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.DrawImage(image, new Rectangle(0, 0, 600, 400), new Rectangle(810, 70, 300, 200), GraphicsUnit.Pixel);
        }

        zoom.Save(Path.Combine(OutputDir, "demo-mosaic.png"), ImageFormat.Png);
    }

    private static float GetDpi()
    {
        using var g = Graphics.FromHwnd(IntPtr.Zero);
        return g.DpiX;
    }
}
