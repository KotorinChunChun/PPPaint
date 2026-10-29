namespace PPPaint.Core;

public enum ResamplingMode { Nearest, Bilinear, Area }

public static class ImageResampling
{
    public static PixelSurface Resize(PixelSurface source, int width, int height, ResamplingMode mode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        cancellationToken.ThrowIfCancellationRequested();
        var result = new PixelSurface(width, height, Rgba32.Transparent);
        for (var y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
            {
                if (mode == ResamplingMode.Nearest)
                {
                    result[x, y] = source[(int)((long)x * source.Width / width), (int)((long)y * source.Height / height)];
                    continue;
                }
                var sum = new PixelAverage();
                if (mode == ResamplingMode.Area)
                {
                    double left = (double)x * source.Width / width, right = (double)(x + 1) * source.Width / width;
                    double top = (double)y * source.Height / height, bottom = (double)(y + 1) * source.Height / height;
                    for (var sy = (int)top; sy < Math.Ceiling(bottom); sy++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        for (var sx = (int)left; sx < Math.Ceiling(right); sx++)
                            sum.Add(source[Math.Min(sx, source.Width - 1), Math.Min(sy, source.Height - 1)],
                                (Math.Min(right, sx + 1) - Math.Max(left, sx)) * (Math.Min(bottom, sy + 1) - Math.Max(top, sy)));
                    }
                }
                else
                {
                    var sx = Math.Clamp((x + 0.5) * source.Width / width - 0.5, 0, source.Width - 1);
                    var sy = Math.Clamp((y + 0.5) * source.Height / height - 0.5, 0, source.Height - 1);
                    var ix = (int)sx; var iy = (int)sy; var fx = sx - ix; var fy = sy - iy;
                    sum.Add(source[ix, iy], (1 - fx) * (1 - fy));
                    sum.Add(source[Math.Min(ix + 1, source.Width - 1), iy], fx * (1 - fy));
                    sum.Add(source[ix, Math.Min(iy + 1, source.Height - 1)], (1 - fx) * fy);
                    sum.Add(source[Math.Min(ix + 1, source.Width - 1), Math.Min(iy + 1, source.Height - 1)], fx * fy);
                }
                result[x, y] = sum.Color;
            }
        }
        return result;
    }
}

internal struct PixelAverage
{
    private double r, g, b, a, weight;
    public void Add(Rgba32 p, double w) { r += p.R * p.A * w; g += p.G * p.A * w; b += p.B * p.A * w; a += p.A * w; weight += w; }
    public Rgba32 Color => a <= 0 || weight <= 0 ? Rgba32.Transparent : new(Byte(r / a), Byte(g / a), Byte(b / a), Byte(a / weight));
    internal static byte Byte(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);
}
