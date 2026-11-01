namespace PPPaint.Core;

public static class ImageTransforms
{
    public static PixelSurface RotateClockwise(PixelSurface source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = new PixelSurface(source.Height, source.Width, Rgba32.Transparent);
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
            result[source.Height - 1 - y, x] = source[x, y];
        return result;
    }

    public static PixelSurface FlipHorizontal(PixelSurface source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = new PixelSurface(source.Width, source.Height, Rgba32.Transparent);
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
            result[source.Width - 1 - x, y] = source[x, y];
        return result;
    }

    public static PixelSurface FlipVertical(PixelSurface source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = new PixelSurface(source.Width, source.Height, Rgba32.Transparent);
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
            result[x, source.Height - 1 - y] = source[x, y];
        return result;
    }
}
