using System.Drawing.Drawing2D;

namespace SnipLite;

internal static class AnnotationGeometry
{
    public static ToolKind Tool(Shape shape) => shape switch
    {
        RectShape => ToolKind.Rectangle,
        EllipseShape => ToolKind.Ellipse,
        ArrowShape => ToolKind.Arrow,
        PenShape => ToolKind.Pen,
        MarkerShape => ToolKind.Marker,
        MosaicShape => ToolKind.Mosaic,
        EraserShape => ToolKind.Eraser,
        TextShape => ToolKind.Text,
        _ => ToolKind.None
    };

    public static RectangleF Bounds(Shape shape, Graphics graphics) => shape switch
    {
        RectShape rectangle => rectangle.Rect,
        EllipseShape ellipse => ellipse.Rect,
        ArrowShape arrow => new Rectangle(Math.Min(arrow.Start.X, arrow.End.X), Math.Min(arrow.Start.Y, arrow.End.Y),
            Math.Max(1, Math.Abs(arrow.Start.X - arrow.End.X)), Math.Max(1, Math.Abs(arrow.Start.Y - arrow.End.Y))),
        PenShape pen => PointBounds(pen.Points),
        StrokeAreaShape { Rectangular: true } area => area.Rect,
        StrokeAreaShape brush => RectangleF.Inflate(PointBounds(brush.Points), brush.Width / 2, brush.Width / 2),
        TextShape text => text.Bounds(graphics),
        _ => RectangleF.Empty
    };

    private static Rectangle PointBounds(IReadOnlyList<Point> points)
    {
        if (points.Count == 0) return Rectangle.Empty;
        var left = points[0].X;
        var right = left;
        var top = points[0].Y;
        var bottom = top;
        foreach (var point in points)
        {
            left = Math.Min(left, point.X);
            right = Math.Max(right, point.X);
            top = Math.Min(top, point.Y);
            bottom = Math.Max(bottom, point.Y);
        }
        return new Rectangle(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    public static bool Hit(Shape shape, Point point, float tolerance)
    {
        using var path = new GraphicsPath();
        var filled = shape.Fill;
        switch (shape)
        {
            case RectShape rectangle:
                path.AddRectangle(rectangle.Rect);
                break;
            case EllipseShape ellipse:
                path.AddEllipse(ellipse.Rect);
                break;
            case ArrowShape arrow:
                if (arrow.HitHead(point, tolerance)) return true;
                path.AddLine(arrow.Start, arrow.End);
                break;
            case PenShape { Points.Count: > 1 } pen:
                path.AddLines(pen.Points.ToArray());
                break;
            case StrokeAreaShape { Rectangular: true } area:
                path.AddRectangle(area.Rect);
                filled = true;
                break;
            case StrokeAreaShape { Points.Count: > 1 } brush:
                path.AddLines(brush.Points.ToArray());
                break;
            case StrokeAreaShape { Points.Count: 1 } dot:
                var radius = dot.Width / 2 + tolerance;
                var dx = point.X - dot.Points[0].X;
                var dy = point.Y - dot.Points[0].Y;
                return dx * dx + dy * dy <= radius * radius;
            default:
                return false;
        }

        using var outline = new Pen(Color.Black, shape.Width + tolerance * 2)
        {
            LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round
        };
        return filled && path.IsVisible(point) || path.IsOutlineVisible(point, outline);
    }

    public static void Transform(Shape target, Shape original, RectangleF from, RectangleF to)
    {
        Point Map(Point point) => new(
            (int)MathF.Round(to.Left + (point.X - from.Left) * to.Width / from.Width),
            (int)MathF.Round(to.Top + (point.Y - from.Top) * to.Height / from.Height));
        Rectangle MapRectangle(Rectangle rectangle) =>
            RectShape.Normalize(Map(rectangle.Location), Map(new Point(rectangle.Right, rectangle.Bottom)));

        switch (target)
        {
            case RectShape rectangle:
                rectangle.Rect = MapRectangle(((RectShape)original).Rect);
                break;
            case EllipseShape ellipse:
                ellipse.Rect = MapRectangle(((EllipseShape)original).Rect);
                break;
            case ArrowShape arrow:
                var originalArrow = (ArrowShape)original;
                arrow.Start = Map(originalArrow.Start);
                arrow.End = Map(originalArrow.End);
                break;
            case PenShape pen:
                CopyPoints(pen.Points, ((PenShape)original).Points, Map);
                break;
            case StrokeAreaShape area:
                var originalArea = (StrokeAreaShape)original;
                area.Rect = MapRectangle(originalArea.Rect);
                CopyPoints(area.Points, originalArea.Points, Map);
                if (!area.Rectangular)
                    area.Width = Math.Max(1, originalArea.Width * Math.Min(to.Width / from.Width, to.Height / from.Height));
                break;
            case TextShape text:
                text.At = Map(((TextShape)original).At);
                break;
        }
    }

    private static void CopyPoints(List<Point> target, List<Point> source, Func<Point, Point> map)
    {
        for (var i = 0; i < source.Count; i++) target[i] = map(source[i]);
    }
}
