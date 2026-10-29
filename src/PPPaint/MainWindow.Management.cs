using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

namespace PPPaint;

public sealed partial class MainWindow
{
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(nint window);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    private bool _managementOpening;

    internal string? ManagementBlockReason(bool checkUnsaved = true)
    {
        if (checkUnsaved && (_dirty || _documents.Any(document => !ReferenceEquals(document, _activeDocument) && document.Dirty))) return "未保存の画像があります。";
        if (_documentBusy || _backupBusy || _managementOpening) return "文書の読込・保存処理中です。";
        if (_closeConfirmationShowing || ManagedDialogs.OpenCount > 0 || ManagedPickers.OpenCount > 0 || !IsWindowEnabled(WindowNative.GetWindowHandle(this)) ||
            PaintCanvas.XamlRoot is { } root && VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Any()) return "ダイアログを表示中です。";
        if (_isDrawing || _advancedDragging || _isDrawingShape || _isSelecting || IsSelectionInteractionActive ||
            _isMovingObject || _isResizingObject || _isRotatingObject) return "描画・編集操作中です。";
        return null;
    }

    internal bool ManagementReady => PaintCanvas.IsLoaded && !_managementOpening && !_documentBusy && !_backupBusy;

    internal void ManagementShow()
    {
        var handle = WindowNative.GetWindowHandle(this);
        if (IsIconic(handle)) ShowWindow(handle, 9);
        Activate();
        SetForegroundWindow(handle);
    }

    internal async Task ManagementOpenAsync(string path)
    {
        _managementOpening = true;
        try { ManagementShow(); await OpenStartupFileAsync(path); }
        finally { _managementOpening = false; }
    }

    internal void ManagementStop()
    {
        // 応答送信からUIキューへ戻るまでに編集された場合も閉じない。
        if (ManagementBlockReason() is null) Close();
    }
}
