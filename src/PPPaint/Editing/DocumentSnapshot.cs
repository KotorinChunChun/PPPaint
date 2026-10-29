namespace PPPaint.Editing;

internal sealed record LayerSnapshot(string Name, bool IsVisible, PPPaint.Core.PixelBlockSnapshot PixelBlocks, string? Id = null,
    bool IsLocked = false, float Opacity = 1f,
    PPPaint.Core.PixelBlockSnapshot? MaskBlocks = null,
    PPPaint.Core.LayerBlendMode BlendMode = PPPaint.Core.LayerBlendMode.Normal,
    string? Group = null,
    PPPaint.Core.PhotoAdjustmentSettings? Adjustment = null)
{
    public byte[] Pixels => PixelBlocks.ToArray();
    public byte[]? Mask => MaskBlocks?.ToArray();
}

internal sealed record DocumentSnapshot(
    int Width,
    int Height,
    int ActiveLayerIndex,
    IReadOnlyList<LayerSnapshot> Layers,
    IReadOnlyList<PPPaint.Core.EditableObject>? Objects = null,
    double Dpi = 96);
