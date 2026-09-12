using ChunPaint.Core;
using ChunPaint.Editing;
using ChunPaint.Imaging;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.UI;

namespace ChunPaint;

public sealed partial class MainWindow
{
    internal async Task OpenStartupFileAsync(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            await LoadStorageFileAsync(file, import: false);
        }
        catch (Exception exception)
        {
            await ShowInfoAsync("ファイルを開く", $"起動時に指定されたファイルを開けませんでした。\n{exception.Message}");
        }
    }

    private void Document_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "ChunPaint で開く";
        e.DragUIOverride.IsCaptionVisible = true;
        e.Handled = true;
    }

    private async void Document_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return;
            }

            e.Handled = true;
            var items = await e.DataView.GetStorageItemsAsync();
            var file = items
                .OfType<StorageFile>()
                .FirstOrDefault(candidate => DocumentFileFormats.IsSupported(candidate.Name));
            if (file is null)
            {
                await ShowInfoAsync("ファイルを開く", "PNG、JPEG、BMP、GIF、ChunPaintプロジェクトをドロップしてください。");
                return;
            }

            await LoadStorageFileAsync(file, import: false);
        }
        catch (OperationCanceledException)
        {
            // Canceled data transfer or load leaves the current document untouched.
        }
        catch (Exception exception)
        {
            await ShowInfoAsync("ファイルを開く", $"ファイルを開けませんでした。\n{exception.Message}");
        }
    }

    private async Task LoadStorageFileAsync(StorageFile file, bool import)
    {
        if (_documentBusy || (import && !CanEditActiveLayer)) return;
        var previous = _activeDocument;
        StoreActiveDocument();
        if (!import)
        {
            _activeDocument = new DocumentSession();
            _documents.Add(_activeDocument);
        }
        _documentBusy = true;
        RefreshDocumentTabs();
        try { await LoadStorageFileCoreAsync(file, import); }
        catch
        {
            if (!import && previous is not null)
            {
                var failed = _activeDocument;
                _documentBusy = false;
                // 破損入力の状態を元のタブへ保存しない。
                _activeDocument = null;
                SwitchDocument(previous);
                if (failed is not null) _documents.Remove(failed);
            }
            throw;
        }
        finally { _documentBusy = false; RefreshDocumentTabs(); }
    }

    private async Task LoadStorageFileCoreAsync(StorageFile file, bool import)
    {
        var fileProperties = await file.GetBasicPropertiesAsync();
        DocumentFileFormats.ValidateInputSize((long)fileProperties.Size);
        if (IsChunPaintProject(file))
        {
            await OpenChunPaintProjectAsync(file);
            AddRecentFile(file);
            return;
        }

        using var stream = await file.OpenReadAsync();
        using var bitmap = await Win2DImageCodec.LoadAsync(PaintCanvas, stream);
        if (!import)
        {
            var width = checked((int)bitmap.SizeInPixels.Width);
            var height = checked((int)bitmap.SizeInPixels.Height);
            if ((long)width * height * 4 > PixelSurface.MaximumPixelBytes) throw new InvalidDataException("画像寸法が上限を超えています。");
            var replacement = new CanvasRenderTarget(PaintCanvas, width, height, 96);
            try
            {
                using var drawing = replacement.CreateDrawingSession();
                drawing.Clear(Colors.Transparent);
                drawing.DrawImage(bitmap);
            }
            catch { replacement.Dispose(); throw; }
            foreach (var layer in _layers) layer.Dispose();
            _layers.Clear();
            _surface = replacement;
            _layers.Add(new LayerState("背景", replacement));
            _imageWidth = width; _imageHeight = height;
            _imageDpi = ImageMeasurements.NormalizeDpi(bitmap.Dpi);
            _activeLayerIndex = 0;
            _objects.Clear(); _activeObjectId = null; _activeObjectOriginal = null;
            _selection = null; _currentFile = file; _undo.Clear(); _redo.Clear();
        }
        else
        {
            EnsureSurface(PaintCanvas);
            var replacement = new CanvasRenderTarget(PaintCanvas, _imageWidth, _imageHeight, 96);
            try
            {
                using var drawing = replacement.CreateDrawingSession();
                drawing.Clear(Colors.Transparent);
                drawing.DrawImage(_surface!);
                drawing.DrawImage(bitmap);
            }
            catch { replacement.Dispose(); throw; }
            CaptureUndo();
            var layer = _layers[_activeLayerIndex];
            layer.Surface.Dispose(); layer.Surface = replacement; _surface = replacement;
        }
        _dirty = import;
        AddRecentFile(file);
        ApplyCanvasSize();
        UpdateHistoryState();
        UpdateTitle();
        RefreshLayersPanel();
        PaintCanvas.Invalidate();
    }
}
