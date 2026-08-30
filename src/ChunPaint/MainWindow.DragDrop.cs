using ChunPaint.Core;
using ChunPaint.Editing;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.UI;

namespace ChunPaint;

public sealed partial class MainWindow
{
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

            if (!await ConfirmDiscardAsync())
            {
                return;
            }
            CommitFloatingSelection();

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
        var fileProperties = await file.GetBasicPropertiesAsync();
        DocumentFileFormats.ValidateInputSize((long)fileProperties.Size);
        if (IsChunPaintProject(file))
        {
            await OpenChunPaintProjectAsync(file);
            AddRecentFile(file);
            return;
        }

        using var stream = await file.OpenReadAsync();
        using var bitmap = await CanvasBitmap.LoadAsync(PaintCanvas, stream);
        if (!import)
        {
            _imageWidth = (int)bitmap.SizeInPixels.Width;
            _imageHeight = (int)bitmap.SizeInPixels.Height;
            foreach (var layer in _layers)
            {
                layer.Dispose();
            }
            _layers.Clear();
            _surface = new CanvasRenderTarget(PaintCanvas, _imageWidth, _imageHeight, 96);
            _layers.Add(new LayerState("背景", _surface));
            _activeLayerIndex = 0;
            _objects.Clear();
            _activeObjectId = null;
            _activeObjectOriginal = null;
            _selection = null;
            _currentFile = file;
            _undo.Clear();
            _redo.Clear();
        }
        else
        {
            EnsureSurface(PaintCanvas);
            CaptureUndo();
        }

        using (var session = _surface!.CreateDrawingSession())
        {
            if (!import)
            {
                session.Clear(Colors.White);
            }
            session.DrawImage(bitmap);
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
