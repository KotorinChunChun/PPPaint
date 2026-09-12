using ChunPaint.Core;
using ChunPaint.Editing;
using ChunPaint.Imaging;
using Microsoft.Graphics.Canvas;
using Windows.Storage;

namespace ChunPaint;

public sealed partial class MainWindow
{
    private static bool IsChunPaintProject(StorageFile file) =>
        Path.GetExtension(file.Name).Equals(".chunpaint", StringComparison.OrdinalIgnoreCase);

    private async Task OpenChunPaintProjectAsync(StorageFile file)
    {
        CommitFloatingSelection();
        ChunPaintProject project;
        await using (var stream = await file.OpenStreamForReadAsync())
        {
            project = ChunPaintProjectArchive.Load(stream);
        }

        var restoredLayers = new List<LayerState>(project.Layers.Count);
        try
        {
            foreach (var savedLayer in project.Layers)
            {
                var surface = new CanvasRenderTarget(PaintCanvas, project.Width, project.Height, 96);
                surface.SetPixelBytes(savedLayer.Pixels);
                restoredLayers.Add(new LayerState(savedLayer.Name, surface, savedLayer.IsVisible, savedLayer.Id)
                    { IsLocked = savedLayer.IsLocked, Opacity = savedLayer.Opacity });
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

    private async Task<bool> SaveChunPaintProjectAsync(StorageFile file)
    {
        var project = CaptureProject();

        await DocumentFileWriter.WriteAsync(file, async randomAccessStream =>
        {
            var stream = randomAccessStream.AsStreamForWrite();
            await Task.Run(() => ChunPaintProjectArchive.Save(stream, project));
            await stream.FlushAsync();
        });

        _currentFile = file;
        _dirty = false;
        if (_activeDocument is not null) DeleteRecovery(_activeDocument.Id);
        AddRecentFile(file);
        UpdateTitle();
        return true;
    }

    private ChunPaintProject CaptureProject()
    {
        var project = new ChunPaintProject(
            _imageWidth,
            _imageHeight,
            _activeLayerIndex,
            _layers.Select(layer => new ChunPaintProjectLayer(
                layer.Name,
                layer.IsVisible,
                layer.Surface.GetPixelBytes(),
                layer.Id, layer.IsLocked, layer.Opacity)).ToArray(),
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
