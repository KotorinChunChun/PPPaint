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
    private sealed record ObjectClipboard(ScreenshotTransfer Transfer, uint Sequence);
    private static ObjectClipboard? _objectClipboard;
    private static bool IsImage(string path) => DocumentFileFormats.IsImage(path);
    private static bool IsOutputImage(string path) => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg";
    private async Task<(ScreenshotFrame Frame, ScreenshotFileRevision Revision)> ReadImageSourceAsync(string path)
    {
        if (!IsImage(path)) throw new InvalidDataException("PNG、JPEG、BMP、GIF、WebP画像を指定してください。");
        var input = await ScreenshotFileIO.ReadAsync(path);
        using var stream = new InMemoryRandomAccessStream();
        var buffer = stream.AsStreamForWrite(); await buffer.WriteAsync(input.Bytes); await buffer.FlushAsync(); stream.Seek(0);
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
        window._selectedIds.Clear(); window._region = null; window._savedByThisWindow = false; window.ShowSelectedOptions(); window.Refresh(); window.FitImage();
    }
    private async void Open_Click(object sender, RoutedEventArgs e) => await RunAsync(async () => { var path = await PickPathAsync(false); if (path is not null) await OpenPathAsync(path); });
    private async void Paste_Click(object sender, RoutedEventArgs e) => await RunAsync(PasteImageAsync);
    private async void PasteOptions_Click(object sender, RoutedEventArgs e) => await RunAsync(() => PasteImageAsync(true));
    private Task PasteImageAsync() => PasteImageAsync(false);
    private async Task PasteImageAsync(bool chooseMode)
    {
        var transfer = _objectClipboard is { Sequence: not 0 } payload && GetClipboardSequenceNumber() == payload.Sequence ? payload.Transfer : null;
        ScreenshotFrame? external = null;
        if (transfer is null)
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Bitmap)) { await MessageAsync("貼り付け", "クリップボードに画像がありません。"); return; }
            using var stream = await (await content.GetBitmapAsync()).OpenReadAsync();
            using var bitmap = await Win2DImageCodec.LoadAsync(PaintCanvas, stream);
            external = new((int)bitmap.SizeInPixels.Width, (int)bitmap.SizeInPixels.Height, bitmap.GetPixelBytes());
        }
        var mode = chooseMode ? await ChoosePasteModeAsync() : PasteMode.Overlay;
        if (mode is null) return;
        if (transfer is not null) PasteTransfer(transfer, mode.Value);
        else
        {
            var origin = !_hasImage && !Dirty ? Vector2.Zero
                : ScreenshotPasting.CenteredOrigin(_document.State.Frame, external!.Width, external.Height);
            PasteTransfer(new(ScreenshotDocument.FromImage(external!, "クリップボード").State with { Background = Rgba32.Transparent }, origin), mode.Value);
        }
    }
    private void PasteTransfer(ScreenshotTransfer transfer, PasteMode mode)
    {
        var before = _document.State.Frame;
        var pristine = !_hasImage && !Dirty && _savePath is null;
        ImmutableArray<string> ids;
        if (mode != PasteMode.Overlay)
        {
            var direction = mode switch { PasteMode.Top => ScreenshotJoinDirection.Top, PasteMode.Bottom => ScreenshotJoinDirection.Bottom,
                PasteMode.Left => ScreenshotJoinDirection.Left, PasteMode.Right => ScreenshotJoinDirection.Right, _ => throw new ArgumentOutOfRangeException(nameof(mode)) };
            var joined = ScreenshotJoining.Join(_document.State, transfer, direction);
            _document.Apply(joined.State); ids = joined.AddedIds;
            StatusText.Text = "画像を連結しました。";
        }
        else
        {
            var pasted = ScreenshotPasting.Overlay(_document.State, transfer);
            _document.Apply(pasted.State); ids = pasted.AddedIds;
            if (pristine) { _sourceName = "貼り付け"; _savePath = null; _sourceRevision = null; _backupPath = null; _savedByThisWindow = false; }
            StatusText.Text = $"{ids.Length}個を貼り付けました。";
        }
        _hasImage = true; _region = null; SetCurrentTool(ScreenshotTool.Select); SetSelection(ids);
        if (before.Width != _document.State.Frame.Width || before.Height != _document.State.Frame.Height) FitImage();
    }
    private void InsertRaster(ScreenshotFrame frame, Vector2? position, string name)
    {
        var before = _document.State.Frame; string id;
        if (!_hasImage && !Dirty)
        {
            _document.Apply(ScreenshotDocument.FromImage(frame, name).State); id = _document.State.Annotations[^1].Id;
            _sourceName = name; _savePath = null;
        }
        else id = _document.InsertImage(frame, position ?? ScreenshotPasting.CenteredOrigin(before, frame.Width, frame.Height), name);
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
        var transfer = ScreenshotCopy.Capture(state, _region, _selectedIds);
        await PublishClipboardAsync(transfer);
        StatusText.Text = $"{transfer.Scene.Frame.Width} × {transfer.Scene.Frame.Height} pxをコピーしました。";
    }
    private async Task PublishClipboardAsync(ScreenshotTransfer transfer, Rgba32? externalBackground = null)
    {
        var scene = externalBackground is { } background ? transfer.Scene with { Background = background } : transfer.Scene;
        using var target = ScreenshotRenderer.Render(PaintCanvas, scene);
        using var stream = new InMemoryRandomAccessStream();
        await Win2DImageCodec.SaveAsync(target, stream, DocumentFileFormat.Png);
        stream.Seek(0); var data = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        data.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream)); Clipboard.SetContent(data); Clipboard.Flush();
        _objectClipboard = new(transfer, GetClipboardSequenceNumber());
    }
    private async void Cut_Click(object sender, RoutedEventArgs e) => await RunAsync(CutImageAsync);
    private async Task CutImageAsync()
    {
        var state = _document.State; ScreenshotTransfer transfer; PixelRect? area = null;
        if (_region is not null)
        {
            if (RegionArea is not { } selected) { StatusText.Text = "切り取る範囲を選択してください。"; return; }
            area = selected;
            transfer = ScreenshotEditing.Capture(state, selected);
        }
        else if (_selectedIds.Count > 0) transfer = ScreenshotEditing.Capture(state, new(0, 0, state.Frame.Width, state.Frame.Height), _selectedIds);
        else { StatusText.Text = "切り取る範囲を選択してください。"; return; }
        await PublishClipboardAsync(transfer, area is null ? state.Background : null);
        if (!ReferenceEquals(state, _document.State)) throw new InvalidOperationException("編集中の画像が変更されました。切り取りは確定していません。");
        if (area is { } rectangle) _document.DeleteRange(rectangle); else _document.Delete(_selectedIds);
        _region = null; _selectedIds.Clear(); ShowSelectedOptions(); StatusText.Text = "選択範囲を切り取りました。"; Refresh();
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
