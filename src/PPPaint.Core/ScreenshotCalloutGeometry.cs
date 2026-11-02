using System.Numerics;

namespace PPPaint.Core;

public readonly record struct ScreenshotCalloutControls(int Edge, float Start, float End, Vector2 First, Vector2 Second, Vector2 Tip);

public static class ScreenshotCalloutGeometry
{
    private static Vector2[] Corners(ScreenshotBox b) => [new(b.X, b.Y), new(b.Right, b.Y), new(b.Right, b.Bottom), new(b.X, b.Bottom)];
    public static ScreenshotCalloutControls Controls(ScreenshotAnnotation item)
    {
        var b = item.Bounds; var tip = item.Tail; var corners = Corners(b);
        var edge = item.CalloutEdge;
        float start, end;
        if (edge is < 0 or > 3)
        {
            var distances = new[] { (b.Y - tip.Y) / Math.Max(1, b.Height), (tip.X - b.Right) / Math.Max(1, b.Width), (tip.Y - b.Bottom) / Math.Max(1, b.Height), (b.X - tip.X) / Math.Max(1, b.Width) };
            edge = Array.IndexOf(distances, distances.Max());
            var vector = corners[(edge + 1) % 4] - corners[edge]; var length = Math.Max(.01f, vector.Length());
            var half = Math.Clamp(Math.Min(12, length / 6) / length, .02f, 1f / 6);
            var along = Math.Clamp(Vector2.Dot(tip - corners[edge], vector / length) / length, half, 1 - half);
            start = along - half; end = along + half;
        }
        else { start = Math.Clamp(item.CalloutStart, 0, .96f); end = Math.Clamp(item.CalloutEnd, start + .04f, 1); }
        return new(edge, start, end, Vector2.Lerp(corners[edge], corners[(edge + 1) % 4], start), Vector2.Lerp(corners[edge], corners[(edge + 1) % 4], end), tip);
    }
    public static Vector2[] Points(ScreenshotAnnotation item)
    {
        var control = Controls(item); return [control.Tip, control.First, control.Second];
    }
    public static ScreenshotAnnotation MoveControl(ScreenshotAnnotation item, int index, Vector2 point)
    {
        if (item.Tool != ScreenshotTool.Callout || index is < 0 or > 2) throw new ArgumentException("吹き出しの調整点を指定してください。");
        if (index == 0) return item with { CalloutTip = point };
        var corners = Corners(item.Bounds); var original = Controls(item); var edge = 0; var t = 0f; var nearest = float.PositiveInfinity;
        for (var i = 0; i < 4; i++)
        {
            var vector = corners[(i + 1) % 4] - corners[i]; var along = Math.Clamp(Vector2.Dot(point - corners[i], vector) / Math.Max(.01f, vector.LengthSquared()), 0, 1);
            var distance = Vector2.DistanceSquared(point, corners[i] + vector * along);
            if (distance < nearest) { nearest = distance; edge = i; t = along; }
        }
        var start = original.Start; var end = original.End;
        if (edge != original.Edge)
        {
            var span = Math.Clamp(end - start, .08f, .9f);
            if (index == 1) { start = Math.Clamp(t, 0, 1 - span); end = start + span; }
            else { end = Math.Clamp(t, span, 1); start = end - span; }
        }
        else if (index == 1) start = Math.Clamp(t, 0, end - .04f);
        else end = Math.Clamp(t, start + .04f, 1);
        return item with { CalloutEdge = edge, CalloutStart = start, CalloutEnd = end, CalloutTip = item.Tail };
    }
    public static int TransformedEdge(int edge, ScreenshotOrientationChange change) => edge < 0 ? -1 : change switch
    {
        ScreenshotOrientationChange.RotateClockwise => (edge + 1) % 4,
        ScreenshotOrientationChange.FlipHorizontal => (4 - edge) % 4,
        _ => (2 - edge + 4) % 4
    };
}
