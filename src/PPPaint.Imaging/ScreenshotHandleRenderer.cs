using System.Numerics;
using Microsoft.Graphics.Canvas;
using PPPaint.Core;
using Windows.Foundation;
using Windows.UI;

namespace PPPaint.Imaging;

public static class ScreenshotHandleRenderer
{
    public static void Draw(CanvasDrawingSession ds, Vector2 center, float zoom, bool round)
    {
        var blue = Color.FromArgb(255, 30, 144, 255); var white = Color.FromArgb(255, 255, 255, 255);
        if (round)
        {
            ds.FillCircle(center, ScreenshotHandleGeometry.RoundRadius / zoom, blue);
            ds.FillCircle(center, (ScreenshotHandleGeometry.RoundRadius - 1) / zoom, white);
        }
        else
        {
            var b = ScreenshotHandleGeometry.Square(center, zoom); var inset = 1 / zoom;
            ds.FillRectangle(new Rect(b.X, b.Y, b.Width, b.Height), blue);
            ds.FillRectangle(new Rect(b.X + inset, b.Y + inset, b.Width - 2 * inset, b.Height - 2 * inset), white);
        }
    }
}
