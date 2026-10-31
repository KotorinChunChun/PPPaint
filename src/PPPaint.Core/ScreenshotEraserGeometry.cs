using System.Numerics;

namespace PPPaint.Core;

public static class ScreenshotEraserGeometry
{
    // Pixel coordinates are areas, not circle centers. A width of one always
    // targets the pixel containing the pointer, including fractional positions.
    public static ScreenshotBox Footprint(Vector2 point, float size)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(size) || size <= 0)
            throw new ArgumentOutOfRangeException(nameof(size));
        var side = (int)Math.Clamp(MathF.Round(size, MidpointRounding.AwayFromZero), 1, 100);
        return new(MathF.Floor(point.X) - side / 2, MathF.Floor(point.Y) - side / 2, side, side);
    }

    public static ScreenshotBox Bounds(IEnumerable<Vector2> points, float size)
    {
        using var iterator = points.GetEnumerator();
        if (!iterator.MoveNext()) throw new ArgumentException("消去位置がありません。", nameof(points));
        var box = Footprint(iterator.Current, size);
        var left = box.X; var top = box.Y; var right = box.Right; var bottom = box.Bottom;
        while (iterator.MoveNext())
        {
            box = Footprint(iterator.Current, size);
            left = Math.Min(left, box.X); top = Math.Min(top, box.Y); right = Math.Max(right, box.Right); bottom = Math.Max(bottom, box.Bottom);
        }
        return new(left, top, right - left, bottom - top);
    }

    // The hull of the endpoint squares is the exact sweep of an axis-aligned
    // square brush. Unlike square line caps, the brush does not rotate diagonally.
    public static Vector2[] Sweep(Vector2 from, Vector2 to, float size)
    {
        static IEnumerable<Vector2> Corners(ScreenshotBox box) => [new(box.X, box.Y), new(box.Right, box.Y), new(box.Right, box.Bottom), new(box.X, box.Bottom)];
        var points = Corners(Footprint(from, size)).Concat(Corners(Footprint(to, size))).Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
        static float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        var hull = new List<Vector2>();
        foreach (var point in points)
        {
            while (hull.Count >= 2 && Cross(hull[^2], hull[^1], point) <= 0) hull.RemoveAt(hull.Count - 1);
            hull.Add(point);
        }
        var lowerCount = hull.Count;
        for (var index = points.Length - 2; index >= 0; index--)
        {
            var point = points[index];
            while (hull.Count > lowerCount && Cross(hull[^2], hull[^1], point) <= 0) hull.RemoveAt(hull.Count - 1);
            hull.Add(point);
        }
        hull.RemoveAt(hull.Count - 1);
        return hull.ToArray();
    }
}
