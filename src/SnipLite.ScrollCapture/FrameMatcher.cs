using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SnipLite.ScrollCapture;

internal enum MatchKind { Unchanged, Forward, Reverse, Uncertain }
internal readonly record struct FrameMatch(MatchKind Kind, int Offset = 0);
internal enum AlignmentStatus
{
    SizeChanged, Unchanged, LowTexture, NoCandidates, HighError, Ambiguous, RepaintAmbiguous, BroadPeak,
    VerificationFailed, NearbyUnverified, Accepted, Reverse
}
internal readonly record struct AlignmentCandidate(int Offset, double Score);
internal readonly record struct AlignmentSearch(
    AlignmentStatus Status, AlignmentCandidate? Best = null, AlignmentCandidate? Rival = null,
    double? Limit = null, double? Verification = null, double? EdgeAgreement = null);
internal readonly record struct FrameAlignment(FrameMatch Match, AlignmentSearch Search);

/// <summary>Immutable sampled pixel rows. No GDI objects cross the matching worker boundary.</summary>
internal sealed class FrameSample
{
    public const int Columns = 48;
    public int Width { get; }
    public int Height { get; }
    public byte[] Gray { get; }

    internal FrameSample(int width, int height, byte[] gray)
    {
        if (gray.Length != height * Columns) throw new InvalidDataException("Invalid cached frame sample.");
        Width = width; Height = height; Gray = gray;
    }

    internal FrameSample ScrollingBody(Padding edges)
    {
        var body = StationaryEdges.Body(new Size(Width, Height), edges);
        var first = 0;
        var last = Columns - 1;
        while (first < last && SampleColumn(first) - 1 < body.Left) first++;
        while (last > first && SampleColumn(last) + 1 >= body.Right) last--;
        var gray = new byte[body.Height * Columns];
        for (var x = 0; x < Columns; x++)
        {
            var column = first + x * (last - first) / (Columns - 1);
            for (var y = 0; y < body.Height; y++)
                gray[y * Columns + x] = Gray[(body.Y + y) * Columns + column];
        }
        return new FrameSample(body.Width, body.Height, gray);
    }

    private int SampleColumn(int col) =>
        Math.Clamp(4 + col * Math.Max(1, Width - 9) / (Columns - 1), 1, Width - 2);

    public FrameSample(Bitmap image)
    {
        Width = image.Width; Height = image.Height; Gray = new byte[Height * Columns];
        var data = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[Width * 4];
            for (var y = 0; y < Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                for (var col = 0; col < Columns; col++)
                {
                    var x = SampleColumn(col);
                    var sum = 0;
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var p = (x + dx) * 4;
                        sum += (row[p] * 29 + row[p + 1] * 150 + row[p + 2] * 77) >> 8;
                    }
                    Gray[y * Columns + col] = (byte)(sum / 3);
                }
            }
        }
        finally { image.UnlockBits(data); }
    }
}

internal static class FrameMatcher
{
    private const int NearbyOffsetRadius = 5;
    private const int VerificationBands = 3;
    private const double MinimumBandAdvantage = .1;
    private const double StrictErrorLimit = 5.5;
    private const int DetailContrast = 4;
    private const int EdgeContrast = 12;
    private const double MinimumEdgeDensity = .003;
    private const double MinimumEdgeAgreement = .70;
    private const double MinimumEdgeAdvantage = .20;
    private const double MaximumEdgeDensity = .15;
    private const int EdgeColumnInset = FrameSample.Columns / 12;

    private readonly record struct EdgeBand(int Samples, int Compared, int Aligned)
    {
        public double Agreement => (double)Aligned / Math.Max(1, Compared);
        public bool SupportsOffset => Samples > 0 && Aligned >= Samples * MinimumEdgeDensity && Agreement >= MinimumEdgeAgreement;
    }

    public static FrameMatch Match(FrameSample previous, FrameSample current, CancellationToken token = default)
        => Analyze(previous, current, token).Match;

    internal static int MinimumOverlap(int height) => Math.Max(40, height / 5);

    internal static bool IsVisuallyUnchanged(FrameSample previous, FrameSample current) =>
        previous.Width == current.Width && previous.Height == current.Height && Error(previous, current, 0, 64, 1) <= .65;

    internal static bool HasOverlapDetail(FrameSample previous, FrameSample current, int offset)
    {
        var overlap = current.Height - offset;
        return overlap > 1 && HasVerticalDetail(previous, offset, overlap) && HasVerticalDetail(current, 0, overlap);
    }

