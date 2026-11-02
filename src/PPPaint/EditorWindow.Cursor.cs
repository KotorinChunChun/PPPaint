using System.Numerics;
using Microsoft.UI.Input;
using PPPaint.Core;

namespace PPPaint;

public sealed partial class EditorWindow
{
    private void UpdateCursor(Vector2 point)
    {
        _hoverPoint = point;
        var cursor = InputSystemCursorShape.Arrow;
        string? resource = null;
        var hit = _gesture switch
        {
            Gesture.CanvasCrop => new CanvasHit(IsImagePoint(point), _canvasEdge, -1),
            Gesture.Resize or Gesture.RegionResize or Gesture.Endpoint or Gesture.CalloutTail => new CanvasHit(IsImagePoint(point), null, _handle),
            _ => HitCanvas(point, includeCanvasBorder: _gesture == Gesture.None)
        };
        var edge = hit.Edge; var handle = hit.Handle; var inside = hit.Inside;
        if (KeyDown(Windows.System.VirtualKey.Space) || _gesture == Gesture.Pan) { cursor = InputSystemCursorShape.Hand; _pointerCommand = "画面移動"; }
        else if (_pickingColor) { resource = inside ? "PPPaintCursorPicker" : null; _pointerCommand = inside ? "スポイト" : ""; }
        else if (handle >= 0)
        {
            cursor = (handle == 8 || HasEndpointHandles(Selected)) && _tool != ScreenshotTool.Region ? InputSystemCursorShape.Cross : handle switch
            {
                0 or 4 => InputSystemCursorShape.SizeNorthwestSoutheast,
                2 or 6 => InputSystemCursorShape.SizeNortheastSouthwest,
                1 or 5 => InputSystemCursorShape.SizeNorthSouth,
                _ => InputSystemCursorShape.SizeWestEast
            };
            _pointerCommand = PointerCommand(handle);
        }
        else if (edge is { } canvasEdge)
        {
            cursor = CanvasResizeCursor(canvasEdge);
            _pointerCommand = KeyDown(Windows.System.VirtualKey.Control) ? "中心キャンバスサイズ変更" : "キャンバスサイズ変更";
        }
        else if (_gesture is Gesture.Move or Gesture.RegionMove or Gesture.RegionLift) { cursor = InputSystemCursorShape.SizeAll; _pointerCommand = PointerCommand(-1); }
        else if (inside)
        {
            if (_tool is ScreenshotTool.Select or ScreenshotTool.Region)
            {
                if (_region?.Contains(point) == true || _tool == ScreenshotTool.Select && Hits(point).Length > 0) cursor = InputSystemCursorShape.SizeAll;
                else if (_tool == ScreenshotTool.Region) cursor = InputSystemCursorShape.Cross;
            }
            else if (_tool == ScreenshotTool.Pen) resource = "PPPaintCursorPencil";
            else if (_tool == ScreenshotTool.Eraser) resource = "PPPaintCursorInvisible";
            else cursor = _tool is ScreenshotTool.Text ? InputSystemCursorShape.IBeam : InputSystemCursorShape.Cross;
            _pointerCommand = PointerCommand(-1);
        }
        else _pointerCommand = "";
        _toolCursorResource = resource;
        if (resource is not null) CanvasCursorHost.SetCursorResource(resource); else CanvasCursorHost.SetCursorShape(cursor);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetItemStatus(CanvasCursorHost, resource switch { "PPPaintCursorPencil" => "鉛筆", "PPPaintCursorPicker" => "スポイト", "PPPaintCursorInvisible" => "消しゴム（四角）", _ => _pointerCommand });
        InteractionCanvas.Invalidate();
    }
}
