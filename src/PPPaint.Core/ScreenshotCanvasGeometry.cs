using System.Numerics;

namespace PPPaint.Core;

public enum ScreenshotCanvasEdge { Left, Top, Right, Bottom }

public static class ScreenshotCanvasGeometry
{
    // Both rectangles and the pointer use the same display-coordinate space. The
    // image boundary belongs to image editing; only the visible outside band crops.
    public static ScreenshotCanvasEdge? HitBorder(ScreenshotBox outerBounds, ScreenshotBox imageBounds, Vector2 point)
    {
        if (outerBounds.Width <= 0 || outerBounds.Height <= 0 || imageBounds.Width <= 0 || imageBounds.Height <= 0
            || !outerBounds.Contains(point) || imageBounds.Contains(point)) return null;

        ScreenshotCanvasEdge? nearest = null; var distance = float.PositiveInfinity;
        void Consider(ScreenshotCanvasEdge edge, float candidate)
        {
            if (candidate < distance) { nearest = edge; distance = candidate; }
        }
        // Stable ties follow this order, including the four outside corners.
        if (point.X < imageBounds.X) Consider(ScreenshotCanvasEdge.Left, imageBounds.X - point.X);
        if (point.Y < imageBounds.Y) Consider(ScreenshotCanvasEdge.Top, imageBounds.Y - point.Y);
        if (point.X > imageBounds.Right) Consider(ScreenshotCanvasEdge.Right, point.X - imageBounds.Right);
        if (point.Y > imageBounds.Bottom) Consider(ScreenshotCanvasEdge.Bottom, point.Y - imageBounds.Bottom);
        return nearest;
    }

    public static PixelRect CropFromDrag(int width, int height, ScreenshotCanvasEdge edge,
        double startCoordinate, double currentCoordinate, double zoom, bool fromCenter = false)
    {
        ScreenshotFrame.ValidateSize(width, height);
        if (!double.IsFinite(startCoordinate)) throw new ArgumentOutOfRangeException(nameof(startCoordinate));
        if (!double.IsFinite(currentCoordinate)) throw new ArgumentOutOfRangeException(nameof(currentCoordinate));
        if (!double.IsFinite(zoom) || zoom <= 0) throw new ArgumentOutOfRangeException(nameof(zoom));
        var movement = (currentCoordinate - startCoordinate) / zoom;
        // Round the signed movement, rather than an absolute edge coordinate, so
        // half-pixel inward drags are identical on all four sides.
        var delta = Math.Round(Math.Clamp(movement, -Math.Max(width, height), Math.Max(width, height)), MidpointRounding.AwayFromZero);
        var initialEdge = edge switch { ScreenshotCanvasEdge.Right => width, ScreenshotCanvasEdge.Bottom => height, _ => 0 };
        return CropFromEdge(width, height, edge, (float)(initialEdge + delta), fromCenter);
    }

    public static PixelRect CropFromEdge(int width, int height, ScreenshotCanvasEdge edge, float coordinate, bool fromCenter = false)
    {
        ScreenshotFrame.ValidateSize(width, height);
        if (edge is not (ScreenshotCanvasEdge.Left or ScreenshotCanvasEdge.Top or ScreenshotCanvasEdge.Right or ScreenshotCanvasEdge.Bottom)) throw new ArgumentOutOfRangeException(nameof(edge));
        if (!float.IsFinite(coordinate)) throw new ArgumentOutOfRangeException(nameof(coordinate));
        var value = (int)Math.Clamp(Math.Round(coordinate, MidpointRounding.AwayFromZero), 0, Math.Max(width, height));
        if (fromCenter)
        {
            var horizontal = edge is ScreenshotCanvasEdge.Left or ScreenshotCanvasEdge.Right;
            var size = horizontal ? width : height;
            var inset = edge is ScreenshotCanvasEdge.Left or ScreenshotCanvasEdge.Top ? value : size - value;
            inset = Math.Clamp(inset, 0, (size - 1) / 2);
            return horizontal ? new(inset, 0, width - 2 * inset, height) : new(0, inset, width, height - 2 * inset);
        }
        return edge switch
        {
            ScreenshotCanvasEdge.Left => new(Math.Clamp(value, 0, width - 1), 0, width - Math.Clamp(value, 0, width - 1), height),
            ScreenshotCanvasEdge.Top => new(0, Math.Clamp(value, 0, height - 1), width, height - Math.Clamp(value, 0, height - 1)),
            ScreenshotCanvasEdge.Right => new(0, 0, Math.Clamp(value, 1, width), height),
            ScreenshotCanvasEdge.Bottom => new(0, 0, width, Math.Clamp(value, 1, height)),
            _ => throw new ArgumentOutOfRangeException(nameof(edge))
        };
    }
}
