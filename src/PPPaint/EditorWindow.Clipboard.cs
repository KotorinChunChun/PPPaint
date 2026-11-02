using Microsoft.UI.Xaml;
using PPPaint.Core;
using Windows.ApplicationModel.DataTransfer;

namespace PPPaint;

public sealed partial class EditorWindow
{
    private bool _clipboardHasImage, _trackingClipboard;

    private static ScreenshotTransfer? CopiedObjects =>
        _objectClipboard is { Sequence: not 0 } payload && GetClipboardSequenceNumber() == payload.Sequence ? payload.Transfer : null;

    private void InitializeClipboard()
    {
        _trackingClipboard = true;
        Clipboard.ContentChanged += Clipboard_ContentChanged;
        Activated += ClipboardWindow_Activated;
        RefreshClipboardAvailability();
    }

    private void Clipboard_ContentChanged(object? sender, object args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (_trackingClipboard) RefreshClipboardAvailability();
    });

    private void ClipboardWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (_trackingClipboard && args.WindowActivationState != WindowActivationState.Deactivated) RefreshClipboardAvailability();
    }

    private void RefreshClipboardAvailability()
    {
        try { _clipboardHasImage = CopiedObjects is not null || Clipboard.GetContent().Contains(StandardDataFormats.Bitmap); }
        catch (Exception) { _clipboardHasImage = false; }
        UpdatePasteButtons();
    }

    private void UpdatePasteButtons()
    {
        PasteButton.IsEnabled = PasteOptionsButton.IsEnabled = _clipboardHasImage && !_busy;
    }

    private void StopTrackingClipboard()
    {
        _trackingClipboard = false;
        Clipboard.ContentChanged -= Clipboard_ContentChanged;
        Activated -= ClipboardWindow_Activated;
    }
}
