using System.Numerics;

namespace PPPaint.Core;

public readonly record struct StrokeSample(Vector2 Point, float Pressure);

public sealed class StrokeInputFilter
{
    private readonly float smoothing;
    private StrokeSample? previous;
    private StrokeSample? latest;
    public StrokeInputFilter(float smoothing = 0)
    {
        if (!float.IsFinite(smoothing) || smoothing < 0 || smoothing > 0.95f) throw new ArgumentOutOfRangeException(nameof(smoothing));
        this.smoothing = smoothing;
    }
    public StrokeSample Add(Vector2 point, float pressure = 1, bool isPen = false)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y)) throw new ArgumentOutOfRangeException(nameof(point));
        if (!float.IsFinite(pressure)) throw new ArgumentOutOfRangeException(nameof(pressure));
        latest = new(point, isPen ? Math.Clamp(pressure, 0, 1) : 1);
        previous = previous is { } p ? new(Vector2.Lerp(point, p.Point, smoothing), latest.Value.Pressure) : latest;
        return previous.Value;
    }
    public StrokeSample Flush()
    {
        var result = latest ?? throw new InvalidOperationException("入力点がありません。");
        Reset();
        return result;
    }
    public void Reset() { previous = null; latest = null; }
}
