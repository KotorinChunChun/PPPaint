using System.Numerics;
using Microsoft.Graphics.Canvas;
using Windows.UI;

namespace PPPaint.Imaging;

// Editor overlay only; ScreenshotRenderer never draws the cursor into copied/saved images.
public static class ScreenshotEraserPreview
{
    public static void Draw(CanvasDrawingSession ds, Vector2 center, float diameter, float zoom)
    {
        if (!float.IsFinite(diameter) || diameter <= 0 || !float.IsFinite(zoom) || zoom <= 0) throw new ArgumentOutOfRangeException(nameof(diameter));
        ds.DrawCircle(center, diameter / 2, Color.FromArgb(255, 255, 255, 255), 3 / zoom);
        ds.DrawCircle(center, diameter / 2, Color.FromArgb(255, 0, 0, 0), 1 / zoom);
    }
}
