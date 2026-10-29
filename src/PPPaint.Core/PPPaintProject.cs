namespace PPPaint.Core;

public sealed record PPPaintProject(
    int Width,
    int Height,
    int ActiveLayerIndex,
    IReadOnlyList<PPPaintProjectLayer> Layers,
    IReadOnlyList<EditableObject>? Objects = null,
    double Dpi = 96);

public sealed record PPPaintProjectLayer(
    string Name,
    bool IsVisible,
    byte[] Pixels,
    string? Id = null,
    bool IsLocked = false,
    float Opacity = 1f,
    byte[]? Mask = null,
    LayerBlendMode BlendMode = LayerBlendMode.Normal,
    string? Group = null,
    PhotoAdjustmentSettings? Adjustment = null);
