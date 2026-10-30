using System.Numerics;

namespace PPPaint.Core;

public static class ScreenshotHandleGeometry
{
    public const float SquareSize = 10;
    public const float RoundRadius = 6;
    public static Vector2 DragPoint(Vector2 handleCenter, Vector2 pressed, Vector2 current) => handleCenter + current - pressed;
    public static ScreenshotBox Square(Vector2 center, float zoom)
    {
        ValidateZoom(zoom); var size = SquareSize / zoom;
        return new(center.X - size / 2, center.Y - size / 2, size, size);
    }
    public static bool Contains(Vector2 center, Vector2 point, float zoom, bool round)
    {
        ValidateZoom(zoom);
        return round ? Vector2.DistanceSquared(point, center) <= MathF.Pow(RoundRadius / zoom, 2) : Square(center, zoom).Contains(point);
    }
    public static int Hit(IReadOnlyList<Vector2> centers, Vector2 point, float zoom, bool round = false)
    {
        ValidateZoom(zoom); var best = -1; var distance = float.PositiveInfinity;
        for (var i = 0; i < centers.Count; i++)
        {
            var next = Vector2.DistanceSquared(point, centers[i]);
            if (next <= distance && Contains(centers[i], point, zoom, round)) { best = i; distance = next; }
        }
        return best;
    }
    private static void ValidateZoom(float zoom)
    {
        if (!float.IsFinite(zoom) || zoom <= 0) throw new ArgumentOutOfRangeException(nameof(zoom));
    }
}
