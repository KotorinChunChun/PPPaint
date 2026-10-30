using Microsoft.Graphics.Canvas;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using PPPaint.Core;
using Windows.Foundation;

namespace PPPaint;

public sealed partial class EditorWindow
{
    private PixelRect? _canvasCrop;
    private ScreenshotCanvasEdge _canvasEdge;
    private int _canvasWidth, _canvasHeight;
    private CursorHost[] CanvasEdges => [CanvasLeftGrip, CanvasTopGrip, CanvasRightGrip, CanvasBottomGrip];

    private void InitializeCanvasEdges()
    {
        CanvasLeftGrip.SetCursorShape(InputSystemCursorShape.SizeWestEast); CanvasRightGrip.SetCursorShape(InputSystemCursorShape.SizeWestEast);
        CanvasTopGrip.SetCursorShape(InputSystemCursorShape.SizeNorthSouth); CanvasBottomGrip.SetCursorShape(InputSystemCursorShape.SizeNorthSouth);
    }
    private void CanvasEdge_Pressed(object sender, PointerRoutedEventArgs e)
    {
        if (_busy || _pointerId is not null || sender is not CursorHost grip || !e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed) return;
        if (!Enum.TryParse<ScreenshotCanvasEdge>(grip.Tag?.ToString(), out _canvasEdge)) return;
        CancelGesture(); _regionBefore = _region; _selectionBefore = new(_selectedIds); _primaryBefore = _primaryId;
        _canvasWidth = _document.State.Frame.Width; _canvasHeight = _document.State.Frame.Height;
        _canvasCrop = new(0, 0, _canvasWidth, _canvasHeight); _gesture = Gesture.CanvasCrop; _pointerId = e.Pointer.PointerId;
        PaintCanvas.Focus(FocusState.Pointer); grip.CapturePointer(e.Pointer); PaintCanvas.Invalidate(); e.Handled = true;
    }
    private void CanvasEdge_Moved(object sender, PointerRoutedEventArgs e)
    {
        if (_pointerId != e.Pointer.PointerId || _gesture != Gesture.CanvasCrop) return;
        var point = ImagePoint(e); var coordinate = _canvasEdge is ScreenshotCanvasEdge.Left or ScreenshotCanvasEdge.Right ? point.X : point.Y;
        _canvasCrop = ScreenshotCanvasGeometry.CropFromEdge(_canvasWidth, _canvasHeight, _canvasEdge, coordinate);
        var crop = _canvasCrop.Value; StatusText.Text = $"{crop.Width} × {crop.Height} px";
        PaintCanvas.Invalidate(); e.Handled = true;
    }
    private void CanvasEdge_Released(object sender, PointerRoutedEventArgs e)
    {
        if (_pointerId != e.Pointer.PointerId || _gesture != Gesture.CanvasCrop) return;
        CanvasEdge_Moved(sender, e); var crop = _canvasCrop; _pointerId = null; _canvasCrop = null; _gesture = Gesture.None;
        foreach (var grip in CanvasEdges) grip.ReleasePointerCaptures();
        if (crop is { } rect && (rect.Width != _canvasWidth || rect.Height != _canvasHeight))
        {
            _document.Crop(rect.X, rect.Y, rect.Width, rect.Height); _selectedIds.Clear(); _region = null; _hasImage = true;
            StatusText.Text = "キャンバスを切り詰めました。";
        }
        ShowSelectedOptions(); Refresh(); e.Handled = true;
    }
    private void CanvasEdge_Canceled(object sender, PointerRoutedEventArgs e) { if (_pointerId == e.Pointer.PointerId) CancelGesture(); }
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
