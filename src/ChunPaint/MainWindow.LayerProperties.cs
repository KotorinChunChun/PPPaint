using ChunPaint.Editing;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace ChunPaint;

public sealed partial class MainWindow
{
    private bool CanEditActiveLayer => (uint)_activeLayerIndex < _layers.Count && !_layers[_activeLayerIndex].IsLocked;

    private CanvasRenderTarget CreateLayerContent(LayerState layer)
    {
        var target = new CanvasRenderTarget(PaintCanvas, _imageWidth, _imageHeight, 96);
        try
        {
            using var session = target.CreateDrawingSession();
            session.Clear(Microsoft.UI.Colors.Transparent);
            session.DrawImage(layer.Surface);
            DrawEditableObjects(session, layer);
            return target;
        }
        catch { target.Dispose(); throw; }
    }

    private void DrawLayerComposite(CanvasDrawingSession session, LayerState layer, float zoom = 1f)
    {
        if (!layer.IsVisible || layer.Opacity <= 0) return;
        // ラスターのみの層は既存Surfaceを利用し、描画ごとのGPU面確保を避ける。
        if (!_objects.Any(item => item.LayerId == layer.Id))
        {
            session.DrawImage(layer.Surface, new Rect(0, 0, _imageWidth * zoom, _imageHeight * zoom),
                new Rect(0, 0, _imageWidth, _imageHeight), layer.Opacity);
            return;
        }
        using var content = CreateLayerContent(layer);
        session.DrawImage(content, new Rect(0, 0, _imageWidth * zoom, _imageHeight * zoom),
            new Rect(0, 0, _imageWidth, _imageHeight), layer.Opacity);
    }

    private void ToggleActiveLayerLock()
    {
        if (_documentBusy || _layers.Count == 0) return;
        CommitFloatingSelection();
        CommitEditableObjectForDocumentSwitch();
        CaptureUndo();
        _layers[_activeLayerIndex].IsLocked = !_layers[_activeLayerIndex].IsLocked;
        FinishEdit();
    }

    private async Task ChangeActiveLayerOpacityAsync()
    {
        if (_documentBusy || _layers.Count == 0) return;
        CommitFloatingSelection();
        var layer = _layers[_activeLayerIndex];
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = layer.Opacity * 100, StepFrequency = 1, MinWidth = 280 };
        ConfigureFieldAccessibility(slider, "レイヤーの不透明度", "0は透明、100は不透明です");
        var dialog = new ContentDialog
        {
            XamlRoot = PaintCanvas.XamlRoot, Title = "レイヤーの不透明度 (%)", Content = slider,
            PrimaryButtonText = "適用", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Primary
        };
        ConfigureDialogAccessibility(dialog);
        _documentBusy = true;
        RefreshDocumentTabs();
        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            var opacity = (float)(slider.Value / 100);
            if (Math.Abs(layer.Opacity - opacity) < .0001f) return;
            CaptureUndo();
            layer.Opacity = opacity;
            FinishEdit();
        }
        finally { _documentBusy = false; RefreshDocumentTabs(); }
    }
}
