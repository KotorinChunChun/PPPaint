using System.Collections.Immutable;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using PPPaint.Core;
using PPPaint.Imaging;
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
        if (item?.Tool is not (ScreenshotTool.Text or ScreenshotTool.Callout or ScreenshotTool.Watermark)) return;
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
        _region = null; _selectedIds.Clear(); _hasImage = true; ShowSelectedOptions(); Refresh(); FitImage();
    }
    private PixelRect? CropArea => ScreenshotCrop.Resolve(_document.State, _region, _selectedIds);
    private void Transform_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || sender is not MenuFlyoutItem item) return;
        CancelGesture();
        var frame = _document.State.Frame; var source = new PixelSurface(frame.Width, frame.Height, Rgba32.Transparent); source.Restore(frame.Pixels.Span);
        var operation = item.Tag?.ToString();
        var output = operation switch { "Rotate" => ImageTransforms.RotateClockwise(source), "FlipX" => ImageTransforms.FlipHorizontal(source), _ => ImageTransforms.FlipVertical(source) };
        Vector2 Transform(Vector2 p) => operation switch { "Rotate" => new(frame.Height - p.Y, p.X), "FlipX" => new(frame.Width - p.X, p.Y), _ => new(p.X, frame.Height - p.Y) };
        var annotations = _document.State.Annotations.Select(a =>
        {
            var points = Corners(a.Bounds).Select(Transform).ToArray(); var xs = points.Select(p => p.X); var ys = points.Select(p => p.Y);
            ScreenshotBox TransformBox(ScreenshotBox b) { var corners = Corners(b).Select(Transform).ToArray(); return new(corners.Min(p => p.X), corners.Min(p => p.Y), corners.Max(p => p.X) - corners.Min(p => p.X), corners.Max(p => p.Y) - corners.Min(p => p.Y)); }
            return a with { Bounds = new(xs.Min(), ys.Min(), xs.Max() - xs.Min(), ys.Max() - ys.Min()), Points = a.Points.IsDefault ? a.Points : a.Points.Select(Transform).ToImmutableArray(), Rotation = operation == "Rotate" ? (a.Rotation + 90) % 360 : -a.Rotation,
                MirrorX = a.Tool == ScreenshotTool.Image && operation == "FlipX" ? !a.MirrorX : a.MirrorX, MirrorY = a.Tool == ScreenshotTool.Image && operation == "FlipY" ? !a.MirrorY : a.MirrorY,
                CalloutTip = a.Tool == ScreenshotTool.Callout ? Transform(a.Tail) : a.CalloutTip,
                ClipBounds = a.ClipBounds is { } clip ? TransformBox(clip) : null, SamplingBounds = a.SamplingBounds is { } sampling ? TransformBox(sampling) : null,
                Cutouts = a.Cutouts.IsDefault ? a.Cutouts : a.Cutouts.Select(TransformBox).ToImmutableArray() };
        }).ToImmutableArray();
        _document.Apply(_document.State with { Frame = new(output.Width, output.Height, output.Snapshot()), Annotations = annotations });
        _region = null; _selectedIds.Clear(); _hasImage = true; ShowSelectedOptions(); Refresh(); FitImage();
    }
}
