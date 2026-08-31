using Microsoft.Graphics.Canvas;

namespace ChunPaint.Editing;

internal sealed class LayerState(string name, CanvasRenderTarget surface, bool isVisible = true, string? id = null) : IDisposable
{
    public string Id { get; } = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
    public string Name { get; set; } = name;
    public CanvasRenderTarget Surface { get; set; } = surface;
    public bool IsVisible { get; set; } = isVisible;

    public void Dispose() => Surface.Dispose();
}
