namespace PPPaint.Core;

public enum ScreenshotCanvasViewAnchor { TopLeft, Top, TopRight, Left, Right, BottomLeft, Bottom, BottomRight }

public static class ScreenshotCanvasResizeViews
{
    public const int DetailPixels = 25;
    public const int DetailScale = 4;

    public static ScreenshotBox DetailSource(int width, int height, ScreenshotCanvasViewAnchor anchor)
    {
        ScreenshotFrame.ValidateSize(width, height);
        var point = anchor switch
        {
            ScreenshotCanvasViewAnchor.TopLeft => (0, 0),
            ScreenshotCanvasViewAnchor.Top => (width / 2, 0),
            ScreenshotCanvasViewAnchor.TopRight => (width, 0),
            ScreenshotCanvasViewAnchor.Left => (0, height / 2),
            ScreenshotCanvasViewAnchor.Right => (width, height / 2),
            ScreenshotCanvasViewAnchor.BottomLeft => (0, height),
            ScreenshotCanvasViewAnchor.Bottom => (width / 2, height),
            ScreenshotCanvasViewAnchor.BottomRight => (width, height),
            _ => throw new ArgumentOutOfRangeException(nameof(anchor))
        };
        // Keep source origins on pixel boundaries. The odd span leaves 12 pixels
        // before an edge and 13 after it, avoiding half-pixel sampling at 4x.
        return new(point.Item1 - DetailPixels / 2, point.Item2 - DetailPixels / 2, DetailPixels, DetailPixels);
    }
}
