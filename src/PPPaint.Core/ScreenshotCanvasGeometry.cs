namespace PPPaint.Core;

public enum ScreenshotCanvasEdge { Left, Top, Right, Bottom }

public static class ScreenshotCanvasGeometry
{
    public static PixelRect CropFromEdge(int width, int height, ScreenshotCanvasEdge edge, float coordinate)
    {
        ScreenshotFrame.ValidateSize(width, height);
        if (!float.IsFinite(coordinate)) throw new ArgumentOutOfRangeException(nameof(coordinate));
        var value = (int)Math.Clamp(Math.Round(coordinate, MidpointRounding.AwayFromZero), 0, Math.Max(width, height));
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
