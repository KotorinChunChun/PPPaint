using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PPPaint.Core;
using PPPaint.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PPPaint;

public sealed partial class EditorWindow
{
    private string? _sourceName;
    private bool _savedByThisWindow;
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"];
    private static bool IsImage(string path) => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    private async Task<ScreenshotFrame> ReadImageAsync(string path)
    {
        if (!IsImage(path)) throw new InvalidDataException("PNG、JPEG、BMP、GIF、WebP画像を指定してください。");
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
        using var stream = await file.OpenReadAsync();
        using var bitmap = await Win2DImageCodec.LoadAsync(PaintCanvas, stream);
        return new((int)bitmap.SizeInPixels.Width, (int)bitmap.SizeInPixels.Height, bitmap.GetPixelBytes());
    }
    private async Task OpenPathAsync(string path)
    {
        var frame = await ReadImageAsync(path);
        var window = !_hasImage && !Dirty ? this : App.Instance.CreateWindow(frame);
        window._document = new(frame); window._savedState = window._document.State; window._hasImage = true;
        window._sourceName = Path.GetFileName(path); window._savePath = Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase) ? Path.GetFullPath(path) : null;
        window._selectedId = null; window._region = null; window._savedByThisWindow = false; window.Refresh(); window.FitImage();
    }
    private async void Open_Click(object sender, RoutedEventArgs e) => await RunAsync(async () => { var path = await PickPathAsync(false); if (path is not null) await OpenPathAsync(path); });
    private async void Paste_Click(object sender, RoutedEventArgs e) => await RunAsync(PasteImageAsync);
    private async Task PasteImageAsync()
    {
        var content = Clipboard.GetContent();
        if (!content.Contains(StandardDataFormats.Bitmap)) { await MessageAsync("貼り付け", "クリップボードに画像がありません。"); return; }
        var reference = await content.GetBitmapAsync(); using var stream = await reference.OpenReadAsync();
        using var bitmap = await Win2DImageCodec.LoadAsync(PaintCanvas, stream);
        var frame = new ScreenshotFrame((int)bitmap.SizeInPixels.Width, (int)bitmap.SizeInPixels.Height, bitmap.GetPixelBytes());
        var window = !_hasImage && !Dirty ? this : App.Instance.CreateWindow(frame);
        window._document = new(frame); window._savedState = null; window._hasImage = true;
        window._sourceName = "クリップボード"; window._savePath = null; window._selectedId = null; window._region = null;
        window.Refresh(); window.FitImage();
    }
    private async void Copy_Click(object sender, RoutedEventArgs e) => await RunAsync(CopyImageAsync);
    private async Task CopyImageAsync()
    {
        using var target = ScreenshotRenderer.Render(PaintCanvas, _document.State);
        using var stream = new InMemoryRandomAccessStream();
        if (_region is { } region)
        {
            var x = (int)region.X; var y = (int)region.Y; var width = Math.Max(1, (int)region.Width); var height = Math.Max(1, (int)region.Height);
            var frame = new ScreenshotFrame((int)target.SizeInPixels.Width, (int)target.SizeInPixels.Height, target.GetPixelBytes()).Crop(x, y, width, height);
            using var cropped = ScreenshotRenderer.Bitmap(PaintCanvas, frame);
            await Win2DImageCodec.SaveAsync(cropped, stream, DocumentFileFormat.Png);
        }
        else await Win2DImageCodec.SaveAsync(target, stream, DocumentFileFormat.Png);
        stream.Seek(0); var data = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        data.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream)); Clipboard.SetContent(data); Clipboard.Flush();
        StatusText.Text = "画像をコピーしました。";
    }
    private async void Save_Click(object sender, RoutedEventArgs e) => await RunAsync(async () => await SaveImageAsync());
    private async Task<bool> SaveImageAsync()
    {
        var path = _savePath ?? await PickPathAsync(true);
        if (path is null) return false;
        if (File.Exists(path) && (!_savedByThisWindow || !string.Equals(path, _savePath, StringComparison.OrdinalIgnoreCase)))
        {
            if (await AskAsync("PNG保存", $"「{Path.GetFileName(path)}」を上書きしますか？", "上書き") != ContentDialogResult.Primary) return false;
        }
        using var target = ScreenshotRenderer.Render(PaintCanvas, _document.State);
        using var stream = new InMemoryRandomAccessStream(); await Win2DImageCodec.SaveAsync(target, stream, DocumentFileFormat.Png);
        using var encoded = new MemoryStream(); stream.Seek(0); await stream.AsStreamForRead().CopyToAsync(encoded);
        // Encode fully before replacing a destination. Staging remains beside the destination for an atomic rename.
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllBytesAsync(temporary, encoded.ToArray()); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        _savePath = path; _sourceName = Path.GetFileName(path); _savedByThisWindow = true; _savedState = _document.State;
        StatusText.Text = "PNGを保存しました。"; Refresh(); return true;
    }
    private void Root_DragOver(object sender, DragEventArgs e)
    {
        if (!_busy && e.DataView.Contains(StandardDataFormats.StorageItems)) { e.AcceptedOperation = DataPackageOperation.Copy; e.Handled = true; }
    }
    private async void Root_Drop(object sender, DragEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            await RunAsync(async () =>
            {
                if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
                var files = (await e.DataView.GetStorageItemsAsync()).OfType<StorageFile>().Where(f => IsImage(f.Path)).ToArray();
                if (files.Length == 0) { await MessageAsync("画像を開く", "対応する画像ファイルがありません。"); return; }
                foreach (var file in files) await OpenPathAsync(file.Path);
            });
        }
        finally { deferral.Complete(); }
    }
    private async void Join_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var path = await PickPathAsync(false, "連結する画像"); if (path is null) return;
        var frame = await ReadImageAsync(path);
        if (!_hasImage) { _document = new(frame); _savedState = null; _hasImage = true; }
        else _document.Append(frame, (sender as MenuFlyoutItem)?.Tag?.ToString() == "Vertical");
        _selectedId = null; _region = null; Refresh(); FitImage();
    });
}
