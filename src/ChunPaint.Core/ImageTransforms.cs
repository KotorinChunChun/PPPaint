namespace ChunPaint.Core;

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

    public static PixelSurface RotateCounterClockwise(PixelSurface source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = new PixelSurface(source.Height, source.Width, Rgba32.Transparent);
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
            result[y, source.Width - 1 - x] = source[x, y];
        return result;
    }

    public static PixelSurface Rotate180(PixelSurface source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = new PixelSurface(source.Width, source.Height, Rgba32.Transparent);
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
            result[source.Width - 1 - x, source.Height - 1 - y] = source[x, y];
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

    public static PixelSurface Crop(PixelSurface source, PixelRect rect)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateRect(source, rect);
        var result = new PixelSurface(rect.Width, rect.Height, Rgba32.Transparent);
        for (var y = 0; y < rect.Height; y++)
        for (var x = 0; x < rect.Width; x++)
            result[x, y] = source[rect.X + x, rect.Y + y];
        return result;
    }

    public static PixelSurface ResizeNearestNeighbor(PixelSurface source, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        var result = new PixelSurface(width, height, Rgba32.Transparent);
        for (var y = 0; y < height; y++)
        {
            var sourceY = Math.Min(source.Height - 1, (int)((long)y * source.Height / height));
            for (var x = 0; x < width; x++)
            {
                var sourceX = Math.Min(source.Width - 1, (int)((long)x * source.Width / width));
                result[x, y] = source[sourceX, sourceY];
            }
        }
        return result;
    }

    public static PixelSurface InvertColors(PixelSurface source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = new PixelSurface(source.Width, source.Height, Rgba32.Transparent);
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
        {
            var pixel = source[x, y];
            result[x, y] = new Rgba32((byte)(255 - pixel.R), (byte)(255 - pixel.G), (byte)(255 - pixel.B), pixel.A);
        }
        return result;
    }

    public static PixelSurface RemoveBackgroundFromEdges(PixelSurface source, byte tolerance = 24)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = new PixelSurface(source.Width, source.Height, Rgba32.Transparent);
        result.Restore(source.Snapshot());

        var background = source[0, 0];
        var visited = new bool[checked(source.Width * source.Height)];
        var pending = new Queue<int>();
        for (var x = 0; x < source.Width; x++)
        {
            pending.Enqueue(x);
            pending.Enqueue(((source.Height - 1) * source.Width) + x);
        }
        for (var y = 1; y + 1 < source.Height; y++)
        {
            pending.Enqueue(y * source.Width);
            pending.Enqueue((y * source.Width) + source.Width - 1);
        }

        while (pending.Count > 0)
        {
            var index = pending.Dequeue();
            if (visited[index]) continue;
            visited[index] = true;

            var x = index % source.Width;
            var y = index / source.Width;
            var pixel = source[x, y];
            if (!IsWithinTolerance(pixel, background, tolerance)) continue;

            result[x, y] = new Rgba32(pixel.R, pixel.G, pixel.B, 0);
            if (x > 0) pending.Enqueue(index - 1);
            if (x + 1 < source.Width) pending.Enqueue(index + 1);
            if (y > 0) pending.Enqueue(index - source.Width);
            if (y + 1 < source.Height) pending.Enqueue(index + source.Width);
        }

        return result;
    }

    private static bool IsWithinTolerance(Rgba32 value, Rgba32 reference, byte tolerance)
    {
        return Math.Abs(value.R - reference.R) <= tolerance &&
               Math.Abs(value.G - reference.G) <= tolerance &&
               Math.Abs(value.B - reference.B) <= tolerance &&
               Math.Abs(value.A - reference.A) <= tolerance;
    }

    private static void ValidateRect(PixelSurface source, PixelRect rect)
    {
        if (rect.Width <= 0) throw new ArgumentOutOfRangeException(nameof(rect));
        if (rect.Height <= 0) throw new ArgumentOutOfRangeException(nameof(rect));
        if (rect.X < 0 || rect.Y < 0 || rect.Right > source.Width || rect.Bottom > source.Height)
            throw new ArgumentOutOfRangeException(nameof(rect));
    }
}
