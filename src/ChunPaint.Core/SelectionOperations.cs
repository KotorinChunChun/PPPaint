namespace ChunPaint.Core;

public static class SelectionOperations
{
    private const long MaximumMaskPixels = 64L * 1024 * 1024;

    public static bool[] CreateMask(SelectionRegion region, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(region);
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        if ((long)width * height > MaximumMaskPixels)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "The selection mask exceeds the 64 million pixel safety limit.");
        }

        var mask = new bool[checked(width * height)];
        var bounds = region.Bounds;
        var left = Math.Max(0, bounds.X);
        var top = Math.Max(0, bounds.Y);
        var right = Math.Min(width, bounds.Right);
        var bottom = Math.Min(height, bounds.Bottom);
        for (var y = top; y < bottom; y++)
        for (var x = left; x < right; x++)
            mask[(y * width) + x] = region.ContainsPixel(x, y);
        return mask;
    }

    public static PixelSurface Extract(
        PixelSurface source,
        SelectionRegion region,
        bool transparentSelection = false,
        Rgba32? transparentColor = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(region);
        ValidateRegionWithinSurface(source, region);

        var bounds = region.Bounds;
        var result = new PixelSurface(bounds.Width, bounds.Height, Rgba32.Transparent);
        for (var y = bounds.Y; y < bounds.Bottom; y++)
        for (var x = bounds.X; x < bounds.Right; x++)
        {
            if (!region.ContainsPixel(x, y))
            {
                continue;
            }

            var pixel = source[x, y];
            if (transparentSelection && transparentColor is { } color && pixel == color)
            {
                pixel = Rgba32.Transparent;
            }
            result[x - bounds.X, y - bounds.Y] = pixel;
        }
        return result;
    }

    public static void Clear(PixelSurface target, SelectionRegion region, Rgba32 replacement)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(region);
        ValidateRegionWithinSurface(target, region);
        for (var y = region.Bounds.Y; y < region.Bounds.Bottom; y++)
        for (var x = region.Bounds.X; x < region.Bounds.Right; x++)
        {
            if (region.ContainsPixel(x, y))
            {
                target[x, y] = replacement;
            }
        }
    }

    public static void InvertColors(PixelSurface target, SelectionRegion region)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(region);
        ValidateRegionWithinSurface(target, region);
        for (var y = region.Bounds.Y; y < region.Bounds.Bottom; y++)
        for (var x = region.Bounds.X; x < region.Bounds.Right; x++)
        {
            if (!region.ContainsPixel(x, y))
            {
                continue;
            }
            var pixel = target[x, y];
            target[x, y] = new Rgba32((byte)(255 - pixel.R), (byte)(255 - pixel.G), (byte)(255 - pixel.B), pixel.A);
        }
    }

    public static void Apply(PixelSurface destination, SelectionRegion region, PixelSurface selection)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(selection);
        ValidateRegionWithinSurface(destination, region);
        if (selection.Width != region.Bounds.Width || selection.Height != region.Bounds.Height)
        {
            throw new ArgumentException("The floating selection dimensions must match the region bounds.", nameof(selection));
        }

        for (var y = region.Bounds.Y; y < region.Bounds.Bottom; y++)
        for (var x = region.Bounds.X; x < region.Bounds.Right; x++)
        {
            if (!region.ContainsPixel(x, y))
            {
                continue;
            }
            destination[x, y] = BlendOver(destination[x, y], selection[x - region.Bounds.X, y - region.Bounds.Y]);
        }
    }

    public static PixelSurface CompositeVisibleLayers(IEnumerable<(PixelSurface Surface, bool IsVisible)> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        var items = layers.ToArray();
        if (items.Length == 0)
        {
            throw new ArgumentException("At least one layer is required.", nameof(layers));
        }

        var width = items[0].Surface.Width;
        var height = items[0].Surface.Height;
        var result = new PixelSurface(width, height, Rgba32.Transparent);
        foreach (var item in items)
        {
            if (item.Surface.Width != width || item.Surface.Height != height)
            {
                throw new ArgumentException("All layers must have matching dimensions.", nameof(layers));
            }
            if (!item.IsVisible)
            {
                continue;
            }

            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                result[x, y] = BlendOver(result[x, y], item.Surface[x, y]);
        }
        return result;
    }

    private static Rgba32 BlendOver(Rgba32 background, Rgba32 foreground)
    {
        if (foreground.A == 0) return background;
        if (foreground.A == 255) return foreground;

        var foregroundAlpha = foreground.A / 255d;
        var backgroundAlpha = background.A / 255d;
        var resultAlpha = foregroundAlpha + (backgroundAlpha * (1d - foregroundAlpha));
        if (resultAlpha <= 0) return Rgba32.Transparent;

        static byte Channel(byte foregroundChannel, byte backgroundChannel, double foregroundAlpha, double backgroundAlpha, double resultAlpha) =>
            (byte)Math.Clamp(Math.Round(((foregroundChannel * foregroundAlpha) + (backgroundChannel * backgroundAlpha * (1d - foregroundAlpha))) / resultAlpha), 0, 255);

        return new Rgba32(
            Channel(foreground.R, background.R, foregroundAlpha, backgroundAlpha, resultAlpha),
            Channel(foreground.G, background.G, foregroundAlpha, backgroundAlpha, resultAlpha),
            Channel(foreground.B, background.B, foregroundAlpha, backgroundAlpha, resultAlpha),
            (byte)Math.Clamp(Math.Round(resultAlpha * 255), 0, 255));
    }

    private static void ValidateRegionWithinSurface(PixelSurface surface, SelectionRegion region)
    {
        if (region.Bounds.X < 0 || region.Bounds.Y < 0 || region.Bounds.Right > surface.Width || region.Bounds.Bottom > surface.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(region), "The selection region must be inside the surface.");
        }
    }
}
