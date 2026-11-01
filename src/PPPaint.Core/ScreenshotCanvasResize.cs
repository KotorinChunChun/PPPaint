using System.Collections.Immutable;
using System.Numerics;

namespace PPPaint.Core;

public sealed record ScreenshotCanvasResize
{
    public int SourceWidth { get; }
    public int SourceHeight { get; }
    public int Width { get; }
    public int Height { get; }
    public int OffsetX { get; }
    public int OffsetY { get; }
    public Vector2 Offset => new(OffsetX, OffsetY);
    private ScreenshotCanvasResize(int sourceWidth, int sourceHeight, int width, int height, bool centered)
        : this(sourceWidth, sourceHeight, width, height, centered ? (width - sourceWidth) / 2 : 0, centered ? (height - sourceHeight) / 2 : 0)
    {
    }
    private ScreenshotCanvasResize(int sourceWidth, int sourceHeight, int width, int height, int offsetX, int offsetY)
    {
        ScreenshotFrame.ValidateSize(sourceWidth, sourceHeight); ScreenshotFrame.ValidateSize(width, height);
        SourceWidth = sourceWidth; SourceHeight = sourceHeight; Width = width; Height = height;
        // For odd differences, the right/bottom side gains or loses the extra pixel.
        OffsetX = offsetX; OffsetY = offsetY;
    }
    public static ScreenshotCanvasResize Create(ScreenshotState state, int width, int height, bool centered) => new(state.Frame.Width, state.Frame.Height, width, height, centered);
    public static ScreenshotCanvasResize FromBounds(ScreenshotState state, PixelRect bounds)
    {
        ScreenshotFrame.ValidateSize(bounds.Width, bounds.Height);
        if (bounds.X >= state.Frame.Width || bounds.Y >= state.Frame.Height || bounds.Right <= 0 || bounds.Bottom <= 0)
            throw new ArgumentOutOfRangeException(nameof(bounds), "変更後のキャンバスは元の画像と重なる必要があります。");
        return new(state.Frame.Width, state.Frame.Height, bounds.Width, bounds.Height, checked(-bounds.X), checked(-bounds.Y));
    }
    public void ValidateSource(ScreenshotState state)
    {
        if (state.Frame.Width != SourceWidth || state.Frame.Height != SourceHeight) throw new ArgumentException("プレビュー元のキャンバス寸法が変わりました。", nameof(state));
    }
    public ScreenshotState Apply(ScreenshotState state)
    {
        ValidateSource(state);
        if (Width == SourceWidth && Height == SourceHeight && OffsetX == 0 && OffsetY == 0) return state;
        var canvas = new ScreenshotBox(0, 0, Width, Height);
        return state with
        {
            Frame = state.Frame.Reframe(Width, Height, OffsetX, OffsetY),
            Annotations = state.Annotations.Select(a => a.Move(Offset)).Where(a => ScreenshotEditing.DrawingBounds(a).Intersects(canvas)).ToImmutableArray()
        };
    }
}
