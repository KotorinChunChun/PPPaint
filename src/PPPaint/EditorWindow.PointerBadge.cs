using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Xaml.Input;
using PPPaint.Core;
using PPPaint.Imaging;
using Windows.Foundation;
using Windows.System;

namespace PPPaint;

public sealed partial class EditorWindow
{
    private Vector2 _hoverPoint;
    private bool _pointerInside;
    private string _pointerCommand = "選択";
    private void Canvas_Entered(object sender, PointerRoutedEventArgs e) { _pointerInside = true; UpdateCursor(ImagePoint(e)); }
    private void Canvas_Exited(object sender, PointerRoutedEventArgs e) { _pointerInside = false; InvalidateCanvas(); }
    private void DrawPointerBadge(CanvasDrawingSession ds)
    {
        if (!_pointerInside || _busy || _dialogOpen || string.IsNullOrEmpty(_pointerCommand)) return;
        if (EraserFootprintVisible) return;
        var previous = ds.Transform; ds.Transform = Matrix3x2.Identity;
        try
        {
            using var format = new CanvasTextFormat { FontFamily = "Yu Gothic UI", FontSize = 16 };
            using var text = new CanvasTextLayout(ds, _pointerCommand, format, 230, 32);
            var width = (float)text.LayoutBounds.Width + 12; const float height = 27;
            var origin = InteractionOrigin;
            var x = Math.Clamp((float)origin.X + _hoverPoint.X * (float)_zoom + 18, 1, Math.Max(1, (float)InteractionCanvas.ActualWidth - width - 1));
            var y = Math.Clamp((float)origin.Y + _hoverPoint.Y * (float)_zoom + 20, 1, Math.Max(1, (float)InteractionCanvas.ActualHeight - height - 1));
            ds.FillRoundedRectangle(new Rect(x, y, width, height), 4, 4, Windows.UI.Color.FromArgb(235, 255, 255, 255));
            ds.DrawRoundedRectangle(new Rect(x, y, width, height), 4, 4, Colors.DodgerBlue, 1);
            ds.DrawTextLayout(text, x + 6, y + 2, Colors.Black);
        }
        finally { ds.Transform = previous; }
    }
    private bool EraserFootprintVisible => _tool == ScreenshotTool.Eraser && !_pickingColor && _gesture is Gesture.None or Gesture.Draw && (IsImagePoint(_hoverPoint) || _draft?.Tool == ScreenshotTool.Eraser) && !KeyDown(VirtualKey.Space) && _pointerInside && !_busy && !_dialogOpen;
    private void DrawEraserFootprint(CanvasDrawingSession ds)
    {
        if (!EraserFootprintVisible) return;
        var eraser = _draft is { Tool: ScreenshotTool.Eraser } draft ? draft : null;
        var position = eraser is { Points.IsDefaultOrEmpty: false } ? eraser.Points[^1] : _hoverPoint;
        ScreenshotEraserPreview.Draw(ds, position, eraser?.Width ?? CurrentWidth, (float)_zoom);
    }
    private string PointerCommand(int handle)
    {
        if (_pickingColor) return "スポイト";
        if (_gesture == Gesture.CanvasCrop) return "キャンバス切取";
        if (_gesture == Gesture.Pan || KeyDown(VirtualKey.Space)) return "画面移動";
        if (_gesture == Gesture.NumberPlace || _tool == ScreenshotTool.Number) return "番号 " + _document.State.NextNumber;
        if (_gesture == Gesture.CalloutTail || handle == 8) return "尾の移動";
        if (_gesture == Gesture.Endpoint) return "端点の移動";
        if (_gesture is Gesture.Resize or Gesture.RegionResize || handle >= 0) return KeyDown(VirtualKey.Control) ? "中心サイズ変更" : "サイズ変更";
        if (_gesture == Gesture.Move) return KeyDown(VirtualKey.Control) ? "複製" : "移動";
        if (_gesture == Gesture.RegionMove) return "範囲移動";
        if (_tool == ScreenshotTool.Select && Hits(_hoverPoint).Length > 0) return KeyDown(VirtualKey.Control) ? "複製" : "移動";
        return ScreenshotCommands.Tools.FirstOrDefault(c => c.Tool == _tool)?.Name ?? "選択";
    }
}
