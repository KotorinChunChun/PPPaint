using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace PPPaint;

public sealed partial class EditorWindow
{
    private void Viewport_Pressed(object sender, PointerRoutedEventArgs e)
    {
        if (_busy || _dialogOpen || _pointerId is not null || _pickingColor || KeyDown(VirtualKey.Space)) return;
        var pointer = e.GetCurrentPoint(PaintCanvas);
        if (!pointer.Properties.IsLeftButtonPressed || pointer.Properties.IsMiddleButtonPressed) return;
        var point = ImagePoint(e);
        if (IsImagePoint(point) || HitCanvas(point, includeCanvasBorder: true).Edge is not null) return;
        if (_selectedIds.Count == 0 && _region is null) return;
        _region = null; SetSelection([]); InteractionCanvas.Focus(FocusState.Pointer); e.Handled = true;
    }
}
