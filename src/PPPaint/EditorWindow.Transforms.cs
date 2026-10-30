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
    private void Canvas_Wheel(object sender, PointerRoutedEventArgs e)
    {
        if (!KeyDown(VirtualKey.Control)) return;
        e.Handled = true;
        if (_busy || _dialogOpen || _pointerId is not null) return;
        var wheel = e.GetCurrentPoint(PaintCanvas); var anchor = e.GetCurrentPoint(Viewport).Position;
        var image = new Vector2((float)(wheel.Position.X / _zoom), (float)(wheel.Position.Y / _zoom));
        var next = ScreenshotZoom.Wheel(ZoomSlider.Value, wheel.Properties.MouseWheelDelta);
        ZoomSlider.Value = next; Viewport.UpdateLayout();
        var origin = PaintCanvas.TransformToVisual(Viewport).TransformPoint(new Point(0, 0));
        Viewport.ChangeView(Viewport.HorizontalOffset + origin.X + image.X * _zoom - anchor.X, Viewport.VerticalOffset + origin.Y + image.Y * _zoom - anchor.Y, null, true);
        UpdateCursor(image);
    }
    private void Canvas_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (_busy || _dialogOpen) return;
        var point = e.GetPosition(PaintCanvas); var item = Hits(new((float)(point.X / _zoom), (float)(point.Y / _zoom))).FirstOrDefault();
        if (item?.Tool is not (ScreenshotTool.Text or ScreenshotTool.Callout or ScreenshotTool.Watermark)) return;
        CancelGesture(); _region = null; SetCurrentTool(ScreenshotTool.Select); SetSelection([item.Id]);
        TextInput.Focus(FocusState.Programmatic); TextInput.SelectAll(); e.Handled = true;
    }
    private void Zoom_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        _zoom = e.NewValue / 100; if (ZoomLabel is null) return; ZoomLabel.Content = KeyLabel($"{e.NewValue:0}%", "Alt+D"); Refresh();
    }
    private void FitImage()
    {
        if (Viewport is null || Viewport.ActualWidth < 1 || Viewport.ActualHeight < 1) return;
        _fitting = true;
        try { ZoomSlider.Value = Math.Clamp(Math.Min((Viewport.ActualWidth - 44) / (_canvasResizePreview?.Width ?? _document.State.Frame.Width), (Viewport.ActualHeight - 44) / (_canvasResizePreview?.Height ?? _document.State.Frame.Height)) * 100, 10, 100); }
        finally { _fitting = false; }
    }
    private void Fit_Click(object sender, RoutedEventArgs e) { if (!_busy) FitImage(); }
    private void ZoomReset_Click(object sender, RoutedEventArgs e) { if (!_busy) ZoomSlider.Value = 100; }
    private void Viewport_SizeChanged(object sender, SizeChangedEventArgs e) { if (!_fitting && !_hasImage && !Dirty) FitImage(); }
    private void Crop_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _region is not { } region) return;
        ClampRegion(); region = _region!.Value;
        var x = (int)region.X; var y = (int)region.Y;
        _document.Crop(x, y, Math.Max(1, (int)region.Width), Math.Max(1, (int)region.Height));
        _region = null; _selectedIds.Clear(); _hasImage = true; Refresh(); FitImage();
    }
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
                ClipBounds = a.ClipBounds is { } clip ? TransformBox(clip) : null, Cutouts = a.Cutouts.IsDefault ? a.Cutouts : a.Cutouts.Select(TransformBox).ToImmutableArray() };
        }).ToImmutableArray();
        _document.Apply(_document.State with { Frame = new(output.Width, output.Height, output.Snapshot()), Annotations = annotations });
        _region = null; _selectedIds.Clear(); _hasImage = true; Refresh(); FitImage();
    }
}
