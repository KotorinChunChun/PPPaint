using ChunPaint.Core;
using ChunPaint.Editing;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;

namespace ChunPaint;

public sealed partial class MainWindow
{
    private sealed class DocumentSession
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public DocumentSnapshot? Snapshot { get; set; }
        public DocumentSnapshot[] Undo { get; set; } = [];
        public DocumentSnapshot[] Redo { get; set; } = [];
        public StorageFile? File { get; set; }
        public bool Dirty { get; set; }
        public double Zoom { get; set; } = 1;
    }

    private readonly List<DocumentSession> _documents = [];
    private DocumentSession? _activeDocument;
    private DispatcherQueueTimer? _maintenanceTimer;
    private bool _documentBusy;
    private bool _backupBusy;
    private DateTime _lastBackup = DateTime.MinValue;
    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private FileStream? _instanceLock;
    private string RecoveryDirectory => Path.Combine(UserDataDirectory, "Recovery", _instanceId);

    private void InitializeDocuments()
    {
        _activeDocument = new DocumentSession();
        _documents.Add(_activeDocument);
        RefreshDocumentTabs();
        Directory.CreateDirectory(RecoveryDirectory);
        _instanceLock = new FileStream(Path.Combine(RecoveryDirectory, "owner.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        _maintenanceTimer = DispatcherQueue.CreateTimer();
        _maintenanceTimer.Interval = TimeSpan.FromSeconds(5);
        _maintenanceTimer.Tick += async (_, _) =>
        {
            SavePreferences();
            if (DateTime.UtcNow - _lastBackup >= TimeSpan.FromSeconds(_preferences.AutosaveSeconds)) await BackupDocumentsAsync();
        };
        _maintenanceTimer.Start();
        PaintCanvas.Loaded += async (_, _) => await OfferRecoveryAsync();
        Closed += (_, _) =>
        {
            _maintenanceTimer.Stop();
            SavePreferences();
            _instanceLock?.Dispose();
        };
    }

    private void StoreActiveDocument()
    {
        if (_activeDocument is null) return;
        EnsureSurface(PaintCanvas);
        CommitFloatingSelection();
        CommitEditableObjectForDocumentSwitch();
        _activeDocument.Snapshot = CaptureDocumentSnapshot();
        _activeDocument.Undo = _undo.Reverse().ToArray();
        _activeDocument.Redo = _redo.Reverse().ToArray();
        _activeDocument.File = _currentFile;
        _activeDocument.Dirty = _dirty;
        _activeDocument.Zoom = _zoom;
    }

    private void SwitchDocument(DocumentSession document)
    {
        if (_documentBusy || _isDrawing || _isDrawingShape || ReferenceEquals(document, _activeDocument)) return;
        StoreActiveDocument();
        if (document.Snapshot is not null) RestoreDocumentSnapshot(document.Snapshot);
        _activeDocument = document;
        _undo.Clear();
        foreach (var state in document.Undo) _undo.Push(state);
        _redo.Clear();
        foreach (var state in document.Redo) _redo.Push(state);
        _currentFile = document.File;
        _dirty = document.Dirty;
        ZoomSlider.Value = document.Zoom * 100;
        // 読み込んだ文書は画面とスタックが所有する。古いセッション参照を残さない。
        document.Snapshot = null; document.Undo = []; document.Redo = [];
        TrimDocumentHistory();
        UpdateHistoryState();
        UpdateTitle();
        PaintCanvas.Invalidate();
    }

    private void RefreshDocumentTabs()
    {
        if (DocumentTabsPanel is null) return;
        DocumentTabsPanel.Children.Clear();
        foreach (var document in _documents)
        {
            var active = ReferenceEquals(document, _activeDocument);
            var file = active ? _currentFile : document.File;
            var dirty = active ? _dirty : document.Dirty;
            var tab = new Button { Content = $"{(active ? "● " : "")}{(dirty ? "*" : "")}{file?.Name ?? "無題"}",
                MinWidth = 110, MaxWidth = 250, IsEnabled = !_documentBusy };
            tab.Click += (_, _) => SwitchDocument(document);
            DocumentTabsPanel.Children.Add(tab);
        }
        var add = new Button { IsEnabled = !_documentBusy };
        add.Content = "＋ 新しい画像";
        add.Click += async (_, _) => await NewDocumentAsync();
        DocumentTabsPanel.Children.Add(add);
        var close = new Button { Content = "タブを閉じる", IsEnabled = !_documentBusy };
        close.Click += async (_, _) => await CloseDocumentAsync();
        DocumentTabsPanel.Children.Add(close);
    }

    private async Task CloseDocumentAsync()
    {
        if (_documentBusy || !await ConfirmDiscardAsync()) return;
        var closing = _activeDocument;
        if (closing is null) return;
        DeleteRecovery(closing.Id);
        var next = _documents.FirstOrDefault(item => !ReferenceEquals(item, closing));
        if (next is not null)
        {
            SwitchDocument(next);
            _documents.Remove(closing);
            RefreshDocumentTabs();
        }
        else
        {
            _documents.Remove(closing);
            _activeDocument = null;
            await NewDocumentAsync();
        }
    }

    private readonly HashSet<string> _invalidatedRecovery = [];

    private void DeleteRecovery(string documentId)
    {
        _invalidatedRecovery.Add(documentId);
        try { File.Delete(Path.Combine(RecoveryDirectory, documentId + ".chunpaint")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { PointerStatusText.Text = $"復元ファイルを削除できませんでした: {ex.Message}"; }
    }

    private async Task<bool> BackupDocumentsAsync(bool allowDocumentBusy = false)
    {
        if (_backupBusy || (_documentBusy && !allowDocumentBusy) || _isDrawing || _isDrawingShape || _surface is null) return false;
        _backupBusy = true;
        var backups = new List<(string Id, ChunPaintProject Project)>();
        try
        {
            foreach (var document in _documents)
            {
                if (ReferenceEquals(document, _activeDocument))
                {
                    if (_dirty) backups.Add((document.Id, CaptureProject()));
                }
                else if (document.Dirty && document.Snapshot is { } saved)
                {
                    backups.Add((document.Id, new ChunPaintProject(saved.Width, saved.Height, saved.ActiveLayerIndex,
                        saved.Layers.Select(layer => new ChunPaintProjectLayer(layer.Name, layer.IsVisible, layer.Pixels, layer.Id, layer.IsLocked, layer.Opacity)).ToArray(), saved.Objects, saved.Dpi)));
                }
            }
            foreach (var backup in backups) _invalidatedRecovery.Remove(backup.Id);
            await Task.Run(() =>
            {
                foreach (var backup in backups)
                {
                    var path = Path.Combine(RecoveryDirectory, backup.Id + ".chunpaint");
                    using (var stream = File.Create(path + ".tmp")) ChunPaintProjectArchive.Save(stream, backup.Project);
                    File.Move(path + ".tmp", path, true);
                }
            });
            _lastBackup = DateTime.UtcNow;
            return true;
        }
        catch (Exception exception) { PointerStatusText.Text = $"自動保存に失敗: {exception.Message}"; return false; }
        finally
        {
            // 書き込み中に保存または破棄された文書の古いバックアップを残さない。
            foreach (var backup in backups)
            {
                var document = _documents.FirstOrDefault(item => item.Id == backup.Id);
                if (_invalidatedRecovery.Contains(backup.Id) || document is null ||
                    !(ReferenceEquals(document, _activeDocument) ? _dirty : document.Dirty)) DeleteRecovery(backup.Id);
            }
            _backupBusy = false;
        }
    }

    private async Task OfferRecoveryAsync()
    {
        if (_documentBusy || _backupBusy) return;
        _documentBusy = true; RefreshDocumentTabs();
        try
        {
            var root = Path.Combine(UserDataDirectory, "Recovery");
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                if (directory == RecoveryDirectory) continue;
                FileStream owner;
                try { owner = new FileStream(Path.Combine(directory, "owner.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) { continue; }
                using (owner)
                {
                    foreach (var path in Directory.EnumerateFiles(directory, "*.chunpaint"))
                    {
                        var dialog = new ContentDialog { XamlRoot = PaintCanvas.XamlRoot, Title = "前回の作業を復元", Content = $"自動保存された画像があります。\n保存日時: {File.GetLastWriteTime(path):g}",
                            PrimaryButtonText = "復元", SecondaryButtonText = "破棄", CloseButtonText = "後で" };
                        var result = await dialog.ShowAsync();
                        if (result == ContentDialogResult.None) continue;
                        if (result == ContentDialogResult.Secondary) { File.Delete(path); continue; }
                        var previous = _activeDocument;
                        DocumentSession? recovered = null;
                        try
                        {
                            StoreActiveDocument();
                            recovered = new DocumentSession(); _documents.Add(recovered); _activeDocument = recovered;
                            await OpenChunPaintProjectAsync(await StorageFile.GetFileFromPathAsync(path));
                            _currentFile = null; _dirty = true; UpdateTitle();
                            // 新しい復元コピーが保存できるまで、元のバックアップを消さない。
                            if (await BackupDocumentsAsync(allowDocumentBusy: true))
                            {
                                try { File.Delete(path); }
                                catch (Exception deleteError) when (deleteError is IOException or UnauthorizedAccessException)
                                { PointerStatusText.Text = $"復元済みの元バックアップを削除できませんでした: {deleteError.Message}"; }
                            }
                            else PointerStatusText.Text = "復元できましたが、自動保存できなかったため元の復元ファイルを保持しました。";
                        }
                        catch (Exception exception)
                        {
                            if (recovered is not null) _documents.Remove(recovered);
                            _activeDocument = previous;
                            if (previous?.Snapshot is { } snapshot)
                            {
                                RestoreDocumentSnapshot(snapshot);
                                _undo.Clear(); foreach (var state in previous.Undo) _undo.Push(state);
                                _redo.Clear(); foreach (var state in previous.Redo) _redo.Push(state);
                                _currentFile = previous.File; _dirty = previous.Dirty; ZoomSlider.Value = previous.Zoom * 100;
                                UpdateHistoryState(); UpdateTitle();
                            }
                            await ShowInfoAsync("復元失敗", exception.Message);
                        }
                    }
                }
            }
        }
        catch (Exception exception) { await ShowInfoAsync("復元ファイルの確認", $"復元ファイルを確認できませんでした。元のファイルは保持されています。\n{exception.Message}"); }
        finally { _documentBusy = false; RefreshDocumentTabs(); }
    }
}