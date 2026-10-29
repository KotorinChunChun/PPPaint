using System.Numerics;
namespace PPPaint.Core;

public static class RetouchOperations
{
    public static PixelSurface Stroke(PixelSurface source, PixelSurface target, Vector2 sourceAnchor, Vector2 targetAnchor,
        IReadOnlyList<Vector2> points, float radius, float hardness, float opacity, bool heal, SelectionRegion? region = null)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(points);
        ValidatePoint(sourceAnchor); ValidatePoint(targetAnchor);
        if (!float.IsFinite(radius) || radius <= 0 || radius > 512) throw new ArgumentOutOfRangeException(nameof(radius));
        if (!float.IsFinite(hardness) || hardness < 0 || hardness > 1) throw new ArgumentOutOfRangeException(nameof(hardness));
        if (!float.IsFinite(opacity) || opacity < 0 || opacity > 1) throw new ArgumentOutOfRangeException(nameof(opacity));
        if (points.Count > 100000) throw new ArgumentOutOfRangeException(nameof(points));
        foreach (var point in points) ValidatePoint(point);
        var result = new PixelSurface(target.Width, target.Height, Rgba32.Transparent); result.Restore(target.Snapshot());
        if (points.Count == 0 || opacity == 0) return result;
        var coverage = new byte[checked(target.Width * target.Height)];
        var offset = sourceAnchor - targetAnchor;
        // 修復では周辺の色差を転写色へ加え、コピー先の明るさと色になじませる。
        var correction = heal ? Neighborhood(target, targetAnchor, radius + 2) - Neighborhood(source, sourceAnchor, radius + 2) : Vector3.Zero;
        long totalSteps = 0;
        for (var i = 0; i < points.Count; i++)
        {
            var start = i == 0 ? points[0] : points[i - 1]; var end = points[i];
            var distance = Vector2.Distance(start, end);
            var stepsValue = MathF.Ceiling(distance / Math.Max(.5f, radius / 3));
            if (!float.IsFinite(stepsValue) || stepsValue > 1000000 || (totalSteps += (long)stepsValue + 1) > 1000000) throw new ArgumentOutOfRangeException(nameof(points));
            var steps = Math.Max(1, (int)stepsValue);
            for (var step = 0; step <= steps; step++) Dab(Vector2.Lerp(start, end, step / (float)steps));
        }
        return result;

        void Dab(Vector2 center)
        {
            if (center.X + radius < 0 || center.Y + radius < 0 || center.X - radius >= target.Width || center.Y - radius >= target.Height) return;
            var left = (int)Math.Max(0, MathF.Floor(center.X - radius)); var right = (int)Math.Min(target.Width - 1, MathF.Ceiling(center.X + radius));
            var top = (int)Math.Max(0, MathF.Floor(center.Y - radius)); var bottom = (int)Math.Min(target.Height - 1, MathF.Ceiling(center.Y + radius));
            for (var y = top; y <= bottom; y++) for (var x = left; x <= right; x++)
            {
                var distance = Vector2.Distance(new Vector2(x + .5f, y + .5f), center) / radius;
                if (distance >= 1) continue;
                var edge = distance <= hardness || hardness == 1 ? 1 : (1 - distance) / (1 - hardness);
                var weight = (byte)Math.Clamp(MathF.Round(edge * opacity * (region?.GetCoverage(x, y) ?? 255)), 0, 255);
                var index = y * target.Width + x;
                if (weight <= coverage[index]) continue;
                var samplePosition = new Vector2(x, y) + offset;
                if (samplePosition.X < 0 || samplePosition.Y < 0 || samplePosition.X > source.Width - 1 || samplePosition.Y > source.Height - 1) continue;
                var sampled = Sample(source, samplePosition);
                if (heal) sampled = new(ToByte(sampled.R + correction.X), ToByte(sampled.G + correction.Y), ToByte(sampled.B + correction.Z), sampled.A);
                var original = target[x, y]; var amount = weight / 255d;
                var alpha = original.A * (1 - amount) + sampled.A * amount;
                byte Mix(byte a, byte b) => alpha <= 0 ? (byte)0 : ToByte((a * original.A * (1 - amount) + b * sampled.A * amount) / alpha);
                result[x, y] = new(Mix(original.R, sampled.R), Mix(original.G, sampled.G), Mix(original.B, sampled.B), ToByte(alpha));
                coverage[index] = weight;
            }
        }
    }

    private static Rgba32 Sample(PixelSurface source, Vector2 point)
    {
        var x = (int)point.X; var y = (int)point.Y; var fx = point.X - x; var fy = point.Y - y;
        var sum = new PixelAverage();
        sum.Add(source[x, y], (1 - fx) * (1 - fy)); sum.Add(source[Math.Min(x + 1, source.Width - 1), y], fx * (1 - fy));
        sum.Add(source[x, Math.Min(y + 1, source.Height - 1)], (1 - fx) * fy); sum.Add(source[Math.Min(x + 1, source.Width - 1), Math.Min(y + 1, source.Height - 1)], fx * fy);
        return sum.Color;
    }
    private static Vector3 Neighborhood(PixelSurface surface, Vector2 center, float radius)
    {
        var sum = Vector3.Zero; var weight = 0f;
        for (var i = 0; i < 32; i++)
        {
            var angle = i * MathF.Tau / 32;
            var x = Math.Clamp(center.X + MathF.Cos(angle) * radius, 0, surface.Width - 1);
            var y = Math.Clamp(center.Y + MathF.Sin(angle) * radius, 0, surface.Height - 1);
            var p = Sample(surface, new Vector2(x, y)); sum += new Vector3(p.R, p.G, p.B) * p.A; weight += p.A;
        }
        return weight == 0 ? Vector3.Zero : sum / weight;
    }
    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);
    private static void ValidatePoint(Vector2 point) { if (!float.IsFinite(point.X) || !float.IsFinite(point.Y)) throw new ArgumentOutOfRangeException(nameof(point)); }
}
