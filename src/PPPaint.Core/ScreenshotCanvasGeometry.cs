namespace PPPaint.Core;

public enum ScreenshotCanvasEdge { Left, Top, Right, Bottom }

public static class ScreenshotCanvasGeometry
{
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
