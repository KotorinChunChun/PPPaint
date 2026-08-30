namespace ChunPaint.Core;

public static class ImageTransforms
{
    public const int MaximumDimension = 100_000;

    public static PixelSurface RotateNearestNeighbor(PixelSurface source, float degrees)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!float.IsFinite(degrees)) throw new ArgumentOutOfRangeException(nameof(degrees));

        var radians = degrees * (MathF.PI / 180f);
        var sin = SnapNearInteger(MathF.Abs(MathF.Sin(radians)));
        var cos = SnapNearInteger(MathF.Abs(MathF.Cos(radians)));
        var width = GetSafeDimension((source.Width * cos) + (source.Height * sin), nameof(degrees));
        var height = GetSafeDimension((source.Width * sin) + (source.Height * cos), nameof(degrees));
        ValidateSurfaceSize(width, height, nameof(degrees));
        var result = new PixelSurface(width, height, Rgba32.Transparent);
        var centerX = (source.Width - 1) / 2f;
        var centerY = (source.Height - 1) / 2f;
        var resultCenterX = (width - 1) / 2f;
        var resultCenterY = (height - 1) / 2f;
        var sine = MathF.Sin(radians);
        var cosine = MathF.Cos(radians);

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var relativeX = x - resultCenterX;
            var relativeY = y - resultCenterY;
            var sourceX = (relativeX * cosine) + (relativeY * sine) + centerX;
            var sourceY = (-relativeX * sine) + (relativeY * cosine) + centerY;
            var nearestX = (int)MathF.Round(sourceX);
            var nearestY = (int)MathF.Round(sourceY);
            if ((uint)nearestX < source.Width && (uint)nearestY < source.Height)
            {
                result[x, y] = source[nearestX, nearestY];
            }
        }
        return result;
    }

    public static PixelSurface SkewHorizontal(PixelSurface source, float degrees) =>
        Skew(source, degrees, horizontal: true);

    public static PixelSurface SkewVertical(PixelSurface source, float degrees) =>
        Skew(source, degrees, horizontal: false);

    private static PixelSurface Skew(PixelSurface source, float degrees, bool horizontal)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!float.IsFinite(degrees) || MathF.Abs(degrees) >= 89.9f)
        {
            throw new ArgumentOutOfRangeException(nameof(degrees), "Skew angle must be finite and between -89.9 and 89.9 degrees.");
        }

        var shear = MathF.Tan(degrees * (MathF.PI / 180f));
        var extent = MathF.Abs(shear * (horizontal ? source.Height - 1 : source.Width - 1));
        var width = horizontal ? GetSafeDimension(source.Width + extent, nameof(degrees)) : source.Width;
        var height = horizontal ? source.Height : GetSafeDimension(source.Height + extent, nameof(degrees));
        ValidateSurfaceSize(width, height, nameof(degrees));
        var result = new PixelSurface(width, height, Rgba32.Transparent);
        var sourceCenterX = (source.Width - 1) / 2f;
        var sourceCenterY = (source.Height - 1) / 2f;
        var offset = extent / 2f;

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var sourceX = horizontal
                ? x - offset - (shear * (y - sourceCenterY)) + sourceCenterX
                : x;
            var sourceY = horizontal
                ? y
                : y - offset - (shear * (x - sourceCenterX)) + sourceCenterY;
            var nearestX = (int)MathF.Round(sourceX);
            var nearestY = (int)MathF.Round(sourceY);
            if ((uint)nearestX < source.Width && (uint)nearestY < source.Height)
            {
                result[x, y] = source[nearestX, nearestY];
            }
        }

        return result;
    }

    private static float SnapNearInteger(float value)
    {
        var rounded = MathF.Round(value);
        return MathF.Abs(value - rounded) < 0.0001f ? rounded : value;
    }

    private static int GetSafeDimension(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value < 1 || value > MaximumDimension)
        {
            throw new ArgumentOutOfRangeException(parameterName, $"The transformed dimension must be between 1 and {MaximumDimension} pixels.");
        }
        return Math.Max(1, (int)MathF.Ceiling(value));
    }

    private static void ValidateSurfaceSize(int width, int height, string parameterName)
    {
        if ((long)width * height * 4 > 256L * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(parameterName, "The transformed pixel data exceeds the 256 MiB safety limit.");
        }
    }

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
        ValidateSurfaceSize(width, height, nameof(width));

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
        return BackgroundRemovalOperations.RemoveFromEdges(source, tolerance: tolerance);
    }

    private static void ValidateRect(PixelSurface source, PixelRect rect)
    {
        if (rect.Width <= 0) throw new ArgumentOutOfRangeException(nameof(rect));
        if (rect.Height <= 0) throw new ArgumentOutOfRangeException(nameof(rect));
        if (rect.X < 0 || rect.Y < 0 || rect.Right > source.Width || rect.Bottom > source.Height)
            throw new ArgumentOutOfRangeException(nameof(rect));
    }
}
