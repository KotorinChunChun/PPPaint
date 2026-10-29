namespace PPPaint.Core;

public sealed record PhotoAdjustmentSettings(double BlackPoint = 0, double WhitePoint = 1, double Gamma = 1,
    double Temperature = 0, double Tint = 0, double Shadows = 0, double Midtones = 0, double Highlights = 0);

public static class PhotoAdjustments
{
    public static void Validate(PhotoAdjustmentSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Range(settings.BlackPoint, 0, 1, nameof(settings.BlackPoint));
        Range(settings.WhitePoint, 0, 1, nameof(settings.WhitePoint));
        if (settings.BlackPoint >= settings.WhitePoint) throw new ArgumentException("黒点は白点より小さくしてください。", nameof(settings));
        Range(settings.Gamma, .1, 10, nameof(settings.Gamma));
        Range(settings.Temperature, -1, 1, nameof(settings.Temperature));
        Range(settings.Tint, -1, 1, nameof(settings.Tint));
        Range(settings.Shadows, -.5, .5, nameof(settings.Shadows));
        Range(settings.Midtones, -.5, .5, nameof(settings.Midtones));
        Range(settings.Highlights, -.5, .5, nameof(settings.Highlights));
    }

    public static PixelSurface Apply(PixelSurface source, PhotoAdjustmentSettings settings,
        SelectionRegion? region = null, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        Validate(settings);
        GradientOperations.ValidateRegion(source, region);
        token.ThrowIfCancellationRequested();
        var output = ImageAdjustments.Clone(source);
        var curve = BuildCurve(settings);
        var red = new byte[256]; var green = new byte[256]; var blue = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            var normalized = Math.Clamp((i / 255d - settings.BlackPoint) / (settings.WhitePoint - settings.BlackPoint), 0, 1);
            var value = EvaluateCurve(Math.Pow(normalized, 1 / settings.Gamma), curve.Values, curve.Slopes);
            red[i] = PixelAverage.Byte(255 * (value + settings.Temperature * .15 + settings.Tint * .075));
            green[i] = PixelAverage.Byte(255 * (value - settings.Tint * .15));
            blue[i] = PixelAverage.Byte(255 * (value - settings.Temperature * .15 + settings.Tint * .075));
        }
        for (var y = 0; y < source.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < source.Width; x++)
            {
                var coverage = (region?.GetCoverage(x, y) ?? 255) / 255d;
                if (coverage == 0) continue;
                var pixel = source[x, y];
                if (pixel.A == 0) continue;
                byte Blend(byte original, byte adjusted) => PixelAverage.Byte(original + (adjusted - original) * coverage);
                output[x, y] = new Rgba32(Blend(pixel.R, red[pixel.R]), Blend(pixel.G, green[pixel.G]), Blend(pixel.B, blue[pixel.B]), pixel.A);
            }
        }
        return output;
    }

    /// <summary>完全透明画素を除外し、表示色の輝度を画素数として集計する。</summary>
    public static long[] Histogram(PixelSurface source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var bins = new long[256];
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
        {
            var pixel = source[x, y];
            if (pixel.A != 0) bins[PixelAverage.Byte(pixel.R * .2126 + pixel.G * .7152 + pixel.B * .0722)]++;
        }
        return bins;
    }

    private static (double[] Values, double[] Slopes) BuildCurve(PhotoAdjustmentSettings settings)
    {
        double[] values = [0, Math.Clamp(.25 + settings.Shadows, 0, 1), Math.Clamp(.5 + settings.Midtones, 0, 1), Math.Clamp(.75 + settings.Highlights, 0, 1), 1];
        // 交差した制御点を平均でまとめ、黒白端点を固定した単調なトーンカーブにする。
        var groups = new List<(double Sum, int Count)>();
        for (var i = 1; i <= 3; i++)
        {
            groups.Add((values[i], 1));
            while (groups.Count >= 2 && groups[^2].Sum / groups[^2].Count > groups[^1].Sum / groups[^1].Count)
            {
                var last = groups[^1]; var previous = groups[^2];
                groups.RemoveAt(groups.Count - 1);
                groups[^1] = (previous.Sum + last.Sum, previous.Count + last.Count);
            }
        }
        var position = 1;
        foreach (var group in groups)
            for (var i = 0; i < group.Count; i++) values[position++] = group.Sum / group.Count;
        var delta = new double[4];
        for (var i = 0; i < 4; i++) delta[i] = (values[i + 1] - values[i]) * 4;
        var slopes = new double[5];
        slopes[0] = Math.Clamp((3 * delta[0] - delta[1]) / 2, 0, 3 * delta[0]);
        slopes[4] = Math.Clamp((3 * delta[3] - delta[2]) / 2, 0, 3 * delta[3]);
        for (var i = 1; i < 4; i++) slopes[i] = delta[i - 1] <= 0 || delta[i] <= 0 ? 0 : 2 * delta[i - 1] * delta[i] / (delta[i - 1] + delta[i]);
        return (values, slopes);
    }

    private static double EvaluateCurve(double x, double[] values, double[] slopes)
    {
        var segment = Math.Min(3, (int)(x * 4));
        var t = x * 4 - segment;
        var t2 = t * t; var t3 = t2 * t;
        var result = (2 * t3 - 3 * t2 + 1) * values[segment] + (t3 - 2 * t2 + t) * .25 * slopes[segment]
            + (-2 * t3 + 3 * t2) * values[segment + 1] + (t3 - t2) * .25 * slopes[segment + 1];
        return Math.Clamp(result, values[segment], values[segment + 1]);
    }

    private static void Range(double value, double minimum, double maximum, string name)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum) throw new ArgumentOutOfRangeException(name);
    }
}
