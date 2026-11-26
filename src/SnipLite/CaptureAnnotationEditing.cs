using System.Drawing.Drawing2D;

namespace SnipLite;

internal sealed partial class CaptureForm
{
    private sealed record AnnotationDrag(Shape Shape, Shape Original, RectangleF Bounds, Point Start,
        int Handle, bool Duplicate, List<Shape>? Before = null);

    private static Shape? _copiedAnnotation;
    private Shape? _selectedAnnotation;
    private AnnotationDrag? _annotationDrag;
    private Shape? _draggedCopy;

    private Shape? SelectedAnnotation => _bar.Tool == ToolKind.Text ? _activeText : _selectedAnnotation;
    private bool CanConstrainAnnotationResize => _annotationDrag is { Handle: >= 0, Shape: RectShape or EllipseShape };

    private Shape? HitAnnotation(Point point)
    {
        if (!_selection.Contains(point) || _bar.Tool is ToolKind.None or ToolKind.Text) return null;
        for (var i = _shapes.Count - 1; i >= 0; i--)
        {
            var shape = _shapes[i];
            if (AnnotationGeometry.Tool(shape) == _bar.Tool && AnnotationGeometry.Hit(shape, point, 4 * Ui))
                return shape;
        }
        return null;
    }

    private Point[] AnnotationHandles(Shape shape, Graphics graphics) => shape is ArrowShape arrow
        ? [arrow.Start, arrow.End]
        : Anchors(Rectangle.Round(AnnotationGeometry.Bounds(shape, graphics))).ToArray();

    private Rectangle AnnotationHandleBounds(Point point)
    {
        var size = Math.Max(6, (int)Math.Round(8 * Ui));
        return new Rectangle(point.X - size / 2, point.Y - size / 2, size, size);
    }

    private int HitAnnotationHandle(Shape shape, Graphics graphics, Point point)
    {
        var handles = AnnotationHandles(shape, graphics);
        var tolerance = Math.Max(1, (int)Math.Round(2 * Ui));
        var nearest = -1;
        var distance = double.MaxValue;
        for (var i = 0; i < handles.Length; i++)
        {
            if (!Rectangle.Inflate(AnnotationHandleBounds(handles[i]), tolerance, tolerance).Contains(point)) continue;
            var dx = (double)handles[i].X - point.X;
            var dy = (double)handles[i].Y - point.Y;
            var squared = dx * dx + dy * dy;
            if (squared >= distance) continue;
            distance = squared;
            nearest = i;
        }
        return nearest;
    }

    private void DrawAnnotationSelection(Graphics graphics)
    {
        if (!_hasSelection || _selectedAnnotation is not { } shape || _ocrMode || _recognitionBusy) return;
        var bounds = AnnotationGeometry.Bounds(shape, graphics);
        var frame = RectangleF.Inflate(bounds, Ui * 3, Ui * 3);
        using var border = new Pen(_settings.Accent, Math.Max(1, Ui)) { DashStyle = DashStyle.Dash };
        graphics.DrawRectangle(border, frame.X, frame.Y, frame.Width, frame.Height);
        border.DashStyle = DashStyle.Solid;
        using var fill = new SolidBrush(AppTheme.Current.Card);
        foreach (var point in AnnotationHandles(shape, graphics))
        {
            var handle = AnnotationHandleBounds(point);
            graphics.FillRectangle(fill, handle);
            graphics.DrawRectangle(border, handle);
        }
    }

    private bool BeginAnnotationDrag(Point point, bool clone)
    {
        if (_bar.Tool == ToolKind.Text)
        {
            if (!clone || HitText(point) is not { } text) return false;
            SelectAnnotation(text);
            StartAnnotationDrag(text, point, -1, true);
            return true;
        }

        if (_selectedAnnotation is { } selected)
        {
            using var graphics = CreateGraphics();
            var handle = HitAnnotationHandle(selected, graphics, point);
            if (!clone && handle >= 0)
            {
                StartAnnotationDrag(selected, point, handle, false);
                return true;
            }
        }

        var hit = HitAnnotation(point);
        if (hit is null && _selectedAnnotation is { } current && _selection.Contains(point))
        {
            using var graphics = CreateGraphics();
            if (RectangleF.Inflate(AnnotationGeometry.Bounds(current, graphics), 3 * Ui, 3 * Ui).Contains(point))
                hit = current;
        }
        SelectAnnotation(hit);
        if (hit is null) return false;
        StartAnnotationDrag(hit, point, -1, clone);
        return true;
    }

    private void StartAnnotationDrag(Shape shape, Point point, int handle, bool clone)
    {
        using var graphics = CreateGraphics();
        _annotationDrag = new AnnotationDrag(shape, shape.Clone(), AnnotationGeometry.Bounds(shape, graphics),
            point, handle, clone);
        Capture = true;
        UpdateCursor();
        Invalidate();
    }

