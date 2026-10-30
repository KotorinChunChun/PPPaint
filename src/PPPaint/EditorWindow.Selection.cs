using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using PPPaint.Core;
using System.Numerics;
using Windows.Foundation;

namespace PPPaint;

public sealed partial class EditorWindow
{
    private void DrawSelection(CanvasDrawingSession ds)
    {
        var objects = SelectedObjects.Select(a => _previews.GetValueOrDefault(a.Id, a)).ToArray();
        if (_region is { } region) DrawSelectionBox(ds, region, true, _tool == ScreenshotTool.Region);
        if (objects.Length > 1)
        {
            foreach (var item in objects) DrawSelectionBox(ds, item.Bounds, false, false);
            DrawSelectionBox(ds, ScreenshotGeometry.Bounds(objects), false, _region is null);
        }
        else if (objects.Length == 1)
        {
            var item = objects[0];
            if (HasEndpointHandles(item))
            {
                foreach (var point in item.Points) DrawHandle(ds, point, true);
            }
            else { DrawSelectionBox(ds, item.VisualBounds, false, _region is null); if (item.Tool == ScreenshotTool.Callout && item.VisibleAt(item.Tail)) DrawHandle(ds, item.Tail, true); }
        }
        if (_draft is { } draft) DrawSelectionBox(ds, draft.Bounds, false, false);
    }
    private void DrawSnapGuides(CanvasDrawingSession ds)
    {
        var frame = _document.State.Frame;
        if (_snapX is { } x) ds.DrawLine(x, 0, x, frame.Height, Colors.Magenta, 1 / (float)_zoom);
        if (_snapY is { } y) ds.DrawLine(0, y, frame.Width, y, Colors.Magenta, 1 / (float)_zoom);
    }
    private void DrawSelectionBox(CanvasDrawingSession ds, ScreenshotBox b, bool fill, bool handles)
    {
        var rect = new Rect(b.X, b.Y, Math.Max(1, b.Width), Math.Max(1, b.Height)); var scale = (float)_zoom;
        if (fill) ds.FillRectangle(rect, Windows.UI.Color.FromArgb(25, 0, 120, 215));
        using var style = new CanvasStrokeStyle { DashStyle = CanvasDashStyle.Dash };
        ds.DrawRectangle(rect, Colors.White, 3 / scale);
        ds.DrawRectangle(rect, Colors.DodgerBlue, 1.5f / scale, style);
        if (handles) foreach (var point in ScreenshotGeometry.Handles(b)) DrawHandle(ds, point, false);
    }
    private void DrawHandle(CanvasDrawingSession ds, Vector2 point, bool round)
    {
        var size = 9f / (float)_zoom;
        if (round) { ds.FillCircle(point, size * .6f, Colors.White); ds.DrawCircle(point, size * .6f, Colors.DodgerBlue, 2 / (float)_zoom); }
        else { var rect = new Rect(point.X - size / 2, point.Y - size / 2, size, size); ds.FillRectangle(rect, Colors.White); ds.DrawRectangle(rect, Colors.DodgerBlue, 1.5f / (float)_zoom); }
    }
    private void UpdateCursor(Vector2 point)
    {
        _hoverPoint = point;
        var cursor = InputSystemCursorShape.Arrow; var handle = -1;
        if (KeyDown(Windows.System.VirtualKey.Space) || _gesture == Gesture.Pan) cursor = InputSystemCursorShape.Hand;
        else if (_tool is ScreenshotTool.Select or ScreenshotTool.Region)
        {
            handle = _tool == ScreenshotTool.Region ? HitRegionHandle(point) : HitHandle(point);
            if (handle >= 0)
            {
                cursor = (handle == 8 || HasEndpointHandles(Selected)) && _tool != ScreenshotTool.Region ? InputSystemCursorShape.Cross : handle switch
                {
                    0 or 4 => InputSystemCursorShape.SizeNorthwestSoutheast,
                    2 or 6 => InputSystemCursorShape.SizeNortheastSouthwest,
                    1 or 5 => InputSystemCursorShape.SizeNorthSouth,
                    _ => InputSystemCursorShape.SizeWestEast
                };
            }
            else if (_region?.Contains(point) == true || SelectedObjects.Any(a => a.Hit(point, (float)(5 / _zoom))) || _tool == ScreenshotTool.Select && Hits(point).Length > 0) cursor = InputSystemCursorShape.SizeAll;
            else if (_tool == ScreenshotTool.Region) cursor = InputSystemCursorShape.Cross;
        }
        else cursor = _tool is ScreenshotTool.Text or ScreenshotTool.Watermark ? InputSystemCursorShape.IBeam : InputSystemCursorShape.Cross;
        _pointerCommand = PointerCommand(handle);
        CanvasCursorHost.SetCursorShape(cursor);
        PaintCanvas.Invalidate();
    }
    private void Order_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || sender is not MenuFlyoutItem item || !Enum.TryParse<ScreenshotStackOrder>(item.Tag?.ToString(), out var order)) return;
        _document.Reorder(_selectedIds, order); Refresh();
    }
    private void ShowObjectMenu(Vector2 point, Point position)
    {
        var flyout = new MenuFlyout();
        foreach (var (label, order) in new[] { ("最前面へ", ScreenshotStackOrder.Front), ("1つ前へ", ScreenshotStackOrder.Forward), ("1つ後ろへ", ScreenshotStackOrder.Backward), ("最背面へ", ScreenshotStackOrder.Back) })
        {
            var item = new MenuFlyoutItem { Text = label, Tag = order.ToString(), IsEnabled = _selectedIds.Count > 0 }; item.Click += Order_Click; flyout.Items.Add(item);
        }
        var hits = Hits(point);
        if (hits.Length > 0)
        {
            flyout.Items.Add(new MenuFlyoutSeparator()); var targets = new MenuFlyoutSubItem { Text = "選択する対象" };
            foreach (var hit in hits)
            {
                var label = hit.Tool switch { ScreenshotTool.Image => "画像: " + hit.Text, ScreenshotTool.Text => "文字: " + hit.Text, ScreenshotTool.Number => "番号: " + hit.Text,
                    ScreenshotTool.Arrow => "矢印", ScreenshotTool.Line => "直線", ScreenshotTool.Rectangle => "四角", ScreenshotTool.Ellipse => "丸", ScreenshotTool.Redact => "情報隠し", ScreenshotTool.Highlight => "蛍光ペン", ScreenshotTool.Eraser => "消しゴム", ScreenshotTool.Blur => "ぼかし", ScreenshotTool.Mosaic => "モザイク", ScreenshotTool.Callout => "吹き出し", ScreenshotTool.Watermark => "透かし", _ => "ペン" };
                var item = new MenuFlyoutItem { Text = label.Length > 48 ? label[..48] + "…" : label };
                item.Click += (_, _) => { _region = null; SetCurrentTool(ScreenshotTool.Select); SetSelection([hit.Id]); };
                targets.Items.Add(item);
            }
            flyout.Items.Add(targets);
        }
        flyout.Items.Add(new MenuFlyoutSeparator()); var delete = new MenuFlyoutItem { Text = "削除", IsEnabled = _selectedIds.Count > 0 }; delete.Click += Delete_Click; flyout.Items.Add(delete);
        flyout.ShowAt(PaintCanvas, new FlyoutShowOptions { Position = position });
    }
}
