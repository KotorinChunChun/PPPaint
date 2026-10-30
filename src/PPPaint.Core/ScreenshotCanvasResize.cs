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
    {
        ScreenshotFrame.ValidateSize(sourceWidth, sourceHeight); ScreenshotFrame.ValidateSize(width, height);
        SourceWidth = sourceWidth; SourceHeight = sourceHeight; Width = width; Height = height;
        // For odd differences, the right/bottom side gains or loses the extra pixel.
        OffsetX = centered ? (width - sourceWidth) / 2 : 0; OffsetY = centered ? (height - sourceHeight) / 2 : 0;
    }
    public static ScreenshotCanvasResize Create(ScreenshotState state, int width, int height, bool centered) => new(state.Frame.Width, state.Frame.Height, width, height, centered);
    public void ValidateSource(ScreenshotState state)
    {
        if (state.Frame.Width != SourceWidth || state.Frame.Height != SourceHeight) throw new ArgumentException("プレビュー元のキャンバス寸法が変わりました。", nameof(state));
    }
    public ScreenshotState Apply(ScreenshotState state)
    {
        ValidateSource(state);
        if (Width == SourceWidth && Height == SourceHeight) return state;
        var canvas = new ScreenshotBox(0, 0, Width, Height);
        return state with
        {
            Frame = state.Frame.Reframe(Width, Height, OffsetX, OffsetY),
            Annotations = state.Annotations.Select(a => a.Move(Offset)).Where(a => ScreenshotEditing.DrawingBounds(a).Intersects(canvas)).ToImmutableArray()
        };
    }
}
