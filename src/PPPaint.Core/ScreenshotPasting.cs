using System.Collections.Immutable;
using System.Numerics;

namespace PPPaint.Core;

public sealed record ScreenshotPasteResult(ScreenshotState State, ImmutableArray<string> AddedIds);

public static class ScreenshotPasting
{
    public static ScreenshotPasteResult Overlay(ScreenshotState destination, ScreenshotTransfer source, Vector2? position = null)
    {
        ArgumentNullException.ThrowIfNull(destination); ArgumentNullException.ThrowIfNull(source);
        var origin = position ?? source.Origin;
        if (!float.IsFinite(origin.X) || !float.IsFinite(origin.Y)) throw new ArgumentOutOfRangeException(nameof(position));
        var width = Math.Max(destination.Frame.Width, checked((int)Math.Ceiling((double)origin.X + source.Scene.Frame.Width)));
        var height = Math.Max(destination.Frame.Height, checked((int)Math.Ceiling((double)origin.Y + source.Scene.Frame.Height)));
        // Validate before copying any raster or objects. Overlay only adds space;
        // even a pristine, transparent canvas keeps its current dimensions.
        ScreenshotFrame.ValidateSize(width, height);
        var added = source.ObjectsAt(origin);
        return new(destination with
        {
            Frame = destination.Frame.Expand(width, height),
            Annotations = destination.Annotations.AddRange(added)
        }, added.Select(item => item.Id).ToImmutableArray());
    }
}
