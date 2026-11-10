namespace SnipLite;

internal static partial class SelfTest
{
    public static void RunTextCheck()
    {
        try { File.WriteAllText(Path.Combine(OutputDir, "text-test.txt"), CheckTextEditing()); }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;
            File.WriteAllText(Path.Combine(OutputDir, "text-test.txt"), "FAILED: " + ex);
        }
    }

    private static string CheckTextEditing()
    {
        var report = new List<string>();
        void Check(string name, bool passed)
        {
            report.Add($"{name}={(passed ? "ok" : "FAILED")}");
            if (!passed) Environment.ExitCode = 1;
        }
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        object? Get(CaptureForm form, string name) => typeof(CaptureForm).GetField(name, flags)!.GetValue(form);
        void Set(CaptureForm form, string name, object value) => typeof(CaptureForm).GetField(name, flags)!.SetValue(form, value);
        object? Call(CaptureForm form, string name, params object[] args) => typeof(CaptureForm).GetMethod(name, flags)!.Invoke(form, args);
        void Input(CaptureForm form, string name, Point point, MouseButtons button = MouseButtons.Left) =>
            Call(form, name, new MouseEventArgs(button, 1, point.X, point.Y, 0));
        using var capture = new CaptureForm(SystemInformation.VirtualScreen, CaptureMode.Snip,
            new AppSettings { DetectWindows = false, DetectElements = false, AutoMagnifier = false, ShowCrosshair = false, IgnoreInjectedCopy = false });
        _ = capture.Handle;
        Set(capture, "_selection", new Rectangle(60, 90, 1400, 900)); Set(capture, "_hasSelection", true);
        var bar = (AnnotationBar)Get(capture, "_bar")!; bar.Tool = ToolKind.Text;
        var shapes = (List<Shape>)Get(capture, "_shapes")!;
        var anchor = new Point(140, 180);
        const string original = "Hello 世界\r\n第二行";
        Call(capture, "BeginText", anchor); ((ImeSink)Get(capture, "_ime")!).Text = original;
        Call(capture, "CommitText");
        var text = (TextShape)shapes.Single();
        Point At(int index)
        {
            using var graphics = capture.CreateGraphics();
            using var font = TextShape.CreateFont(text.FontFamily, text.FontSize, TextShape.StyleOf(text.Bold, text.Italic));
            var caret = TextEditLayout.Caret(graphics, font, text.Text, text.At, index);
            return new Point(caret.X, caret.Top + caret.Height / 2);
        }
        Input(capture, "OnMouseMove", At(2), MouseButtons.None);
        var initialCursor = (TextIBeamCursor?)Get(capture, "_textCursor");
        Check("text_hover_ibeam", initialCursor is not null && capture.Cursor.Handle == initialCursor.Cursor.Handle);
        Input(capture, "OnMouseDown", At(2)); Input(capture, "OnMouseUp", At(2));
        var editor = (ImeSink?)Get(capture, "_ime");
        Check("text_click_reopens_at_character", editor?.Text == original && editor.SelectionStart == 2);
        if (editor is null) throw new InvalidOperationException("Click did not reopen text input.");
        var layer = (Bitmap)Call(capture, "GetAnnotationLayer", (Rectangle)Get(capture, "_selection")!)!;
        Check("text_edit_hides_committed_layer", Enumerable.Range(0, layer.Height).All(y =>
            Enumerable.Range(0, layer.Width).All(x => layer.GetPixel(x, y).A == 0)));
        editor.SelectedText = "插入"; Call(capture, "CommitText");
        const string inserted = "He插入llo 世界\r\n第二行";
        Check("text_insert_updates_same_shape", ReferenceEquals(text, shapes.Single()) && text.Text == inserted);
        Call(capture, "Undo"); Check("text_edit_undo", ((TextShape)shapes.Single()).Text == original);
        Call(capture, "Redo"); text = (TextShape)shapes.Single(); Check("text_edit_redo", text.Text == inserted);
        Input(capture, "OnMouseDown", At(2)); Input(capture, "OnMouseMove", At(6)); Input(capture, "OnMouseUp", At(6));
        editor = (ImeSink)Get(capture, "_ime")!;
        Check("text_mouse_select_characters", editor.SelectedText == inserted[2..6]);
        editor.SelectedText = "替换"; Call(capture, "CommitText");
        var replaced = inserted[..2] + "替换" + inserted[6..];
        Check("text_replace_selected", text.Text == replaced && shapes.Count == 1);
        Input(capture, "OnMouseDown", At(text.Text.Length)); Input(capture, "OnMouseUp", At(text.Text.Length));
        editor = (ImeSink)Get(capture, "_ime")!;
        editor.Text = "本次修改应被取消"; Call(capture, "CancelText");
        Check("text_cancel_preserves_existing", text.Text == replaced && shapes.Count == 1);
        Call(capture, "EditText", text, At(1));
        var smallCaret = (Rectangle)Get(capture, "_caretRect")!;
        bar.FontSize = 40; Set(capture, "_cursor", new Point(anchor.X + 2, anchor.Y + 2)); Call(capture, "RestyleText");
        var largeCaret = (Rectangle)Get(capture, "_caretRect")!;
        var largeCursor = (TextIBeamCursor?)Get(capture, "_textCursor");
        Check("text_caret_and_cursor_follow_font", largeCaret.Height > smallCaret.Height * 2 && largeCursor?.Height == largeCaret.Height);
        // Cursor.Size reports system cursor metrics; inspect the actual native
        // bitmap so a large font cannot silently receive a small mouse pointer.
        Check("text_native_cursor_dimensions", largeCursor is not null && largeCursor.NativeSize.Height == largeCaret.Height + 4);
        using (var preview = new Bitmap(760, 320))
        using (var graphics = Graphics.FromImage(preview))
        {
            graphics.Clear(Color.White); graphics.TranslateTransform(30 - anchor.X, 30 - anchor.Y);
            Call(capture, "DrawTextPreview", graphics); graphics.ResetTransform();
            largeCursor?.Draw(graphics, new Point(650, 35));
            preview.Save(Path.Combine(OutputDir, "text-edit-preview.png"));
        }
        Call(capture, "CancelText");
        using (var graphics = capture.CreateGraphics())
        {
            var bounds = text.Bounds(graphics); var edge = new Point((int)bounds.Left - 4, (int)(bounds.Top + bounds.Height / 2));
            var before = text.At;
            Input(capture, "OnMouseDown", edge); Input(capture, "OnMouseMove", new Point(edge.X + 20, edge.Y + 15));
            Input(capture, "OnMouseUp", new Point(edge.X + 20, edge.Y + 15));
            Check("text_frame_drag_preserved", text.At == new Point(before.X + 20, before.Y + 15));
        }
        // Hit testing must retain spaces, line boundaries and Unicode clusters.
        using (var bitmap = new Bitmap(500, 200))
        using (var graphics = Graphics.FromImage(bitmap))
        using (var font = TextShape.CreateFont("Microsoft YaHei UI", 20, FontStyle.Regular))
        {
            const string unicode = "A e\u0301😀 中\r\n末行";
            var stops = System.Globalization.StringInfo.ParseCombiningCharacters(unicode);
            var roundTrip = stops.Where(index => unicode[index] is not ('\r' or '\n')).All(index =>
            {
                var caret = TextEditLayout.Caret(graphics, font, unicode, Point.Empty, index);
                return TextEditLayout.HitTest(graphics, font, unicode, Point.Empty, new Point(caret.X, caret.Top + caret.Height / 2)) == index;
            });
            Check("text_unicode_multiline_hit_test", roundTrip);
        }
        capture.Close(); Application.DoEvents();
        return string.Join(Environment.NewLine, report) + Environment.NewLine;
    }
}