    internal static FrameAlignment Analyze(FrameSample previous, FrameSample current, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (previous.Width != current.Width || previous.Height != current.Height)
            return new(new(MatchKind.Uncertain), new(AlignmentStatus.SizeChanged));
        if (IsVisuallyUnchanged(previous, current))
            return HasOverlapDetail(previous, current, 0)
                ? new(new(MatchKind.Unchanged), new(AlignmentStatus.Unchanged))
                : new(new(MatchKind.Uncertain), new(AlignmentStatus.LowTexture));
        if (!Textured(previous) || !Textured(current))
            return new(new(MatchKind.Uncertain), new(AlignmentStatus.LowTexture));
        var forward = Find(previous, current, token, verifyAmbiguous: true);
        if (forward.Status == AlignmentStatus.Accepted && !HasOverlapDetail(previous, current, forward.Best!.Value.Offset))
            forward = forward with { Status = AlignmentStatus.LowTexture };
        if (forward.Status == AlignmentStatus.Accepted && forward.Verification is null)
            return new(new(MatchKind.Forward, forward.Best!.Value.Offset), forward);
        var reverse = Find(current, previous, token);
        if (reverse.Status == AlignmentStatus.Accepted)
            return new(new(MatchKind.Reverse), reverse with { Status = AlignmentStatus.Reverse });
        return forward.Status == AlignmentStatus.Accepted
            ? new(new(MatchKind.Forward, forward.Best!.Value.Offset), forward)
            : new(new(MatchKind.Uncertain), forward);
    }

    private static bool Textured(FrameSample frame)
    {
        double sum = 0, squares = 0;
        foreach (var pixel in frame.Gray) { sum += pixel; squares += pixel * pixel; }
        var count = frame.Gray.Length;
        return squares / count - Math.Pow(sum / count, 2) > 45;
    }

    private static bool HasVerticalDetail(FrameSample frame, int start, int height)
    {
        var required = (height - 1) * (FrameSample.Columns - EdgeColumnInset * 2) * MinimumEdgeDensity;
        var edges = 0;
        for (var y = start; y < start + height - 1; y++)
        for (var x = EdgeColumnInset; x < FrameSample.Columns - EdgeColumnInset; x++)
        {
            var pixel = y * FrameSample.Columns + x;
            if (Math.Abs(frame.Gray[pixel] - frame.Gray[pixel + FrameSample.Columns]) >= DetailContrast && ++edges >= required)
                return true;
        }
        return false;
    }

    private static AlignmentSearch Find(FrameSample previous, FrameSample current, CancellationToken token, bool verifyAmbiguous = false)
    {
        var maximumOffset = current.Height - MinimumOverlap(current.Height);
        var candidates = new List<AlignmentCandidate>();
        for (var offset = 1; offset <= maximumOffset; offset++)
        {
            if ((offset & 31) == 0) token.ThrowIfCancellationRequested();
            candidates.Add(new(offset, Error(previous, current, offset, 28, 3, true)));
        }
        var refined = candidates.OrderBy(c => c.Score).Take(32)
            .Select(c => new AlignmentCandidate(c.Offset, Error(previous, current, c.Offset, 160, 1, true)))
            .OrderBy(c => c.Score).ToArray();
        if (refined.Length == 0) return new(AlignmentStatus.NoCandidates);

        var best = refined[0];
        var runner = refined.FirstOrDefault(c => Math.Abs(c.Offset - best.Offset) > 2);
        var search = new AlignmentSearch(AlignmentStatus.Accepted, best, runner.Offset > 0 ? runner : null);
        if (best.Score > 8) return search with { Status = AlignmentStatus.HighError, Limit = 8 };

        var ambiguityLimit = best.Score * 1.6 + 1.5;
        if (runner.Offset > 0 && runner.Score <= ambiguityLimit)
        {
            var ambiguous = search with { Status = AlignmentStatus.Ambiguous, Limit = ambiguityLimit };
            if (!verifyAmbiguous || best.Score > StrictErrorLimit)
                return ambiguous;
            if (Math.Abs(runner.Offset - best.Offset) <= NearbyOffsetRadius)
                return VerifyNearby(previous, current, maximumOffset, best, ambiguous, ambiguityLimit, token);
            var verified = Error(previous, current, best.Offset, current.Height, 1, true);
            return VerifySparseEdges(previous, current, maximumOffset, best, ambiguous, verified, token);
        }
        if (best.Score > StrictErrorLimit)
        {
            // Small repaint/color differences can exceed the strict threshold even
            // at the correct offset. Accept these only with a much stronger unique
            // peak, pixel-accurate neighbors, and verification across every row.
            var repaintLimit = best.Score * 2 + 2;
            if (runner.Offset == 0 || runner.Score <= repaintLimit)
                return search with { Status = AlignmentStatus.RepaintAmbiguous, Limit = repaintLimit };
            var neighborLimit = best.Score * 1.35 + 1;
            foreach (var neighbor in new[] { best.Offset - 1, best.Offset + 1 })
            {
                if (neighbor <= 0 || neighbor > maximumOffset) continue;
                var neighborScore = Error(previous, current, neighbor, 160, 1, true);
                if (neighborScore <= neighborLimit)
                    return search with { Status = AlignmentStatus.BroadPeak, Rival = new(neighbor, neighborScore), Limit = neighborLimit };
            }
            token.ThrowIfCancellationRequested();
            var verifiedScore = Error(previous, current, best.Offset, current.Height, 1, true);
            if (verifiedScore > 8)
                return search with { Status = AlignmentStatus.VerificationFailed, Limit = 8, Verification = verifiedScore };
        }
        return search;
    }

