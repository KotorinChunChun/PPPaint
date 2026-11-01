using System.Collections.Immutable;
using System.Numerics;

namespace PPPaint.Core;

public enum ScreenshotOrientationChange { RotateClockwise, FlipHorizontal, FlipVertical }

// Rotates or mirrors the whole canvas: background pixels and every object move together.
public static class ScreenshotOrientation
{
    public static ScreenshotState Apply(ScreenshotState state, ScreenshotOrientationChange change)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!Enum.IsDefined(change)) throw new ArgumentOutOfRangeException(nameof(change));
        var frame = state.Frame;
        Vector2 Point(Vector2 p) => change switch
        {
            ScreenshotOrientationChange.RotateClockwise => new(frame.Height - p.Y, p.X),
            ScreenshotOrientationChange.FlipHorizontal => new(frame.Width - p.X, p.Y),
            _ => new(p.X, frame.Height - p.Y)
        };
        ScreenshotBox Box(ScreenshotBox b)
        {
            Vector2[] corners = [Point(new(b.X, b.Y)), Point(new(b.Right, b.Y)), Point(new(b.Right, b.Bottom)), Point(new(b.X, b.Bottom))];
            var left = corners.Min(p => p.X); var top = corners.Min(p => p.Y);
            return new(left, top, corners.Max(p => p.X) - left, corners.Max(p => p.Y) - top);
        }
        var rotate = change == ScreenshotOrientationChange.RotateClockwise;
        var annotations = state.Annotations.Select(a => a with
        {
            Bounds = Box(a.Bounds),
            Points = a.Points.IsDefault ? a.Points : a.Points.Select(Point).ToImmutableArray(),
            Rotation = rotate ? (a.Rotation + 90) % 360 : -a.Rotation,
            // Rasters are mirrored as pixels; text stays readable and only changes position.
            MirrorX = a.Tool == ScreenshotTool.Image && change == ScreenshotOrientationChange.FlipHorizontal ? !a.MirrorX : a.MirrorX,
            MirrorY = a.Tool == ScreenshotTool.Image && change == ScreenshotOrientationChange.FlipVertical ? !a.MirrorY : a.MirrorY,
            CalloutTip = a.Tool == ScreenshotTool.Callout ? Point(a.Tail) : a.CalloutTip,
            ClipBounds = a.ClipBounds is { } clip ? Box(clip) : null,
            SamplingBounds = a.SamplingBounds is { } sampling ? Box(sampling) : null,
            Cutouts = a.Cutouts.IsDefault ? a.Cutouts : a.Cutouts.Select(Box).ToImmutableArray()
        }).ToImmutableArray();
        return state with { Frame = Pixels(frame, change), Annotations = annotations };
    }

    private static ScreenshotFrame Pixels(ScreenshotFrame frame, ScreenshotOrientationChange change)
    {
        int width = frame.Width, height = frame.Height;
        var rotate = change == ScreenshotOrientationChange.RotateClockwise;
        var outputWidth = rotate ? height : width;
        var source = frame.Pixels.Span; var output = new byte[source.Length];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var (tx, ty) = change switch
                {
                    ScreenshotOrientationChange.RotateClockwise => (height - 1 - y, x),
                    ScreenshotOrientationChange.FlipHorizontal => (width - 1 - x, y),
                    _ => (x, height - 1 - y)
                };
                source.Slice((y * width + x) * 4, 4).CopyTo(output.AsSpan((ty * outputWidth + tx) * 4, 4));
            }
        return new(outputWidth, rotate ? width : height, output);
    }
}
