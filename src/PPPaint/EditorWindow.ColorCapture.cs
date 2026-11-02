using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml.Input;
using PPPaint.Core;
using PPPaint.Imaging;

namespace PPPaint;

public sealed partial class EditorWindow
{
    private CanvasRenderTarget? _colorCapture;
    private Rgba32? _sampledColor;
    private void BeginColorCapture(Vector2 point, PointerRoutedEventArgs e)
    {
        try
        {
            _colorCapture = ScreenshotRenderer.Render(PaintCanvas, _document.State);
            _selectionBefore = new(_selectedIds); _regionBefore = _region; _primaryBefore = _primaryId;
            _pointerId = e.Pointer.PointerId; _gesture = Gesture.PickColor;
            InteractionCanvas.CapturePointer(e.Pointer); PreviewPickedColor(point); UpdateCursor(point);
        }
        catch (Exception) { ClearColorCapture(); SetColorPicking(false); StatusText.Text = "色を取得できませんでした。"; }
    }
    private void PreviewPickedColor(Vector2 point)
    {
        if (_colorCapture is null || !IsImagePoint(point)) { _sampledColor = null; InvalidateCanvas(); return; }
        var size = _colorCapture.SizeInPixels;
        var x = Math.Clamp((int)Math.Floor(point.X), 0, (int)size.Width - 1); var y = Math.Clamp((int)Math.Floor(point.Y), 0, (int)size.Height - 1);
        _sampledColor = Rgba32.FromPremultipliedBgra(_colorCapture.GetPixelBytes(x, y, 1, 1)) with { A = 255 };
        StatusText.Text = ColorHex(_sampledColor.Value); InvalidateCanvas();
    }
    private void FinishColorCapture(Vector2 point, PointerRoutedEventArgs e)
    {
        PreviewPickedColor(point); var color = _sampledColor;
        _pointerId = null; _gesture = Gesture.None; InteractionCanvas.ReleasePointerCapture(e.Pointer);
        ClearColorCapture(); SetColorPicking(false);
        if (color is { } value) SetDrawingColor(value);
        StatusText.Text = ColorHex(_drawingColor);
        ShowSelectedOptions(); Refresh(); e.Handled = true;
    }
    private void ClearColorCapture() { _colorCapture?.Dispose(); _colorCapture = null; _sampledColor = null; }
}
