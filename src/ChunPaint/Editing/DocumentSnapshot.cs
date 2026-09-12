namespace ChunPaint.Editing;

internal sealed record LayerSnapshot(string Name, bool IsVisible, ChunPaint.Core.PixelBlockSnapshot PixelBlocks, string? Id = null,
    bool IsLocked = false, float Opacity = 1f)
{
    public byte[] Pixels => PixelBlocks.ToArray();
}

internal sealed record DocumentSnapshot(
    int Width,
    int Height,
    int ActiveLayerIndex,
    IReadOnlyList<LayerSnapshot> Layers,
    IReadOnlyList<ChunPaint.Core.EditableObject>? Objects = null,
    double Dpi = 96);
