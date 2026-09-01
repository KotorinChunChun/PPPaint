using ChunPaint.Core;
using ChunPaint.Editing;
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
                restoredLayers.Add(new LayerState(savedLayer.Name, surface, savedLayer.IsVisible, savedLayer.Id));
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
        _imageDpi = ImageMeasurements.DefaultDpi;
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
        var project = new ChunPaintProject(
            _imageWidth,
            _imageHeight,
            _activeLayerIndex,
            _layers.Select(layer => new ChunPaintProjectLayer(
                layer.Name,
                layer.IsVisible,
                layer.Surface.GetPixelBytes(),
                layer.Id)).ToArray(),
            _objects.ToArray());

        await using (var stream = await file.OpenStreamForWriteAsync())
        {
            stream.SetLength(0);
            ChunPaintProjectArchive.Save(stream, project);
            await stream.FlushAsync();
        }

        _currentFile = file;
        _dirty = false;
        AddRecentFile(file);
        UpdateTitle();
        return true;
    }
}
