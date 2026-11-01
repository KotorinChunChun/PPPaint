using System.Numerics;

namespace PPPaint.Core;

public static class ScreenshotResizePreviewGeometry
{
    public static ScreenshotBox Original(ScreenshotCanvasResize layout) => new(layout.OffsetX, layout.OffsetY, layout.SourceWidth, layout.SourceHeight);
    public static ScreenshotBox Extent(ScreenshotCanvasResize layout)
    {
        var original = Original(layout); var left = Math.Min(0, original.X); var top = Math.Min(0, original.Y);
        return new(left, top, Math.Max(layout.Width, original.Right) - left, Math.Max(layout.Height, original.Bottom) - top);
    }
    public static ScreenshotBox Overview(ScreenshotCanvasResize layout, float width, float height)
    {
        var extent = Extent(layout); var scale = Math.Min((width - 8) / extent.Width, (height - 8) / extent.Height);
        return new(extent.X - (width / scale - extent.Width) / 2, extent.Y - (height / scale - extent.Height) / 2, width / scale, height / scale);
    }
    public static Vector2 ImagePoint(ScreenshotBox viewport, Vector2 point, float width, float height) => new(viewport.X + point.X * viewport.Width / width, viewport.Y + point.Y * viewport.Height / height);
    public static ScreenshotBox FrozenViewport(ScreenshotBox viewport, ScreenshotCanvasResize start, ScreenshotCanvasResize current) =>
        viewport with { X = viewport.X + current.OffsetX - start.OffsetX, Y = viewport.Y + current.OffsetY - start.OffsetY };
    public static ScreenshotCanvasHandle? Hit(ScreenshotCanvasResize layout, ScreenshotBox viewport, Vector2 point, float width, float height)
    {
        var image = ImagePoint(viewport, point, width, height); var tolerance = 6 * viewport.Width / width;
        if (image.X < -tolerance || image.Y < -tolerance || image.X > layout.Width + tolerance || image.Y > layout.Height + tolerance) return null;
        var left = Math.Abs(image.X) <= tolerance; var right = Math.Abs(image.X - layout.Width) <= tolerance;
        var top = Math.Abs(image.Y) <= tolerance; var bottom = Math.Abs(image.Y - layout.Height) <= tolerance;
        return (left, right, top, bottom) switch
        {
            (true, _, true, _) => ScreenshotCanvasHandle.TopLeft,
            (_, true, true, _) => ScreenshotCanvasHandle.TopRight,
            (true, _, _, true) => ScreenshotCanvasHandle.BottomLeft,
            (_, true, _, true) => ScreenshotCanvasHandle.BottomRight,
            (true, _, _, _) => ScreenshotCanvasHandle.Left,
            (_, true, _, _) => ScreenshotCanvasHandle.Right,
            (_, _, true, _) => ScreenshotCanvasHandle.Top,
            (_, _, _, true) => ScreenshotCanvasHandle.Bottom,
            _ => null
        };
    }
    public static ScreenshotCanvasResize Drag(ScreenshotState source, ScreenshotCanvasResize start, ScreenshotCanvasHandle handle, Vector2 delta, bool centered, bool ratio)
    {
        var local = ScreenshotCanvasGeometry.ResizeFromDrag(start.Width, start.Height, handle, 0, 0, delta.X, delta.Y, 1, centered);
        if (ratio)
        {
            var horizontal = handle is not (ScreenshotCanvasHandle.Top or ScreenshotCanvasHandle.Bottom);
            var vertical = handle is not (ScreenshotCanvasHandle.Left or ScreenshotCanvasHandle.Right);
            var widthDriven = horizontal && (!vertical || Math.Abs(local.Width - start.Width) / (double)start.Width >= Math.Abs(local.Height - start.Height) / (double)start.Height);
            double w = widthDriven ? local.Width : Math.Round(local.Height * source.Frame.Width / (double)source.Frame.Height);
            double h = widthDriven ? Math.Round(local.Width * source.Frame.Height / (double)source.Frame.Width) : local.Height;
            var limit = Math.Min(1, Math.Min(16384 / Math.Max(1, w), 16384 / Math.Max(1, h)));
            limit = Math.Min(limit, Math.Sqrt(PixelSurface.MaximumPixelBytes / (4 * Math.Max(1, w) * Math.Max(1, h))));
            var width = Math.Max(1, (int)Math.Floor(w * limit)); var height = Math.Max(1, (int)Math.Floor(h * limit));
            var nearX = handle is ScreenshotCanvasHandle.Left or ScreenshotCanvasHandle.TopLeft or ScreenshotCanvasHandle.BottomLeft;
            var nearY = handle is ScreenshotCanvasHandle.Top or ScreenshotCanvasHandle.TopLeft or ScreenshotCanvasHandle.TopRight;
            local = new(centered || !horizontal ? (start.Width - width) / 2 : nearX ? start.Width - width : 0,
                centered || !vertical ? (start.Height - height) / 2 : nearY ? start.Height - height : 0, width, height);
        }
        return ScreenshotCanvasResize.FromBounds(source, new(-start.OffsetX + local.X, -start.OffsetY + local.Y, local.Width, local.Height));
    }
}
