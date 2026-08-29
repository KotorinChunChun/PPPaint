namespace ChunPaint.Core;

public sealed record ChunPaintProject(
    int Width,
    int Height,
    int ActiveLayerIndex,
    IReadOnlyList<ChunPaintProjectLayer> Layers);

public sealed record ChunPaintProjectLayer(
    string Name,
    bool IsVisible,
    byte[] Pixels);
