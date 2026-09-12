using ChunPaint.Core;
using ChunPaint.Editing;
using Microsoft.Graphics.Canvas;

namespace ChunPaint;

public sealed partial class MainWindow
{
    private PixelSurface CaptureCopySource(bool visibleLayers)
    {
        using var composite = new CanvasRenderTarget(PaintCanvas, _imageWidth, _imageHeight, 96);
        using (var drawing = composite.CreateDrawingSession())
        {
            drawing.Clear(Microsoft.UI.Colors.Transparent);
            foreach (var layer in visibleLayers ? _layers.AsEnumerable() : new[] { _layers[_activeLayerIndex] })
            {
                if (ReferenceEquals(layer, _layers[_activeLayerIndex]) && _floatingSelectionPixels is not null && _selection is not null)
                {
                    // 浮動選択は文書へ確定せず、コピー用画素にだけ適用する。
                    var pixels = ToPixelSurface(layer.Surface);
                    SelectionOperations.Apply(pixels, _selection, _floatingSelectionPixels);
                    var surface = ToRenderTarget(pixels);
                    using var copyLayer = new LayerState(layer.Name, surface, layer.IsVisible, layer.Id)
                        { Opacity = layer.Opacity, IsLocked = layer.IsLocked };
                    DrawLayerComposite(drawing, copyLayer);
                }
                else DrawLayerComposite(drawing, layer);
            }
        }
        return ToPixelSurface(composite);
    }
}
