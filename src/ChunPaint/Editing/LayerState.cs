using Microsoft.Graphics.Canvas;

namespace ChunPaint.Editing;

internal sealed class LayerState(string name, CanvasRenderTarget surface, bool isVisible = true) : IDisposable
{
    public string Name { get; set; } = name;
    public CanvasRenderTarget Surface { get; set; } = surface;
    public bool IsVisible { get; set; } = isVisible;

    public void Dispose() => Surface.Dispose();
}
