using System.Collections.Immutable;
using System.Numerics;

namespace PPPaint.Core;

public enum ScreenshotStackOrder { Front, Forward, Backward, Back }

public static class ScreenshotGeometry
{
    public static int HitHandle(IReadOnlyList<Vector2> points, Vector2 point, float radius)
    {
        var best = -1; var distance = radius * radius;
        for (var i = 0; i < points.Count; i++) { var d = Vector2.DistanceSquared(points[i], point); if (d <= distance) { distance = d; best = i; } }
        return best;
    }
    public static bool HitSegment(Vector2 point, Vector2 start, Vector2 end, float radius)
    {
        var line = end - start; var t = line.LengthSquared() < .001f ? 0 : Math.Clamp(Vector2.Dot(point - start, line) / line.LengthSquared(), 0, 1);
        return Vector2.Distance(point, start + line * t) <= radius;
    }
    public static (Vector2 Left, Vector2 Right) ArrowHead(Vector2 start, Vector2 end, float width)
    {
        var delta = end - start; if (delta.LengthSquared() <= 1) return (end, end);
        var direction = Vector2.Normalize(delta); var normal = new Vector2(-direction.Y, direction.X); var length = Math.Max(12, width * 4);
        return (end - direction * length + normal * length * .5f, end - direction * length - normal * length * .5f);
    }
    public static bool SwapsAxes(float rotation) => Math.Abs(rotation % 180) > 45 && Math.Abs(rotation % 180) < 135;
    public static Vector2[] Handles(ScreenshotBox b) => [new(b.X, b.Y), new(b.X + b.Width / 2, b.Y), new(b.Right, b.Y), new(b.Right, b.Y + b.Height / 2),
        new(b.Right, b.Bottom), new(b.X + b.Width / 2, b.Bottom), new(b.X, b.Bottom), new(b.X, b.Y + b.Height / 2)];
    public static ScreenshotBox Bounds(IEnumerable<ScreenshotAnnotation> items)
    {
        var list = items.ToArray(); if (list.Length == 0) return default;
        var x = list.Min(a => a.VisualBounds.X); var y = list.Min(a => a.VisualBounds.Y);
        return new(x, y, list.Max(a => a.VisualBounds.Right) - x, list.Max(a => a.VisualBounds.Bottom) - y);
    }
    public static Vector2[] CalloutOutline(ScreenshotBox b, Vector2 tip)
    {
        Vector2[] corners = [new(b.X, b.Y), new(b.Right, b.Y), new(b.Right, b.Bottom), new(b.X, b.Bottom)];
        if (b.Contains(tip)) return corners;
        var distances = new[] { (b.Y - tip.Y) / Math.Max(1, b.Height), (tip.X - b.Right) / Math.Max(1, b.Width), (tip.Y - b.Bottom) / Math.Max(1, b.Height), (b.X - tip.X) / Math.Max(1, b.Width) };
        var edge = Array.IndexOf(distances, distances.Max()); var first = corners[edge]; var last = corners[(edge + 1) % 4];
        var vector = last - first; var length = Math.Max(.01f, vector.Length()); var direction = vector / length;
        var half = Math.Min(12, length / 6); var along = Math.Clamp(Vector2.Dot(tip - first, direction), half, length - half);
        var polygon = new List<Vector2>();
        for (var i = 0; i < 4; i++) { polygon.Add(corners[i]); if (i == edge) { polygon.Add(first + direction * (along - half)); polygon.Add(tip); polygon.Add(first + direction * (along + half)); } }
        return polygon.ToArray();
    }
    public static Vector2 SnapDirection(Vector2 origin, Vector2 point)
    {
        var delta = point - origin; var angle = MathF.Round(MathF.Atan2(delta.Y, delta.X) / (MathF.PI / 4)) * (MathF.PI / 4);
        return origin + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * delta.Length();
    }
    public static ScreenshotAnnotation MoveEndpoint(ScreenshotAnnotation item, int endpoint, Vector2 point, bool snap = false)
    {
        if (item.Tool is not (ScreenshotTool.Arrow or ScreenshotTool.Line) || item.Points.Length != 2 || endpoint is < 0 or > 1) throw new ArgumentException("線の端点を指定してください。");
        var points = item.Points.SetItem(endpoint, snap ? SnapDirection(item.Points[1 - endpoint], point) : point);
        return item with { Points = points, Bounds = ScreenshotBox.Between(points[0], points[1]) };
    }
    public static ScreenshotBox Resize(ScreenshotBox original, int handle, Vector2 point, bool preserveRatio = false, bool fromCenter = false)
    {
        if (handle is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(handle));
        var center = new Vector2(original.X + original.Width / 2, original.Y + original.Height / 2);
        var anchor = fromCenter ? center : Handles(original)[(handle + 4) % 8];
        var horizontal = handle is not (1 or 5); var vertical = handle is not (3 or 7);
        var factor = fromCenter ? 2 : 1;
        var width = horizontal ? Math.Abs(point.X - anchor.X) * factor : original.Width;
        var height = vertical ? Math.Abs(point.Y - anchor.Y) * factor : original.Height;
        var ratio = Math.Max(.01f, original.Width) / Math.Max(.01f, original.Height);
        if (preserveRatio)
        {
            width = horizontal && vertical ? Math.Max(width, height * ratio) : horizontal ? width : height * ratio;
            width = Math.Max(width, Math.Max(2, 2 * ratio)); height = width / ratio;
        }
        else { width = Math.Max(2, width); height = Math.Max(2, height); }
        var x = fromCenter || !horizontal ? center.X - width / 2 : point.X < anchor.X || point.X == anchor.X && handle is 0 or 6 or 7 ? anchor.X - width : anchor.X;
        var y = fromCenter || !vertical ? center.Y - height / 2 : point.Y < anchor.Y || point.Y == anchor.Y && handle is 0 or 1 or 2 ? anchor.Y - height : anchor.Y;
        return new(x, y, width, height);
    }
    public static ImmutableArray<ScreenshotAnnotation> ResizeGroup(IEnumerable<ScreenshotAnnotation> items, ScreenshotBox original, ScreenshotBox target)
    {
        var sx = target.Width / Math.Max(1, original.Width); var sy = target.Height / Math.Max(1, original.Height);
        return items.Select(a => a.Resize(new(target.X + (a.Bounds.X - original.X) * sx, target.Y + (a.Bounds.Y - original.Y) * sy, a.Bounds.Width * sx, a.Bounds.Height * sy))).ToImmutableArray();
    }
}
