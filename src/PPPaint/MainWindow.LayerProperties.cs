using PPPaint.Editing;
using PPPaint.Core;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace PPPaint;

public sealed partial class MainWindow
{
    private bool CanEditActiveLayer => (uint)_activeLayerIndex < _layers.Count && !_layers[_activeLayerIndex].IsLocked && _layers[_activeLayerIndex].Adjustment is null;
    private CanvasRenderTarget? _stackCache;
    private int _stackCacheStamp;
    private long _stackRevision;
    private bool IsFloatingSelectionInStack => _floatingSelectionPixels is not null && _selection is not null && (uint)_activeLayerIndex < _layers.Count;

    private bool HasFloatingLayer(IReadOnlyList<LayerState> layers) => IsFloatingSelectionInStack && layers.Any(layer => ReferenceEquals(layer, _layers[_activeLayerIndex]));

    private void DisposeStackCache()
    {
        _stackCache?.Dispose(); _stackCache = null; _stackCacheStamp = 0;
        _stackRevision++;
    }

    private static bool NeedsStackComposition(IEnumerable<LayerState> layers) => layers.Any(layer => layer.Mask is not null || layer.BlendMode != LayerBlendMode.Normal || layer.Adjustment is not null);

    private CanvasRenderTarget CreateStackComposite(IReadOnlyList<LayerState> layers)
    {
        if (!NeedsStackComposition(layers) && !HasFloatingLayer(layers))
        {
            var target = new CanvasRenderTarget(PaintCanvas, _imageWidth, _imageHeight, 96);
            using var drawing = target.CreateDrawingSession(); drawing.Clear(Microsoft.UI.Colors.Transparent);
            foreach (var layer in layers) DrawLayerComposite(drawing, layer);
            return target;
        }
        var result = new PixelSurface(_imageWidth, _imageHeight, Rgba32.Transparent);
        foreach (var layer in layers)
        {
            if (!layer.IsVisible || layer.Opacity <= 0) continue;
            if (layer.Adjustment is { } settings)
            {
                var adjusted = PhotoAdjustments.Apply(result, settings);
                for (var y = 0; y < _imageHeight; y++) for (var x = 0; x < _imageWidth; x++)
                {
                    var mix = layer.Opacity * (layer.Mask is null ? 1 : layer.Mask[y * _imageWidth + x] / 255f);
                    var a = result[x, y]; var b = adjusted[x, y];
                    byte Channel(byte old, byte value)
                    {
                        var cb = old / 255d; var cf = value / 255d;
                        var blended = layer.BlendMode switch
                        {
                            LayerBlendMode.Multiply => cb * cf,
                            LayerBlendMode.Screen => cb + cf - cb * cf,
                            LayerBlendMode.Overlay => cb <= .5 ? 2 * cb * cf : 1 - 2 * (1 - cb) * (1 - cf),
                            _ => cf
                        };
                        return (byte)Math.Clamp(Math.Round((cb + (blended - cb) * mix) * 255), 0, 255);
                    }
                    result[x, y] = new Rgba32(Channel(a.R, b.R), Channel(a.G, b.G), Channel(a.B, b.B), a.A);
                }
            }
            else
            {
                var source = IsFloatingSelectionInStack && ReferenceEquals(layer, _layers[_activeLayerIndex])
                    ? GetFloatingLayerSource(layer) : GetAdjustmentSource(layer);
                result = LayerCompositor.Composite(result, source, layer.Opacity, layer.BlendMode, layer.Mask);
            }
        }
        return ToRenderTarget(result);
    }

    private void DrawDocumentStack(CanvasDrawingSession drawing)
    {
        if (!NeedsStackComposition(_layers) && !IsFloatingSelectionInStack)
        {
            if (_stackCache is not null) DisposeStackCache();
            foreach (var layer in _layers) DrawLayerComposite(drawing, layer, (float)_zoom);
            return;
        }
        var hash = new HashCode(); hash.Add(_stackRevision); hash.Add(_imageWidth); hash.Add(_imageHeight);
        foreach (var layer in _layers)
        {
            hash.Add(layer.Surface); hash.Add(layer.Mask); hash.Add(layer.BlendMode); hash.Add(layer.Adjustment); hash.Add(layer.Opacity); hash.Add(layer.IsVisible);
        }
        foreach (var item in _objects) hash.Add(item);
        var stamp = hash.ToHashCode();
        if (_stackCache is null || _stackCacheStamp != stamp || _floatingSelectionPixels is not null)
        {
            var replacement = CreateStackComposite(_layers);
            _stackCache?.Dispose(); _stackCache = replacement; _stackCacheStamp = stamp;
        }
        drawing.DrawImage(_stackCache, new Rect(0, 0, _imageWidth * _zoom, _imageHeight * _zoom), new Rect(0, 0, _imageWidth, _imageHeight));
    }

    private PixelSurface GetFloatingLayerSource(LayerState layer)
    {
        var pixels = ToPixelSurface(layer.Surface);
        // coverageはExtract済みの画素alphaに含まれる。Applyは二重にcoverageを掛けない。
        SelectionOperations.Apply(pixels, _selection!, _floatingSelectionPixels!);
        using var raster = ToRenderTarget(pixels);
        using var target = new CanvasRenderTarget(PaintCanvas, _imageWidth, _imageHeight, 96);
        using (var drawing = target.CreateDrawingSession())
        {
            drawing.Clear(Microsoft.UI.Colors.Transparent);
            drawing.DrawImage(raster);
            // 浮動画素を確定した場合と同じく、所属オブジェクトは画素面の上へ描く。
            DrawEditableObjects(drawing, layer);
        }
        return ToPixelSurface(target);
    }

    private static PixelSurface MaskSurface(byte[] mask, int width, int height)
    {
        var pixels = new PixelSurface(width, height, Rgba32.Transparent);
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) { var value = mask[y * width + x]; pixels[x, y] = new Rgba32(value, value, value, 255); }
        return pixels;
    }

    private static byte[] SurfaceMask(PixelSurface pixels)
    {
        var mask = new byte[checked(pixels.Width * pixels.Height)];
        for (var y = 0; y < pixels.Height; y++) for (var x = 0; x < pixels.Width; x++) mask[y * pixels.Width + x] = pixels[x, y].R;
        return mask;
    }

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
        if (layer.Mask is not null || layer.Adjustment is not null || layer.BlendMode != LayerBlendMode.Normal)
        {
            using var rendered = CreateStackComposite(new[] { layer });
            session.DrawImage(rendered, new Rect(0, 0, _imageWidth * zoom, _imageHeight * zoom), new Rect(0, 0, _imageWidth, _imageHeight));
            return;
        }
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
            if (await dialog.ShowManagedAsync() != ContentDialogResult.Primary) return;
            var opacity = (float)(slider.Value / 100);
            if (Math.Abs(layer.Opacity - opacity) < .0001f) return;
            CaptureUndo();
            layer.Opacity = opacity;
            FinishEdit();
        }
        finally { _documentBusy = false; RefreshDocumentTabs(); }
    }
}
