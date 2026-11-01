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

    public static void DrawComparison(CanvasDrawingSession ds, CanvasBitmap original, CanvasBitmap candidate, ScreenshotCanvasResize layout, ScreenshotBox viewport, float width, float height, bool detail)
    {
        ds.Clear(Outside);
        if (detail) ds.Antialiasing = CanvasAntialiasing.Aliased;
        var sx = width / viewport.Width; var sy = height / viewport.Height;
        Rect Map(ScreenshotBox box) => new((box.X - viewport.X) * sx, (box.Y - viewport.Y) * sy, box.Width * sx, box.Height * sy);
        var interpolation = detail ? CanvasImageInterpolation.NearestNeighbor : CanvasImageInterpolation.HighQualityCubic;
        var old = ScreenshotResizePreviewGeometry.Original(layout); var next = new ScreenshotBox(0, 0, layout.Width, layout.Height);
        void Image(CanvasBitmap image, ScreenshotBox bounds)
        {
            if (ScreenshotEditing.Intersection(bounds, viewport) is not { } visible) return;
            var destination = Map(visible); Checker(ds, destination);
            ds.DrawImage(image, destination, new Rect(visible.X - bounds.X, visible.Y - bounds.Y, visible.Width, visible.Height), 1, interpolation);
        }
        // Keep the original content visible, including everything outside the new canvas.
        Image(original, old);
        if (ScreenshotEditing.Intersection(old, viewport) is { } oldVisible)
            ds.FillRectangle(Map(oldVisible), Color.FromArgb(92, 0, 0, 0));
        // Reset the checker inside the candidate before drawing alpha, avoiding double compositing.
        Image(candidate, next);
        using var style = new CanvasStrokeStyle { DashStyle = CanvasDashStyle.Dash };
        var originalEdge = Map(old);
        ds.DrawRectangle(new Rect(originalEdge.X - .5, originalEdge.Y - .5, originalEdge.Width + 1, originalEdge.Height + 1), Color.FromArgb(255, 196, 100, 0), 1, style);
        var edge = Map(next);
        ds.DrawRectangle(new Rect(edge.X - .5, edge.Y - .5, edge.Width + 1, edge.Height + 1), Edge, 1);
    }

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
