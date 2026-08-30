namespace ChunPaint.Editing;

internal sealed record LayerSnapshot(string Name, bool IsVisible, byte[] Pixels);

internal sealed record DocumentSnapshot(
    int Width,
    int Height,
    int ActiveLayerIndex,
    IReadOnlyList<LayerSnapshot> Layers,
    IReadOnlyList<ChunPaint.Core.EditableObject>? Objects = null);
