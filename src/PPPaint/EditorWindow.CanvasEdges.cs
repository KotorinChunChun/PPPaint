using Microsoft.Graphics.Canvas;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using PPPaint.Core;
using Windows.Foundation;
using Windows.System;

namespace PPPaint;

public sealed partial class EditorWindow
{
    private PixelRect? _canvasCrop;
    private ScreenshotCanvasEdge _canvasEdge;
    private int _canvasWidth, _canvasHeight;
    private double _canvasCropZoom;
    private Point _canvasCropStart, _canvasCropCurrent, _canvasCropOrigin;
    private CursorHost[] CanvasEdges => [CanvasLeftGrip, CanvasTopGrip, CanvasRightGrip, CanvasBottomGrip];

    private void InitializeCanvasEdges()
    {
        CanvasLeftGrip.SetCursorShape(InputSystemCursorShape.SizeWestEast); CanvasRightGrip.SetCursorShape(InputSystemCursorShape.SizeWestEast);
        CanvasTopGrip.SetCursorShape(InputSystemCursorShape.SizeNorthSouth); CanvasBottomGrip.SetCursorShape(InputSystemCursorShape.SizeNorthSouth);
        foreach (var grip in CanvasEdges) Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(grip, "ドラッグで切り詰め、Ctrlで反対辺も同量変更");
    }
    private void BeginCanvasCrop(ScreenshotCanvasEdge edge, PointerRoutedEventArgs e)
    {
        _canvasEdge = edge; _canvasWidth = _document.State.Frame.Width; _canvasHeight = _document.State.Frame.Height;
        _canvasCropZoom = _zoom;
        _canvasCropStart = _canvasCropCurrent = e.GetCurrentPoint(PaintCanvas).Position;
        _canvasCropOrigin = PaintCanvas.TransformToVisual(Viewport).TransformPoint(new Point(0, 0));
        _canvasCrop = new(0, 0, _canvasWidth, _canvasHeight); _gesture = Gesture.CanvasCrop; _pointerId = e.Pointer.PointerId;
        InteractionCanvas.Focus(FocusState.Pointer); InteractionCanvas.CapturePointer(e.Pointer); PreviewCanvasCrop();
    }
    private void PreviewCanvasCrop(PointerRoutedEventArgs e)
    {
        _canvasCropCurrent = e.GetCurrentPoint(PaintCanvas).Position;
        _lastPoint = _hoverPoint = ImagePoint(e);
        PreviewCanvasCrop();
    }
    private void PreviewCanvasCrop()
    {
        var origin = PaintCanvas.TransformToVisual(Viewport).TransformPoint(new Point(0, 0));
        if (_canvasCropZoom != _zoom || Math.Abs(origin.X - _canvasCropOrigin.X) > .001 || Math.Abs(origin.Y - _canvasCropOrigin.Y) > .001)
        {
            // Fit/viewport layout can move the image while a pointer is held.
            // Cancel instead of treating that movement as a user's crop drag.
            CancelGesture(); StatusText.Text = "表示位置が変わったためキャンバスの切り詰めを取り消しました。"; return;
        }
        var horizontal = _canvasEdge is ScreenshotCanvasEdge.Left or ScreenshotCanvasEdge.Right;
        _canvasCrop = ScreenshotCanvasGeometry.CropFromDrag(_canvasWidth, _canvasHeight, _canvasEdge,
            horizontal ? _canvasCropStart.X : _canvasCropStart.Y, horizontal ? _canvasCropCurrent.X : _canvasCropCurrent.Y,
            _canvasCropZoom, KeyDown(VirtualKey.Control));
        _pointerInside = true; _pointerCommand = KeyDown(VirtualKey.Control) ? "中心キャンバス切取" : "キャンバス切取";
        var crop = _canvasCrop.Value; StatusText.Text = $"{crop.Width} × {crop.Height} px";
        InvalidateCanvas();
    }
    private void FinishCanvasCrop(PointerRoutedEventArgs e)
    {
        PreviewCanvasCrop(e); var crop = _canvasCrop; _pointerId = null; _canvasCrop = null; _gesture = Gesture.None;
        InteractionCanvas.ReleasePointerCapture(e.Pointer);
        if (crop is { } rect && (rect.Width != _canvasWidth || rect.Height != _canvasHeight))
        {
            _document.Crop(rect.X, rect.Y, rect.Width, rect.Height); _selectedIds.Clear(); _region = null; _hasImage = true;
            StatusText.Text = "キャンバスを切り詰めました。";
        }
        ShowSelectedOptions(); Refresh(); UpdateCursor(ImagePoint(e));
    }
    private void DrawCanvasCrop(CanvasDrawingSession ds)
    {
        if (_canvasCrop is not { } crop) return;
        var shade = Windows.UI.Color.FromArgb(135, 30, 41, 59);
        if (crop.X > 0) ds.FillRectangle(new Rect(0, 0, crop.X, _canvasHeight), shade);
        if (crop.Y > 0) ds.FillRectangle(new Rect(crop.X, 0, crop.Width, crop.Y), shade);
        if (crop.Right < _canvasWidth) ds.FillRectangle(new Rect(crop.Right, 0, _canvasWidth - crop.Right, _canvasHeight), shade);
        if (crop.Bottom < _canvasHeight) ds.FillRectangle(new Rect(crop.X, crop.Bottom, crop.Width, _canvasHeight - crop.Bottom), shade);
        DrawSelectionBox(ds, new(crop.X, crop.Y, crop.Width, crop.Height), false, false);
    }
}
