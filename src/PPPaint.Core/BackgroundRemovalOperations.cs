namespace PPPaint.Core;

public static class BackgroundRemovalOperations
{
    public static PixelSurface RemoveFromEdges(
        PixelSurface source,
        SelectionRegion? region = null,
        byte tolerance = 24,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateRegion(source, region);
        var result = new PixelSurface(source.Width, source.Height, Rgba32.Transparent);
        result.Restore(source.Snapshot());
        RemoveFromEdgesInPlace(result, region, tolerance, cancellationToken);
        return result;
    }

    public static int RemoveFromEdgesInPlace(
        PixelSurface target,
        SelectionRegion? region = null,
        byte tolerance = 24,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ValidateRegion(target, region);
        var bounds = region?.Bounds ?? new PixelRect(0, 0, target.Width, target.Height);
        var background = FindBackgroundSample(target, region, bounds);
        var visited = new bool[checked(target.Width * target.Height)];
        var pending = new Queue<int>();
        for (var x = bounds.X; x < bounds.Right; x++)
        {
            EnqueueIfInside(pending, x, bounds.Y, target, region);
            EnqueueIfInside(pending, x, bounds.Bottom - 1, target, region);
        }
        for (var y = bounds.Y + 1; y + 1 < bounds.Bottom; y++)
        {
            EnqueueIfInside(pending, bounds.X, y, target, region);
            EnqueueIfInside(pending, bounds.Right - 1, y, target, region);
        }

        var removed = 0;
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = pending.Dequeue();
            if (visited[index]) continue;
            visited[index] = true;

            var x = index % target.Width;
            var y = index / target.Width;
            if (region is not null && !region.ContainsPixel(x, y)) continue;
            var pixel = target[x, y];
            if (!IsWithinTolerance(pixel, background, tolerance)) continue;

            target[x, y] = new Rgba32(pixel.R, pixel.G, pixel.B, 0);
            removed++;
            EnqueueIfInside(pending, x - 1, y, target, region);
            EnqueueIfInside(pending, x + 1, y, target, region);
            EnqueueIfInside(pending, x, y - 1, target, region);
            EnqueueIfInside(pending, x, y + 1, target, region);
        }

        return removed;
    }

    public static int RemoveFromVisibleLayers(
        IEnumerable<(PixelSurface Surface, bool IsVisible)> layers,
        SelectionRegion? region = null,
        byte tolerance = 24,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layers);
        var removed = 0;
        foreach (var layer in layers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (layer.IsVisible)
            {
                removed += RemoveFromEdgesInPlace(layer.Surface, region, tolerance, cancellationToken);
            }
        }
        return removed;
    }

    private static Rgba32 FindBackgroundSample(PixelSurface target, SelectionRegion? region, PixelRect bounds)
    {
        for (var y = bounds.Y; y < bounds.Bottom; y++)
        for (var x = bounds.X; x < bounds.Right; x++)
        {
            if (region is null || region.ContainsPixel(x, y))
            {
                return target[x, y];
            }
        }
        throw new ArgumentException("The selection region does not contain any pixels.", nameof(region));
    }

    private static void EnqueueIfInside(Queue<int> pending, int x, int y, PixelSurface target, SelectionRegion? region)
    {
        if ((uint)x < target.Width && (uint)y < target.Height && (region is null || region.ContainsPixel(x, y)))
        {
            pending.Enqueue((y * target.Width) + x);
        }
    }

    private static bool IsWithinTolerance(Rgba32 value, Rgba32 reference, byte tolerance) =>
        Math.Abs(value.R - reference.R) <= tolerance &&
        Math.Abs(value.G - reference.G) <= tolerance &&
        Math.Abs(value.B - reference.B) <= tolerance &&
        Math.Abs(value.A - reference.A) <= tolerance;

    private static void ValidateRegion(PixelSurface surface, SelectionRegion? region)
    {
        if (region is not null && (region.Bounds.X < 0 || region.Bounds.Y < 0 ||
            region.Bounds.Right > surface.Width || region.Bounds.Bottom > surface.Height))
        {
            throw new ArgumentOutOfRangeException(nameof(region), "The selection region must be inside the surface.");
        }
    }
}