    private static AlignmentSearch VerifyNearby(FrameSample previous, FrameSample current, int maximumOffset,
        AlignmentCandidate best, AlignmentSearch ambiguous, double ambiguityLimit, CancellationToken token)
    {
        var fullOverlap = current.Height - best.Offset;
        var verified = Error(previous, current, best.Offset, fullOverlap, 1, true);
        if (verified > StrictErrorLimit)
            return ambiguous with { Status = AlignmentStatus.VerificationFailed, Limit = StrictErrorLimit, Verification = verified };

        var first = Math.Max(1, best.Offset - NearbyOffsetRadius);
        var last = Math.Min(maximumOffset, best.Offset + NearbyOffsetRadius);
        var sharedOverlap = current.Height - last;
        Span<double> bestBandErrors = stackalloc double[VerificationBands];
        for (var band = 0; band < VerificationBands; band++)
        {
            var start = band * sharedOverlap / VerificationBands;
            var length = (band + 1) * sharedOverlap / VerificationBands - start;
            bestBandErrors[band] = Error(previous, current, best.Offset, length, 1, true, start, length);
        }

        // Use the same rows in both comparisons. Two bands must support the exact
        // offset, and a third band may be neutral, for example over blank space.
        for (var offset = first; offset <= last; offset++)
        {
            if (offset == best.Offset) continue;
            var supportingBands = 0;
            for (var band = 0; band < VerificationBands; band++)
            {
                var start = band * sharedOverlap / VerificationBands;
                var length = (band + 1) * sharedOverlap / VerificationBands - start;
                var advantage = Error(previous, current, offset, length, 1, true, start, length) - bestBandErrors[band];
                if (advantage <= -MinimumBandAdvantage)
                    return ambiguous with { Status = AlignmentStatus.NearbyUnverified, Verification = verified };
                if (advantage >= MinimumBandAdvantage) supportingBands++;
            }
            if (supportingBands < 2)
                return ambiguous with { Status = AlignmentStatus.NearbyUnverified, Verification = verified };
        }

        // A repeat farther away can be discarded by the coarse shortlist. Check
        // the remaining offsets at the same resolution as the original winner.
        var distantLimit = Math.Max(ambiguityLimit, verified * 1.6 + 1.5);
        for (var offset = 1; offset <= maximumOffset; offset++)
        {
            if ((offset & 31) == 0) token.ThrowIfCancellationRequested();
            if (offset >= first && offset <= last) continue;
            var score = Error(previous, current, offset, 160, 1, true);
            if (score <= distantLimit)
            {
                var conflict = ambiguous with { Rival = new(offset, score), Limit = distantLimit, Verification = verified };
                return VerifySparseEdges(previous, current, maximumOffset, best, conflict, verified, token, nearbyVerified: true);
            }
        }

        return ambiguous with { Status = AlignmentStatus.Accepted, Limit = null, Verification = verified };
    }

