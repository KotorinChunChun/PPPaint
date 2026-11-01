using System.Collections.Immutable;
using System.Numerics;

namespace PPPaint.Core;

public sealed record ScreenshotTransfer(ScreenshotState Scene, Vector2 Origin)
{
    public ImmutableArray<ScreenshotAnnotation> ObjectsAt(Vector2 position)
    {
        var result = ImmutableArray.CreateBuilder<ScreenshotAnnotation>();
        var background = Scene.Frame.Over(Scene.Background);
        if (background.HasVisiblePixels()) result.Add(ScreenshotDocument.ImageObject(background, position, "貼り付けの背景"));
        result.AddRange(Scene.Annotations.Select(a => a.Move(position) with { Id = Guid.NewGuid().ToString("N") }));
        return result.ToImmutable();
    }
}

public static class ScreenshotEditing
{
    public static bool SameImage(ScreenshotState? left, ScreenshotState right) => left is not null && ReferenceEquals(left.Frame, right.Frame) && left.Background == right.Background && left.Annotations.SequenceEqual(right.Annotations);
    public static ScreenshotBox? Intersection(ScreenshotBox a, ScreenshotBox b)
    {
        var left = Math.Max(a.X, b.X); var top = Math.Max(a.Y, b.Y); var right = Math.Min(a.Right, b.Right); var bottom = Math.Min(a.Bottom, b.Bottom);
        return right > left && bottom > top ? new(left, top, right - left, bottom - top) : null;
    }
    public static ScreenshotBox DrawingBounds(ScreenshotAnnotation item)
    {
        if (item.Tool == ScreenshotTool.Eraser && !item.Points.IsDefaultOrEmpty)
            return ScreenshotEraserGeometry.Bounds(item.Points, item.Width);
        if (item.Tool == ScreenshotTool.Pen && item.Width == 1 && !item.Points.IsDefaultOrEmpty)
        {
            var left = MathF.Floor(item.Points.Min(p => p.X)); var top = MathF.Floor(item.Points.Min(p => p.Y));
            return new(left, top, MathF.Floor(item.Points.Max(p => p.X)) + 1 - left, MathF.Floor(item.Points.Max(p => p.Y)) + 1 - top);
        }
        var b = item.VisualBounds;
        var pad = item.Tool == ScreenshotTool.Image ? 0 : item.Tool == ScreenshotTool.Arrow ? Math.Max(12, item.Width * 4) : item.Width / 2;
        return new(b.X - pad, b.Y - pad, b.Width + 2 * pad, b.Height + 2 * pad);
    }
    public static ScreenshotTransfer Capture(ScreenshotState state, PixelRect area, IReadOnlySet<string>? objectsOnly = null)
    {
        Validate(state, area);
        var box = new ScreenshotBox(area.X, area.Y, area.Width, area.Height); var origin = new Vector2(area.X, area.Y);
        var objects = ImmutableArray.CreateBuilder<ScreenshotAnnotation>();
        foreach (var item in state.Annotations)
        {
            if (objectsOnly is not null && !objectsOnly.Contains(item.Id) || !DrawingBounds(item).Intersects(box)) continue;
            ScreenshotBox? clip;
            if (item.ClipBounds is { } previous) { clip = Intersection(previous, box); if (clip is null) continue; }
            else clip = box.Contains(DrawingBounds(item)) ? null : box;
            objects.Add((item with { ClipBounds = clip }).Move(-origin));
        }
        return new(new(objectsOnly is null ? state.Frame.Crop(area.X, area.Y, area.Width, area.Height) : ScreenshotFrame.Transparent(area.Width, area.Height),
            objects.ToImmutable(), state.NextNumber, objectsOnly is null ? state.Background : Rgba32.Transparent), origin);
    }
    public static ScreenshotState RemoveRange(ScreenshotState state, PixelRect area)
    {
        Validate(state, area);
        if (area.X == 0 && area.Y == 0 && area.Width == state.Frame.Width && area.Height == state.Frame.Height)
            return state with { Frame = ScreenshotFrame.Transparent(state.Frame.Width, state.Frame.Height), Annotations = [] };
        var box = new ScreenshotBox(area.X, area.Y, area.Width, area.Height); var objects = ImmutableArray.CreateBuilder<ScreenshotAnnotation>();
        foreach (var item in state.Annotations)
        {
            var visible = item.ClipBounds is { } clip ? Intersection(DrawingBounds(item), clip) : DrawingBounds(item);
            if (visible is { } bounds && box.Contains(bounds)) continue;
            objects.Add(visible is { } b && box.Intersects(b) ? item with { Cutouts = (item.Cutouts.IsDefault ? [] : item.Cutouts).Add(box) } : item);
        }
        return state with { Frame = state.Frame.Clear(area), Annotations = objects.ToImmutable() };
    }
    // Freehand strokes ignore sub-half-pixel jitter; bounds always cover every point.
    public static ScreenshotAnnotation AppendStrokePoint(ScreenshotAnnotation stroke, Vector2 point)
    {
        if (!stroke.Points.IsDefaultOrEmpty && Vector2.Distance(stroke.Points[^1], point) < .5f) return stroke;
        var points = stroke.Points.IsDefault ? ImmutableArray.Create(point) : stroke.Points.Add(point);
        var left = points.Min(p => p.X); var top = points.Min(p => p.Y);
        return stroke with { Points = points, Bounds = new(left, top, Math.Max(1, points.Max(p => p.X) - left), Math.Max(1, points.Max(p => p.Y) - top)) };
    }
    // Text and watermark boxes grow to fit the typed lines but never past the canvas' right edge.
    public static ScreenshotBox FitTextBounds(ScreenshotAnnotation item, string text, int canvasWidth)
    {
        var bounds = item.Bounds;
        if (item.Tool is not (ScreenshotTool.Text or ScreenshotTool.Watermark)) return bounds;
        var lines = text.Replace("\r", "").Split('\n');
        var width = Math.Min(Math.Max(bounds.Width, lines.Max(line => line.Length) * item.FontSize), Math.Max(32, canvasWidth - bounds.X));
        var rows = lines.Sum(line => Math.Max(1, (int)Math.Ceiling(line.Length * item.FontSize / width)));
        return bounds with { Width = width, Height = Math.Max(bounds.Height, rows * item.FontSize * 1.6f) };
    }
    private static void Validate(ScreenshotState state, PixelRect area)
    {
        if (area.X < 0 || area.Y < 0 || area.Width < 1 || area.Height < 1 || area.Right > state.Frame.Width || area.Bottom > state.Frame.Height) throw new ArgumentOutOfRangeException(nameof(area));
    }
}

