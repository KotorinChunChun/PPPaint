using System.Numerics;
using Microsoft.Graphics.Canvas.UI.Xaml;
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
    private ScreenshotCanvasHandle _canvasEdge;
    private int _canvasWidth, _canvasHeight;
    private double _canvasCropZoom;
    private Point _canvasCropStart, _canvasCropCurrent, _canvasCropOrigin;
    private CursorHost[] CanvasEdges => [CanvasLeftGrip, CanvasTopGrip, CanvasRightGrip, CanvasBottomGrip,
        CanvasTopLeftGrip, CanvasTopRightGrip, CanvasBottomLeftGrip, CanvasBottomRightGrip];

    private static InputSystemCursorShape CanvasResizeCursor(ScreenshotCanvasHandle handle) => handle switch
    {
        ScreenshotCanvasHandle.Left or ScreenshotCanvasHandle.Right => InputSystemCursorShape.SizeWestEast,
        ScreenshotCanvasHandle.Top or ScreenshotCanvasHandle.Bottom => InputSystemCursorShape.SizeNorthSouth,
        ScreenshotCanvasHandle.TopLeft or ScreenshotCanvasHandle.BottomRight => InputSystemCursorShape.SizeNorthwestSoutheast,
        _ => InputSystemCursorShape.SizeNortheastSouthwest
    };

    private void InitializeCanvasEdges()
    {
        CanvasLeftGrip.SetCursorShape(InputSystemCursorShape.SizeWestEast); CanvasRightGrip.SetCursorShape(InputSystemCursorShape.SizeWestEast);
        CanvasTopGrip.SetCursorShape(InputSystemCursorShape.SizeNorthSouth); CanvasBottomGrip.SetCursorShape(InputSystemCursorShape.SizeNorthSouth);
        CanvasTopLeftGrip.SetCursorShape(InputSystemCursorShape.SizeNorthwestSoutheast); CanvasBottomRightGrip.SetCursorShape(InputSystemCursorShape.SizeNorthwestSoutheast);
        CanvasTopRightGrip.SetCursorShape(InputSystemCursorShape.SizeNortheastSouthwest); CanvasBottomLeftGrip.SetCursorShape(InputSystemCursorShape.SizeNortheastSouthwest);
        foreach (var grip in CanvasEdges) Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(grip, "ドラッグで余白追加・切り詰め、Ctrlで対角側も同量変更");
    }
    private void BeginCanvasCrop(ScreenshotCanvasHandle edge, PointerRoutedEventArgs e)
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
            CancelGesture(); StatusText.Text = "表示位置が変わったためキャンバスのサイズ変更を取り消しました。"; return;
        }
        _canvasCrop = ScreenshotCanvasGeometry.ResizeFromDrag(_canvasWidth, _canvasHeight, _canvasEdge,
            _canvasCropStart.X, _canvasCropStart.Y, _canvasCropCurrent.X, _canvasCropCurrent.Y,
            _canvasCropZoom, KeyDown(VirtualKey.Control));
        _pointerInside = true; _pointerCommand = KeyDown(VirtualKey.Control) ? "中心キャンバスサイズ変更" : "キャンバスサイズ変更";
        var crop = _canvasCrop.Value; StatusText.Text = $"{crop.Width} × {crop.Height} px";
        InvalidateCanvas();
    }
    private void FinishCanvasCrop(PointerRoutedEventArgs e)
    {
        PreviewCanvasCrop(e); var crop = _canvasCrop; _pointerId = null; _canvasCrop = null; _gesture = Gesture.None;
        InteractionCanvas.ReleasePointerCapture(e.Pointer);
        if (crop is { } rect && (rect.X != 0 || rect.Y != 0 || rect.Width != _canvasWidth || rect.Height != _canvasHeight))
        {
            try
            {
                _document.Apply(ScreenshotCanvasResize.FromBounds(_document.State, rect).Apply(_document.State)); _selectedIds.Clear(); _region = null; _hasImage = true;
                StatusText.Text = "キャンバスのサイズを変更しました。";
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or OutOfMemoryException)
            {
                StatusText.Text = "キャンバスのサイズを変更できませんでした。";
                _ = RunAsync(() => MessageAsync("キャンバスサイズ", error is OutOfMemoryException ? "画像用のメモリを確保できませんでした。元の画像は保持しています。" : error.Message));
            }
        }
        ShowSelectedOptions(); Refresh(); UpdateCursor(ImagePoint(e));
    }
    private void CanvasResizeOverlay_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession; ds.Clear(Colors.Transparent);
        if (_canvasCrop is not { } crop) return;
        var origin = PaintCanvas.TransformToVisual(sender).TransformPoint(new Point(0, 0));
        ds.Transform = Matrix3x2.CreateScale((float)_zoom) * Matrix3x2.CreateTranslation((float)origin.X, (float)origin.Y);
        var shade = Windows.UI.Color.FromArgb(135, 30, 41, 59);
        var left = Math.Clamp(crop.X, 0, _canvasWidth); var right = Math.Clamp(crop.Right, 0, _canvasWidth);
        var top = Math.Clamp(crop.Y, 0, _canvasHeight); var bottom = Math.Clamp(crop.Bottom, 0, _canvasHeight);
        if (left > 0) ds.FillRectangle(new Rect(0, 0, left, _canvasHeight), shade);
        if (right < _canvasWidth) ds.FillRectangle(new Rect(right, 0, _canvasWidth - right, _canvasHeight), shade);
        if (top > 0) ds.FillRectangle(new Rect(left, 0, right - left, top), shade);
        if (bottom < _canvasHeight) ds.FillRectangle(new Rect(left, bottom, right - left, _canvasHeight - bottom), shade);
        DrawSelectionBox(ds, new(crop.X, crop.Y, crop.Width, crop.Height), false, false);
    }
}
