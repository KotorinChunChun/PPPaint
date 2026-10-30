using System.Collections.Immutable;
using System.Numerics;

namespace PPPaint.Core;

public readonly record struct ScreenshotSnap(Vector2 Point, float? X = null, float? Y = null);

public static class ScreenshotManipulation
{
    public static Vector2 ConstrainMove(Vector2 delta, bool shift) => !shift ? delta
        : Math.Abs(delta.X) >= Math.Abs(delta.Y) ? new(delta.X, 0) : new(0, delta.Y);

    public static ScreenshotBox CreateBox(Vector2 start, Vector2 point, bool ratio, bool center)
    {
        var delta = point - start;
        if (ratio) { var size = Math.Max(Math.Abs(delta.X), Math.Abs(delta.Y)); delta = new(MathF.CopySign(size, delta.X), MathF.CopySign(size, delta.Y)); }
        return ScreenshotBox.Between(center ? start - delta : start, start + delta);
    }

    // Targets include the canvas, then unselected objects. Tolerance is supplied in image pixels.
    public static ScreenshotSnap SnapPoint(Vector2 point, IEnumerable<ScreenshotBox> targets, float tolerance, bool horizontal = true, bool vertical = true)
    {
        var boxes = targets.ToArray();
        var x = horizontal ? Nearest([point.X], boxes.SelectMany(b => Axis(b.X, b.Width)), tolerance) : null;
        var y = vertical ? Nearest([point.Y], boxes.SelectMany(b => Axis(b.Y, b.Height)), tolerance) : null;
        return new(new(point.X + (x?.Offset ?? 0), point.Y + (y?.Offset ?? 0)), x?.Guide, y?.Guide);
    }

    public static ScreenshotSnap SnapMove(ScreenshotBox original, Vector2 delta, IEnumerable<ScreenshotBox> targets, float tolerance, bool shift)
    {
        delta = ConstrainMove(delta, shift); var boxes = targets.ToArray();
        var horizontal = !shift || delta.Y == 0; var vertical = !shift || delta.X == 0 && delta.Y != 0;
        var x = horizontal ? Nearest(Axis(original.X + delta.X, original.Width), boxes.SelectMany(b => Axis(b.X, b.Width)), tolerance) : null;
        var y = vertical ? Nearest(Axis(original.Y + delta.Y, original.Height), boxes.SelectMany(b => Axis(b.Y, b.Height)), tolerance) : null;
        return new(delta + new Vector2(x?.Offset ?? 0, y?.Offset ?? 0), x?.Guide, y?.Guide);
    }

    public static ImmutableArray<ScreenshotAnnotation> Duplicate(IEnumerable<ScreenshotAnnotation> items) =>
        items.Select(a => a with { Id = Guid.NewGuid().ToString("N") }).ToImmutableArray();

    public static HashSet<string> ClickSelection(IEnumerable<string> selected, string id, bool extend)
    {
        var result = selected.ToHashSet();
        if (extend) { if (!result.Add(id)) result.Remove(id); }
        else if (!result.Contains(id)) { result.Clear(); result.Add(id); }
        return result;
    }

    public static bool IsAligned(ScreenshotBox box, float guide, bool horizontal) =>
        Axis(horizontal ? box.X : box.Y, horizontal ? box.Width : box.Height).Any(value => Math.Abs(value - guide) < .01f);

    private static float[] Axis(float start, float length) => [start, start + length / 2, start + length];
    private static (float Offset, float Guide)? Nearest(IEnumerable<float> values, IEnumerable<float> targets, float tolerance)
    {
        (float Offset, float Guide)? best = null; var distance = tolerance;
        var axes = targets.ToArray();
        foreach (var value in values) foreach (var target in axes)
        {
            var offset = target - value;
            if (Math.Abs(offset) > distance || best is not null && Math.Abs(offset) == distance) continue;
            distance = Math.Abs(offset); best = (offset, target);
        }
        return best;
    }
}
