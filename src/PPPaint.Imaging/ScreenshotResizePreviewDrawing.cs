using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using PPPaint.Core;
using Windows.Foundation;
using Windows.UI;

namespace PPPaint.Imaging;

public static class ScreenshotResizePreviewDrawing
{
    private static readonly Color Outside = Color.FromArgb(255, 226, 231, 237);
    private static readonly Color Edge = Color.FromArgb(255, 0, 112, 192);

    public static void DrawOverview(CanvasDrawingSession ds, CanvasBitmap image, float width, float height)
    {
        ds.Clear(Outside);
        var scale = Math.Min((width - 8) / (float)image.Size.Width, (height - 8) / (float)image.Size.Height);
        var destination = new Rect((width - image.Size.Width * scale) / 2, (height - image.Size.Height * scale) / 2, image.Size.Width * scale, image.Size.Height * scale);
        Checker(ds, destination);
        ds.DrawImage(image, destination, new Rect(0, 0, image.Size.Width, image.Size.Height), 1, CanvasImageInterpolation.HighQualityCubic);
        ds.DrawRectangle(destination, Edge, 1);
    }

    public static void DrawDetail(CanvasDrawingSession ds, CanvasBitmap image, ScreenshotCanvasViewAnchor anchor)
    {
        ds.Clear(Outside);
        var source = ScreenshotCanvasResizeViews.DetailSource((int)image.SizeInPixels.Width, (int)image.SizeInPixels.Height, anchor);
        const int scale = ScreenshotCanvasResizeViews.DetailScale;
        var left = Math.Max(0, source.X); var top = Math.Max(0, source.Y);
        var right = Math.Min((float)image.Size.Width, source.Right); var bottom = Math.Min((float)image.Size.Height, source.Bottom);
        var destination = new Rect((left - source.X) * scale, (top - source.Y) * scale, (right - left) * scale, (bottom - top) * scale);
        ds.Antialiasing = CanvasAntialiasing.Aliased;
        Checker(ds, destination);
        ds.DrawImage(image, destination, new Rect(left, top, right - left, bottom - top), 1, CanvasImageInterpolation.NearestNeighbor);
        // Draw the boundary outside the image so it cannot cover a one-pixel mark.
        ds.DrawRectangle(new Rect(-source.X * scale - .5, -source.Y * scale - .5, image.Size.Width * scale + 1, image.Size.Height * scale + 1), Edge, 1);
    }

    private static void Checker(CanvasDrawingSession ds, Rect bounds)
    {
        using var clip = ds.CreateLayer(1, bounds);
        const int step = 8;
        for (var y = (int)Math.Floor(bounds.Top / step) * step; y < bounds.Bottom; y += step)
            for (var x = (int)Math.Floor(bounds.Left / step) * step; x < bounds.Right; x += step)
                ds.FillRectangle(x, y, step, step, (x / step + y / step) % 2 == 0 ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 210, 210, 210));
    }
}
