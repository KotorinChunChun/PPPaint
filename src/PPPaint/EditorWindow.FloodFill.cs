using System.Numerics;
using Microsoft.UI.Xaml.Input;
using PPPaint.Core;
using PPPaint.Imaging;

namespace PPPaint;

public sealed partial class EditorWindow
{
    private Rgba32 _floodFillColor;

    private void FinishFloodFill(Vector2 point, PointerRoutedEventArgs e)
    {
        _pointerId = null;
        _gesture = Gesture.None;
        InteractionCanvas.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
        if (!IsImagePoint(point)) return;

        try
        {
            var state = _document.State;
            using var rendered = ScreenshotRenderer.Render(PaintCanvas, state);
            var patch = ScreenshotFloodFill.CreatePatch(rendered.GetPixelBytes(), state.Frame.Width, state.Frame.Height,
                (int)MathF.Floor(_start.X), (int)MathF.Floor(_start.Y), _floodFillColor);
            if (patch is null) return;
            _document.Add(patch);
            _hasImage = true;
            _region = null;
            SetSelection([]);
        }
        catch (Exception error)
        {
            StatusText.Text = error is InvalidOperationException ? error.Message : "塗りつぶしを適用できませんでした。";
        }
    }
}
