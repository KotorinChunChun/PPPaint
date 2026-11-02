using System.Numerics;
using Microsoft.Graphics.Canvas;
using PPPaint.Core;
using Windows.Foundation;

namespace PPPaint.Imaging;

// Editor overlay only. Render the same 96dpi brush stamp as the scene, then
// scale it with nearest-neighbor interpolation, including for a one-pixel pen.
public static class ScreenshotPenPreview
{
    public static void Draw(CanvasDrawingSession ds, Vector2 position, float width, Rgba32 color, int canvasWidth, int canvasHeight)
    {
        if (!float.IsFinite(width) || width is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(width));
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y)) throw new ArgumentOutOfRangeException(nameof(position));
        // Clip on the source pixel grid, before scaling. A display-space clip
        // would partially fade the last pixel again at fractional zooms.
        var origin = Vector2.Max(Vector2.Zero, new(MathF.Floor(position.X - width / 2) - 1, MathF.Floor(position.Y - width / 2) - 1));
        var right = Math.Min(canvasWidth, MathF.Ceiling(position.X + width / 2) + 1);
        var bottom = Math.Min(canvasHeight, MathF.Ceiling(position.Y + width / 2) + 1);
        var stampWidth = (int)(right - origin.X); var stampHeight = (int)(bottom - origin.Y);
        if (stampWidth <= 0 || stampHeight <= 0) return;
        var point = position - origin;
        var dot = new ScreenshotAnnotation("preview", ScreenshotTool.Pen, new(point.X, point.Y, 0, 0), color, Width: width, Points: [point]);
        using var stamp = ScreenshotRenderer.Render(ds, new(ScreenshotFrame.Transparent(stampWidth, stampHeight), [dot]));
        ds.DrawImage(stamp, new Rect(origin.X, origin.Y, stampWidth, stampHeight), new Rect(0, 0, stampWidth, stampHeight), 1, CanvasImageInterpolation.NearestNeighbor);
    }
}