public sealed class ScreenshotNumberPlacement
{
    private readonly ScreenshotState _initial;
    private readonly Vector2 _press;
    private readonly ScreenshotAnnotation _original;
    private bool _finished;
    public ScreenshotAnnotation Preview { get; private set; }
    public ScreenshotNumberPlacement(ScreenshotState state, Vector2 point, float fontSize, Rgba32 color)
    {
        if (state.NextNumber == int.MaxValue) throw new InvalidOperationException("開始番号を小さくしてください。");
        _initial = state; _press = point;
        var diameter = fontSize * 1.8f;
        Preview = _original = new(Guid.NewGuid().ToString("N"), ScreenshotTool.Number, new(point.X - diameter / 2, point.Y - diameter / 2, diameter, diameter), color, FontSize: fontSize, Text: state.NextNumber.ToString());
    }
    public void Move(Vector2 point) { if (!_finished) Preview = _original.Move(point - _press); }
    public string Commit(ScreenshotDocument document)
    {
        if (_finished || !ReferenceEquals(_initial, document.State)) throw new InvalidOperationException("番号の配置を確定できません。");
        document.Add(Preview); _finished = true; return Preview.Id;
    }
    public void Cancel() => _finished = true;
}

public static class ScreenshotZoom
{
    public static double Wheel(double percent, int delta) => Math.Clamp(Math.Round(percent * Math.Pow(1.1, delta / 120d)), 10, 400);
}
