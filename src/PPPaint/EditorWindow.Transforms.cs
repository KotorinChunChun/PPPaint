using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PPPaint.Core;
using Windows.Foundation;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace PPPaint;

public sealed partial class EditorWindow
{
    private bool _fitting;
    private bool _fitToWindow = true;
    private int _fitCanvasWidth, _fitCanvasHeight;
    private void Canvas_Wheel(object sender, PointerRoutedEventArgs e)
    {
        if (!KeyDown(VirtualKey.Control)) return;
        e.Handled = true;
        if (_busy || _dialogOpen || _pointerId is not null) return;
        var wheel = e.GetCurrentPoint(PaintCanvas); var anchor = e.GetCurrentPoint(Viewport).Position;
        var image = new Vector2((float)(wheel.Position.X / _zoom), (float)(wheel.Position.Y / _zoom));
        var next = ScreenshotZoom.Wheel(ZoomSlider.Value, wheel.Properties.MouseWheelDelta);
        _fitToWindow = false;
        ZoomSlider.Value = next; UpdateFitState(); Viewport.UpdateLayout();
        var origin = PaintCanvas.TransformToVisual(Viewport).TransformPoint(new Point(0, 0));
        Viewport.ChangeView(Viewport.HorizontalOffset + origin.X + image.X * _zoom - anchor.X, Viewport.VerticalOffset + origin.Y + image.Y * _zoom - anchor.Y, null, true);
        UpdateCursor(image);
    }
    private void Canvas_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (_busy || _dialogOpen) return;
        var point = e.GetPosition(PaintCanvas); var item = Hits(new((float)(point.X / _zoom), (float)(point.Y / _zoom))).FirstOrDefault();
        if (!IsImagePoint(new((float)(point.X / _zoom), (float)(point.Y / _zoom)))) return;
        if (item?.Tool is not (ScreenshotTool.Text or ScreenshotTool.Callout)) return;
        CancelGesture(); _region = null; SetCurrentTool(ScreenshotTool.Select); SetSelection([item.Id]);
        TextInput.Focus(FocusState.Programmatic); TextInput.SelectAll(); e.Handled = true;
    }
    private void Zoom_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        _fitToWindow = _fitting;
        _zoom = e.NewValue / 100; if (ZoomLabel is null) return; ZoomLabel.Content = KeyLabel($"{e.NewValue:0}%", "Alt+D"); UpdateFitState(); Refresh();
    }
    private void UpdateFitState()
    {
        if (FitButton is null) return;
        FitButton.IsChecked = _fitToWindow;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetItemStatus(FitButton, _fitToWindow ? "全体表示中" : "");
    }
    private void FitImage()
    {
        if (Viewport is null || Viewport.ActualWidth < 1 || Viewport.ActualHeight < 1) return;
        _fitting = true; _fitToWindow = true;
        _fitCanvasWidth = _document.State.Frame.Width;
        _fitCanvasHeight = _document.State.Frame.Height;
        try { ZoomSlider.Value = Math.Clamp(Math.Min((Viewport.ActualWidth - 44) / _fitCanvasWidth, (Viewport.ActualHeight - 44) / _fitCanvasHeight) * 100, 10, 100); }
        finally { _fitting = false; UpdateFitState(); }
    }
    private void Fit_Checked(object sender, RoutedEventArgs e) { if (_busy) UpdateFitState(); else if (!_fitToWindow) FitImage(); }
    private void Fit_Unchecked(object sender, RoutedEventArgs e) { if (_fitToWindow) { _fitToWindow = false; UpdateFitState(); } }
    private void ZoomReset_Click(object sender, RoutedEventArgs e) { if (!_busy) { _fitToWindow = false; ZoomSlider.Value = 100; UpdateFitState(); } }
    private void Viewport_SizeChanged(object sender, SizeChangedEventArgs e) { if (!_fitting && _fitToWindow) FitImage(); }
    private void Crop_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || CropArea is not { } area) return;
        _document.Crop(area.X, area.Y, area.Width, area.Height);
        ShowChangedCanvas();
    }
    private PixelRect? CropArea => ScreenshotCrop.Resolve(_document.State, _region, _selectedIds);
    private PixelRect? RegionArea => _region is { } region ? ScreenshotCrop.RegionArea(region, _document.State.Frame.Width, _document.State.Frame.Height) : null;
    private void Transform_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || sender is not MenuFlyoutItem item) return;
        var change = item.Tag?.ToString() switch
        {
            "Rotate" => ScreenshotOrientationChange.RotateClockwise,
            "FlipX" => ScreenshotOrientationChange.FlipHorizontal,
            _ => ScreenshotOrientationChange.FlipVertical
        };
        CancelGesture();
        _document.Apply(ScreenshotOrientation.Apply(_document.State, change));
        ShowChangedCanvas();
    }
    // After the canvas itself changes, old selections no longer describe the same pixels.
    private void ShowChangedCanvas()
    {
        _region = null; _selectedIds.Clear(); _hasImage = true;
        ShowSelectedOptions(); Refresh(); FitImage();
    }
}
