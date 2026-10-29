using Microsoft.Graphics.Canvas;

namespace PPPaint.Editing;

internal sealed class LayerState(string name, CanvasRenderTarget surface, bool isVisible = true, string? id = null) : IDisposable
{
    public string Id { get; } = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
    public string Name { get; set; } = name;
    public CanvasRenderTarget Surface { get; set; } = surface;
    public bool IsVisible { get; set; } = isVisible;
    public bool IsLocked { get; set; }
    public float Opacity { get; set; } = 1f;
    public byte[]? Mask { get; set; }
    public PPPaint.Core.LayerBlendMode BlendMode { get; set; }
    public string? Group { get; set; }
    public PPPaint.Core.PhotoAdjustmentSettings? Adjustment { get; set; }

    public void Dispose() => Surface.Dispose();
}
