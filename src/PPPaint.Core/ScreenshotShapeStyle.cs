using System.Numerics;

namespace PPPaint.Core;

public enum ScreenshotArrowStyle { Line, DoubleEnded, Block, ThreeDimensional }
public enum ScreenshotNumberStyle { Circle, Square, Plain }
public sealed record ScreenshotShapeStyle(bool Filled = false, ScreenshotArrowStyle Arrow = ScreenshotArrowStyle.Line, ScreenshotNumberStyle Number = ScreenshotNumberStyle.Circle)
{
    public static bool Supports(ScreenshotTool tool) => tool is ScreenshotTool.Rectangle or ScreenshotTool.Ellipse or ScreenshotTool.Arrow or ScreenshotTool.Number or ScreenshotTool.Redact;
    public static ScreenshotShapeStyle Default(ScreenshotTool tool) => new(tool is ScreenshotTool.Number or ScreenshotTool.Redact);
    public static ScreenshotShapeStyle Read(ScreenshotAnnotation item) => new(item.IsFilled, item.ArrowStyle, item.NumberStyle);
    public ScreenshotAnnotation Apply(ScreenshotAnnotation item) => item with { Tool = item.Tool == ScreenshotTool.Redact ? ScreenshotTool.Rectangle : item.Tool, Filled = Filled, ArrowStyle = Arrow, NumberStyle = Number };
}

public static class ScreenshotArrowGeometry
{
    public static Vector2[] Block(Vector2 start, Vector2 end, float width)
    {
        var delta = end - start; var length = delta.Length();
        if (length < .01f) return [];
        var direction = delta / length; var normal = new Vector2(-direction.Y, direction.X);
        var head = Math.Min(length * .45f, Math.Max(12, width * 4));
        var shoulder = Math.Max(8, width * 2.5f); var shaft = Math.Max(3, width);
        var neck = end - direction * head;
        return [start + normal * shaft, neck + normal * shaft, neck + normal * shoulder, end,
            neck - normal * shoulder, neck - normal * shaft, start - normal * shaft];
    }
    public static Vector2 Depth(float width) => new(Math.Clamp(width * .8f + 2, 3, 8));
    public static bool HitHead(Vector2 tip, Vector2 left, Vector2 right, Vector2 point, float tolerance, bool filled) =>
        filled ? HitPolygon([tip, left, right], point, tolerance, true)
            : ScreenshotGeometry.HitSegment(point, tip, left, tolerance) || ScreenshotGeometry.HitSegment(point, tip, right, tolerance);
    public static bool HitPolygon(IReadOnlyList<Vector2> points, Vector2 point, float tolerance, bool filled)
    {
        var inside = false;
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i]; var b = points[(i + 1) % points.Count];
            if (ScreenshotGeometry.HitSegment(point, a, b, tolerance)) return true;
            if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return filled && inside;
    }
}
