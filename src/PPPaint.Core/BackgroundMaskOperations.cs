namespace PPPaint.Core;

/// <summary>アルファを残す範囲のマスクとして扱い、原画の色を保って境界と手直しを適用する。</summary>
public static class BackgroundMaskOperations
{
    public static PixelSurface Refine(PixelSurface original, PixelSurface removed, int expansion = 0, int featherRadius = 0, CancellationToken cancellationToken = default)
    {
        Validate(original, removed);
        if (expansion < -32 || expansion > 32) throw new ArgumentOutOfRangeException(nameof(expansion));
        if (featherRadius < 0 || featherRadius > 256) throw new ArgumentOutOfRangeException(nameof(featherRadius));
        cancellationToken.ThrowIfCancellationRequested();
        var mask = new PixelSurface(original.Width, original.Height, Rgba32.Transparent);
        for (var y = 0; y < mask.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < mask.Width; x++)
            {
                // 元画像の半透明度を二重に乗算しないよう、除去後との比率を使う。
                var alpha = original[x, y].A;
                var value = alpha == 0 ? (byte)0 : PixelAverage.Byte(removed[x, y].A * 255.0 / alpha);
                mask[x, y] = new(value, value, value, 255);
            }
        }
        if (expansion != 0)
        {
            mask = Morphology(mask, Math.Abs(expansion), expansion > 0, true, cancellationToken);
            mask = Morphology(mask, Math.Abs(expansion), expansion > 0, false, cancellationToken);
        }
        if (featherRadius != 0) mask = ImageAdjustments.Blur(mask, featherRadius, cancellationToken: cancellationToken);
        var result = ImageAdjustments.Clone(original);
        for (var y = 0; y < result.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < result.Width; x++)
            {
                var p = original[x, y]; result[x, y] = new(p.R, p.G, p.B, PixelAverage.Byte(p.A * mask[x, y].R / 255.0));
            }
        }
        return result;
    }

    public static PixelSurface Retouch(PixelSurface original, PixelSurface current, SelectionRegion region, bool restore, CancellationToken cancellationToken = default)
    {
        Validate(original, current); ArgumentNullException.ThrowIfNull(region);
        if (region.Bounds.X < 0 || region.Bounds.Y < 0 || region.Bounds.Right > original.Width || region.Bounds.Bottom > original.Height) throw new ArgumentOutOfRangeException(nameof(region));
        cancellationToken.ThrowIfCancellationRequested();
        var result = ImageAdjustments.Clone(current);
        for (var y = region.Bounds.Y; y < region.Bounds.Bottom; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = region.Bounds.X; x < region.Bounds.Right; x++)
            {
                var coverage = region.GetCoverage(x, y);
                if (coverage == 0) continue;
                var p = original[x, y];
                result[x, y] = SelectionOperations.InterpolateCoverage(current[x, y], restore ? p : new(p.R, p.G, p.B, 0), coverage);
            }
        }
        return result;
    }

    private static PixelSurface Morphology(PixelSurface source, int radius, bool expand, bool horizontal, CancellationToken token)
    {
        var result = new PixelSurface(source.Width, source.Height, Rgba32.Transparent);
        for (var y = 0; y < result.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < result.Width; x++)
            {
                var value = expand ? 0 : 255;
                for (var offset = -radius; offset <= radius; offset++)
                {
                    var sx = horizontal ? x + offset : x; var sy = horizontal ? y : y + offset;
                    if ((uint)sx >= source.Width || (uint)sy >= source.Height) continue;
                    value = expand ? Math.Max(value, source[sx, sy].R) : Math.Min(value, source[sx, sy].R);
                }
                result[x, y] = new((byte)value, (byte)value, (byte)value, 255);
            }
        }
        return result;
    }
    private static void Validate(PixelSurface original, PixelSurface current)
    {
        ArgumentNullException.ThrowIfNull(original); ArgumentNullException.ThrowIfNull(current);
        if (original.Width != current.Width || original.Height != current.Height) throw new ArgumentException("原画とマスクのサイズが一致しません。", nameof(current));
    }
}
