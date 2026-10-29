using System.Numerics;

namespace PPPaint.Core;

public static class GradientOperations
{
    /// <summary>整数座標は画素中心。範囲外は端色へ固定し、始点と終点が同じ場合は拒否する。</summary>
    public static PixelSurface Apply(PixelSurface source, Vector2 start, Vector2 end, Rgba32 startColor,
        Rgba32 endColor, bool radial, SelectionRegion? region = null, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!float.IsFinite(start.X) || !float.IsFinite(start.Y) || !float.IsFinite(end.X) || !float.IsFinite(end.Y))
            throw new ArgumentOutOfRangeException(nameof(start));
        var dx = (double)end.X - start.X; var dy = (double)end.Y - start.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= 0) throw new ArgumentException("グラデーションの始点と終点を離してください。", nameof(end));
        ValidateRegion(source, region);
        token.ThrowIfCancellationRequested();
        var output = ImageAdjustments.Clone(source);
        for (var y = 0; y < source.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < source.Width; x++)
            {
                var coverage = (region?.GetCoverage(x, y) ?? 255) / 255d;
                if (coverage == 0) continue;
                var px = x - (double)start.X; var py = y - (double)start.Y;
                var progress = Math.Clamp(radial ? Math.Sqrt((px * px + py * py) / lengthSquared) : (px * dx + py * dy) / lengthSquared, 0, 1);
                var startAlpha = startColor.A / 255d * (1 - progress) * coverage; var endAlpha = endColor.A / 255d * progress * coverage;
                var alpha = startAlpha + endAlpha;
                if (alpha <= 0) continue;
                var original = source[x, y];
                var retainedAlpha = original.A / 255d * (1 - alpha);
                var finalAlpha = alpha + retainedAlpha;
                var finalAlphaByte = PixelAverage.Byte(finalAlpha * 255);
                if (finalAlphaByte == 0) { output[x, y] = Rgba32.Transparent; continue; }
                byte Channel(byte first, byte last, byte previous) => PixelAverage.Byte((first * startAlpha + last * endAlpha + previous * retainedAlpha) / finalAlpha);
                output[x, y] = new Rgba32(Channel(startColor.R, endColor.R, original.R), Channel(startColor.G, endColor.G, original.G),
                    Channel(startColor.B, endColor.B, original.B), finalAlphaByte);
            }
        }
        return output;
    }

    internal static void ValidateRegion(PixelSurface source, SelectionRegion? region)
    {
        if (region is not null && (region.Bounds.X < 0 || region.Bounds.Y < 0 || region.Bounds.Right > source.Width || region.Bounds.Bottom > source.Height))
            throw new ArgumentOutOfRangeException(nameof(region));
    }
}
