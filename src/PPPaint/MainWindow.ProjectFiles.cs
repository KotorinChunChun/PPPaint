using PPPaint.Core;
using PPPaint.Editing;
using PPPaint.Imaging;
using Microsoft.Graphics.Canvas;
using Windows.Storage;

namespace PPPaint;

public sealed partial class MainWindow
{
    private static bool IsPPPaintProject(StorageFile file) =>
        DocumentFileFormats.IsProject(file.Name);

    private async Task OpenPPPaintProjectAsync(StorageFile file)
    {
        CommitFloatingSelection();
        PPPaintProject project;
        await using (var stream = await file.OpenStreamForReadAsync())
        {
            project = PPPaintProjectArchive.Load(stream);
        }

        var restoredLayers = new List<LayerState>(project.Layers.Count);
        try
        {
            foreach (var savedLayer in project.Layers)
            {
                var surface = new CanvasRenderTarget(PaintCanvas, project.Width, project.Height, 96);
                surface.SetPixelBytes(savedLayer.Pixels);
                restoredLayers.Add(new LayerState(savedLayer.Name, surface, savedLayer.IsVisible, savedLayer.Id)
                    { IsLocked = savedLayer.IsLocked, Opacity = savedLayer.Opacity, Mask = savedLayer.Mask, BlendMode = savedLayer.BlendMode, Group = savedLayer.Group, Adjustment = savedLayer.Adjustment });
            }
        }
        catch
        {
            foreach (var layer in restoredLayers)
            {
                layer.Dispose();
            }
            throw;
        }

        foreach (var layer in _layers)
        {
            layer.Dispose();
        }
        _layers.Clear();
        _layers.AddRange(restoredLayers);
        _imageWidth = project.Width;
        _imageHeight = project.Height;
        _imageDpi = project.Dpi;
        _activeLayerIndex = project.ActiveLayerIndex;
        _surface = _layers[_activeLayerIndex].Surface;
        _objects.Clear();
        _objects.AddRange(project.Objects ?? []);
        NormalizeObjectLayerIds();
        _activeObjectId = null;
        _activeObjectOriginal = null;
        _selection = null;
        _currentFile = file;
        AddRecentFile(file);
        _layerCount = Math.Max(_layerCount, _layers.Count);
        _undo.Clear();
        _redo.Clear();
        _dirty = false;
        ApplyCanvasSize();
        UpdateHistoryState();
        RefreshLayersPanel();
        UpdateTitle();
        PaintCanvas.Invalidate();
    }

    private async Task<bool> SavePPPaintProjectAsync(StorageFile file)
    {
        var project = CaptureProject();

        await DocumentFileWriter.WriteAsync(file, async randomAccessStream =>
        {
            var stream = randomAccessStream.AsStreamForWrite();
            await Task.Run(() => PPPaintProjectArchive.Save(stream, project));
            await stream.FlushAsync();
        });

        _currentFile = file;
        _dirty = false;
        if (_activeDocument is not null) DeleteRecovery(_activeDocument.Id);
        AddRecentFile(file);
        UpdateTitle();
        return true;
    }

    private PPPaintProject CaptureProject()
    {
        var project = new PPPaintProject(
            _imageWidth,
            _imageHeight,
            _activeLayerIndex,
            _layers.Select(layer => new PPPaintProjectLayer(
                layer.Name,
                layer.IsVisible,
                layer.Surface.GetPixelBytes(),
                layer.Id, layer.IsLocked, layer.Opacity, layer.Mask, layer.BlendMode, layer.Group, layer.Adjustment)).ToArray(),
            _objects.ToArray(), _imageDpi);
        // 浮動選択は画面を確定せず、バックアップのコピーだけに合成する。
        if (_floatingSelectionPixels is not null && _selection is not null)
        {
            var pixels = new PixelSurface(_imageWidth, _imageHeight, Rgba32.Transparent);
            pixels.Restore(PixelAlpha.PremultipliedToStraight(project.Layers[_activeLayerIndex].Pixels));
            SelectionOperations.Apply(pixels, _selection, _floatingSelectionPixels);
            var layers = project.Layers.ToArray();
            layers[_activeLayerIndex] = layers[_activeLayerIndex] with { Pixels = PixelAlpha.StraightToPremultiplied(pixels.Snapshot()) };
            project = project with { Layers = layers };
        }
        return project;
    }
}
