using System.Collections.Immutable;
using System.Numerics;

namespace PPPaint.Core;

public enum ScreenshotStackOrder { Front, Forward, Backward, Back }

public static class ScreenshotGeometry
{
    public static bool SwapsAxes(float rotation) => Math.Abs(rotation % 180) > 45 && Math.Abs(rotation % 180) < 135;
    public static Vector2[] Handles(ScreenshotBox b) => [new(b.X, b.Y), new(b.X + b.Width / 2, b.Y), new(b.Right, b.Y), new(b.Right, b.Y + b.Height / 2),
        new(b.Right, b.Bottom), new(b.X + b.Width / 2, b.Bottom), new(b.X, b.Bottom), new(b.X, b.Y + b.Height / 2)];
    public static ScreenshotBox Bounds(IEnumerable<ScreenshotAnnotation> items)
    {
        var list = items.ToArray(); if (list.Length == 0) return default;
        var x = list.Min(a => a.Bounds.X); var y = list.Min(a => a.Bounds.Y);
        return new(x, y, list.Max(a => a.Bounds.Right) - x, list.Max(a => a.Bounds.Bottom) - y);
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
    public static ScreenshotBox Resize(ScreenshotBox original, int handle, Vector2 point, bool preserveRatio = false)
    {
        if (handle is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(handle));
        var left = original.X; var right = original.Right; var top = original.Y; var bottom = original.Bottom;
        if (handle is 0 or 6 or 7) left = point.X;
        if (handle is 2 or 3 or 4) right = point.X;
        if (handle is 0 or 1 or 2) top = point.Y;
        if (handle is 4 or 5 or 6) bottom = point.Y;
        if (preserveRatio && handle % 2 == 0)
        {
            var anchor = Handles(original)[(handle + 4) % 8]; var delta = point - anchor;
            var ratio = Math.Max(.01f, original.Width) / Math.Max(.01f, original.Height);
            var width = Math.Max(Math.Abs(delta.X), Math.Abs(delta.Y) * ratio); var height = width / ratio;
            point = anchor + new Vector2(MathF.CopySign(width, delta.X), MathF.CopySign(height, delta.Y));
            return ScreenshotBox.Between(anchor, point) with { Width = Math.Max(2, width), Height = Math.Max(2, height) };
        }
        return new(Math.Min(left, right), Math.Min(top, bottom), Math.Max(2, Math.Abs(right - left)), Math.Max(2, Math.Abs(bottom - top)));
    }
    public static ImmutableArray<ScreenshotAnnotation> ResizeGroup(IEnumerable<ScreenshotAnnotation> items, ScreenshotBox original, ScreenshotBox target)
    {
        var sx = target.Width / Math.Max(1, original.Width); var sy = target.Height / Math.Max(1, original.Height);
        return items.Select(a => a.Resize(new(target.X + (a.Bounds.X - original.X) * sx, target.Y + (a.Bounds.Y - original.Y) * sy, a.Bounds.Width * sx, a.Bounds.Height * sy))).ToImmutableArray();
    }
}