    private void DragAnnotation(Point point, bool constrain)
    {
        var drag = _annotationDrag!;
        var dx = point.X - drag.Start.X;
        var dy = point.Y - drag.Start.Y;
        if (drag.Before is null)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) < Math.Max(2, 2 * Ui)) return;
            _annotationDrag = drag = drag with { Before = Snapshot() };
        }
        var shape = drag.Shape;
        if (drag.Duplicate)
        {
            if (_draggedCopy is null)
            {
                _draggedCopy = drag.Original.Clone();
                _shapes.Add(_draggedCopy);
                SelectAnnotation(_draggedCopy);
            }
            shape = _draggedCopy;
        }
        var bounds = drag.Bounds;
        if (drag.Handle < 0)
        {
            var location = AnnotationLocation(bounds, dx, dy);
            bounds.Location = location;
            AnnotationGeometry.Transform(shape, drag.Original, drag.Bounds, bounds);
        }
        else if (shape is ArrowShape arrow)
        {
            var original = (ArrowShape)drag.Original;
            var endpoint = drag.Handle == 0 ? original.Start : original.End;
            endpoint = new Point(Math.Clamp(endpoint.X + dx, _selection.Left, _selection.Right),
                Math.Clamp(endpoint.Y + dy, _selection.Top, _selection.Bottom));
            var other = drag.Handle == 0 ? original.End : original.Start;
            if (Math.Max(Math.Abs(endpoint.X - other.X), Math.Abs(endpoint.Y - other.Y)) >= 3)
            {
                if (drag.Handle == 0) arrow.Start = endpoint;
                else arrow.End = endpoint;
            }
        }
        else
        {
            var resized = ResizeAnnotation(bounds, drag.Handle, dx, dy, constrain && CanConstrainAnnotationResize);
            AnnotationGeometry.Transform(shape, drag.Original, bounds, resized);
        }
        InvalidateAnnotations();
    }

    private RectangleF ResizeAnnotation(RectangleF bounds, int handle, int dx, int dy, bool constrain)
    {
        var movesLeft = handle is 0 or 3 or 5;
        var movesRight = handle is 2 or 4 or 7;
        var movesTop = handle is 0 or 1 or 2;
        var movesBottom = handle is 5 or 6 or 7;
        var limits = RectangleF.Union(_selection, bounds);
        var minWidth = Math.Min(2, bounds.Width);
        var minHeight = Math.Min(2, bounds.Height);
        var left = movesLeft ? Math.Clamp(bounds.Left + dx, limits.Left, bounds.Right - minWidth) : bounds.Left;
        var right = movesRight ? Math.Clamp(bounds.Right + dx, bounds.Left + minWidth, limits.Right) : bounds.Right;
        var top = movesTop ? Math.Clamp(bounds.Top + dy, limits.Top, bounds.Bottom - minHeight) : bounds.Top;
        var bottom = movesBottom ? Math.Clamp(bounds.Bottom + dy, bounds.Top + minHeight, limits.Bottom) : bounds.Bottom;
        if (!constrain) return RectangleF.FromLTRB(left, top, right, bottom);

        var horizontal = movesLeft || movesRight;
        var vertical = movesTop || movesBottom;
        var centerX = bounds.Left + bounds.Width / 2;
        var centerY = bounds.Top + bounds.Height / 2;
        var widthLimit = horizontal
            ? movesLeft ? right - limits.Left : limits.Right - left
            : 2 * Math.Min(centerX - limits.Left, limits.Right - centerX);
        var heightLimit = vertical
            ? movesTop ? bottom - limits.Top : limits.Bottom - top
            : 2 * Math.Min(centerY - limits.Top, limits.Bottom - centerY);
        var side = Math.Min(Math.Max(horizontal ? right - left : 0, vertical ? bottom - top : 0), Math.Min(widthLimit, heightLimit));

        // Corners pivot around the opposite corner. Edge handles keep the other axis centered.
        var squareLeft = horizontal ? (movesLeft ? right - side : left) : MathF.Floor(centerX - side / 2);
        var squareTop = vertical ? (movesTop ? bottom - side : top) : MathF.Floor(centerY - side / 2);
        return new RectangleF(squareLeft, squareTop, side, side);
    }

    private PointF AnnotationLocation(RectangleF bounds, int dx, int dy) => new(
        Math.Clamp(bounds.X + dx, Math.Min(bounds.X, _selection.Left), Math.Max(bounds.X, _selection.Right - bounds.Width)),
        Math.Clamp(bounds.Y + dy, Math.Min(bounds.Y, _selection.Top), Math.Max(bounds.Y, _selection.Bottom - bounds.Height)));

    private void FinishAnnotationDrag(bool cancel)
    {
        if (_annotationDrag is not { } drag) return;
        _annotationDrag = null;
        _draggedCopy = null;
        if (drag.Before is { } before && cancel)
        {
            var index = _shapes.IndexOf(drag.Shape);
            RestoreShapes(before);
            if (index >= 0) SelectAnnotation(_shapes[index]);
        }
        else if (drag.Before is { } undo)
        {
            ClearQrResult();
            _undo.Push(undo);
            _redo.Clear();
            SelectAnnotation(SelectedAnnotation);
        }
        Capture = false;
        UpdateCursor();
        InvalidateAnnotations();
    }

    private void SelectAnnotation(Shape? shape)
    {
        _selectedAnnotation = shape is TextShape ? null : shape;
        _activeText = shape as TextShape;
        if (shape is TextShape text) LoadTextStyle(text);
        else if (shape is not null)
        {
            _bar.Color = shape.Color;
            _bar.Fill = shape.Fill;
            if (shape is MarkerShape marker)
            {
                _bar.MarkerWidth = shape.Width / Ui;
                _bar.MarkerOpacity = marker.Opacity;
                _bar.MarkerRectangle = marker.Rectangular;
            }
            else if (shape is StrokeAreaShape area)
            {
                _bar.BrushWidth = shape.Width / Ui;
                if (area is MosaicShape mosaic)
                {
                    _bar.MosaicBlock = mosaic.Block;
                    _bar.MosaicRectangle = area.Rectangular;
                }
                else _bar.EraserRectangle = area.Rectangular;
            }
            else
            {
                _bar.StrokeWidth = shape.Width / Ui;
                if (shape is ArrowShape arrow) _bar.ArrowStyle = arrow.Style;
            }
        }
        Invalidate();
    }

    private bool HandleAnnotationShortcut(Keys keys)
    {
        if (!_hasSelection || _recognitionBusy || _ocrMode || _ime is not null || _sizeEditorOpen) return false;
        if (_annotationDrag is not null)
        {
            if ((keys & Keys.KeyCode) == Keys.ShiftKey && CanConstrainAnnotationResize) return false;
            if ((keys & ~Keys.Shift) == Keys.Escape) FinishAnnotationDrag(cancel: true);
            return true;
        }
        if (_dragMode != 0 || _movingText || _resizingText || _slider is not null) return false;
        var selected = SelectedAnnotation;
        if (keys == (Keys.Control | Keys.Shift | Keys.C) && selected is not null)
        {
            _copiedAnnotation = selected.Clone();
            return true;
        }
        if (keys == (Keys.Control | Keys.Shift | Keys.V) && _copiedAnnotation is { } copied)
        {
            using var graphics = CreateGraphics();
            var bounds = AnnotationGeometry.Bounds(copied, graphics);
            var offset = (int)Math.Round(12 * Ui);
            var source = SelectedAnnotation ?? copied;
            var reference = AnnotationGeometry.Bounds(source, graphics);
            var target = new RectangleF(
                Math.Clamp(reference.X + offset, _selection.Left, Math.Max(_selection.Left, _selection.Right - bounds.Width)),
                Math.Clamp(reference.Y + offset, _selection.Top, Math.Max(_selection.Top, _selection.Bottom - bounds.Height)),
                bounds.Width, bounds.Height);
            var shape = copied.Clone();
            AnnotationGeometry.Transform(shape, copied, bounds, target);
            SaveUndo();
            _shapes.Add(shape);
            ActivateTool(AnnotationGeometry.Tool(shape));
            SelectAnnotation(shape);
            InvalidateAnnotations();
            return true;
        }
        if (keys == Keys.Delete && selected is not null)
        {
            SaveUndo();
            _shapes.Remove(selected);
            SelectAnnotation(null);
            InvalidateAnnotations();
            return true;
        }
        return false;
    }

    private bool SetAnnotationCursor(Point point)
    {
        if (_selectedAnnotation is { } shape)
        {
            using var graphics = CreateGraphics();
            var handle = HitAnnotationHandle(shape, graphics, point);
            if (handle >= 0)
            {
                Cursor = shape is ArrowShape ? Cursors.Cross : handle switch
                {
                    0 or 7 => Cursors.SizeNWSE,
                    1 or 6 => Cursors.SizeNS,
                    2 or 5 => Cursors.SizeNESW,
                    _ => Cursors.SizeWE
                };
                return true;
            }
            if (_selection.Contains(point) && RectangleF.Inflate(AnnotationGeometry.Bounds(shape, graphics), 3 * Ui, 3 * Ui).Contains(point))
            {
                Cursor = Cursors.SizeAll;
                return true;
            }
        }
        if (HitAnnotation(point) is null) return false;
        Cursor = Cursors.SizeAll;
        return true;
    }

    private void ChangeAnnotationStyle<T>(Func<T, bool> needsChange, Action<T> change) where T : Shape
    {
        if (_selectedAnnotation is not T shape || !needsChange(shape)) return;
        SaveUndo();
        change(shape);
        InvalidateAnnotations();
    }

    private void CommitAnnotationSlider(string slider)
    {
        if (slider == "opacity")
            ChangeAnnotationStyle<MarkerShape>(shape => shape.Opacity != _bar.MarkerOpacity, shape => shape.Opacity = _bar.MarkerOpacity);
        else if (slider == "strength")
            ChangeAnnotationStyle<MosaicShape>(shape => shape.Block != _bar.MosaicBlock, shape => shape.Block = _bar.MosaicBlock);
    }
}
