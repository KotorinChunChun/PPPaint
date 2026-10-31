using System.Collections.Immutable;
using System.Numerics;

namespace PPPaint.Core;

public enum ScreenshotJoinDirection { Top, Bottom, Left, Right }

public sealed record ScreenshotJoinResult(ScreenshotState State, ImmutableArray<string> AddedIds);

public static class ScreenshotJoining
{
    public static ScreenshotJoinResult Join(ScreenshotState destination, ScreenshotTransfer source, ScreenshotJoinDirection direction)
    {
        ArgumentNullException.ThrowIfNull(destination); ArgumentNullException.ThrowIfNull(source);
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        var oldFrame = destination.Frame; var incoming = source.Scene.Frame;
        var vertical = direction is ScreenshotJoinDirection.Top or ScreenshotJoinDirection.Bottom;
        var width = vertical ? Math.Max(oldFrame.Width, incoming.Width) : checked(oldFrame.Width + incoming.Width);
        var height = vertical ? checked(oldFrame.Height + incoming.Height) : Math.Max(oldFrame.Height, incoming.Height);
        // Reject invalid output before allocating a new frame or copying clipboard objects.
        ScreenshotFrame.ValidateSize(width, height);

        var oldOrigin = direction switch
        {
            ScreenshotJoinDirection.Top => new Vector2(0, incoming.Height),
            ScreenshotJoinDirection.Left => new Vector2(incoming.Width, 0),
            _ => Vector2.Zero
        };
        var newOrigin = direction switch
        {
            ScreenshotJoinDirection.Bottom => new Vector2(0, oldFrame.Height),
            ScreenshotJoinDirection.Right => new Vector2(oldFrame.Width, 0),
            _ => Vector2.Zero
        };
        var oldPane = new ScreenshotBox(oldOrigin.X, oldOrigin.Y, oldFrame.Width, oldFrame.Height);
        var newPane = new ScreenshotBox(newOrigin.X, newOrigin.Y, incoming.Width, incoming.Height);
        var originalPane = new ScreenshotBox(0, 0, oldFrame.Width, oldFrame.Height);
        var incomingPane = new ScreenshotBox(0, 0, incoming.Width, incoming.Height);
        var existing = destination.Annotations.Select(item => LimitSampling(item, originalPane))
            .Select(item => ClipToPane(oldOrigin == Vector2.Zero ? item : item.Move(oldOrigin), oldPane));
        var preparedSource = source with
        {
            Scene = source.Scene with
            {
                Annotations = source.Scene.Annotations.Select(item =>
                {
                    var limited = LimitSampling(item, incomingPane);
                    return limited.Tool == ScreenshotTool.Eraser && limited.EraserBackground is null
                        ? limited with { EraserBackground = source.Scene.Background } : limited;
                }).ToImmutableArray()
            }
        };
        // Origin records where the selection was copied from, not where a joined pane starts.
        var added = preparedSource.ObjectsAt(newOrigin).Select(item => ClipToPane(item, newPane)).ToImmutableArray();
        var state = destination with
        {
            Frame = oldFrame.Reframe(width, height, (int)oldOrigin.X, (int)oldOrigin.Y),
            Annotations = existing.Concat(added).ToImmutableArray()
        };
        return new(state, added.Select(item => item.Id).ToImmutableArray());
    }

    private static ScreenshotAnnotation LimitSampling(ScreenshotAnnotation item, ScreenshotBox pane) =>
        item.Tool is ScreenshotTool.Blur or ScreenshotTool.Mosaic
            ? item with { SamplingBounds = item.SamplingBounds is { } previous
                ? ScreenshotEditing.Intersection(previous, pane) ?? new ScreenshotBox(pane.X, pane.Y, 0, 0) : pane }
            : item;

    private static ScreenshotAnnotation ClipToPane(ScreenshotAnnotation item, ScreenshotBox pane)
    {
        // Keep even completely hidden objects and their order/IDs. An empty clip
        // prevents them reappearing when a larger canvas is created by the join.
        var clip = item.ClipBounds is { } previous
            ? ScreenshotEditing.Intersection(previous, pane) ?? new ScreenshotBox(pane.X, pane.Y, 0, 0)
            : pane;
        return item with { ClipBounds = clip };
    }
}
