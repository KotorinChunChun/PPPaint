namespace PPPaint.Core;

public static class ScreenshotCrop
{
    public static PixelRect? Resolve(ScreenshotState state, ScreenshotBox? region, IReadOnlySet<string> selectedIds)
    {
        var width = state.Frame.Width; var height = state.Frame.Height;
        if (region is { } area) return RegionArea(area, width, height);
        ScreenshotBox? union = null;
        foreach (var item in state.Annotations.Where(item => selectedIds.Contains(item.Id)))
        {
            var drawing = ScreenshotEditing.DrawingBounds(item);
            if (!Finite(drawing)) continue;
            if (item.ClipBounds is { } clip)
            {
                if (ScreenshotEditing.Intersection(drawing, clip) is not { } clipped) continue;
                drawing = clipped;
            }
            if (ScreenshotEditing.Intersection(drawing, new(0, 0, width, height)) is not { } visible) continue;
            union = union is { } previous
                ? new(Math.Min(previous.X, visible.X), Math.Min(previous.Y, visible.Y), Math.Max(previous.Right, visible.Right) - Math.Min(previous.X, visible.X), Math.Max(previous.Bottom, visible.Bottom) - Math.Min(previous.Y, visible.Y))
                : visible;
        }
        if (union is not { } bounds) return null;
        var left = (int)Math.Floor(bounds.X); var top = (int)Math.Floor(bounds.Y);
        return new(left, top, (int)Math.Ceiling(bounds.Right) - left, (int)Math.Ceiling(bounds.Bottom) - top);
    }

    // Delete, cut and crop all convert a rectangular selection through this one rule.
    public static PixelRect? RegionArea(ScreenshotBox area, int width, int height)
    {
        if (!Finite(area) || area.Width < 1 || area.Height < 1) return null;
        var pixels = area.SnapToPixels();
        var x = (int)Math.Clamp(pixels.X, 0, width - 1); var y = (int)Math.Clamp(pixels.Y, 0, height - 1);
        var right = (int)Math.Clamp(pixels.Right, x + 1, width); var bottom = (int)Math.Clamp(pixels.Bottom, y + 1, height);
        return new(x, y, right - x, bottom - y);
    }

    private static bool Finite(ScreenshotBox bounds) => float.IsFinite(bounds.X) && float.IsFinite(bounds.Y) && float.IsFinite(bounds.Right) && float.IsFinite(bounds.Bottom);
}