    private static AlignmentSearch VerifySparseEdges(FrameSample previous, FrameSample current, int maximumOffset,
        AlignmentCandidate best, AlignmentSearch ambiguous, double verified, CancellationToken token, bool nearbyVerified = false)
    {
        if (verified > StrictErrorLimit) return ambiguous;
        var overlap = current.Height - best.Offset;

        Span<EdgeBand> winner = stackalloc EdgeBand[VerificationBands];
        Span<EdgeBand> other = stackalloc EdgeBand[VerificationBands];
        MeasureEdges(previous, current, best.Offset, overlap, winner);
        var compared = 0;
        var aligned = 0;
        var supported = 0;
        foreach (var band in winner)
        {
            compared += band.Compared;
            aligned += band.Aligned;
            if (band.SupportsOffset) supported++;
        }
        var samples = (overlap - 1) * (FrameSample.Columns - EdgeColumnInset * 2);
        if (supported < 2 || compared > samples * MaximumEdgeDensity) return ambiguous;

        // Flat pixels contribute no evidence for a vertical displacement.
        // Require the same edge position and direction in multiple row bands.
        var limit = Math.Max(best.Score * 1.6 + 1.5, verified * 1.6 + 1.5);
        for (var offset = 1; offset <= maximumOffset; offset++)
        {
            if ((offset & 31) == 0) token.ThrowIfCancellationRequested();
            var distance = Math.Abs(offset - best.Offset);
            if (distance == 0 || nearbyVerified && distance <= NearbyOffsetRadius) continue;
            var score = Error(previous, current, offset, 160, 1, true);
            if (distance > 2 && score > limit) continue;
            var sharedOverlap = Math.Min(overlap, current.Height - offset);
            MeasureEdges(previous, current, best.Offset, sharedOverlap, winner);
            MeasureEdges(previous, current, offset, sharedOverlap, other);
            var wins = 0;
            for (var band = 0; band < VerificationBands; band++)
            {
                if (winner[band].SupportsOffset && winner[band].Agreement - other[band].Agreement >= MinimumEdgeAdvantage)
                    wins++;
            }
            if (wins < 2)
                return ambiguous with
                {
                    Status = distance <= 2 ? AlignmentStatus.NearbyUnverified : AlignmentStatus.Ambiguous,
                    Rival = new(offset, score),
                    Limit = distance <= 2 ? null : limit,
                    Verification = verified
                };
        }

        return ambiguous with { Status = AlignmentStatus.Accepted, Limit = null, Verification = verified, EdgeAgreement = (double)aligned / compared };
    }

    private static void MeasureEdges(FrameSample previous, FrameSample current, int offset, int overlap, Span<EdgeBand> bands)
    {
        for (var band = 0; band < bands.Length; band++)
        {
            var compared = 0;
            var aligned = 0;
            var start = band * (overlap - 1) / bands.Length;
            var end = (band + 1) * (overlap - 1) / bands.Length;
            for (var y = start; y < end; y++)
            for (var x = EdgeColumnInset; x < FrameSample.Columns - EdgeColumnInset; x++)
            {
                var a = (y + offset) * FrameSample.Columns + x;
                var b = y * FrameSample.Columns + x;
                var aEdge = previous.Gray[a + FrameSample.Columns] - previous.Gray[a];
                var bEdge = current.Gray[b + FrameSample.Columns] - current.Gray[b];
                var aStrong = Math.Abs(aEdge) >= EdgeContrast;
                var bStrong = Math.Abs(bEdge) >= EdgeContrast;
                if (aStrong || bStrong) compared++;
                if (aStrong && bStrong && (aEdge > 0) == (bEdge > 0)) aligned++;
            }
            bands[band] = new((end - start) * (FrameSample.Columns - EdgeColumnInset * 2), compared, aligned);
        }
    }

    private static double Error(FrameSample previous, FrameSample current, int offset, int rowCount, int columnStep,
        bool ignoreEdgeColumns = false, int startRow = 0, int? overlapLength = null)
    {
        var overlap = overlapLength ?? current.Height - offset;
        var rows = Math.Min(rowCount, overlap);
        long total = 0; var count = 0;
        Span<int> columnErrors = stackalloc int[FrameSample.Columns];
        columnErrors.Clear();
        for (var i = 0; i < rows; i++)
        {
            var y = startRow + (rows <= 1 ? 0 : i * (overlap - 1) / (rows - 1));
            for (var x = 0; x < FrameSample.Columns; x += columnStep)
            {
                var difference = Math.Abs(previous.Gray[(y + offset) * FrameSample.Columns + x] - current.Gray[y * FrameSample.Columns + x]);
                total += difference; columnErrors[x] += difference;
                count++;
            }
        }
        if (ignoreEdgeColumns)
        {
            // A scrollbar, native border or fixed side gutter can stay still while
            // the article moves. Ignore at most one sixth of sampled columns, and
            // only at the two outside edges. Interior content always participates.
            // Stationary/stability comparisons intentionally keep every column.
            var sampled = (FrameSample.Columns + columnStep - 1) / columnStep;
            var budget = sampled / 6;
            var left = 0; var right = (sampled - 1) * columnStep;
            for (var removed = 0; removed < budget; removed++)
            {
                var x = columnErrors[left] > columnErrors[right] ? left : right;
                total -= columnErrors[x]; count -= rows;
                if (x == left) left += columnStep; else right -= columnStep;
            }
        }
        return (double)total / Math.Max(1, count);
    }
}
