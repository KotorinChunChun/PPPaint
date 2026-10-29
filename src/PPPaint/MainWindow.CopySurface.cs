using PPPaint.Core;
using PPPaint.Editing;
using Microsoft.Graphics.Canvas;

namespace PPPaint;

public sealed partial class MainWindow
{
    private PixelSurface CaptureCopySource(bool visibleLayers)
    {
        // 画面・書出と同じ合成。浮動選択は共通処理内でコピーだけへ反映する。
        var layers = visibleLayers ? _layers.ToArray() : new[] { _layers[_activeLayerIndex] };
        using var composite = CreateStackComposite(layers);
        return ToPixelSurface(composite);
    }
}
