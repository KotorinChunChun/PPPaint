namespace ChunPaint.Core;

public sealed record ChunPaintProject(
    int Width,
    int Height,
    int ActiveLayerIndex,
    IReadOnlyList<ChunPaintProjectLayer> Layers,
    IReadOnlyList<EditableObject>? Objects = null,
    double Dpi = 96);

public sealed record ChunPaintProjectLayer(
    string Name,
    bool IsVisible,
    byte[] Pixels,
    string? Id = null,
    bool IsLocked = false,
    float Opacity = 1f);
