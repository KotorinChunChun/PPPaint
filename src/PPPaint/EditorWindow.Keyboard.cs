using System.Numerics;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PPPaint.Core;
using Windows.System;
using Windows.UI.Core;

namespace PPPaint;

public sealed partial class EditorWindow
{
    private bool _textComposing;

    private static bool KeyDown(VirtualKey key) => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
    private async void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_busy || _dialogOpen) return;
        if (_pointerInside && e.Key is VirtualKey.Control or VirtualKey.Shift or VirtualKey.Menu) UpdateCursor(_hoverPoint);
        if (_pointerId is not null && e.Key != VirtualKey.Escape) { if (e.Key is VirtualKey.Control or VirtualKey.Shift or VirtualKey.Menu) { PreviewGesture(_lastPoint); InvalidateCanvas(); } e.Handled = true; return; }
        var editingText = FocusManager.GetFocusedElement(Root.XamlRoot) is TextBox; var ctrl = KeyDown(VirtualKey.Control);
        if (ctrl && e.Key == VirtualKey.S) { e.Handled = true; await RunAsync(async () => await SaveImageAsync(KeyDown(VirtualKey.Shift))); }
        else if (ctrl && e.Key == VirtualKey.O) { e.Handled = true; await RunAsync(async () => { var path = await PickPathAsync(false); if (path is not null) await OpenPathAsync(path); }); }
        else if (editingText)
        {
            if (e.Key == VirtualKey.Escape && !_textComposing && ReferenceEquals(FocusManager.GetFocusedElement(Root.XamlRoot), TextInput))
            { InteractionCanvas.Focus(FocusState.Programmatic); e.Handled = true; }
            return;
        }
        else if (_pickingColor && e.Key == VirtualKey.Escape) { SetColorPicking(false); e.Handled = true; }
        else if (ctrl && e.Key == VirtualKey.V) { var chooseMode = KeyDown(VirtualKey.Shift); e.Handled = true; await RunAsync(() => PasteImageAsync(chooseMode)); }
        else if (ctrl && e.Key == VirtualKey.E) { e.Handled = true; await RunAsync(MergeSelectionAsync); }
        else if (ctrl && e.Key == VirtualKey.C) { e.Handled = true; await RunAsync(CopyImageAsync); }
        else if (ctrl && KeyDown(VirtualKey.Shift) && e.Key == VirtualKey.X) { e.Handled = true; Crop_Click(sender, new()); }
        else if (ctrl && e.Key == VirtualKey.X) { e.Handled = true; await RunAsync(CutImageAsync); }
        else if (ctrl && e.Key == VirtualKey.Z) { e.Handled = true; Undo_Click(sender, new()); }
        else if (ctrl && e.Key == VirtualKey.Y) { e.Handled = true; Redo_Click(sender, new()); }
        else if (ctrl && e.Key == VirtualKey.N) { e.Handled = true; App.Instance.CreateWindow(); }
        else if (!ctrl && !KeyDown(VirtualKey.Menu) && !KeyDown(VirtualKey.Shift) && e.Key == VirtualKey.I) { SetColorPicking(!_pickingColor); e.Handled = true; }
        else if (_pointerId is null && ScreenshotCommands.TryTool((char)e.Key, ctrl, KeyDown(VirtualKey.Menu), KeyDown(VirtualKey.Shift), editingText, out var tool)) { SelectTool(tool); InteractionCanvas.Focus(FocusState.Programmatic); e.Handled = true; }
        else if (!ctrl && !KeyDown(VirtualKey.Menu) && (e.Key == VirtualKey.C || e.Key == VirtualKey.Enter) && _region is not null) { Crop_Click(sender, new()); e.Handled = true; }
        else if (ctrl && e.Key == VirtualKey.A)
        {
            SelectAll_Click(sender, new()); e.Handled = true;
        }
        else if (ctrl && e.Key is VirtualKey.PageUp or VirtualKey.PageDown)
        {
            _document.Reorder(_selectedIds, e.Key == VirtualKey.PageUp ? KeyDown(VirtualKey.Shift) ? ScreenshotStackOrder.Front : ScreenshotStackOrder.Forward : KeyDown(VirtualKey.Shift) ? ScreenshotStackOrder.Back : ScreenshotStackOrder.Backward); Refresh(); e.Handled = true;
        }
        else if (e.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down && _selectedIds.Count > 0)
        {
            var step = KeyDown(VirtualKey.Shift) ? 10 : 1; var offset = e.Key switch { VirtualKey.Left => new Vector2(-step, 0), VirtualKey.Right => new(step, 0), VirtualKey.Up => new(0, -step), _ => new(0, step) };
            _document.ReplaceMany(SelectedObjects.Select(a => a.Move(offset))); _region = null; Refresh(); e.Handled = true;
        }
        else if (e.Key == VirtualKey.Delete) { e.Handled = true; Delete_Click(sender, new()); }
        else if (e.Key == VirtualKey.Escape)
        {
            var cancelingDrag = _pointerId is not null; CancelGesture();
            if (!cancelingDrag) { _region = null; _selectedIds.Clear(); SetCurrentTool(ScreenshotTool.Select); }
            ShowSelectedOptions(); Refresh(); e.Handled = true;
        }
    }
    private void Root_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (_pointerInside && e.Key is VirtualKey.Control or VirtualKey.Shift or VirtualKey.Menu) UpdateCursor(_hoverPoint);
        if (!_busy && !_dialogOpen && _pointerId is not null && e.Key is VirtualKey.Control or VirtualKey.Shift or VirtualKey.Menu)
        { PreviewGesture(_lastPoint); UpdateCursor(_hoverPoint); e.Handled = true; }
    }
}
