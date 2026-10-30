using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;
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
    private ScreenshotFileRevision? _sourceRevision;
    private string? _backupPath;
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    private sealed record ObjectClipboard(ImmutableArray<ScreenshotAnnotation> Objects, Vector2 Origin, uint Sequence = 0);
    private static ObjectClipboard? _objectClipboard;
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"];
    private static bool IsImage(string path) => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    private static bool IsOutputImage(string path) => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg";
    private async Task<(ScreenshotFrame Frame, ScreenshotFileRevision Revision)> ReadImageSourceAsync(string path)
    {
        if (!IsImage(path)) throw new InvalidDataException("PNG、JPEG、BMP、GIF、WebP画像を指定してください。");
        var input = await ScreenshotFileIO.ReadAsync(path);
        using var bytes = new MemoryStream(input.Bytes, false); using var stream = bytes.AsRandomAccessStream();
        using var bitmap = await Win2DImageCodec.LoadAsync(PaintCanvas, stream);
        return (new((int)bitmap.SizeInPixels.Width, (int)bitmap.SizeInPixels.Height, bitmap.GetPixelBytes()), input.Revision);
    }
    private async Task<ScreenshotFrame> ReadImageAsync(string path) => (await ReadImageSourceAsync(path)).Frame;
    private async Task OpenPathAsync(string path)
    {
        var source = await ReadImageSourceAsync(path); var frame = source.Frame;
        var window = !_hasImage && !Dirty ? this : App.Instance.CreateWindow(frame);
        window._document = ScreenshotDocument.FromImage(frame, Path.GetFileName(path)); window._savedState = window._document.State; window._hasImage = true;
        window._sourceName = Path.GetFileName(path); window._savePath = IsOutputImage(path) ? Path.GetFullPath(path) : null;
        window._sourceRevision = source.Revision; window._backupPath = null;
        window._selectedIds.Clear(); window._region = null; window._savedByThisWindow = false; window.Refresh(); window.FitImage();
    }
    private async void Open_Click(object sender, RoutedEventArgs e) => await RunAsync(async () => { var path = await PickPathAsync(false); if (path is not null) await OpenPathAsync(path); });
    private async void Paste_Click(object sender, RoutedEventArgs e) => await RunAsync(PasteImageAsync);
    private async Task PasteImageAsync()
    {
        var content = Clipboard.GetContent();
        if (_objectClipboard is { Sequence: not 0 } payload && GetClipboardSequenceNumber() == payload.Sequence)
        {
            var offset = payload.Origin + new Vector2(20, 20);
            var copies = payload.Objects.Select(a => a.Move(offset) with { Id = Guid.NewGuid().ToString("N") }).ToImmutableArray();
            var bounds = ScreenshotGeometry.Bounds(copies); var canvas = _document.State.Frame;
            if (!_hasImage && !Dirty) canvas = ScreenshotFrame.Transparent(Math.Max(1, (int)Math.Ceiling(bounds.Right)), Math.Max(1, (int)Math.Ceiling(bounds.Bottom)));
            else canvas = canvas.Expand(Math.Max(canvas.Width, (int)Math.Ceiling(bounds.Right)), Math.Max(canvas.Height, (int)Math.Ceiling(bounds.Bottom)));
            _document.Apply(_document.State with { Frame = canvas, Annotations = _document.State.Annotations.AddRange(copies) });
            _hasImage = true; _region = null; SetCurrentTool(ScreenshotTool.Select); SetSelection(copies.Select(a => a.Id)); StatusText.Text = $"{copies.Length}個を貼り付けました。"; return;
        }
        if (!content.Contains(StandardDataFormats.Bitmap)) { await MessageAsync("貼り付け", "クリップボードに画像がありません。"); return; }
        var reference = await content.GetBitmapAsync(); using var stream = await reference.OpenReadAsync();
        using var bitmap = await Win2DImageCodec.LoadAsync(PaintCanvas, stream);
        var frame = new ScreenshotFrame((int)bitmap.SizeInPixels.Width, (int)bitmap.SizeInPixels.Height, bitmap.GetPixelBytes());
        InsertRaster(frame, null, "クリップボード"); StatusText.Text = "画像を貼り付けました。";
    }
    private void InsertRaster(ScreenshotFrame frame, Vector2? position, string name)
    {
        var before = _document.State.Frame; string id;
        if (!_hasImage && !Dirty)
        {
            _document.Apply(ScreenshotDocument.FromImage(frame, name).State); id = _document.State.Annotations[^1].Id;
            _sourceName = name; _savePath = null;
        }
        else id = _document.InsertImage(frame, position ?? new Vector2(Math.Max(0, (before.Width - frame.Width) / 2f), Math.Max(0, (before.Height - frame.Height) / 2f)), name);
        _hasImage = true; _region = null; SetCurrentTool(ScreenshotTool.Select); SetSelection([id]);
        if (before.Width != _document.State.Frame.Width || before.Height != _document.State.Frame.Height) FitImage();
    }
    private async void InsertImage_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var path = await PickPathAsync(false, "画像を重ねる"); if (path is not null) InsertRaster(await ReadImageAsync(path), null, Path.GetFileName(path));
    });
    private async void Copy_Click(object sender, RoutedEventArgs e) => await RunAsync(CopyImageAsync);
    private async Task CopyImageAsync()
    {
        var state = _document.State;
        ScreenshotBox? copyRegion = _region;
        _objectClipboard = null;
        if (_region is null && _selectedIds.Count > 0)
        {
            var objects = SelectedObjects; var bounds = ScreenshotGeometry.Bounds(objects);
            var canvasBounds = new ScreenshotBox(0, 0, state.Frame.Width, state.Frame.Height);
            var left = Math.Clamp(MathF.Floor(bounds.X), 0, state.Frame.Width - 1); var top = Math.Clamp(MathF.Floor(bounds.Y), 0, state.Frame.Height - 1);
            var right = Math.Clamp(MathF.Ceiling(bounds.Right), left + 1, state.Frame.Width); var bottom = Math.Clamp(MathF.Ceiling(bounds.Bottom), top + 1, state.Frame.Height);
            var origin = new Vector2(left, top);
            var normalized = objects.Select(a => a.Move(-origin)).ToImmutableArray();
            state = new(ScreenshotFrame.Transparent((int)(right - left), (int)(bottom - top)), normalized);
            // A copied crop must never reveal pixels hidden outside the canvas when pasted elsewhere.
            if (canvasBounds.Contains(bounds)) _objectClipboard = new(normalized, origin);
            if (objects.Any(a => a.Tool is ScreenshotTool.Blur or ScreenshotTool.Mosaic))
            {
                state = _document.State; copyRegion = new(left, top, right - left, bottom - top);
            }
        }
        using var target = ScreenshotRenderer.Render(PaintCanvas, state);
        using var stream = new InMemoryRandomAccessStream();
        if (copyRegion is { } region)
        {
            if (_region is not null) { ClampRegion(); region = _region!.Value; }
            var x = (int)region.X; var y = (int)region.Y; var width = Math.Max(1, (int)region.Width); var height = Math.Max(1, (int)region.Height);
            var frame = new ScreenshotFrame((int)target.SizeInPixels.Width, (int)target.SizeInPixels.Height, target.GetPixelBytes()).Crop(x, y, width, height);
            using var cropped = ScreenshotRenderer.Bitmap(PaintCanvas, frame);
            await Win2DImageCodec.SaveAsync(cropped, stream, DocumentFileFormat.Png);
        }
        else await Win2DImageCodec.SaveAsync(target, stream, DocumentFileFormat.Png);
        stream.Seek(0); var data = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        data.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream)); Clipboard.SetContent(data); Clipboard.Flush();
        if (_objectClipboard is { } copied) _objectClipboard = copied with { Sequence = GetClipboardSequenceNumber() };
        StatusText.Text = "画像をコピーしました。";
    }
    private async void Save_Click(object sender, RoutedEventArgs e) => await RunAsync(async () => await SaveImageAsync());
    private async void SaveAs_Click(object sender, RoutedEventArgs e) => await RunAsync(async () => await SaveImageAsync(true));
    private async void OpenBackup_Click(object sender, RoutedEventArgs e) => await RunAsync(async () => { if (_backupPath is { } backup) await OpenPathAsync(backup); });
    private async Task<bool> SaveImageAsync(bool saveAs = false, string? blockedPath = null)
    {
        var path = saveAs ? await PickPathAsync(true) : _savePath ?? await PickPathAsync(true);
        if (path is null) return false;
        path = Path.GetFullPath(path);
        if (blockedPath is not null && string.Equals(path, blockedPath, StringComparison.OrdinalIgnoreCase))
        { await MessageAsync("別名保存", "変更された元ファイルとは別の名前を指定してください。編集内容は保持しています。"); return false; }
        var sameFile = string.Equals(path, _savePath, StringComparison.OrdinalIgnoreCase);
        var expected = sameFile && _sourceRevision is not null ? _sourceRevision : await ScreenshotFileIO.RevisionAsync(path);
        try
        {
            if (await ScreenshotFileIO.RevisionAsync(path) != expected) throw new ScreenshotSaveConflictException();
            if (expected.Exists && (!_savedByThisWindow || !sameFile))
                if (await AskAsync("画像保存", $"「{Path.GetFileName(path)}」を上書きしますか？保存前の画像はバックアップします。", "上書き") != ContentDialogResult.Primary) return false;
            var state = _document.State;
            using var target = ScreenshotRenderer.Render(PaintCanvas, state);
            var format = Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase) ? DocumentFileFormat.Png : DocumentFileFormat.Jpeg;
            using var stream = new InMemoryRandomAccessStream(); await Win2DImageCodec.SaveAsync(target, stream, format);
            using var encoded = new MemoryStream(); stream.Seek(0); await stream.AsStreamForRead().CopyToAsync(encoded);
            var saved = await ScreenshotFileIO.WriteAsync(path, encoded.ToArray(), expected);
            _savePath = path; _sourceName = Path.GetFileName(path); _savedByThisWindow = true; _savedState = state; _sourceRevision = saved.Revision; _backupPath = saved.BackupPath;
            StatusText.Text = saved.BackupPath is null ? "画像を保存しました。" : "保存しました。保存前の画像は「その他」から開けます。";
            ToolTipService.SetToolTip(StatusText, saved.BackupPath ?? path); Refresh(); return true;
        }
        catch (ScreenshotSaveConflictException)
        {
            if (await AskAsync("元の画像が変更されています", "元のファイルは上書きできません。編集結果を別名で保存しますか？", "別名保存") != ContentDialogResult.Primary) return false;
            return await SaveImageAsync(true, path);
        }
    }
    private void Root_DragOver(object sender, DragEventArgs e)
    {
        if (!_busy && (e.DataView.Contains(StandardDataFormats.StorageItems) || e.DataView.Contains(StandardDataFormats.Bitmap))) { e.AcceptedOperation = DataPackageOperation.Copy; e.Handled = true; }
    }
    private async void Root_Drop(object sender, DragEventArgs e)
    {
        var deferral = e.GetDeferral();
        var dropPoint = e.GetPosition(PaintCanvas);
        var position = new Vector2((float)Math.Max(0, dropPoint.X / _zoom), (float)Math.Max(0, dropPoint.Y / _zoom));
        try
        {
            await RunAsync(async () =>
            {
                if (!e.DataView.Contains(StandardDataFormats.StorageItems))
                {
                    if (e.DataView.Contains(StandardDataFormats.Bitmap))
                    {
                        using var stream = await (await e.DataView.GetBitmapAsync()).OpenReadAsync();
                        using var bitmap = await Win2DImageCodec.LoadAsync(PaintCanvas, stream);
                        InsertRaster(new((int)bitmap.SizeInPixels.Width, (int)bitmap.SizeInPixels.Height, bitmap.GetPixelBytes()), position, "ドロップ画像");
                    }
                    return;
                }
                var files = (await e.DataView.GetStorageItemsAsync()).OfType<StorageFile>().Where(f => IsImage(f.Path)).ToArray();
                if (files.Length == 0) { await MessageAsync("画像を開く", "対応する画像ファイルがありません。"); return; }
                foreach (var file in files) { InsertRaster(await ReadImageAsync(file.Path), position, file.Name); position += new Vector2(20, 20); }
            });
        }
        finally { deferral.Complete(); }
    }
    private async void Join_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var path = await PickPathAsync(false, "連結する画像"); if (path is null) return;
        var frame = await ReadImageAsync(path);
        var canvas = _document.State.Frame;
        var position = (sender as MenuFlyoutItem)?.Tag?.ToString() == "Vertical" ? new Vector2(0, canvas.Height) : new(canvas.Width, 0);
        InsertRaster(frame, position, Path.GetFileName(path)); FitImage();
    });
}
