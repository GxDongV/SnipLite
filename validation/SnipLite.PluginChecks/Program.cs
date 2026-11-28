using System.Drawing.Imaging;
using System.Text.Json;
using SnipLite;
using SnipLite.Plugins;
using SnipLite.Plugin.Abstractions;
using SnipLite.ScrollCapture;

namespace SnipLiteChecks;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        _checks++;
    }
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); AppTheme.Refresh();
        if (args.Length is 2 or 3 && args[0] == "--recorder-package")
            return RecorderIntegration.Run(Path.GetFullPath(args[1]), args.Length == 3 ? Path.GetFullPath(args[2]) : null);
        if (args is ["--recorder-benchmark", var recorderPackage]) return RecorderIntegration.Run(Path.GetFullPath(recorderPackage), benchmark: true);
        if (args.Contains("--demo")) { Application.Run(new ScrollDemo()); return 0; }
        try
        {
            if (args.Contains("--alignment-diagnostics"))
            {
                Algorithms();
                FixedEdgeMatching();
                AlignmentDiagnostics();
                SparseOverlapMatching();
                DistantRivalAfterNearbyVerification();
                ScrollUi();
                Console.WriteLine($"PASS: {_checks} scrolling alignment and UI checks.");
                return 0;
            }
            Algorithms();
            FixedEdgeMatching();
            IconAssets();
            Plugins().GetAwaiter().GetResult();
            BundledPlugin();
            PluginPackageLifecycle();
            if (args.Contains("--live-catalog")) LivePluginCatalog().GetAwaiter().GetResult();
            InstallerChecks().GetAwaiter().GetResult();
            AlignmentDiagnostics();
            SparseOverlapMatching();
            DistantRivalAfterNearbyVerification();
            CaptureLifecycle();
            ScrollTargetRecovery();
            ScrollUi();
            PreviewUi();
            PreviewForeground();
            TextCaret();
            PreferencesIntegration();
            Console.WriteLine($"PASS: {_checks} plugin and scrolling-capture checks."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void TextCaret()
    {
        foreach (var (scale, width, edge) in new[] { (1f, 2, 1), (1.5f, 3, 2), (2f, 4, 2) })
        {
            using var bitmap = new Bitmap(32, 28);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Black);
            var center = new Rectangle(16, 4, 1, 20);
            TextEditLayout.DrawCaret(graphics, center, Color.DodgerBlue, scale);
            var left = center.X - width / 2;
            var bands = Enumerable.Range(0, bitmap.Width).Select(x =>
                x >= left && x < left + width ? Color.DodgerBlue.ToArgb()
                : x >= left - edge && x < left + width + edge ? Color.White.ToArgb()
                : Color.Black.ToArgb()).ToArray();
            Check(Enumerable.Range(center.Top, center.Height).All(y =>
                bands.Select((color, x) => bitmap.GetPixel(x, y).ToArgb() == color).All(matches => matches)),
                $"text caret is a straight {width}-pixel stem at {scale:P0} scaling");
        }
    }

    private static void AlignmentDiagnostics()
    {
        static FrameAlignment Analyze(int width, int height, int offset, int brightness, int contentRows = int.MaxValue)
        {
            const int block = 16;
            var random = new Random(0);
            var anchors = new int[(height + offset) / block + 2, FrameSample.Columns];
            for (var y = 0; y < anchors.GetLength(0); y++)
            for (var x = 0; x < FrameSample.Columns; x++)
                anchors[y, x] = random.Next(65, 151);

            FrameSample Sample(int start, int adjustment)
            {
                var gray = new byte[height * FrameSample.Columns];
                for (var y = 0; y < height; y++)
                for (var x = 0; x < FrameSample.Columns; x++)
                {
                    var row = Math.Min(y + start, contentRows);
                    var fraction = (row % block) / (double)block;
                    gray[y * FrameSample.Columns + x] =
                        (byte)(anchors[row / block, x] * (1 - fraction) + anchors[row / block + 1, x] * fraction + adjustment);
                }
                return new FrameSample(width, height, gray);
            }

            return FrameMatcher.Analyze(Sample(0, 0), Sample(offset, brightness));
        }

        const int height = 720, offset = 300, width = 1503;
        var normal = Analyze(width, height, offset, 0);
        Check(normal.Match == new FrameMatch(MatchKind.Forward, offset), "known displacement accepted");
        foreach (var (w, h, shift) in new[] { (width, height, offset), (1963, 541, 150) })
        {
            var recovered = Analyze(w, h, shift, 3);
            Check(recovered.Match == new FrameMatch(MatchKind.Forward, shift) &&
                recovered.Search.Rival is { Offset: var rival } && Math.Abs(rival - shift) == 3 &&
                recovered.Search.Verification is 3,
                $"nearby candidate verified at the exact offset after a brightness change: {w}x{h}, {recovered}");
        }
        var insufficient = Analyze(width, height, offset, 3, offset + (height - offset) / 3);
        Check(insufficient.Match.Kind == MatchKind.Uncertain && insufficient.Search.Status == AlignmentStatus.NearbyUnverified,
            $"a single band cannot establish a precise offset: {insufficient}");
        Check(FrameMatcher.MinimumOverlap(height) == 144, "diagnostic overlap limit is the matcher's limit");

        using var outline = new CaptureOutline(new Rectangle(100, 100, width, height), Color.Blue);
        const int joinRow = height - offset + 3;
        Check(outline.Region is { } rim && !rim.IsVisible(100, joinRow) && rim.IsVisible(1, joinRow),
            "the capture outline leaves the entire selected content visible");
    }

    private static void SparseOverlapMatching()
    {
        const int width = 2067, height = 631, displacement = 300, rowHeight = 184;
        const int columns = FrameSample.Columns;
        static FrameSample LimitedDetail(int h)
        {
            var gray = new byte[h * FrameSample.Columns];
            foreach (var row in new[] { 12, 21 })
            foreach (var column in new[] { 8, 12 })
                gray[row * FrameSample.Columns + column] = 100;
            return new FrameSample(500, h, gray);
        }
        var small = LimitedDetail(55);
        var tall = LimitedDetail(400);
        Check(FrameMatcher.HasOverlapDetail(small, small, 0) && !FrameMatcher.HasOverlapDetail(tall, tall, 0),
            "same retained edges are evaluated relative to the overlap area");
        var blank = new FrameSample(500, height, new byte[height * columns]);
        Check(FrameMatcher.IsVisuallyUnchanged(blank, blank) && FrameMatcher.Analyze(blank, blank).Search.Status == AlignmentStatus.LowTexture,
            "stable blank frames provide no evidence of the end of scrolling");
        foreach (var repeating in new[] { false, true })
        {
            var document = new byte[(height + displacement) * columns];
            Array.Fill(document, (byte)245);
            var random = new Random(651);
            var pattern = new int[6, 12];
            for (var top = 0; top < height + displacement; top += rowHeight)
            {
                if (top == 0 || !repeating)
                    for (var line = 0; line < pattern.GetLength(0); line++)
                    for (var x = 0; x < pattern.GetLength(1); x++)
                        pattern[line, x] = random.Next(-3, 4);
                for (var line = 0; line < pattern.GetLength(0); line++)
                for (var x = 0; x < pattern.GetLength(1); x++)
                {
                    var y = top + 18 + line * 24 + pattern[line, x];
                    if (y + 2 >= height + displacement) continue;
                    document[y * columns + x + 7] = 135;
                    document[(y + 1) * columns + x + 7] = 135;
                }
            }

            var first = new FrameSample(width, height, document[..(height * columns)]);
            var next = new FrameSample(width, height, document[(displacement * columns)..].Select(value => (byte)(value + 3)).ToArray());
            var result = FrameMatcher.Analyze(first, next);
            if (repeating)
                Check(result.Match.Kind == MatchKind.Uncertain, $"identical repeated edges cannot break an offset tie: {result}");
            else
                Check(result.Match == new FrameMatch(MatchKind.Forward, displacement) &&
                    result.Search.Rival is { Offset: var rival } && Math.Abs(rival - displacement) > 100 && result.Search.EdgeAgreement > .70,
                    $"sparse text edges distinguish the real offset from a similarly laid out row: {result}");
        }
    }

    private static void DistantRivalAfterNearbyVerification()
    {
        const int width = 1575, height = 632, displacement = 300, itemHeight = 183;
        const int columns = FrameSample.Columns;
        var document = new byte[(height + displacement) * columns];
        for (var y = 0; y < height + displacement; y++)
        for (var x = 0; x < columns; x++)
        {
            var value = 235;
            if (x % 3 == 0 && x >= 5 && x < 43)
                value -= y * 12 / itemHeight + (int)(4 * Math.Sin((y + 2 * x) / 9.0));
            if (y % itemHeight == 22 && x >= 5 && x < 44 && x % 3 != 0)
                value -= 25;
            document[y * columns + x] = (byte)value;
        }

        // Each item has similarly placed text, with small differences in its
        // exact edges. The smooth columns give the coarse search a nearby rival.
        var random = new Random(2);
        for (var top = 0; top < height + displacement; top += itemHeight)
        for (var line = 0; line < 4; line++)
        for (var x = 6; x < 38; x += 2)
        {
            if (x % 3 == 0) continue;
            var y = top + 35 + line * 23 + random.Next(-6, 7);
            if (y + 2 >= height + displacement) continue;
            document[y * columns + x] = 130;
            document[(y + 1) * columns + x] = 130;
        }

        var first = new FrameSample(width, height, document[..(height * columns)]);
        var next = new FrameSample(width, height, document[(displacement * columns)..].Select(value => (byte)(value + 2)).ToArray());
        var result = FrameMatcher.Analyze(first, next);
        Check(result.Match == new FrameMatch(MatchKind.Forward, displacement) &&
            result.Search.Rival is { Offset: 231 } && result.Search.Verification is 2 && result.Search.EdgeAgreement is 1,
            $"a distant rival found after nearby verification gets checked against the sparse content edges: {result}");
    }

    private static void IconAssets()
    {
        var directory = Path.Combine(Path.GetDirectoryName(ScrollPluginRoot())!, "icon-checks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Check(PluginIcon.Load(directory, "") is null, "legacy plugins need no icon asset");
            using (var source = new Bitmap(48, 24))
            {
                using var g = Graphics.FromImage(source); g.Clear(Color.MediumPurple);
                source.Save(Path.Combine(directory, "custom.png"), ImageFormat.Png);
            }
            using var icon = PluginIcon.Load(directory, "custom.png")!;
            using (var exclusive = new FileStream(Path.Combine(directory, "custom.png"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Check(icon.GetPixel(10, 10).ToArgb() == Color.MediumPurple.ToArgb(), "plugin PNG decoded independently of file lifetime");
            using var rendered = new Bitmap(40, 40);
            using (var graphics = Graphics.FromImage(rendered)) PluginIcon.Draw(graphics, icon, new Rectangle(0, 0, 40, 40), Color.Black);
            Check(rendered.GetPixel(20, 20).ToArgb() == Color.MediumPurple.ToArgb() && rendered.GetPixel(20, 2).A == 0, "arbitrary plugin artwork retains color and aspect ratio");
            foreach (var path in new[] { "../custom.png", "assets/../../custom.png", "C:/custom.png", "\\\\server\\custom.png", "custom.svg" })
            {
                try { PluginIcon.Load(directory, path); throw new Exception("Invalid icon path accepted: " + path); }
                catch (InvalidDataException) { _checks++; }
            }
            File.WriteAllText(Path.Combine(directory, "broken.png"), "not an image");
            try { using var broken = PluginIcon.Load(directory, "broken.png"); throw new Exception("Invalid PNG accepted."); }
            catch (ArgumentException) { _checks++; }
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void Algorithms()
    {
        using var source = new Bitmap(160, 700, PixelFormat.Format32bppArgb);
        var random = new Random(1234);
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++) source.SetPixel(x, y, Color.FromArgb(random.Next(256), random.Next(256), random.Next(256)));
        using var first = source.Clone(new Rectangle(0, 0, 160, 120), PixelFormat.Format32bppArgb);
        using var second = source.Clone(new Rectangle(0, 37, 160, 120), PixelFormat.Format32bppArgb);
        using var third = source.Clone(new Rectangle(0, 81, 160, 120), PixelFormat.Format32bppArgb);
        using var far = source.Clone(new Rectangle(0, 350, 160, 120), PixelFormat.Format32bppArgb);
        var a = new FrameSample(first); var b = new FrameSample(second); var c = new FrameSample(third);
        Check(FrameMatcher.Match(a, b) == new FrameMatch(MatchKind.Forward, 37), "known forward offset");
        Check(FrameMatcher.Match(b, c) == new FrameMatch(MatchKind.Forward, 44), "variable forward offset");
        Check(FrameMatcher.Match(b, a).Kind == MatchKind.Reverse, "reverse scrolling rejected");
        Check(FrameMatcher.Match(a, a).Kind == MatchKind.Unchanged, "stationary frame not appended");
        Check(FrameMatcher.Match(a, new FrameSample(far)).Kind == MatchKind.Uncertain, "missing overlap rejected");
        using (var textSource = new Bitmap(900, 3000))
        {
            using (var graphics = Graphics.FromImage(textSource))
            {
                graphics.Clear(Color.White);
                using var font = new Font("Segoe UI", 16);
                for (var row = 0; row < 60; row++)
                    graphics.DrawString($"Row {row:000} — article content, variable value {row * row + 7919}", font, Brushes.Black, 15, row * 50 + 10);
            }
            using var textFirst = textSource.Clone(new Rectangle(0, 0, 900, 700), PixelFormat.Format32bppArgb);
            foreach (var offset in new[] { 37, 80, 125, 150, 237, 350 })
            {
                using var textNext = textSource.Clone(new Rectangle(0, offset, 900, 700), PixelFormat.Format32bppArgb);
                Check(FrameMatcher.Match(new(textFirst), new(textNext)) == new FrameMatch(MatchKind.Forward, offset), "text-only article offset " + offset);
            }
            foreach (var height in new[] { 181, 317, 533, 700, 880, 947, 1137 })
            foreach (var offset in new[] { height / 9, height / 3, height / 2, height * 2 / 3 })
            {
                using var baseline = textSource.Clone(new Rectangle(0, 13, 900, height), PixelFormat.Format32bppArgb);
                using var next = textSource.Clone(new Rectangle(0, 13 + offset, 900, height), PixelFormat.Format32bppArgb);
                var match = FrameMatcher.Match(new(baseline), new(next));
                Check(match == new FrameMatch(MatchKind.Forward, offset), $"text viewport height={height}, offset={offset}, actual={match}");
            }
        }
        using var blank = new Bitmap(160, 120); using (var g = Graphics.FromImage(blank)) g.Clear(Color.White);
        using var otherBlank = new Bitmap(160, 120); using (var g = Graphics.FromImage(otherBlank)) g.Clear(Color.LightGray);
        Check(FrameMatcher.Match(new(blank), new(otherBlank)).Kind == MatchKind.Uncertain, "untextured content rejected");
        using var stripes = new Bitmap(160, 240);
        for (var y = 0; y < stripes.Height; y++)
        for (var x = 0; x < stripes.Width; x++) stripes.SetPixel(x, y, y % 12 < 6 ? Color.White : Color.Black);
        using var stripeA = stripes.Clone(new Rectangle(0, 0, 160, 120), PixelFormat.Format32bppArgb);
        using var stripeB = stripes.Clone(new Rectangle(0, 3, 160, 120), PixelFormat.Format32bppArgb);
        Check(FrameMatcher.Match(new(stripeA), new(stripeB)).Kind == MatchKind.Uncertain, "ambiguous repeated rows rejected");
        using var resized = new Bitmap(170, 120);
        Check(FrameMatcher.Match(a, new(resized)).Kind == MatchKind.Uncertain, "size change rejected");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { FrameMatcher.Match(a, b, cancellation.Token); throw new Exception("Cancellation was ignored."); } catch (OperationCanceledException) { _checks++; }
        string cache;
        using (var store = new ScrollImageStore(first))
        {
            cache = store.DirectoryPath;
            Check(store.Append(second, b, 37) && store.Append(third, c, 44), "append strips");
            Check(store.Height == 201 && store.Count == 3, "height and strip count");
            using (var result = store.Build()) AssertPixels(result, source, 201);
            Check(store.Undo() && store.Height == 157, "undo last strip");
            Check(FrameMatcher.Match(store.LastSample, c).Offset == 44, "undo restores alignment baseline");
            Check(store.Append(third, c, 44), "resume after undo");
            Check(!store.CanAppend(int.MaxValue) && !store.Append(third, c, 120), "invalid offsets and oversized output rejected");
            Check(store.LastSample.Gray.Length == c.Gray.Length, "only current signature retained in memory");
            Check(Directory.GetFiles(cache, "*.sample").Length == store.Count, "undo signatures cached on disk");
        }
        Check(!Directory.Exists(cache), "temporary cache cleaned");
        using (var store = new ScrollImageStore(first))
        {
            for (var i = 1; i < ScrollImageStore.MaxSegments; i++) Check(store.Append(first, a, 1), "small strips within segment budget");
            Check(!store.Append(first, a, 1), "segment budget enforced");
        }
        var area = new Rectangle(-1600, 0, 1600, 1000);
        var region = new Rectangle(-1500, 200, 1200, 650);
        var toolbar = ScrollCaptureForm.ToolbarBounds(region, new Size(850, 180), new[] { area });
        Check(toolbar is Rectangle rect && area.Contains(rect) && !rect.IntersectsWith(region), "toolbar avoids selection on negative-coordinate monitor");
        Check(ScrollCaptureForm.ToolbarBounds(area, new Size(850, 180), new[] { area }) is null, "full-screen selection without toolbar space rejected");
    }

    private static void FixedEdgeMatching()
    {
        const int height = 1576;
        var random = new Random(116);
        var document = new byte[(height * 3) * FrameSample.Columns]; random.NextBytes(document);
        FrameSample Sample(int offset, int leftEdge, int rightEdge, bool corruptInterior = false)
        {
            var gray = new byte[height * FrameSample.Columns];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < FrameSample.Columns; x++)
            {
                gray[y * FrameSample.Columns + x] = x < leftEdge || x >= FrameSample.Columns - rightEdge
                    ? (byte)((y * 37 + x * 11) % 256)
                    : corruptInterior && x >= 10 && x <= 36 ? (byte)((y * 43 + x * 13) % 256)
                    : document[(y + offset) * FrameSample.Columns + x];
            }
            return new FrameSample(338, height, gray);
        }
        foreach (var (left, right) in new[] { (0, 6), (3, 3), (6, 0), (0, 0) })
        foreach (var offset in new[] { 150, 450, 750 })
        {
            var a = Sample(0, left, right); var b = Sample(offset, left, right);
            Check(FrameMatcher.Match(a, b) == new FrameMatch(MatchKind.Forward, offset), $"narrow long capture aligns with fixed edges: {left}/{right}, offset={offset}");
            Check(FrameMatcher.Match(b, a).Kind == MatchKind.Reverse, "fixed edge matching still rejects reverse scrolling");
            Check(FrameMatcher.Match(a, a).Kind == MatchKind.Unchanged, "stationary matching keeps full frame comparison");
        }
        Check(FrameMatcher.Match(Sample(0, 0, 6), Sample(2400, 0, 6)).Kind == MatchKind.Uncertain, "fixed scrollbar does not create a match when content has no overlap");
        Check(FrameMatcher.Match(Sample(0, 0, 6), Sample(450, 0, 6, true)).Kind == MatchKind.Uncertain, "changed interior cannot be hidden by edge exclusion");
        var blankA = new byte[height * FrameSample.Columns]; var blankB = new byte[blankA.Length];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < FrameSample.Columns; x++)
        {
            blankA[y * FrameSample.Columns + x] = x < 42 ? (byte)255 : (byte)(y % 256);
            blankB[y * FrameSample.Columns + x] = x < 42 ? (byte)255 : (byte)((y + 31) % 256);
        }
        Check(FrameMatcher.Match(new(338, height, blankA), new(338, height, blankB)).Kind == MatchKind.Uncertain, "moving scrollbar alone cannot prove article scrolling");
        var repeatedA = new byte[height * FrameSample.Columns]; var repeatedB = new byte[repeatedA.Length];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < FrameSample.Columns; x++)
        {
            repeatedA[y * FrameSample.Columns + x] = (byte)((y % 42 < 8 ? 20 : 240) + x % 5);
            repeatedB[y * FrameSample.Columns + x] = (byte)(((y + 150) % 42 < 8 ? 20 : 240) + x % 5);
        }
        Check(FrameMatcher.Match(new(338, height, repeatedA), new(338, height, repeatedB)).Kind == MatchKind.Uncertain, "repeated rows remain ambiguous after fixed-edge adjustment");
        foreach (var offset in new[] { 150, 450, 750 })
        {
            var baseline = Sample(0, 0, 6);
            var next = Sample(offset, 0, 6);
            for (var y = 0; y < height; y++)
            for (var x = 0; x < 42; x++)
                next.Gray[y * FrameSample.Columns + x] = (byte)Math.Min(255, next.Gray[y * FrameSample.Columns + x] + 6);
            Check(FrameMatcher.Match(baseline, next) == new FrameMatch(MatchKind.Forward, offset), "unique repaint offset verified despite moderate residual: " + offset);
        }
        var noisyMissingOverlap = Sample(2400, 0, 6);
        for (var i = 0; i < noisyMissingOverlap.Gray.Length; i++)
            noisyMissingOverlap.Gray[i] = (byte)Math.Min(255, noisyMissingOverlap.Gray[i] + 6);
        Check(FrameMatcher.Match(Sample(0, 0, 6), noisyMissingOverlap).Kind == MatchKind.Uncertain, "moderate repaint tolerance does not accept non-overlap");
        for (var i = 0; i < repeatedB.Length; i++) repeatedB[i] = (byte)Math.Min(255, repeatedB[i] + 6);
        Check(FrameMatcher.Match(new(338, height, repeatedA), new(338, height, repeatedB)).Kind == MatchKind.Uncertain, "moderate repaint tolerance does not accept ambiguous repetition");
    }

    private static void CaptureLifecycle()
    {
        var pace = new ScrollStepSize(800);
        static void ObserveStep(ScrollStepSize size, int offset)
        {
            size.Issued(size.Notches);
            size.Observe(offset);
        }
        Check(pace.Notches == 1, "automatic scrolling calibrates with one notch");
        pace.Issued(1);
        Check(!pace.CanProbe, "an unmeasured wheel distance cannot budget extra scrolling");
        pace.Observe(120);
        Check(pace.Notches == 3, "calibrated scrolling advances close to half the viewport");
        ObserveStep(pace, 360);
        Check(pace.Notches == 3, "stable scroll pace retains overlap");
        ObserveStep(pace, 600);
        Check(pace.Notches == 2, "larger-than-expected scrolling reduces next step");
        pace.Reset(); ObserveStep(pace, 10); ObserveStep(pace, 30); ObserveStep(pace, 50);
        Check(pace.Notches == 6, "small system scroll distance has bounded acceleration");
        pace.Reset(); Check(pace.Notches == 1, "resume recalibrates scroll distance");
        var shortViewport = new ScrollStepSize(180); ObserveStep(shortViewport, 120);
        Check(shortViewport.Notches == 1, "short viewport keeps small scroll steps");
        shortViewport.Issued(1);
        Check(!shortViewport.CanProbe, "short viewport has no room for another notch");
        var target = new FakeScrollTarget();
        using (var controller = new AutoScrollController(target))
        {
            Check(controller.Start() && controller.Step(3) && target.Steps == 1 && target.LastNotches == 3, "automatic scroll sends calibrated distance without pointer state");
            target.Available = false;
            Check(!controller.Step() && target.Steps == 1, "covered target receives no wheel message");
            target.Available = true; target.AcceptsWheel = false;
            Check(!controller.Step(), "scroll dispatch failure reported");
            target.AcceptsWheel = true;
            controller.Dispose();
            Check(!controller.Step() && target.Steps == 2, "disposed controller cannot send more input");
            Check(controller.Start() && controller.Step(), "resume reuses original target");
        }
        for (var run = 0; run < 2; run++)
        {
            using var session = new ScrollCaptureForm(new FakeHost(), new Rectangle(100, 100, 64, 100), CancellationToken.None);
            var completion = ScrollCapturePlugin.ObserveCompletion(session);
            var image = new PluginImage(new Bitmap(64, 201));
            typeof(ScrollCaptureForm).GetField("_result", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(session, image);
            // Exercise the modeless FormClosed -> Dispose ordering without opening a desktop window.
            typeof(Form).GetMethod("OnFormClosed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(session, new object[] { new FormClosedEventArgs(CloseReason.UserClosing) });
            session.Dispose();
            using var result = completion.GetAwaiter().GetResult();
            Check(ReferenceEquals(result, image) && result.Image.Height == 201, "completed image survives modeless session disposal, run " + run);
            Check(session.TakeResult() is null, "completed image ownership transferred once");
        }
    }

    private static void ScrollTargetRecovery()
    {
        var api = new FakeScrollWindows();
        var region = new Rectangle(100, 100, 800, 600);
        var target = new WindowScrollTarget(region, api);
        Check(api.Point == new Point(500, 400), "scroll target uses selection content center, away from scrollbar");
        using var controller = new AutoScrollController(target);
        Check(controller.Start() && controller.Step(3) && api.SentTo == 11 && api.Notches == 3, "scroll dispatch targets selected original content");
        api.Child = 12;
        Check(controller.Step() && api.SentTo == 12, "recreated content child is rebound within original root");
        api.HitRoot = 2;
        var count = api.Messages;
        Check(!controller.Step() && controller.CanRetry && api.Messages == count, "temporary covering window receives no scroll input");
        api.HitRoot = 1;
        Check(controller.Step() && controller.Failure.Length == 0, "uncovering original target recovers and clears stale failure");
        api.Bounds = api.Bounds with { X = 1 };
        Check(!controller.CheckTarget() && !controller.CanRetry && controller.Failure.Contains("moved"), "real content movement pauses immediately");
        api.Bounds = new Rectangle(0, 0, 1200, 900); api.IsMinimized = true;
        Check(!controller.CheckTarget() && !controller.CanRetry && controller.Failure.Contains("minimized"), "minimized target is not retried");
        api.IsMinimized = false; api.IsVisible = false;
        Check(!controller.CheckTarget() && !controller.CanRetry, "hidden target is unavailable");
        api.IsVisible = true; api.IsAlive = false;
        Check(!controller.CheckTarget() && !controller.CanRetry, "closed target is unavailable");
        api.IsAlive = true; api.Error = 1460;
        Check(!controller.Step() && controller.CanRetry && controller.Failure.Contains("1460"), "timeout has bounded recovery and precise diagnostic");
        api.Error = 5;
        Check(!controller.Step() && !controller.CanRetry && controller.Failure.Contains("win32=5"), "access denied is not endlessly retried");
        var wait = new ScrollTargetWait();
        Check(wait.ShouldWait(true, 100) && wait.ShouldWait(true, 2099) && !wait.ShouldWait(true, 2100), "temporary occlusion has a finite two-second grace");
        wait.Reset(); Check(wait.ShouldWait(true, 10000), "recovery starts a fresh grace period for later transient failures");
        Check(!wait.ShouldWait(false, 10001), "permanent failures never receive retry grace");

        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        void Field(ScrollCaptureForm form, string name, object value) => typeof(ScrollCaptureForm).GetField(name, flags)!.SetValue(form, value);
        T Value<T>(ScrollCaptureForm form, string name) => (T)typeof(ScrollCaptureForm).GetField(name, flags)!.GetValue(form)!;
        void Tick(ScrollCaptureForm form)
        {
            var task = (Task)typeof(ScrollCaptureForm).GetMethod("SampleAsync", flags)!.Invoke(form, null)!;
            var deadline = Environment.TickCount64 + 10000;
            while (!task.IsCompleted && Environment.TickCount64 < deadline) { Application.DoEvents(); Thread.Sleep(1); }
            Check(task.IsCompleted, "capture sample completes within bounded test timeout"); task.GetAwaiter().GetResult();
        }
        using var source = new Bitmap(160, 500);
        var random = new Random(71);
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++) source.SetPixel(x, y, Color.FromArgb(random.Next(256), random.Next(256), random.Next(256)));
        using (var host = new RecoveryHost(source))
        using (var session = new ScrollCaptureForm(host, new Rectangle(0, 0, 160, 120), CancellationToken.None, host.Target))
        {
            using var first = host.CaptureRegion(new Rectangle(0, 0, 160, 120));
            Field(session, "_store", new ScrollImageStore(first));
            var scrolling = new AutoScrollController(host.Target); scrolling.Start(); Field(session, "_scroller", scrolling);
            Field(session, "_awaitingScroll", true); Field(session, "_scrollIssuedAt", Environment.TickCount64 - 1000);
            host.Target.Available = false; host.Target.Retryable = true;
            var captures = host.Captures;
            Tick(session);
            Check(!Value<bool>(session, "_paused") && Value<bool>(session, "_waitingForTarget") && host.Captures == captures && host.Target.Steps == 0,
                "transient covering neither samples covered pixels nor permanently pauses");
            host.Target.Available = true; Tick(session);
            Check(!Value<bool>(session, "_waitingForTarget") && !Value<bool>(session, "_paused") && host.Captures == captures + 1 && host.Target.Steps == 0,
                "recovery samples outstanding scroll without sending a duplicate wheel");
            host.Target.Available = false;
            Value<ScrollTargetWait>(session, "_targetWait").ShouldWait(true, Environment.TickCount64 - 3000);
            Tick(session);
            Check(Value<bool>(session, "_paused"), "persistent covering pauses after grace expires");
            host.Target.Available = true; Field(session, "_unchangedAttempts", 2);
            var pause = Descendants(session).OfType<SnipLite.ScrollCapture.ScrollButton>().Single(b => b.Glyph == "resume");
            typeof(Control).GetMethod("OnClick", flags)!.Invoke(pause, new object[] { EventArgs.Empty });
            Check(!Value<bool>(session, "_paused") && Value<int>(session, "_unchangedAttempts") == 0, "resume clears stale end-of-content detection");
            Check(Value<bool>(session, "_awaitingScroll"), "resume confirms current position before another wheel");
            host.Target.Steps = 0;
            Field(session, "_scrollIssuedAt", Environment.TickCount64 - 1000); Tick(session); Tick(session);
            Check(host.Target.Steps == 0, "resuming at baseline does not scroll before confirming alignment");
        }
        using (var host = new RecoveryHost(source))
        using (var session = new ScrollCaptureForm(host, new Rectangle(0, 0, 160, 120), CancellationToken.None, host.Target))
        {
            using var first = host.CaptureRegion(new Rectangle(0, 0, 160, 120));
            Field(session, "_store", new ScrollImageStore(first));
            var scrolling = new AutoScrollController(host.Target); scrolling.Start(); Field(session, "_scroller", scrolling);
            host.Target.Retryable = true; host.Target.OnStep = () => { host.Offset = 30; return false; };
            Tick(session);
            Check(Value<bool>(session, "_awaitingScroll") && !Value<bool>(session, "_paused"), "timed-out wheel may already have been delivered");
            Field(session, "_scrollIssuedAt", Environment.TickCount64 - 1000);
            Tick(session); Tick(session); Tick(session);
            Check(host.Target.Steps == 1 && Value<ScrollImageStore>(session, "_store").Height == 150,
                "delivered timeout is stitched once without retrying scroll or skipping content");
            var finish = Descendants(session).OfType<SnipLite.ScrollCapture.ScrollButton>().Single(b => b.Glyph == "finish");
            var enabledChanges = 0; finish.EnabledChanged += (_, _) => enabledChanges++;
            Field(session, "_scrollIssuedAt", Environment.TickCount64 - 1000);
            Tick(session); Tick(session);
            Check(enabledChanges == 0, "sampling does not flash finish button enabled state every timer tick");
        }
        foreach (var detailReturns in new[] { true, false })
        {
            var tallRegion = new Rectangle(0, 0, 160, 250);
            using var host = new RecoveryHost(source);
            using var session = new ScrollCaptureForm(host, tallRegion, CancellationToken.None, host.Target);
            using var first = host.CaptureRegion(tallRegion);
            var store = new ScrollImageStore(first);
            Field(session, "_store", store);
            using var scrolling = new AutoScrollController(host.Target);
            scrolling.Start();
            Field(session, "_scroller", scrolling);
            host.Target.OnStep = () =>
            {
                host.Offset += host.Target.LastNotches * 50;
                host.Blank = host.Target.Steps == 2 || host.Target.Steps == 3 && !detailReturns;
                return true;
            };

            void Samples(int count)
            {
                Field(session, "_scrollIssuedAt", Environment.TickCount64 - 1000);
                for (var i = 0; i < count; i++) Tick(session);
            }

            Tick(session); Samples(3);
            Check(store.Height == 300, "first wheel establishes a 50 px per notch calibration");
            Tick(session); Samples(5);
            Check(host.Target.Steps == 3 && host.Target.LastNotches == 1 && store.Height == 300,
                "blank frame triggers one notch while the accepted image is retained");
            Samples(detailReturns ? 3 : 5);
            if (detailReturns)
            {
                Check(store.Height == 450 && !Value<bool>(session, "_paused"), "new detail confirms the whole 150 px movement");
                using var result = store.Build();
                AssertPixels(result, source, 450);
            }
            else
            {
                Check(store.Height == 300 && Value<bool>(session, "_paused") && host.Target.Steps == 3,
                    "persistent blank stops at the overlap limit without appending or issuing another notch");
            }
        }

        using (var host = new RecoveryHost(source))
        using (var session = new ScrollCaptureForm(host, new Rectangle(0, 0, 160, 120), CancellationToken.None, host.Target))
        {
            using var first = host.CaptureRegion(new Rectangle(0, 0, 160, 120));
            var store = new ScrollImageStore(first);
            Field(session, "_store", store);
            using var scrolling = new AutoScrollController(host.Target);
            scrolling.Start();
            Field(session, "_scroller", scrolling);
            Tick(session);
            Tick(session); Tick(session); Tick(session);
            Check(host.Target.Steps == 1 && Value<bool>(session, "_awaitingScroll") && Value<int>(session, "_unchangedAttempts") == 0,
                "the old frame remaining visible does not trigger another wheel or count toward end of page");
            host.Offset = 9;
            Tick(session); Tick(session);
            host.Offset = 24;
            Tick(session); Tick(session);
            Check(store.Count == 1, "movement restarts the stability check before any append");
            Field(session, "_scrollIssuedAt", Environment.TickCount64);
            Tick(session);
            Check(store.Count == 2 && store.Height == 144 &&
                host.Messages.Any(message => message.Contains("wheel to stable frame=") &&
                    message.Contains("capture=") && message.Contains("match=") && message.Contains("write=")),
                "a stable changed frame stitches immediately and reports stage timings");
            using var result = store.Build();
            AssertPixels(result, source, 144);
        }

        var footerRegion = new Rectangle(0, 0, 160, 180);
        using (var host = new RecoveryHost(source) { FooterHeight = 36 })
        using (var session = new ScrollCaptureForm(host, footerRegion, CancellationToken.None, host.Target))
        {
            using var first = host.CaptureRegion(footerRegion);
            var store = new ScrollImageStore(first);
            Field(session, "_store", store);
            using var scrolling = new AutoScrollController(host.Target);
            scrolling.Start();
            Field(session, "_scroller", scrolling);
            host.Target.OnStep = () => { host.Offset += host.Target.LastNotches * 45; return true; };

            void CaptureStep()
            {
                Tick(session);
                Tick(session); Tick(session); Tick(session);
            }

            CaptureStep();
            Check(store.FixedEdges.Bottom == 36 && store.Height == 225, $"the first accepted movement confirms and relocates the fixed footer: {store.FixedEdges}, {store.Height}");
            CaptureStep();
            Check(host.Target.LastNotches == 1 && store.Count == 3 && store.Height == 270,
                $"the smaller moving region controls subsequent scroll steps: {store.Count} strips, {store.Height} px");
            using (var result = store.Build())
            {
                using var body = result.Clone(new Rectangle(0, 0, 160, 234), PixelFormat.Format32bppArgb);
                AssertPixels(body, source, 234);
                Check(result.GetPixel(80, 245) == first.GetPixel(80, 155), "the original stationary footer is visible at the very end");
            }
            Check(store.Undo() && store.FixedEdges.Bottom == 36 && store.Height == 225, "undoing an appended strip keeps the confirmed footer");
            Check(store.Undo() && store.FixedEdges == Padding.Empty && store.Height == 180, "undoing to the first frame restores the full selection");
            using var original = store.Build();
            for (var y = 0; y < first.Height; y++)
                Check(first.GetPixel(80, y) == original.GetPixel(80, y), "original screenshot pixels are restored after undo");
        }
        FixedEdgesComposition(Tick);
    }

    private sealed class FakeScrollWindows : IScrollWindowApi
    {
        internal nint Child = 11, HitRoot = 1, SentTo;
        internal bool IsAlive = true, IsVisible = true, IsMinimized;
        internal Rectangle Bounds = new(0, 0, 1200, 900);
        internal Point Point;
        internal int Notches, Messages, Error;
        public nint At(Point point) { Point = point; return Child; }
        public nint Root(nint window) => HitRoot;
        public bool Exists(nint window) => IsAlive;
        public bool Visible(nint window) => IsVisible;
        public bool Minimized(nint window) => IsMinimized;
        public Rectangle? ClientBounds(nint window) => Bounds;
        public bool Wheel(nint control, Point point, int notches, out int error)
        { SentTo = control; Notches = notches; Messages++; error = Error; return error == 0; }
    }

    private sealed class RecoveryHost(Bitmap source) : IPluginHost, IDisposable
    {
        internal readonly FakeScrollTarget Target = new();
        internal int Offset, Captures;
        internal bool Blank;
        internal int FooterHeight;
        internal Func<int, Bitmap>? CaptureFrame;
        internal List<string> Messages { get; } = [];
        public string Language => "en-US";
        public Bitmap CaptureRegion(Rectangle region)
        {
            Captures++;
            if (CaptureFrame is { } captureFrame) return captureFrame(Offset);
            var frame = Blank ? new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb)
                : source.Clone(new Rectangle(0, Offset, region.Width, region.Height), PixelFormat.Format32bppArgb);
            if (!Blank && FooterHeight == 0) return frame;
            using var graphics = Graphics.FromImage(frame);
            if (Blank) graphics.Clear(Color.White);
            if (FooterHeight > 0)
            {
                var top = frame.Height - FooterHeight;
                graphics.FillRectangle(Brushes.LightGray, 0, top, frame.Width, FooterHeight);
                graphics.FillRectangle(Brushes.White, 16, top + 5, 120, FooterHeight - 10);
                graphics.FillRectangle(Brushes.DimGray, 104, top + 10, 20, FooterHeight - 20);
            }
            return frame;
        }
        public Task<Rectangle?> SelectRegionAsync(CancellationToken token) => Task.FromResult<Rectangle?>(null);
        public Task<PluginImage?> SelectImageAsync(CancellationToken token) => Task.FromResult<PluginImage?>(null);
        public Task PreviewImageAsync(PluginImage image, CancellationToken token) => Task.CompletedTask;
        public void StyleWindow(Form form) { }
        public void Log(string message, Exception? exception = null) => Messages.Add(message);
        public void Dispose() { }
    }

    private static void ScrollUi()
    {
        var artifacts = Path.Combine(Path.GetDirectoryName(ScrollPluginRoot())!, "scroll-ui-checks");
        Directory.CreateDirectory(artifacts);
        var originalLanguage = SnipLite.Localization.LanguageService.CurrentLanguage;
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        try
        {
            foreach (var language in new[] { "zh-CN", "zh-TW", "en-US" })
            foreach (var dark in new[] { false, true })
            {
                SnipLite.Localization.LanguageService.Configure(language);
                using var theme = AppTheme.Override(dark);
                var host = new PluginHostServices(new AppSettings(), (_, _) => { });
                using var form = new ScrollUiPreview(host)
                { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };
                form.Show(); Application.DoEvents(); form.PerformLayout();
                var card = Descendants(form).OfType<ScrollCard>().Single();
                var buttons = Descendants(form).OfType<SnipLite.ScrollCapture.ScrollButton>().ToArray();
                Check(form.ClientRectangle.Contains(card.Bounds) && card.Width > 0, "plugin card fits themed window");
                foreach (Control control in card.Controls)
                {
                    Check(card.ClientRectangle.Contains(control.Bounds), $"plugin control fits card: {language} / {control.GetType().Name}: control={control.Bounds}, card={card.ClientRectangle}, client={form.ClientSize}, dpi={form.DeviceDpi}");
                    Check(!card.Controls.Cast<Control>().Any(other => other != control && other.Bounds.IntersectsWith(control.Bounds)), "plugin controls do not overlap");
                }
                foreach (var button in buttons)
                {
                    var label = TextRenderer.MeasureText(button.Text, button.Font, Size.Empty, TextFormatFlags.NoPadding).Width;
                    Check(button.Width >= label + (int)(34 * form.DeviceDpi / 96f), "plugin action text and icon fit: " + button.Text);
                }
                Check(buttons.Single(b => b.Glyph == "finish").Primary, "finish action uses primary treatment");
                Check(form.BackColor == AppTheme.Current.Window && ScrollPalette.For(card).Dark == dark, "plugin inherits host theme through public StyleWindow API");
                using var first = new Bitmap(500, 400);
                var store = new ScrollImageStore(first);
                typeof(ScrollCaptureForm).GetField("_store", flags)!.SetValue(form, store);
                typeof(ScrollCaptureForm).GetMethod("UpdateButtons", flags)!.Invoke(form, null);
                typeof(ScrollCaptureForm).GetMethod("SetStatus", flags)!.Invoke(form, new object[] { language.StartsWith("zh") ? "正在自动滚动，鼠标可自由移动，可随时暂停或完成。" : "Scrolling automatically. The pointer can move freely; pause or finish at any time." });
                foreach (var paused in new[] { false, true })
                {
                    if (paused) typeof(Control).GetMethod("OnClick", flags)!.Invoke(buttons.Single(b => b.Glyph == "pause"), new object[] { EventArgs.Empty });
                    using var snapshot = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(snapshot, new Rectangle(Point.Empty, form.Size));
                    snapshot.Save(Path.Combine(artifacts, $"scroll-{language}-{(dark ? "dark" : "light")}-{(paused ? "paused" : "capturing")}.png"));
                }
                var reason = (string)typeof(ScrollCaptureForm).GetMethod("AlignmentFailure", flags)!.Invoke(form, new object[] { AlignmentStatus.Ambiguous })!;
                typeof(ScrollCaptureForm).GetMethod("SetStatus", flags)!.Invoke(form, new object[] { reason });
                var status = Descendants(form).OfType<ScrollStatus>().Single();
                var textWidth = status.Width - (int)(20 * form.DeviceDpi / 96f) - (int)(10 * form.DeviceDpi / 96f);
                Check(TextRenderer.MeasureText(status.Text, status.Font).Width <= textWidth, $"pause message fits {language}");
                Check(!status.Text.Any(char.IsDigit) && status.AccessibleName == status.Text, "capture status only exposes a plain-language pause message");
                using var diagnostic = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(diagnostic, new Rectangle(Point.Empty, form.Size));
                diagnostic.Save(Path.Combine(artifacts, $"scroll-{language}-{(dark ? "dark" : "light")}-alignment.png"));
                Check(!typeof(ScrollCaptureForm).Assembly.GetReferencedAssemblies().Any(a => a.Name == "SnipLite"), "plugin UI has no host implementation dependency");
            }
        }
        finally { SnipLite.Localization.LanguageService.Configure(originalLanguage); }
    }

    private static void FixedEdgesComposition(Action<ScrollCaptureForm> tick)
    {
        const int width = 252, height = 210, offset = 38;
        var edges = new Padding(29, 23, 31, 28);
        var frameSize = new Size(width, height);
        var body = StationaryEdges.Body(frameSize, edges);
        using var content = new Bitmap(body.Width, body.Height + 2 * offset, PixelFormat.Format32bppArgb);
        var random = new Random(121);
        Color Ink() => Color.FromArgb(random.Next(256), random.Next(256), random.Next(256));
        for (var y = 0; y < content.Height; y++)
        for (var x = 0; x < content.Width; x++)
            content.SetPixel(x, y, Ink());
        using var first = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            first.SetPixel(x, y, body.Contains(x, y) ? content.GetPixel(x - body.Left, y - body.Top) : Ink());

        Bitmap Frame(int start)
        {
            var frame = new Bitmap(first);
            using var graphics = Graphics.FromImage(frame);
            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            graphics.SetClip(body);
            graphics.DrawImageUnscaled(content, body.X, body.Y - start);
            return frame;
        }

        using var second = Frame(offset);
        using var third = Frame(2 * offset);
        third.SetPixel(13, height - 8, Color.Magenta);
        var confirmed = StationaryEdges.Find(first, second, offset);
        Check(confirmed == edges, $"all four stationary edges use precise screen coordinates: {confirmed}");

        using var host = new RecoveryHost(content)
        {
            CaptureFrame = start => new Bitmap(start == 0 ? first : start == offset ? second : third)
        };
        var region = new Rectangle(Point.Empty, frameSize);
        using var session = new ScrollCaptureForm(host, region, CancellationToken.None, host.Target);
        var store = new ScrollImageStore(first);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(ScrollCaptureForm).GetField("_store", flags)!.SetValue(session, store);
        using var scrolling = new AutoScrollController(host.Target);
        scrolling.Start();
        typeof(ScrollCaptureForm).GetField("_scroller", flags)!.SetValue(session, scrolling);
        host.Target.OnStep = () => { host.Offset += offset; return true; };
        for (var i = 0; i < 2; i++)
        {
            tick(session);
            tick(session);
            tick(session);
            tick(session);
        }
        Check(host.Target.Steps == 2 && store.Count == 3 && store.FixedEdges == edges,
            $"automatic capture confirms and uses all four edges: {store.Count} frames, {store.FixedEdges}");
        using var result = store.Build();
        Check(result.Size == new Size(width, height + offset * 2), "fixed regions are kept once within the original width");
        for (var y = 0; y < result.Height; y++)
        for (var x = 0; x < width; x++)
        {
            var expected = y >= result.Height - edges.Bottom ? third.GetPixel(x, y - offset * 2)
                : y < edges.Top ? first.GetPixel(x, y)
                : x >= body.Left && x < body.Right ? content.GetPixel(x - body.Left, y - body.Top)
                : y < body.Bottom ? first.GetPixel(x, y) : Color.White;
            var actual = result.GetPixel(x, y);
            if (actual.ToArgb() != expected.ToArgb())
                throw new InvalidOperationException($"fixed-edge composition differs at ({x}, {y}): {actual} vs {expected}");
        }
        Check(store.Undo(), "undo removes the last scroll distance with fixed edges");
        using var shorter = store.Build();
        Check(shorter.Height == height + offset && shorter.GetPixel(13, shorter.Height - 8) == second.GetPixel(13, height - 8),
            "undo restores the footer from the prior accepted frame");

        using var blankBefore = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var blankAfter = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(blankBefore)) graphics.Clear(Color.White);
        using (var graphics = Graphics.FromImage(blankAfter)) graphics.Clear(Color.White);
        Check(StationaryEdges.Find(blankBefore, blankAfter, offset) == Padding.Empty,
            "flat blank space alone does not identify any fixed region");
    }

    private static void PreviewForeground()
    {
        using var input = new PluginImage(new Bitmap(64, 400));
        var settings = new AppSettings();
        var host = new PluginHostServices(settings, (_, _) => { });
        using var preview = new PluginImagePreview(input, settings, (_, _) => { }, host, null, CancellationToken.None)
        { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };
        var shown = false;
        var topmost = false;
        var iconsFit = false;
        var zoomWorks = false;
        preview.Shown += (_, _) =>
        {
            try
            {
                shown = true;
                topmost = preview.TopMost;
                var toolbar = Descendants(preview).OfType<ToolStrip>().Single();
                var actions = toolbar.Items.OfType<ToolStripButton>().ToArray();
                iconsFit =
                    actions.Select(button => button.Name).SequenceEqual(
                        new[] { "copy", "save", "fit-width", "fit-image", "actual", "zoom-out", "zoom-in" }) &&
                    actions.All(button =>
                    button.DisplayStyle == ToolStripItemDisplayStyle.Image &&
                    !string.IsNullOrWhiteSpace(button.AccessibleName) &&
                    button.AccessibleName == button.ToolTipText &&
                    toolbar.ClientRectangle.Contains(button.Bounds));
                var viewport = Descendants(preview).OfType<ZoomImageViewport>().Single();
                var startingZoom = viewport.Zoom;
                actions.Single(button => button.Name == "zoom-in").PerformClick();
                zoomWorks = viewport.Zoom > startingZoom;
                actions.Single(button => button.Name == "fit-width").PerformClick();
                zoomWorks &= Math.Abs(viewport.Zoom - startingZoom) < .00001;
            }
            finally { preview.BeginInvoke(preview.Close); }
        };
        preview.ShowDialog();
        Check(shown && topmost, "the first visible preview is raised above other windows");
        Check(iconsFit, "the shared preview shows seven accessible icon buttons without overflow");
        Check(zoomWorks, "the shared preview icons keep their zoom and fit actions");
    }

    private static void PreviewUi()
    {
        var artifacts = Path.Combine(Path.GetDirectoryName(ScrollPluginRoot())!, "preview-ui-checks");
        Directory.CreateDirectory(artifacts);
        using var input = new PluginImage(new Bitmap(800, 6000));
        using (var g = Graphics.FromImage(input.Image))
        {
            g.Clear(Color.White);
            using var font = new Font("Segoe UI", 18);
            for (var row = 0; row < 120; row++)
            {
                using var back = new SolidBrush(row % 2 == 0 ? Color.FromArgb(232, 241, 252) : Color.White);
                g.FillRectangle(back, 0, row * 50, 800, 50);
                g.DrawString($"Row {row:000} — Preview zoom and pan — {row * 71}", font, Brushes.Black, 20, row * 50 + 8);
            }
        }
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var originalLanguage = SnipLite.Localization.LanguageService.CurrentLanguage;
        try
        {
            foreach (var language in new[] { "zh-CN", "en-US", "de" })
            foreach (var dark in new[] { false, true })
            {
                SnipLite.Localization.LanguageService.Configure(language);
                using var theme = AppTheme.Override(dark);
                var published = new List<(Size Size, bool Pin)>();
                var settings = new AppSettings { DisplayLanguage = language };
                var host = new PluginHostServices(settings, (_, _) => { });
                using (var form = new PluginImagePreview(input, settings, (image, pin) => published.Add((image.Image.Size, pin)), host, null, CancellationToken.None)
                { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) })
                {
                    form.Show(); Application.DoEvents();
                    var view = Descendants(form).OfType<ZoomImageViewport>().Single();
                    var tools = Descendants(form).OfType<ToolStrip>().Single();
                    ToolStripButton Button(string icon) => (ToolStripButton)tools.Items[icon]!;
                    ToolStripMenuItem MenuItem(string icon) => view.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>()
                        .Single(item => Equals(item.Tag, "fluent:" + icon));
                    Check(view.FitMode == ImageFitMode.Width && view.Zoom <= 1 && view.AutoScrollMinSize.Height > view.Height, "long preview opens at readable fit width");
                    Check(view.ImageBounds.Width <= view.ClientSize.Width, "fit width avoids horizontal overflow");
                    view.Fit(ImageFitMode.Image); Application.DoEvents();
                    Check(view.ImageBounds.Width <= view.ClientSize.Width && view.ImageBounds.Height <= view.ClientSize.Height, "fit image shows whole long capture");
                    Button("zoom-in").PerformClick();
                    Check(view.FitMode == ImageFitMode.Manual, "zoom buttons switch from fit mode to manual");
                    view.SetZoom(2); view.AutoScrollPosition = new Point(160, 700);
                    var anchor = new Point(view.ClientSize.Width / 2, view.ClientSize.Height / 2);
                    var before = view.ImagePoint(anchor);
                    view.SetZoom(2.4, anchor); Application.DoEvents();
                    var after = view.ImagePoint(anchor);
                    Check(Math.Abs(before.X - after.X) < 1 && Math.Abs(before.Y - after.Y) < 1, "zoom preserves image point beneath pointer");
                    var position = view.AutoScrollPosition;
                    form.ClientSize = new Size(form.ClientSize.Width + 50, form.ClientSize.Height + 40); Application.DoEvents();
                    Check(view.Zoom == 2.4 && view.AutoScrollPosition == position, "resize preserves manual zoom and scroll position");
                    view.SetZoom(double.PositiveInfinity); Check(view.Zoom == 2.4, "invalid zoom ignored");
                    view.SetZoom(100); Check(view.Zoom == ZoomImageViewport.MaxZoom && !Button("zoom-in").Enabled, "upper zoom bound updates button state");
                    view.SetZoom(0); Check(view.Zoom == ZoomImageViewport.MinZoom && !Button("zoom-out").Enabled, "lower zoom bound updates button state");
                    view.SetZoom(2); view.AutoScrollPosition = new Point(100, 300);
                    var startScroll = view.AutoScrollPosition;
                    typeof(Control).GetMethod("OnMouseDown", flags)!.Invoke(view, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 250, 250, 0) });
                    typeof(Control).GetMethod("OnMouseMove", flags)!.Invoke(view, new object[] { new MouseEventArgs(MouseButtons.Left, 0, 210, 200, 0) });
                    typeof(Control).GetMethod("OnMouseUp", flags)!.Invoke(view, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 210, 200, 0) });
                    Check(view.AutoScrollPosition.X < startScroll.X && view.AutoScrollPosition.Y < startScroll.Y, "drag pans long image horizontally and vertically");
                    MenuItem("pin").PerformClick(); Button("copy").PerformClick();
                    Check(published.SequenceEqual(new[] { (new Size(800, 6000), true), (new Size(800, 6000), false) }), "zoom preserves original pixels for pin and copy");
                    MenuItem("crop").PerformClick(); Application.DoEvents();
                    var numbers = Descendants(form).OfType<NumericUpDown>().ToArray();
                    numbers[0].Value = 100; numbers[1].Value = 200;
                    Descendants(form).OfType<FluentButton>().Single().PerformClick();
                    Button("copy").PerformClick();
                    Check(published.Last() == (new Size(800, 5700), false), "crop exports pixel dimensions independently of zoom");
                    Check(input.Image.Size == new Size(800, 6000), "preview crop leaves borrowed source intact");
                    MenuItem("crop").PerformClick(); Button("fit-width").PerformClick(); Application.DoEvents();
                    var message = Message.Create(form.Handle, 0x100, 0, 0);
                    var keyArgs = new object[] { message, Keys.Control | Keys.D0 };
                    Check((bool)typeof(PluginImagePreview).GetMethod("ProcessCmdKey", flags)!.Invoke(form, keyArgs)! && view.Zoom == 1, "Ctrl+0 resets actual size");
                    keyArgs[1] = Keys.Add;
                    Check((bool)typeof(PluginImagePreview).GetMethod("ProcessCmdKey", flags)!.Invoke(form, keyArgs)! && Math.Abs(view.Zoom - 1.2) < .00001, "plus key zooms preview");
                    if (language == "zh-CN")
                    {
                        view.SetZoom(1.5); Application.DoEvents();
                        using var enlarged = new Bitmap(form.Width, form.Height);
                        form.DrawToBitmap(enlarged, new Rectangle(Point.Empty, form.Size));
                        enlarged.Save(Path.Combine(artifacts, $"preview-zoomed-{(dark ? "dark" : "light")}.png"));
                    }
                    foreach (var small in new[] { false, true })
                    {
                        if (small) form.Size = form.MinimumSize;
                        Button("fit-width").PerformClick(); Application.DoEvents();
                        foreach (ToolStripItem item in tools.Items)
                            Check(tools.ClientRectangle.Contains(item.Bounds), "preview toolbar items stay inside toolbar on resize");
                        Check(!view.Bounds.IntersectsWith(tools.Bounds), "preview toolbar leaves image viewport clear");
                            using var snapshot = new Bitmap(form.Width, form.Height);
                        form.DrawToBitmap(snapshot, new Rectangle(Point.Empty, form.Size));
                        snapshot.Save(Path.Combine(artifacts, $"preview-{language}-{(dark ? "dark" : "light")}-{(small ? "small" : "normal")}.png"));
                        MenuItem("crop").PerformClick(); Application.DoEvents();
                        var cropPanel = (FlowLayoutPanel)typeof(PluginImagePreview).GetField("_cropPanel", flags)!.GetValue(form)!;
                        Check(cropPanel.Visible && !cropPanel.Bounds.IntersectsWith(view.Bounds), "crop panel reserves space above image");
                        foreach (Control control in cropPanel.Controls) Check(cropPanel.ClientRectangle.Contains(control.Bounds), "crop editors fit narrow preview");
                        var cropButton = cropPanel.Controls.OfType<FluentButton>().Single();
                        Check(cropButton.Width > TextRenderer.MeasureText(cropButton.Text, cropButton.Font).Width + (int)(18 * form.DeviceDpi / 96f), "crop action text fits without ellipsis");
                        if (language == "zh-CN" && small)
                        {
                            using var cropSnapshot = new Bitmap(form.Width, form.Height);
                            form.DrawToBitmap(cropSnapshot, new Rectangle(Point.Empty, form.Size));
                            cropSnapshot.Save(Path.Combine(artifacts, $"preview-crop-{(dark ? "dark" : "light")}.png"));
                        }
                        MenuItem("crop").PerformClick(); Application.DoEvents();
                    }
                    Check(form.FormBorderStyle == FormBorderStyle.Sizable && form.MinimizeBox && form.MaximizeBox && form.ControlBox,
                        "preview uses native window sizing and caption controls");
                    form.WindowState = FormWindowState.Maximized; Application.DoEvents();
                    Check(form.WindowState == FormWindowState.Maximized, "preview can maximize");
                    var screen = Screen.FromHandle(form.Handle);
                    Check(screen.WorkingArea.Contains(form.RectangleToScreen(form.ClientRectangle)),
                        "maximized preview client fits the monitor work area");
                    form.WindowState = FormWindowState.Normal; Application.DoEvents();
                    Check(form.WindowState == FormWindowState.Normal, "preview restores from maximized");
                    keyArgs[1] = Keys.Escape;
                    Check((bool)typeof(PluginImagePreview).GetMethod("ProcessCmdKey", flags)!.Invoke(form, keyArgs)! && form.IsDisposed,
                        "Escape closes the preview");
                }
                Check(input.Image.GetPixel(10, 10).A == 255, "preview disposal does not dispose source image");
            }
        }
        finally { SnipLite.Localization.LanguageService.Configure(originalLanguage); }
    }

    private sealed class ScrollUiPreview(IPluginHost host) : ScrollCaptureForm(host, new Rectangle(100, 100, 500, 400), CancellationToken.None)
    {
        // Establish real native child visibility for rendering without starting capture or scrolling.
        protected override void OnShown(EventArgs e) { }
    }

    private sealed class FakeScrollTarget : IScrollTarget
    {
        public bool Available = true;
        public bool AcceptsWheel = true;
        public bool Retryable;
        public bool CanRetry => Retryable;
        public string Failure => "test target unavailable";
        public Func<bool>? OnStep;
        public int Steps;
        public int LastNotches;
        public bool IsAvailable() => Available;
        public bool SendWheel(int notches) { Steps++; LastNotches = notches; return OnStep?.Invoke() ?? AcceptsWheel; }
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    internal static string ScrollPluginRoot() => Path.GetDirectoryName(ScrollPluginDirectory())!;
    private static string ScrollPluginDirectory()
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "SnipLite.slnx"))) repo = repo.Parent;
        return Path.Combine(repo!.FullName, "src/SnipLite.ScrollCapture/bin/Release/net10.0-windows");
    }

    private static void PreferencesIntegration()
    {
        var directory = Path.Combine(Path.GetDirectoryName(ScrollPluginRoot())!, "preferences-checks");
        Directory.CreateDirectory(directory);
        var preferences = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json");
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        using var manager = new PluginManager(new FakeHost(), () => true, preferences);
        manager.Discover(ScrollPluginRoot());
        var official = manager.Records.Single();
        Check(official.Icon is not null, "disabled plugin row reads artwork from its package");
        Check(!official.Loaded && !official.Enabled && manager.Commands.Count == 0, "separate scrolling plugin starts disabled");
        Check(!typeof(AppSettings).Assembly.GetReferencedAssemblies().Any(n => n.Name == "SnipLite.ScrollCapture"), "host has no scrolling plugin assembly dependency");
        Check(new AppSettings().HiddenToolbarButtons.Count == 0, "all toolbar buttons visible by default");
        Check(JsonSerializer.Deserialize<AppSettings>("{\"HiddenToolbarButtons\":[\"rect\"]}")!.HiddenToolbarButtons.Contains("rect"), "individual visibility preference round-trips");
        foreach (var language in SnipLite.Localization.LanguageService.Languages)
        {
            var settings = new AppSettings { DisplayLanguage = language.Code };
            using var form = new PreferencesForm(settings, PreferencesForm.PluginsTab, manager)
            { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };
            form.Show(); Application.DoEvents();
            var toggle = Descendants(form).OfType<FluentToggle>().Single();
            Check(toggle.Checked == official.Enabled, "plugin row reflects enable state: " + language.Code);
            var uninstall = Descendants(form).OfType<FluentButton>()
                .Single(button => button.Text == UiText.T("卸载插件", language.Code));
            Check(uninstall.Enabled && uninstall.Parent == toggle.Parent && uninstall.Left > toggle.Right &&
                uninstall.Anchor.HasFlag(AnchorStyles.Right), "uninstall is at the far right of the plugin row: " + language.Code);
            toggle.Checked = !official.Enabled;
            Application.DoEvents();
            Check(manager.Commands.Count == (official.Enabled ? 1 : 0), "plugin switch changes commands immediately: " + language.Code);
            using (var restartedPlugin = new PluginManager(new FakeHost(), () => true, preferences))
            {
                restartedPlugin.Discover(ScrollPluginRoot());
                Check(restartedPlugin.Commands.Count == (official.Enabled ? 1 : 0), "plugin switch persists across restart");
            }
            manager.SetEnabled(official, true); Application.DoEvents();
            form.SelectSubPage(0, 1);
            Check(Descendants(form).OfType<FluentToggle>().Count() == AnnotationBar.BuiltInButtons.Length + manager.Commands.Count, "individual built-in and enabled plugin toolbar switches");
            foreach (var tab in new[] { 0, PreferencesForm.PluginsTab })
            {
                form.SelectSubPage(tab, tab == 0 ? 1 : 0);
                var controls = Descendants(form).ToArray(); var scale = form.DeviceDpi / 96f;
                foreach (var size in new[] { new Size((int)(760 * scale), (int)(520 * scale)), new Size((int)(1160 * scale), (int)(800 * scale)) })
                {
                    form.ClientSize = size; Application.DoEvents();
                    Check(controls.SequenceEqual(Descendants(form)), "new preference pages retain controls during resize");
                    using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
                    using var graphics = Graphics.FromImage(bitmap);
                    typeof(PreferencesForm).GetMethod("LayoutTabs", flags)!.Invoke(form, new object[] { graphics });
                    var tabs = (List<(Rectangle Rect, int Index)>)typeof(PreferencesForm).GetField("_tabRects", flags)!.GetValue(form)!;
                    Check(tabs.Count == 9 && tabs.All(t => t.Rect.Bottom < form.ClientSize.Height - (int)(34 * scale)), "all nine navigation tabs fit");
                    Check(!tabs.Any(a => tabs.Any(b => a.Index != b.Index && a.Rect.IntersectsWith(b.Rect))), "plugin navigation does not overlap");
                    if (tab == 0 && language.Code == "zh-CN")
                    {
                        var preview = controls.OfType<ToolbarLayoutPreview>().Single();
                        var toolbar = (AnnotationBar)typeof(ToolbarLayoutPreview).GetField("_bar", flags)!.GetValue(preview)!;
                        var buttonBounds = AnnotationBar.BuiltInButtons.Select(button => button.Id)
                            .Concat(manager.Commands.Select(command => command.Id)).Select(toolbar.ItemBounds).ToArray();
                        Check(buttonBounds.All(bounds => !bounds.IsEmpty && bounds.Top == buttonBounds[0].Top),
                            "toolbar preview keeps built-in and plugin buttons on one row at every window width");

                        var selected = controls.OfType<FluentButton>().Single(button => button.Selected);
                        using var tabImage = new Bitmap(selected.Width, selected.Height);
                        selected.DrawToBitmap(tabImage, new Rectangle(Point.Empty, tabImage.Size));
                        var accent = AppTheme.Current.Accent.ToArgb();
                        Check(tabImage.GetPixel(tabImage.Width / 2, 0).ToArgb() == accent
                            && tabImage.GetPixel(tabImage.Width / 2, tabImage.Height - 1).ToArgb() == accent
                            && tabImage.GetPixel(0, tabImage.Height / 2).ToArgb() == accent
                            && tabImage.GetPixel(tabImage.Width - 1, tabImage.Height / 2).ToArgb() == accent,
                            "the selected subtab has a crisp accent border on all four sides");
                        if (size.Width < 1000 * scale)
                        {
                            using var narrow = new Bitmap(form.Width, form.Height);
                            form.DrawToBitmap(narrow, new Rectangle(Point.Empty, narrow.Size));
                            narrow.Save(Path.Combine(directory, "preferences-layout-narrow.png"));
                        }
                    }
                }
                if (language.Code == "zh-CN")
                {
                    using var image = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                    image.Save(Path.Combine(directory, $"preferences-{tab}.png"));
                    if (tab == 0)
                    {
                        var preview = Descendants(form).OfType<ToolbarLayoutPreview>().Single();
                        var annotationBar = (AnnotationBar)typeof(ToolbarLayoutPreview).GetField("_bar", flags)!.GetValue(preview)!;
                        var command = manager.Commands.Single();
                        Check(ReferenceEquals(annotationBar.PluginButtons.Single().Icon, command.Icon), "toolbar preview uses the plugin's artwork");
                        Check(!annotationBar.ItemBounds(command.Id).IsEmpty && !annotationBar.ItemBounds("rect").IsEmpty, "toolbar preview initially shows plugin and built-in buttons");
                        var previewLocation = preview.PointToScreen(Point.Empty);
                        var page = (Panel)typeof(PreferencesForm).GetField("_page", flags)!.GetValue(form)!;
                        page.AutoScrollPosition = new Point(0, 10000); Application.DoEvents();
                        Check(page.AutoScrollPosition.Y < 0 && preview.PointToScreen(Point.Empty) == previewLocation, "toolbar preview stays visible when settings scroll");

                        settings.HiddenToolbarButtons = new HashSet<string> { command.Id };
                        preview.RefreshToolbar();
                        Check(annotationBar.ItemBounds(command.Id).IsEmpty && !annotationBar.ItemBounds("rect").IsEmpty, "toolbar preview hides the selected plugin button");
                        settings.HiddenToolbarButtons = AnnotationBar.BuiltInButtons.Select(button => button.Id).Append(command.Id).ToHashSet();
                        preview.RefreshToolbar();
                        Check(!annotationBar.Visible, "toolbar preview handles all buttons hidden");
                        settings.HiddenToolbarButtons = new HashSet<string> { "rect" };
                        preview.RefreshToolbar();
                        Check(annotationBar.ItemBounds("rect").IsEmpty && !annotationBar.ItemBounds(command.Id).IsEmpty, "toolbar preview hides a built-in button and restores the plugin button");
                        settings.HiddenToolbarButtons.Clear();
                        preview.RefreshToolbar();
                        using var pluginButtons = new Bitmap(form.Width, form.Height);
                        form.DrawToBitmap(pluginButtons, new Rectangle(Point.Empty, form.Size));
                        pluginButtons.Save(Path.Combine(directory, "preferences-plugin-buttons.png"));
                    }
                }
            }
        }
        PluginHotkeyPreferences(manager, official, preferences);
        File.Delete(preferences);
        var previewBar = new AnnotationBar
        {
            PluginButtons = manager.Commands.Select(c => (c.Id, c.Title.Get("zh-CN"), c.Icon)).ToArray()
        };
        previewBar.Layout(new Rectangle(80, 100, 1000, 500), new Size(1800, 900));
        using (var toolbar = new Bitmap(1800, 900))
        {
            using var graphics = Graphics.FromImage(toolbar); graphics.Clear(Color.WhiteSmoke); previewBar.Render(graphics);
            toolbar.Save(Path.Combine(directory, "toolbar-plugin-icon.png"));
        }
        var bar = new AnnotationBar { PluginButtons = new[] { ("plugin/capture", "Capture", (Image?)null) } };
        var selection = new Rectangle(80, 100, 600, 500); var screen = new Size(1200, 900);
        bar.Layout(selection, screen);
        var before = bar.MainBounds.Width;
        foreach (var (id, _) in AnnotationBar.BuiltInButtons)
        {
            bar.HiddenButtons = new HashSet<string> { id };
            bar.Layout(selection, screen);
            Check(bar.ItemBounds(id).IsEmpty && bar.MainBounds.Width < before, "hidden toolbar button removed from layout: " + id);
            Check(!bar.ItemBounds("plugin/capture").IsEmpty, "plugin button unaffected by built-in visibility");
        }
        bar.HiddenButtons = new HashSet<string> { "plugin/capture" }; bar.Layout(selection, screen);
        Check(bar.ItemBounds("plugin/capture").IsEmpty && !bar.ItemBounds("rect").IsEmpty, "plugin toolbar visibility is independent");
        bar.HiddenButtons = AnnotationBar.BuiltInButtons.Select(b => b.Id).Append("plugin/capture").ToHashSet(); bar.Layout(selection, screen);
        Check(!bar.Visible && bar.MainBounds.Height == 0, "all hidden buttons leave no clickable toolbar");
        bar.Status = "Recognition status"; bar.Layout(selection, screen);
        Check(bar.Visible && bar.MessageBounds.Height > 0 && bar.MainBounds.Height == 0, "button visibility preserves recognition status strip");
        bar.Status = "";
        bar.HiddenButtons = new HashSet<string>(); bar.PluginButtons = Enumerable.Range(0, 25).Select(i => ($"plugin/{i}", "Plugin " + i, (Image?)null)).ToArray();
        bar.Layout(selection, new Size(800, 900));
        Check(bar.MainBounds.Width < 800 && bar.MainBounds.Height > 44, "many plugin buttons wrap within screen");
        bar.ButtonOrder = AnnotationBar.BuiltInButtons.Select(b => b.Id).ToArray();
        bar.PluginButtons = [("sniplite.screen-recorder/record", "Record", null)];
        var withNewRecorder = bar.OrderedButtons().ToArray();
        Check(Array.IndexOf(withNewRecorder, "sniplite.screen-recorder/record") < Array.IndexOf(withNewRecorder, "close"),
            "new recorder remains in the plugin group after a toolbar order was saved");
        var host = new PluginHostServices(new AppSettings(), (_, _) => { }) { PendingSelection = selection };
        Check(host.SelectRegionAsync(CancellationToken.None).Result == selection && host.PendingSelection is null, "F1 plugin invocation consumes existing region once");
    }

    private static void PluginHotkeyPreferences(PluginManager manager, PluginRecord plugin, string preferences)
    {
        var command = manager.Commands.Single();
        var title = command.Title.Get("zh-CN");
        var original = Keys.Control | Keys.Shift | Keys.F23;
        var replacement = Keys.Control | Keys.Shift | Keys.F24;
        manager.SetHotkey(command.Id, original);
        using var form = new PreferencesForm(new AppSettings { DisplayLanguage = "zh-CN" }, PreferencesForm.ControlTab, manager)
        { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };
        form.HotkeyAvailable = binding => binding != Keys.F22;
        form.Show();
        Application.DoEvents();

        FluentSettingRow Row(string label) => Descendants(form).OfType<FluentSettingRow>()
            .Single(row => row.Controls.OfType<Label>().Any(control => control.Text == label));
        Check(Row(title).Controls.OfType<Label>().Any(label => label.Text == HotkeyDefinitions.Format(original)), "existing plugin binding is shown in global shortcuts");
        Check(Row(title).Top > Row(UiText.T(HotkeyDefinitions.Items.Last().Label)).Top, "plugin shortcuts follow built-in rows");

        var processKey = typeof(HotkeyBox).GetMethod("ProcessCmdKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        void Edit(string label, Keys binding, string? validationMessage = null)
        {
            Exception? error = null;
            form.BeginInvoke(() =>
            {
                var dialog = Application.OpenForms.Cast<Form>().Single(window => window.Text == label && Descendants(window).OfType<HotkeyBox>().Any());
                try
                {
                    var box = Descendants(dialog).OfType<HotkeyBox>().Single();
                    processKey.Invoke(box, new object[] { new Message(), binding });
                    Descendants(dialog).OfType<FluentButton>().Single(button => button.Text == UiText.T("确认")).PerformClick();
                    if (validationMessage is not null)
                    {
                        Check(dialog.DialogResult == DialogResult.None, "a conflicting shortcut leaves the editor open");
                        Check(Descendants(dialog).OfType<Label>().Any(status => status.Text == UiText.T(validationMessage)), "editor explains the shortcut conflict");
                    }
                    else Check(dialog.DialogResult == DialogResult.OK, "a valid shortcut closes the editor");
                }
                catch (Exception ex) { error = ex; }
                finally { if (dialog.DialogResult == DialogResult.None) dialog.Close(); }
            });
            Row(label).Controls.OfType<FluentButton>().Single().PerformClick();
            if (error is not null) throw new InvalidOperationException("Plugin shortcut editor failed.", error);
            Application.DoEvents();
        }

        Edit(title, Keys.F1, "与其他功能重复，请重新输入。");
        Edit(UiText.T("截屏"), original, "与其他功能重复，请重新输入。");
        Edit(title, Keys.F22, "组合键被占用或由系统保留，请重新输入。");
        Check(manager.Hotkey(command.Id) == original, "shortcut conflicts preserve the previous plugin binding");
        Edit(title, replacement);
        using (var reopened = new PluginManager(new FakeHost(), () => true, preferences))
            Check(reopened.Hotkey(command.Id) == replacement, "saving from preferences persists the plugin binding");
        Check(Row(title).Controls.OfType<Label>().Any(label => label.Text == HotkeyDefinitions.Format(replacement)), "saved plugin hotkey updates its settings row");
        Edit(title, Keys.Delete);
        Check(manager.Hotkey(command.Id) == Keys.None, "Delete clears the plugin shortcut");

        manager.SetEnabled(plugin, false);
        Application.DoEvents();
        Check(!Descendants(form).OfType<Label>().Any(label => label.Text == title), "disabling the plugin removes its shortcut row");
        manager.SetEnabled(plugin, true);
        Application.DoEvents();
        Check(Row(title).Controls.OfType<Label>().Any(label => label.Text == HotkeyDefinitions.Format(Keys.None)), "enabling the plugin adds its shortcut row");
    }

    private static void BundledPlugin()
    {
        var directory = Path.Combine(Path.GetDirectoryName(ScrollPluginRoot())!, "bundled-checks", Guid.NewGuid().ToString("N"));
        var cache = Path.Combine(directory, "cache");
        var preferences = Path.Combine(directory, "plugins.json");
        var imports = Path.Combine(directory, "imports");
        var legacy = Path.Combine(imports, "old-scroll");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "plugin.json"), """{"id":"SNIPLITE.SCROLL-CAPTURE","version":"0.1.0"}""");
        var host = new FakeHost();
        string extracted;
        using (var manager = new PluginManager(host, () => true, preferences))
        {
            manager.DiscoverBundled(cache);
            manager.Discover(imports);
            var bundled = manager.Records.Single();
            Check(bundled.Bundled && bundled.Loaded && bundled.Enabled && bundled.Icon is not null,
                "clean profile loads the bundled plugin, manifest and icon by default");
            Check(manager.Commands.Single().Id == BundledScrollPlugin.Id + "/capture",
                "legacy manual copy does not produce a second command or error row");
            extracted = bundled.Directory;
            Check(BundledScrollPlugin.Prepare(cache) == extracted, "valid extracted bundle is reused");
            manager.SetEnabled(bundled, false);
        }
        using (var manager = new PluginManager(host, () => true, preferences))
        {
            manager.DiscoverBundled(cache);
            manager.Discover(imports);
            Check(!manager.Records.Single().Enabled && manager.Commands.Count == 0, "bundled plugin honors previous opt-out on restart");
        }
        File.WriteAllText(Path.Combine(extracted, "plugin.json"), "{}");
        var repaired = BundledScrollPlugin.Prepare(cache);
        Check(repaired != extracted && PluginManifest.Read(Path.Combine(repaired, "plugin.json")).Id == BundledScrollPlugin.Id,
            "altered bundle cache is replaced by resources from the host");
    }

    private static void PluginPackageLifecycle()
    {
        var directory = Path.Combine(Path.GetDirectoryName(ScrollPluginRoot())!, "updates-checks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var cache = Path.Combine(directory, "bundled");
        var plugins = Path.Combine(directory, "plugins");
        var settings = Path.Combine(directory, "plugins.json");
        var host = new FakeHost();
        PluginManager Open()
        {
            var manager = new PluginManager(host, () => true, settings, plugins);
            manager.DiscoverBundled(cache);
            manager.DiscoverInstalled();
            return manager;
        }
        string Package(string id, string version, string? entryType = null)
        {
            var path = Path.Combine(directory, $"{id}-{version}{(entryType is null ? "" : "-broken")}.snipplugin");
            using var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create);
            var original = PluginManifest.Read(Path.Combine(ScrollPluginDirectory(), "plugin.json"));
            original.Id = id;
            original.Version = version;
            if (entryType is not null) original.EntryType = entryType;
            using (var writer = new StreamWriter(zip.CreateEntry("plugin.json").Open()))
                writer.Write(System.Text.Json.JsonSerializer.Serialize(original));
            foreach (var name in new[] { original.EntryAssembly, "SnipLite.ScrollCapture.deps.json", original.Icon })
                System.IO.Compression.ZipFileExtensions.CreateEntryFromFile(zip, Path.Combine(ScrollPluginDirectory(), name), name);
            return path;
        }

        var scrollId = BundledScrollPlugin.Id;
        var externalId = "fixtures.update";
        var binding = Keys.Control | Keys.Shift | Keys.F9;
        var update = Package(scrollId, "99.0.0");
        using (var manager = Open())
        {
            var builtIn = manager.Records.Single();
            try { manager.ImportPackage(Package(scrollId, builtIn.Manifest!.Version)); throw new Exception("Same version accepted."); }
            catch (IOException) { _checks++; }
            manager.SetHotkey(scrollId + "/capture", binding);
            manager.ImportPackage(update);
            Check(manager.HasPendingChanges && builtIn.Bundled && manager.Commands.Count == 1,
                "updates leave the active DLL and command in place until restart");
            try { manager.ImportPackage(Package(scrollId, "98.0.0")); throw new Exception("Downgrade of queued update accepted."); }
            catch (IOException) { _checks++; }
        }
        using (var manager = Open())
        {
            var active = manager.Records.Single();
            Check(active.Manifest!.Version == "99.0.0" && !active.Bundled && active.Enabled &&
                manager.Hotkey(scrollId + "/capture") == binding, "bundled update takes over once and inherits defaults and hotkey");
            manager.ImportPackage(Package(scrollId, "100.0.0", "Missing.EntryType"));
        }
        using (var manager = Open())
        {
            var active = manager.Records.Single();
            Check(active.Bundled && manager.Commands.Count == 1 && manager.CanRestoreBundled(active),
                "a broken update falls back to a usable bundled plugin and can be removed");
            manager.RestoreBundled(active);
            manager.ImportPackage(Package(externalId, "1.0.0"));
            var imported = manager.Records.Single(record => record.Manifest?.Id == externalId);
            Check(!imported.Enabled, "new third-party imports still require opt-in");
            manager.SetEnabled(imported, true);
            manager.SetHotkey(externalId + "/capture", binding);
            manager.ImportPackage(Package(externalId, "2.0.0"));
            Check(imported.Manifest!.Version == "1.0.0" && imported.RestartPending,
                "third-party update can be queued while its old DLL is loaded");
        }
        using (var manager = Open())
        {
            var active = manager.Records.Single(record => record.Manifest?.Id == externalId);
            Check(manager.Records.Single(record => record.IsScrollCapture).Bundled && active.Manifest!.Version == "2.0.0"
                && active.Enabled && manager.Hotkey(externalId + "/capture") == binding,
                "restart restores bundled version and loads updated third-party plugin with its settings");
            manager.Uninstall(active);
        }
        using (var manager = Open())
        {
            var bundled = manager.Records.Single();
            Check(bundled.Bundled && manager.Hotkey(externalId + "/capture") == binding,
                "uninstall removes the third-party entry while keeping preferences");
            Check(manager.CanUninstall(bundled) && !manager.CanRestoreBundled(bundled),
                "the bundled plugin has an uninstall action without a redundant restore action");
            manager.Uninstall(bundled);
            Check(!manager.CanUninstall(bundled) && manager.HasPendingChanges,
                "uninstall is staged once and shows a pending restart");
        }
        using (var manager = Open())
        {
            Check(manager.Records.Count == 0 && manager.Commands.Count == 0 &&
                manager.Hotkey(scrollId + "/capture") == binding, "bundled uninstall persists on startup and keeps the hotkey");
            manager.ImportPackage(update);
            var installed = manager.Records.Single();
            Check(!installed.Bundled && manager.CanRestoreBundled(installed),
                "the user can reinstall an independent package after removing the bundled plugin");
        }
        using (var manager = Open())
        {
            var installed = manager.Records.Single();
            Check(!installed.Bundled, "restarting after reinstall loads the independent version");
            manager.Uninstall(installed);
        }
        using (var manager = Open())
            Check(manager.Records.Count == 0 && manager.Commands.Count == 0,
                "uninstalling an independent long capture package leaves the plugin removed");
    }

    private static async Task LivePluginCatalog()
    {
        var entries = await PluginCatalog.FetchAsync(CancellationToken.None);
        var entry = entries.Single(plugin => plugin.Id == BundledScrollPlugin.Id);
        Check(entry.IsCompatible, "the published scrolling plugin supports this host");

        var root = Path.Combine(Path.GetTempPath(), "SnipLite-catalog-check-" + Guid.NewGuid().ToString("N"));
        var settings = Path.Combine(root, "plugins.json");
        var cache = Path.Combine(root, "bundled");
        var packages = Path.Combine(root, "packages");
        var commandId = entry.Id + "/capture";
        var shortcut = Keys.Control | Keys.Shift | Keys.F9;
        PluginManager Open()
        {
            var plugins = new PluginManager(new FakeHost(), () => true, settings, packages);
            plugins.DiscoverBundled(cache);
            plugins.DiscoverInstalled();
            return plugins;
        }

        try
        {
            var bundledManifest = PluginManifest.Read(Path.Combine(BundledScrollPlugin.Prepare(cache), "plugin.json"));
            if (Version.Parse(entry.Version) <= Version.Parse(bundledManifest.Version))
            {
                using (var plugins = Open())
                {
                    var bundled = plugins.Records.Single();
                    plugins.SetHotkey(commandId, shortcut);
                    plugins.Uninstall(bundled);
                }
                using (var plugins = Open())
                {
                    Check(plugins.Records.Count == 0, "the bundled plugin was removed");
                    await plugins.InstallFromCatalogAsync(entry, CancellationToken.None);
                }
                using (var plugins = Open())
                {
                    var installed = plugins.Records.Single();
                    Check(!installed.Bundled && installed.Manifest?.Version == entry.Version && plugins.Hotkey(commandId) == shortcut,
                        "the published package installs when the bundled version is already current");
                    plugins.SetEnabled(installed, true);
                    Check(installed.Loaded && plugins.Commands.Any(command => command.Id == commandId), "the published package loads and registers its command");
                }
                Console.WriteLine($"Verified live catalog and plugin release {entry.Version}.");
                return;
            }

            using (var plugins = Open())
            {
                var bundled = plugins.Records.Single();
                Check(bundled.Bundled && bundled.Loaded, "the trial starts from the offline bundled plugin");
                plugins.SetHotkey(commandId, shortcut);
                plugins.SetEnabled(bundled, false);
                await plugins.InstallFromCatalogAsync(entry, CancellationToken.None);
                Check(bundled.PendingVersion == entry.Version && bundled.RestartPending, "the published update is staged until restart");
            }
            using (var plugins = Open())
            {
                var updated = plugins.Records.Single();
                Check(!updated.Bundled && updated.Manifest?.Version == entry.Version &&
                    !updated.Enabled && plugins.Hotkey(commandId) == shortcut, "update preserves the disabled setting and shortcut");
                plugins.SetEnabled(updated, true);
                Check(updated.Loaded && plugins.Commands.Any(command => command.Id == commandId), "the independent package loads and registers its command");
                plugins.RestoreBundled(updated);
            }
            using (var plugins = Open())
                Check(plugins.Records.Single() is { Bundled: true, Loaded: true } && plugins.Hotkey(commandId) == shortcut,
                    "the offline version is restored with the saved shortcut");
            Console.WriteLine($"Verified live catalog and plugin release {entry.Version}.");
        }
        finally
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Could not delete temporary catalog test files: {root}. {ex.Message}");
            }
        }
    }

    private static async Task InstallerChecks()
    {
        var temporary = Path.Combine(Path.GetDirectoryName(ScrollPluginRoot())!, "installer-checks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var package = Path.Combine(temporary, "scroll.snipplugin");
            System.IO.Compression.ZipFile.CreateFromDirectory(ScrollPluginDirectory(), package);
            var installedRoot = Path.Combine(temporary, "installed");
            using var manager = new PluginManager(new FakeHost(), () => true, Path.Combine(temporary, "settings.json"));
            manager.Discover(installedRoot); Check(manager.Records.Count == 0, "clean application has no scrolling plugin installed");
            var folder = PluginInstaller.Import(package, installedRoot);
            Check(File.Exists(Path.Combine(folder, "SnipLite.ScrollCapture.dll")), "ZIP installs separate scrolling DLL");
            manager.Discover(installedRoot); manager.Discover(installedRoot);
            Check(manager.Records.Count == 1 && manager.Commands.Count == 0, "import appears once and never enables code automatically");
            manager.SetEnabled(manager.Records.Single(), true);
            Check(manager.Commands.Count == 1, "imported plugin enables without restart");
            manager.SetEnabled(manager.Records.Single(), false);
            Check(manager.Commands.Count == 0, "plugin disable immediately removes commands");
            try { PluginInstaller.Import(package, installedRoot); throw new Exception("Duplicate package overwritten."); }
            catch (IOException) { _checks++; }
            Check(!Directory.GetDirectories(installedRoot, ".install-*").Any(), "duplicate import cleans staging folder");
            void RejectPackage(string name, params string[] entries)
            {
                var zipPath = Path.Combine(temporary, name + ".zip");
                using (var zip = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
                    foreach (var entry in entries) { using var writer = new StreamWriter(zip.CreateEntry(entry).Open()); writer.Write("invalid"); }
                try { PluginInstaller.Import(zipPath, installedRoot); throw new Exception("Invalid package accepted: " + name); }
                catch (InvalidDataException) { _checks++; }
                Check(!Directory.GetDirectories(installedRoot, ".install-*").Any(), "rejected package leaves no staging: " + name);
            }
            RejectPackage("traversal", "../outside.txt"); RejectPackage("absolute", "C:/outside.txt");
            RejectPackage("duplicate", "a.txt", "A.txt"); RejectPackage("missing-manifest", "readme.txt");
            RejectPackage("multiple-manifests", "one/plugin.json", "two/plugin.json");
            Check(!File.Exists(Path.Combine(temporary, "outside.txt")), "ZIP traversal writes nothing outside staging");
            using (var client = new HttpClient(new PackageHandler(File.ReadAllBytes(package))))
            {
                var downloadRoot = Path.Combine(temporary, "downloaded");
                var downloaded = await PluginInstaller.DownloadAsync(new Uri("https://plugins.example/test.snipplugin"), downloadRoot, CancellationToken.None, client);
                Check(File.Exists(Path.Combine(downloaded, "plugin.json")), "HTTPS response downloads and installs through shared importer");
                var manifest = PluginManifest.Read(Path.Combine(downloaded, "plugin.json"));
                var release = new CatalogPlugin(manifest.Id, manifest.Name, "", "Test", "Test package", "", manifest.Version,
                    manifest.ApiVersion, manifest.MinHostVersion, manifest.Architecture, new Uri("https://plugins.example/test.snipplugin"),
                    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(package))));
                var catalogRoot = Path.Combine(temporary, "catalog-download");
                using var catalogManager = new PluginManager(new FakeHost(), () => true, Path.Combine(temporary, "catalog.json"), catalogRoot);
                try { await catalogManager.InstallFromCatalogAsync(release with { Sha256 = new string('0', 64) }, CancellationToken.None, client); throw new Exception("Wrong digest accepted."); }
                catch (InvalidDataException) { _checks++; }
                try { await catalogManager.InstallFromCatalogAsync(release with { Id = "checks.different" }, CancellationToken.None, client); throw new Exception("Wrong plugin identity accepted."); }
                catch (InvalidDataException) { _checks++; }
                Check(catalogManager.Records.Count == 0, "rejected catalog packages do not register plugins");
                await catalogManager.InstallFromCatalogAsync(release, CancellationToken.None, client);
                Check(catalogManager.Records.Single().Manifest?.Id == manifest.Id, "a matching catalog package is installed");
                try { await PluginInstaller.DownloadAsync(new Uri("http://plugins.example/test.zip"), downloadRoot, CancellationToken.None, client); throw new Exception("Insecure URL accepted."); }
                catch (ArgumentException) { _checks++; }
                using var canceled = new CancellationTokenSource(); canceled.Cancel();
                try { await PluginInstaller.DownloadAsync(new Uri("https://plugins.example/test.zip"), Path.Combine(temporary, "canceled"), canceled.Token, client); throw new Exception("Cancellation ignored."); }
                catch (OperationCanceledException) { _checks++; }
                Check(!Directory.Exists(Path.Combine(temporary, "canceled")), "canceled download installs no plugin");
            }
        }
        finally
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            try { Directory.Delete(temporary, true); } catch (IOException) { }
        }
    }

    private sealed class PackageHandler(byte[] package) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { RequestMessage = request, Content = new ByteArrayContent(package) });
        }
    }

    private static void AssertPixels(Bitmap actual, Bitmap source, int height)
    {
        Check(actual.Size == new Size(source.Width, height), "stitched dimensions");
        for (var y = 0; y < height; y++)
        for (var x = 0; x < source.Width; x++)
            if (actual.GetPixel(x, y) != source.GetPixel(x, y)) throw new Exception($"Stitch differs at {x},{y}.");
        _checks++;
    }

    private static async Task Plugins()
    {
        var rootInfo = new DirectoryInfo(AppContext.BaseDirectory);
        while (rootInfo is not null && !File.Exists(Path.Combine(rootInfo.FullName, "SnipLite.slnx"))) rootInfo = rootInfo.Parent;
        var root = rootInfo?.FullName ?? throw new DirectoryNotFoundException("Run checks from the source checkout.");
        var fixtureOutput = Path.Combine(root, "validation/PluginFixture/bin/Release/net10.0-windows");
        var temporary = Path.Combine(root, ".local/plugin-checks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var plugins = Path.Combine(temporary, "plugins"); Directory.CreateDirectory(plugins);
            string Fixture(string folder, string id, string type = "PluginFixture.FixturePlugin", int api = 1, string min = "0.3.14", string entry = "PluginFixture.dll", string arch = "any")
            {
                var directory = Path.Combine(plugins, folder); Directory.CreateDirectory(directory);
                foreach (var file in Directory.GetFiles(fixtureOutput)) File.Copy(file, Path.Combine(directory, Path.GetFileName(file)), true);
                File.WriteAllText(Path.Combine(directory, "plugin.json"), JsonSerializer.Serialize(new
                { id, name = folder, version = "1.0.0", apiVersion = api, minHostVersion = min, entryAssembly = entry, entryType = type, architecture = arch }));
                return directory;
            }
            Fixture("a-fixture", "checks.fixture");
            var preferences = Path.Combine(temporary, "preferences.json"); var host = new FakeHost();
            using (var manager = new PluginManager(host, () => true, preferences))
            {
                manager.Discover(plugins);
                Check(manager.Commands.Count == 0 && !manager.Records[0].Enabled, "external plugins default disabled");
                Check(manager.Records[0].Icon is null, "existing iconless plugin remains compatible");
                manager.SetEnabled(manager.Records[0], true);
            }
            using (var manager = new PluginManager(host, () => true, preferences))
            {
                manager.Discover(plugins);
                Check(manager.Commands.Count == 5 && manager.Records[0].Loaded, "shared SDK identity and all four extension types load");
                await manager.ExecuteAsync("checks.fixture/ping");
                Check(host.Messages.Contains("fixture-private-dependency"), "private dependency resolves");
                await manager.ExecuteAsync("checks.fixture/capture");
                await manager.ExecuteAsync("checks.fixture/process");
                await manager.ExecuteAsync("checks.fixture/export");
                Check(host.Previews == 2 && host.Messages.Contains("fixture-export:64"), "capture processor and output adapters execute");
                manager.SetHotkey("checks.fixture/ping", Keys.Control | Keys.Alt | Keys.F10);
                Check(manager.Hotkey("checks.fixture/ping") == (Keys.Control | Keys.Alt | Keys.F10), "shortcut persists");
                var hold = manager.ExecuteAsync("checks.fixture/hold");
                Check(manager.IsRunning && !manager.WhenIdle.IsCompleted, "execution lifetime tracked");
                var count = host.Messages.Count; await manager.ExecuteAsync("checks.fixture/ping");
                Check(count == host.Messages.Count, "overlapping commands refused");
                manager.Cancel(); await hold;
                Check(!manager.IsRunning && manager.WhenIdle.IsCompleted, "command cancellation completes");
                var disabledTask = manager.ExecuteAsync("checks.fixture/hold");
                manager.SetEnabled(manager.Records.Single(), false);
                await disabledTask;
                Check(!manager.IsRunning && manager.Commands.Count == 0 && manager.Processors.Count == 0 && manager.Outputs.Count == 0, "disable cancels activity and removes all extension types immediately");
                manager.SetEnabled(manager.Records.Single(), true);
                Check(manager.Commands.Count == 5, "reenable restores contributions without duplicate registration");
                manager.Dispose(); Check(manager.Commands.Count == 0 && manager.Processors.Count == 0, "registrations released on disposal");
            }
            Fixture("b-duplicate", "checks.fixture");
            Fixture("c-broken", "checks.broken", "PluginFixture.BrokenPlugin");
            Fixture("d-api", "checks.api", api: 99);
            Fixture("e-version", "checks.future", min: "99.0.0");
            Fixture("f-path", "checks.path", entry: "../PluginFixture.dll");
            Fixture("g-arch", "checks.arch", arch: "not-a-platform");
            using (var manager = new PluginManager(host, () => true, Path.Combine(temporary, "fresh.json")))
            {
                manager.Discover(plugins);
                foreach (var record in manager.Records.Where(r => r.Error.Length == 0).ToArray())
                { try { manager.SetEnabled(record, true); } catch (Exception ex) { record.Error = ex.GetBaseException().Message; } }
                Check(manager.Commands.Count == 5, "failed initialization rolls back commands");
                Check(manager.Records.Count(r => r.Error.Length > 0) == 6, "duplicates invalid API version path and architecture isolated");
                Check(manager.Records.Any(r => r.Error.Contains("fixture-initialization-failed")), "initialization error reported");
            }
            using var official = new PluginManager(host, () => true, Path.Combine(temporary, "official.json"));
            official.Discover(ScrollPluginRoot());
            official.SetEnabled(official.Records.Single(), true);
            Check(official.Commands.Any(c => c.Id == "sniplite.scroll-capture/capture"), "separately distributed official plugin loads independently");
            Check(official.Commands.Single().Icon is not null && ReferenceEquals(official.Commands.Single().Icon, official.Records.Single().Icon), "registered command uses plugin-supplied artwork");
        }
        finally
        {
            // Windows may retain mapped plugin DLLs until the collectible context is collected.
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            try { Directory.Delete(temporary, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class FakeHost : IPluginHost
    {
        public string Language => "en-US";
        public List<string> Messages { get; } = new();
        public int Previews;
        public Bitmap CaptureRegion(Rectangle bounds) => new(bounds.Width, bounds.Height);
        public Task<Rectangle?> SelectRegionAsync(CancellationToken token) => Task.FromResult<Rectangle?>(new Rectangle(0, 0, 64, 100));
        public Task<PluginImage?> SelectImageAsync(CancellationToken token) => Task.FromResult<PluginImage?>(new(new Bitmap(64, 100)));
        public Task PreviewImageAsync(PluginImage image, CancellationToken token) { Previews++; return Task.CompletedTask; }
        public void StyleWindow(Form form) { }
        public void Log(string message, Exception? exception = null) { Messages.Add(message); }
    }
}

internal sealed class ScrollDemo : Form
{
    private readonly PluginManager _manager;
    private readonly Bitmap _document = new(900, 7000);
    public ScrollDemo()
    {
        Text = "SnipLite — 自动长截图测试（半屏加速版）";
        StartPosition = FormStartPosition.Manual; Location = new Point(100, 100); ClientSize = new Size(980, 850);
        using (var g = Graphics.FromImage(_document))
        {
            g.Clear(Color.White);
            using var font = new Font("Segoe UI", 16);
            for (var row = 0; row < 140; row++)
            {
                using var background = new SolidBrush(row % 2 == 0 ? Color.FromArgb(240, 245, 252) : Color.White);
                g.FillRectangle(background, 0, row * 50, 900, 50);
                g.DrawString($"Row {row:000} — scrolling screenshot test — value {row * row + 7919}", font, Brushes.Black, 15, row * 50 + 10);
                using var marker = new SolidBrush(Color.FromArgb((row * 43) % 255, (row * 71) % 255, (row * 23) % 255));
                g.FillRectangle(marker, 780, row * 50 + 5, 85, 40);
            }
        }
        var view = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var picture = new PictureBox { Image = _document, Size = _document.Size };
        view.Controls.Add(picture);
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 55 };
        var start = new Button { Text = "Start scroll capture", Width = 180, Height = 40 };
        var manage = new Button { Text = "Plugin manager", Width = 160, Height = 40 };
        top.Controls.AddRange(new Control[] { start, manage });
        Controls.Add(view); Controls.Add(top);
        var host = new PluginHostServices(new AppSettings { ShowToast = false, AutoSave = false, HistoryImageLimit = 0 }, (image, _) => Services.CopyImage(image.Image, true));
        _manager = new PluginManager(host, () => true, Path.Combine(AppContext.BaseDirectory, "demo-plugins.json")); host.Manager = _manager;
        _manager.Discover(Program.ScrollPluginRoot());
        _manager.SetEnabled(_manager.Records.Single(), true);
        start.Click += async (_, _) =>
        {
            if (_manager.IsRunning) return;
            view.AutoScrollPosition = Point.Empty; view.Refresh();
            await _manager.ExecuteAsync("sniplite.scroll-capture/capture");
        };
        manage.Click += (_, _) => new PreferencesForm(new AppSettings(), PreferencesForm.PluginsTab, _manager).Show(this);
        FormClosed += (_, _) => { _manager.Dispose(); picture.Image = null; _document.Dispose(); };
    }
}
