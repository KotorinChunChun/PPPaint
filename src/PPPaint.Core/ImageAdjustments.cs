namespace PPPaint.Core;

public static class ImageAdjustments
{
    public static PixelSurface Blackout(PixelSurface source, SelectionRegion? region = null, CancellationToken cancellationToken = default) =>
        Map(source, region, (_, _, _) => new Rgba32(0, 0, 0, 255), cancellationToken);

    public static PixelSurface Adjust(PixelSurface source, double brightness, double contrast, double saturation, SelectionRegion? region = null, CancellationToken cancellationToken = default)
    {
        Validate(brightness, -1, 1, nameof(brightness)); Validate(contrast, -1, 1, nameof(contrast)); Validate(saturation, 0, 2, nameof(saturation));
        return Map(source, region, (p, _, _) =>
        {
            var gray = p.R * 0.2126 + p.G * 0.7152 + p.B * 0.0722;
            byte Channel(byte v) => PixelAverage.Byte(((gray + (v - gray) * saturation - 127.5) * (1 + contrast)) + 127.5 + brightness * 255);
            return new Rgba32(Channel(p.R), Channel(p.G), Channel(p.B), p.A);
        }, cancellationToken);
    }

    public static PixelSurface Mosaic(PixelSurface source, int blockSize, SelectionRegion? region = null, CancellationToken cancellationToken = default)
    {
        if (blockSize < 1 || blockSize > 4096) throw new ArgumentOutOfRangeException(nameof(blockSize));
        ValidateSource(source, region); cancellationToken.ThrowIfCancellationRequested();
        var result = Clone(source);
        for (var y = 0; y < source.Height; y += blockSize)
        for (var x = 0; x < source.Width; x += blockSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sum = new PixelAverage();
            for (var sy = y; sy < Math.Min(y + blockSize, source.Height); sy++)
            for (var sx = x; sx < Math.Min(x + blockSize, source.Width); sx++)
            {
                var coverage = region?.GetCoverage(sx, sy) ?? 255;
                if (coverage != 0) sum.Add(source[sx, sy], coverage / 255d);
            }
            var color = sum.Color;
            for (var sy = y; sy < Math.Min(y + blockSize, source.Height); sy++)
            for (var sx = x; sx < Math.Min(x + blockSize, source.Width); sx++)
                result[sx, sy] = SelectionOperations.InterpolateCoverage(source[sx, sy], color, region?.GetCoverage(sx, sy) ?? 255);
        }
        return result;
    }

    public static PixelSurface Blur(PixelSurface source, int radius, SelectionRegion? region = null, CancellationToken cancellationToken = default)
    {
        if (radius < 0 || radius > 256) throw new ArgumentOutOfRangeException(nameof(radius));
        ValidateSource(source, region); cancellationToken.ThrowIfCancellationRequested();
        if (radius == 0) return Clone(source);
        // 走査線の移動合計により、半径に依存しない計算量でボックスぼかしを行う。
        var horizontal = Pass(source, radius, true, cancellationToken);
        var blurred = Pass(horizontal, radius, false, cancellationToken);
        if (region is null) return blurred;
        return Map(source, region, (_, x, y) => blurred[x, y], cancellationToken);
    }

    private static PixelSurface Pass(PixelSurface source, int radius, bool horizontal, CancellationToken token)
    {
        var result = new PixelSurface(source.Width, source.Height, Rgba32.Transparent);
        int lines = horizontal ? source.Height : source.Width, length = horizontal ? source.Width : source.Height;
        for (var line = 0; line < lines; line++)
        {
            token.ThrowIfCancellationRequested();
            var sum = new PixelAverage();
            Rgba32 Get(int i) => horizontal ? source[i, line] : source[line, i];
            for (var i = 0; i <= Math.Min(radius, length - 1); i++) sum.Add(Get(i), 1);
            for (var i = 0; i < length; i++)
            {
                if (horizontal) result[i, line] = sum.Color; else result[line, i] = sum.Color;
                if (i - radius >= 0) sum.Add(Get(i - radius), -1);
                if (i + radius + 1 < length) sum.Add(Get(i + radius + 1), 1);
            }
        }
        return result;
    }
    private static PixelSurface Map(PixelSurface source, SelectionRegion? region, Func<Rgba32, int, int, Rgba32> transform, CancellationToken token)
    {
        ValidateSource(source, region); token.ThrowIfCancellationRequested(); var result = Clone(source);
        for (var y = 0; y < source.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < source.Width; x++)
            {
                var coverage = region?.GetCoverage(x, y) ?? 255;
                if (coverage != 0) result[x, y] = SelectionOperations.InterpolateCoverage(source[x, y], transform(source[x, y], x, y), coverage);
            }
        }
        return result;
    }
    internal static PixelSurface Clone(PixelSurface source) { var result = new PixelSurface(source.Width, source.Height, Rgba32.Transparent); result.Restore(source.Snapshot()); return result; }
    private static void ValidateSource(PixelSurface source, SelectionRegion? region)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (region is not null && (region.Bounds.X < 0 || region.Bounds.Y < 0 || region.Bounds.Right > source.Width || region.Bounds.Bottom > source.Height)) throw new ArgumentOutOfRangeException(nameof(region));
    }
    private static void Validate(double value, double min, double max, string name)
    { if (!double.IsFinite(value) || value < min || value > max) throw new ArgumentOutOfRangeException(name); }
}
