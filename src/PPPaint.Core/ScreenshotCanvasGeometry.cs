using System.Numerics;

namespace PPPaint.Core;

public enum ScreenshotCanvasEdge { Left, Top, Right, Bottom }
public enum ScreenshotCanvasHandle { Left, Top, Right, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }

public static class ScreenshotCanvasGeometry
{
    // Canvas resizing uses the whole displayed outer band. A corner of that band
    // changes both axes; pixels on or inside the image retain object editing.
    public static ScreenshotCanvasHandle? HitResizeBorder(ScreenshotBox outerBounds, ScreenshotBox imageBounds, Vector2 point)
    {
        if (outerBounds.Width <= 0 || outerBounds.Height <= 0 || imageBounds.Width <= 0 || imageBounds.Height <= 0
            || !outerBounds.Contains(point) || imageBounds.Contains(point)) return null;
        var left = point.X < imageBounds.X; var right = point.X > imageBounds.Right;
        var top = point.Y < imageBounds.Y; var bottom = point.Y > imageBounds.Bottom;
        if (left && top) return ScreenshotCanvasHandle.TopLeft;
        if (right && top) return ScreenshotCanvasHandle.TopRight;
        if (left && bottom) return ScreenshotCanvasHandle.BottomLeft;
        if (right && bottom) return ScreenshotCanvasHandle.BottomRight;
        return left ? ScreenshotCanvasHandle.Left : right ? ScreenshotCanvasHandle.Right
            : top ? ScreenshotCanvasHandle.Top : bottom ? ScreenshotCanvasHandle.Bottom : null;
    }

    public static PixelRect ResizeFromDrag(int width, int height, ScreenshotCanvasHandle handle,
        double startX, double startY, double currentX, double currentY, double zoom, bool fromCenter = false)
    {
        ScreenshotFrame.ValidateSize(width, height);
        if (!Enum.IsDefined(handle)) throw new ArgumentOutOfRangeException(nameof(handle));
        if (!double.IsFinite(startX) || !double.IsFinite(startY)) throw new ArgumentOutOfRangeException(nameof(startX));
        if (!double.IsFinite(currentX) || !double.IsFinite(currentY)) throw new ArgumentOutOfRangeException(nameof(currentX));
        if (!double.IsFinite(zoom) || zoom <= 0) throw new ArgumentOutOfRangeException(nameof(zoom));
        var dx = Math.Round(Math.Clamp((currentX - startX) / zoom, -16384, 16384), MidpointRounding.AwayFromZero);
        var dy = Math.Round(Math.Clamp((currentY - startY) / zoom, -16384, 16384), MidpointRounding.AwayFromZero);
        var left = handle is ScreenshotCanvasHandle.Left or ScreenshotCanvasHandle.TopLeft or ScreenshotCanvasHandle.BottomLeft;
        var right = handle is ScreenshotCanvasHandle.Right or ScreenshotCanvasHandle.TopRight or ScreenshotCanvasHandle.BottomRight;
        var top = handle is ScreenshotCanvasHandle.Top or ScreenshotCanvasHandle.TopLeft or ScreenshotCanvasHandle.TopRight;
        var bottom = handle is ScreenshotCanvasHandle.Bottom or ScreenshotCanvasHandle.BottomLeft or ScreenshotCanvasHandle.BottomRight;
        PixelRect At(double fraction)
        {
            var horizontal = ResizeAxis(width, (int)Math.Round(dx * fraction, MidpointRounding.AwayFromZero), left, right, fromCenter);
            var vertical = ResizeAxis(height, (int)Math.Round(dy * fraction, MidpointRounding.AwayFromZero), top, bottom, fromCenter);
            return new(horizontal.Origin, vertical.Origin, horizontal.Length, vertical.Length);
        }
        static bool Fits(PixelRect bounds) => (long)bounds.Width * bounds.Height * 4 <= ScreenshotFrame.MaximumPixelBytes;
        var result = At(1);
        if (Fits(result)) return result;
        // Limit the requested movement along the same drag path. Capping height
        // from width alone could otherwise shrink an axis the user pulled outward.
        double low = 0, high = 1; result = At(0);
        for (var i = 0; i < 48; i++)
        {
            var middle = (low + high) / 2; var candidate = At(middle);
            if (Fits(candidate)) { low = middle; result = candidate; }
            else high = middle;
        }
        return result;
    }

    private static (int Origin, int Length) ResizeAxis(int size, int delta, bool near, bool far, bool fromCenter)
    {
        if (!near && !far) return (0, size);
        if (fromCenter)
        {
            var inset = Math.Clamp(near ? delta : -delta, -(16384 - size) / 2, (size - 1) / 2);
            return (inset, size - inset * 2);
        }
        if (near)
        {
            var origin = Math.Clamp(delta, size - 16384, size - 1);
            return (origin, size - origin);
        }
        return (0, Math.Clamp(size + delta, 1, 16384));
    }

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
