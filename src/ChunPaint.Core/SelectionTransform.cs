using System.Numerics;

namespace ChunPaint.Core;

public static class SelectionTransform
{
    public static SelectionRegion Move(SelectionRegion region, Vector2 offset, PixelRect canvasBounds)
    {
        ArgumentNullException.ThrowIfNull(region);
        return region.MoveBy(offset, canvasBounds);
    }

    public static SelectionRegion Resize(SelectionRegion region, SelectionHandle handle, Vector2 delta, PixelRect canvasBounds)
    {
        ArgumentNullException.ThrowIfNull(region);
        return region.Resize(handle, delta, canvasBounds);
    }

    public static SelectionRegion Rotate(SelectionRegion region, float degrees, PixelRect canvasBounds)
    {
        ArgumentNullException.ThrowIfNull(region);
        return region.Rotate(degrees, canvasBounds);
    }

    public static SelectionRegion Skew(SelectionRegion region, float degrees, bool horizontal, PixelRect canvasBounds)
    {
        ArgumentNullException.ThrowIfNull(region);
        return region.Skew(degrees, horizontal, canvasBounds);
    }
}
