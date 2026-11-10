using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;

namespace SnipLite;

internal static partial class SelfTest
{
    public static void RunTranslationCheck(bool live)
    {
        var report = new List<string>();
        void Check(string name, bool passed)
        {
            report.Add($"{name}={(passed ? "ok" : "FAILED")}");
            if (!passed) Environment.ExitCode = 1;
        }
        try
        {
            var document = OcrDocument.FromCharacters(new[]
            {
                new RecognizedCharacter("A", new(20, 20, 10, 20), ""),
                new RecognizedCharacter("B", new(32, 20, 10, 20), ""),
                new RecognizedCharacter("C", new(20, 65, 10, 20), Environment.NewLine)
            });
            Check("ocr_margin_before_first", document.HitTest(new(13, 28), tolerance: 8) == 0);
            Check("ocr_margin_after_last", document.HitTest(new(48, 28), tolerance: 8) == 1);
            Check("ocr_margin_above_glyph", document.HitTest(new(24, 13), tolerance: 8) == 0);
            Check("ocr_gap_uses_nearest_not_first_expanded", document.HitTest(new(31.5f, 28), tolerance: 8) == 1);
            Check("ocr_distant_whitespace_not_selected", document.HitTest(new(25, 52), tolerance: 8) == -1);
            Check("ocr_drag_outside_still_snaps", document.HitTest(new(120, 28), nearest: true) == 1);
            Check("english_localization_initializes", UiText.T("文字识别", "en-US") == "OCR" && UiText.T("识别引擎", "en-US") == "OCR engine");

            var handler = new TranslationHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("[[[\"你好\",\"Hello\"],[\"世界\",\"world\"]],null,\"en\"]", Encoding.UTF8, "application/json") }));
            using var http = new HttpClient(handler);
            var provider = new GooglePublicTranslationProvider(http);
            var result = provider.TranslateAsync("Hello & world\n?", "auto", "zh-CN", default).GetAwaiter().GetResult();
            Check("translation_multi_segment_and_detection", result.Text == "你好世界" && result.DetectedLanguage == "en");
            Check("translation_query_encoded", handler.LastUri!.Query.Contains("q=Hello%20%26%20world%0A%3F") && handler.LastUri.Query.Contains("sl=auto") && handler.LastUri.Query.Contains("tl=zh-CN"));
            var calls = handler.Calls;
            result = provider.TranslateAsync("same text", "en", "en", default).GetAwaiter().GetResult();
            Check("same_language_avoids_network", result.Text == "same text" && handler.Calls == calls);
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            try { provider.TranslateAsync("Hello", "en", "zh-CN", canceled.Token).GetAwaiter().GetResult(); Check("translation_cancel", false); }
            catch (OperationCanceledException) { Check("translation_cancel", handler.Calls == calls); }
            using var rateHttp = new HttpClient(new TranslationHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests))));
            try { new GooglePublicTranslationProvider(rateHttp).TranslateAsync("Hello", "en", "zh-CN", default).GetAwaiter().GetResult(); Check("translation_rate_limit", false); }
            catch (InvalidOperationException ex) { Check("translation_rate_limit", ex.Message.Contains("频繁")); }
            using var failHttp = new HttpClient(new TranslationHttpHandler((_, _) => throw new HttpRequestException("private request details")));
            try { new GooglePublicTranslationProvider(failHttp).TranslateAsync("Hello", "en", "zh-CN", default).GetAwaiter().GetResult(); Check("translation_network_error", false); }
            catch (InvalidOperationException ex) { Check("translation_network_error", ex.Message.Contains("检查网络") && !ex.Message.Contains("private")); }
            var longText = new string('a', 599) + "😀\n" + string.Concat(Enumerable.Repeat("这是中文内容。 ", 200));
            var chunks = GooglePublicTranslationProvider.SplitText(longText).ToArray();
            Check("translation_chunks_preserve_unicode_and_spaces", string.Concat(chunks.Select(c => c.Text + c.Separator)) == longText
                && chunks.All(c => c.Text.Length <= 600 && (c.Text.Length == 0 || !char.IsHighSurrogate(c.Text[^1]))));
            try { GooglePublicTranslationProvider.ParseResponse("{\"unexpected\":true}"); Check("translation_invalid_response", false); }
            catch (InvalidOperationException) { Check("translation_invalid_response", true); }

            foreach (var scale in new[] { 1f, 1.5f, 2f })
            {
                var bar = new AnnotationBar { Scale = scale, TranslationMode = true, AttachedPanelHeight = (int)(44 * scale) };
                bar.Layout(new Rectangle(160, 680, 800, 100), new Size(1920, 1080));
                Check($"translation_group_in_screen_{scale}", bar.Bounds.Bottom + (int)(48 * scale) <= 1080 && !bar.ItemBounds("translate").IsEmpty);
                Check($"translation_keeps_full_toolbar_{scale}", new[] { "rect", "pen", "ocr", "qr", "undo", "save", "pin", "copy" }.All(id => !bar.ItemBounds(id).IsEmpty));
                var ocr = new AnnotationBar { Scale = scale, OcrMode = true, OcrCopyVisible = true, Status = "拖动选字 · Ctrl+A 全选 · Ctrl+C 复制 · Esc 返回编辑" };
                ocr.Layout(new Rectangle(20, 10, 1200, 10), new Size(1920, 1080));
                Check($"ocr_keeps_full_toolbar_{scale}", new[] { "rect", "pen", "translate", "qr", "save", "pin", "copy" }.All(id => !ocr.ItemBounds(id).IsEmpty));
                Check($"ocr_separate_status_and_copy_{scale}", ocr.MessageBounds.Height == (int)(44 * scale)
                    && ocr.MessageBounds.Top >= ocr.MainBounds.Bottom - 1 && ocr.MessageBounds.Contains(ocr.ItemBounds("ocrtextcopy")));
                using (var ocrPreview = new Bitmap(ocr.Bounds.Right + 1, ocr.Bounds.Bottom + 1))
                using (var painter = Graphics.FromImage(ocrPreview))
                {
                    painter.Clear(SnipMenuRenderer.Back); ocr.Render(painter);
                    using var crop = ocrPreview.Clone(ocr.Bounds, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    crop.Save(Path.Combine(OutputDir, $"ocr-toolbar-preview-{scale}.png"));
                }
                using var host = new Form { ClientSize = new Size((int)(730 * scale), (int)(360 * scale)), StartPosition = FormStartPosition.Manual, Location = new Point(40, 40), BackColor = Color.FromArgb(88, 91, 96) };
                using var panel = new TranslationPanel(scale, "en", "zh-CN") { Location = new Point((int)(20 * scale), (int)(20 * scale)) };
                host.Controls.Add(panel); host.Show(); Application.DoEvents();
                Check($"translation_language_defaults_{scale}", panel.SourceLanguage == "en" && panel.TargetLanguage == "zh-CN");
                panel.SelectLanguage(true, "auto"); panel.SetOriginal("Hello world"); panel.SetResult(new("你好，世界。\r\n选择文字后即可复制。", "en"));
                Check($"translation_uses_one_compact_row_{scale}", panel.Height == (int)(44 * scale) && !panel.Controls.OfType<TextBox>().Any() && panel.ResultText.Contains("世界"));
                Check($"translation_defaults_to_translated_view_{scale}", !panel.ShowOriginal);
                panel.ToggleView(); Check($"translation_single_icon_toggles_original_{scale}", panel.ShowOriginal);
                panel.ToggleView(); Check($"translation_single_icon_toggles_back_{scale}", !panel.ShowOriginal);
                using var preview = new Bitmap(panel.Width, panel.Height); panel.DrawToBitmap(preview, panel.ClientRectangle);
                preview.Save(Path.Combine(OutputDir, $"translation-panel-{scale}.png"));
                if (scale == 1f)
                {
                    panel.ShowLanguages(true); Application.DoEvents();
                    var menu = ((List<ContextMenuStrip>)typeof(TranslationPanel).GetField("_menus", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(panel)!).Last();
                    Check("language_menu_height_and_auto", menu.Height <= 284 && menu.Items[0].Text == "自动检测语言" && menu.Items.Count == TranslationLanguages.All.Length);
                    using var menuImage = new Bitmap(menu.Width, menu.Height); menu.DrawToBitmap(menuImage, menu.ClientRectangle);
                    menuImage.Save(Path.Combine(OutputDir, "translation-language-menu.png")); panel.CloseMenus(); Application.DoEvents();
                }
                panel.SwapLanguages();
                Check($"auto_source_swap_uses_detected_language_{scale}", panel.SourceLanguage == "zh-CN" && panel.TargetLanguage == "en");
                panel.SelectLanguage(false, "auto"); Check($"target_never_auto_{scale}", panel.TargetLanguage != "auto");
                host.Close(); Application.DoEvents();
            }
            CheckTranslationOverlay(Check);
            CheckTranslationCapture(Check, live);
        }
        catch (Exception ex) { Environment.ExitCode = 1; report.Add("FAILED: " + ex); }
        File.WriteAllLines(Path.Combine(OutputDir, "translation-test.txt"), report);
        foreach (var line in report) Console.WriteLine(line);
    }

    private static void CheckTranslationCapture(Action<string, bool> check, bool live)
    {
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        object? Get(CaptureForm form, string name) => typeof(CaptureForm).GetField(name, flags)!.GetValue(form);
        void Set(CaptureForm form, string name, object value) => typeof(CaptureForm).GetField(name, flags)!.SetValue(form, value);
        object? Call(CaptureForm form, string name, params object[] args) => typeof(CaptureForm).GetMethod(name, flags)!.Invoke(form, args);
        static void Complete(Task task)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (!task.IsCompleted && timer.ElapsedMilliseconds < 45_000) { Application.DoEvents(); Thread.Sleep(10); }
            if (!task.IsCompleted) throw new TimeoutException("Translation check timed out");
            task.GetAwaiter().GetResult(); Application.DoEvents();
        }
        var screen = SystemInformation.VirtualScreen;
        var crop = new Rectangle(100, 100, 680, 145);
        var preset = crop; preset.Offset(screen.Location);
        using var capture = new CaptureForm(screen, CaptureMode.Snip, new AppSettings
        {
            DetectWindows = false, DetectElements = false, AutoMagnifier = false,
            ShowCrosshair = false, ExitCaptureOnDeactivate = false
        }, preset);
        capture.Show(); Application.DoEvents();
        var controlled = new ControlledTranslationProvider();
        Set(capture, "_translationProvider", controlled);
        Call(capture, "EnsureTranslationPanel");
        var panel = (TranslationPanel)Get(capture, "_translationPanel")!;
        panel.Visible = true;
        Set(capture, "_translationInput", "first request");
        var first = (Task)Call(capture, "TranslateRecognizedTextAsync", false)!;
        Set(capture, "_translationInput", "second request");
        var second = (Task)Call(capture, "TranslateRecognizedTextAsync", false)!;
        check("new_language_cancels_previous_request", controlled.Requests[0].Token.IsCancellationRequested);
        controlled.Requests[1].Completion.SetResult(new("第二个结果", "en")); Complete(second);
        controlled.Requests[0].Completion.SetResult(new("旧的结果", "en")); Complete(first);
        check("stale_translation_does_not_replace_latest", panel.ResultText == "第二个结果");
        Set(capture, "_translationInput", "pending");
        var pending = (Task)Call(capture, "TranslateRecognizedTextAsync", false)!;
        var keyArgs = new object[] { Message.Create(capture.Handle, 0, IntPtr.Zero, IntPtr.Zero), Keys.Escape };
        typeof(CaptureForm).GetMethod("ProcessCmdKey", flags)!.Invoke(capture, keyArgs);
        controlled.Requests[2].Completion.SetResult(new("不应弹回", "en")); Complete(pending);
        check("escape_cancels_and_returns_to_editing", !panel.Visible && !(bool)Get(capture, "_finished")! && controlled.Requests[2].Token.IsCancellationRequested);

        // Paint the entire selected crop with our fixture; only these generated words can go online.
        var bitmap = (Bitmap)Get(capture, "_screenshot")!;
        using (var g = Graphics.FromImage(bitmap))
        using (var font = new Font("Segoe UI", 26, GraphicsUnit.Pixel))
        {
            g.FillRectangle(Brushes.White, crop);
            g.DrawString("Hello world\nPlease select the text and copy it.", font, Brushes.Black, crop.Left + 20, crop.Top + 20);
        }
        if (live)
        {
            Set(capture, "_translationProvider", new GooglePublicTranslationProvider());
            Complete((Task)Call(capture, "TranslateSelectionAsync")!);
            check("live_capture_ocr_to_chinese", panel.ResultText.Contains("你好") && panel.ResultText.Contains("复制"));
            var bounds = panel.Bounds;
            check("live_panel_attached_to_toolbar", bounds.Width == ((AnnotationBar)Get(capture, "_bar")!).MainBounds.Width && bounds.Bottom <= capture.ClientSize.Height);
            using var render = (Bitmap)Call(capture, "RenderComposite", crop)!;
            render.Save(Path.Combine(OutputDir, "translation-live-result.png"));
            reportLive(panel.ResultText);
        }
        else
        {
            var immediate = new ImmediateTranslationProvider(); Set(capture, "_translationProvider", immediate);
            Complete((Task)Call(capture, "TranslateSelectionAsync")!);
            check("capture_ocr_pipeline_passes_text_to_provider", immediate.Text.Contains("Hello world") && immediate.Source == "en" && immediate.Target == "zh-CN");
            check("capture_result_ready_for_overlay", panel.ResultText.Contains("你好世界") && panel.Parent == capture);
        }
        using var translated = (Bitmap)Call(capture, "RenderComposite", crop)!;
        using var original = bitmap.Clone(crop, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        bool SamePixels(Bitmap firstImage, Bitmap secondImage) => Enumerable.Range(0, crop.Height).All(y =>
            Enumerable.Range(0, crop.Width).All(x => firstImage.GetPixel(x, y) == secondImage.GetPixel(x, y)));
        check("translation_changes_exported_pixels", !SamePixels(translated, original));
        panel.ToggleView(); Application.DoEvents();
        using (var restored = (Bitmap)Call(capture, "RenderComposite", crop)!) check("original_toggle_restores_exact_source_pixels", SamePixels(restored, original));
        panel.ToggleView(); Application.DoEvents();
        var bar = (AnnotationBar)Get(capture, "_bar")!;
        check("translation_button_remains_selected_with_full_toolbar", bar.TranslationMode && !bar.ItemBounds("rect").IsEmpty && !bar.ItemBounds("ocr").IsEmpty);
        RenderTranslationPreview(translated, panel, bar.Scale);
        Call(capture, "Command", "pen"); Application.DoEvents();
        check("annotation_tools_still_work_during_translation", bar.Tool == ToolKind.Pen && bar.TranslationMode && panel.Visible);
        var shapes = (List<Shape>)Get(capture, "_shapes")!;
        shapes.Add(new RectShape { Rect = new(crop.X + 24, crop.Y + 24, 16, 16), Fill = true, Color = Color.Red });
        Set(capture, "_annotationDirty", true);
        using (var marked = (Bitmap)Call(capture, "RenderComposite", crop)!)
            check("new_annotations_render_above_translation", marked.GetPixel(32, 32).R > 240 && marked.GetPixel(32, 32).G < 20);
        shapes.Clear(); Set(capture, "_annotationDirty", true);
        var failed = new FailingTranslationProvider(); Set(capture, "_translationProvider", failed);
        Set(capture, "_translationBlocks", new[]
        {
            new TranslationBlock("failure-one", new(0, 0, 20, 20), 1),
            new TranslationBlock("failure-two", new(0, 30, 20, 20), 1),
            new TranslationBlock("failure-three", new(0, 60, 20, 20), 1)
        });
        Complete((Task)Call(capture, "TranslateRecognizedTextAsync", true)!);
        check("translation_failure_stops_remaining_blocks", failed.Calls == 1 && panel.Controls.OfType<Label>().Single().Text.Contains("测试翻译失败"));
        Call(capture, "CloseTranslation"); Application.DoEvents();
        using (var restored = (Bitmap)Call(capture, "RenderComposite", crop)!) check("translation_off_restores_exact_source_pixels", SamePixels(restored, original));
        var selected = OcrDocument.FromCharacters(new[]
        {
            new RecognizedCharacter("A", new(20, 20, 10, 20), ""),
            new RecognizedCharacter("B", new(32, 20, 10, 20), "")
        });
        Set(capture, "_ocrMode", true); Set(capture, "_ocrCrop", crop); Set(capture, "_ocrDocument", selected);
        Call(capture, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, crop.X + 13, crop.Y + 28, 0));
        check("ocr_margin_hover_shows_text_cursor", capture.Cursor == Cursors.IBeam);
        Call(capture, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, crop.X + 13, crop.Y + 28, 0));
        Call(capture, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, crop.X + 48, crop.Y + 28, 0));
        Call(capture, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, crop.X + 48, crop.Y + 28, 0));
        check("ocr_margin_drag_selects_full_text", selected.GetText((int)Get(capture, "_ocrAnchor")!, (int)Get(capture, "_ocrEnd")!) == "AB");
        bar.OcrMode = true; bar.OcrCopyVisible = true; bar.Status = "拖动选字";
        capture.Refresh(); Application.DoEvents();
        var penButton = bar.ItemBounds("pen"); bar.Tool = ToolKind.None;
        Call(capture, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, penButton.Left + penButton.Width / 2, penButton.Top + penButton.Height / 2, 0));
        check("ocr_toolbar_click_returns_to_editing_tool", !(bool)Get(capture, "_ocrMode")! && bar.Tool == ToolKind.Pen && !bar.OcrMode && !bar.OcrCopyVisible);
        capture.Close(); Application.DoEvents();
        static void reportLive(string text) => Console.WriteLine("Live sample translation: " + text.ReplaceLineEndings(" / "));
    }

    private static void CheckTranslationOverlay(Action<string, bool> check)
    {
        var document = OcrDocument.FromCharacters(new[]
        {
            new RecognizedCharacter("First line", new(20, 20, 120, 22), ""),
            new RecognizedCharacter("Wrapped line", new(20, 46, 140, 22), Environment.NewLine),
            new RecognizedCharacter("Separate label", new(280, 20, 140, 22), Environment.NewLine)
        });
        var blocks = TranslationOverlay.GetBlocks(document);
        check("overlay_groups_wrapped_lines_not_other_columns", blocks.Count == 2 && blocks[0].LineCount == 2 && blocks[1].Text == "Separate label");
        foreach (var dark in new[] { false, true })
        {
            using var image = new Bitmap(440, 100);
            using (var g = Graphics.FromImage(image))
            using (var font = new Font("Segoe UI", 20, GraphicsUnit.Pixel))
            {
                g.Clear(dark ? Color.FromArgb(32, 32, 32) : Color.White);
                g.DrawString("First line\nWrapped line", font, dark ? Brushes.White : Brushes.Black, 20, 20);
            }
            using var overlay = TranslationOverlay.Render(image, blocks.Take(1).ToArray(), new[] { new TranslationResult("第一行\n自动换行内容", "en") });
            check($"overlay_keeps_outside_transparent_{dark}", overlay.GetPixel(0, 0).A == 0 && overlay.GetPixel(22, 22).A == 255);
            check($"overlay_text_has_no_transparent_glyph_holes_{dark}", Enumerable.Range(20, 48).All(y => Enumerable.Range(20, 140).All(x => overlay.GetPixel(x, y).A == 255)));
            using var composed = new Bitmap(image);
            using (var g = Graphics.FromImage(composed)) g.DrawImageUnscaled(overlay, 0, 0);
            check($"overlay_preserves_background_outside_text_{dark}", composed.GetPixel(0, 0) == image.GetPixel(0, 0));
            composed.Save(Path.Combine(OutputDir, $"translation-overlay-{dark}.png"));
        }
    }

    private static void RenderTranslationPreview(Bitmap translated, TranslationPanel panel, float scale)
    {
        var bar = new AnnotationBar { Scale = scale, TranslationMode = true, AttachedPanelHeight = panel.Height };
        var canvasSize = new Size(Math.Max((int)(800 * scale), translated.Width + 40), translated.Height + (int)(145 * scale));
        bar.Layout(new Rectangle(20, 10, canvasSize.Width - 40, translated.Height), canvasSize);
        using var canvas = new Bitmap(canvasSize.Width, canvasSize.Height);
        using (var g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.FromArgb(225, 227, 230));
            g.DrawImageUnscaled(translated, (canvas.Width - translated.Width) / 2, 10);
            bar.Render(g);
            using var row = new Bitmap(panel.Width, panel.Height); panel.DrawToBitmap(row, panel.ClientRectangle);
            g.DrawImageUnscaled(row, bar.MainBounds.Left, bar.Bounds.Bottom + (int)(4 * scale));
        }
        canvas.Save(Path.Combine(OutputDir, "translation-overlay-preview.png"));
    }

    private sealed class TranslationHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; LastUri = request.RequestUri; return send(request, cancellationToken); }
    }
    private sealed class ControlledTranslationProvider : ITranslationProvider
    {
        public List<(TaskCompletionSource<TranslationResult> Completion, CancellationToken Token)> Requests { get; } = new();
        public Task<TranslationResult> TranslateAsync(string text, string source, string target, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<TranslationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Requests.Add((completion, cancellationToken)); return completion.Task;
        }
    }
    private sealed class ImmediateTranslationProvider : ITranslationProvider
    {
        public string Text = "", Source = "", Target = "";
        public Task<TranslationResult> TranslateAsync(string text, string source, string target, CancellationToken cancellationToken)
        { Text += text; Source = source; Target = target; return Task.FromResult(new TranslationResult("你好世界", "en")); }
    }
    private sealed class FailingTranslationProvider : ITranslationProvider
    {
        public int Calls;
        public Task<TranslationResult> TranslateAsync(string text, string source, string target, CancellationToken cancellationToken)
        { Calls++; throw new InvalidOperationException("测试翻译失败"); }
    }
}
