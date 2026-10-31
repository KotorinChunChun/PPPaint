using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using PPPaint.Core;
using PPPaint.Imaging;
using System.Numerics;
using Windows.Foundation;

namespace PPPaint;

public sealed partial class EditorWindow
{
    private void DrawSelection(CanvasDrawingSession ds)
    {
        if (_tool == ScreenshotTool.Eraser) return;
        var objects = SelectedObjects.Select(a => _previews.GetValueOrDefault(a.Id, a)).ToArray();
        if (_region is { } region) DrawSelectionBox(ds, region, true, _tool == ScreenshotTool.Region && !_pickingColor);
        if (objects.Length > 1)
        {
            foreach (var item in objects) DrawSelectionBox(ds, item.VisualBounds, false, false);
            DrawSelectionBox(ds, ObjectSelectionBounds(objects), false, _region is null && ObjectHandlesEnabled);
        }
        else if (objects.Length == 1)
        {
            var item = objects[0];
            if (HasEndpointHandles(item) && ObjectHandlesEnabled)
            {
                foreach (var point in item.Points) DrawHandle(ds, point, true);
            }
            else { DrawSelectionBox(ds, ObjectSelectionBounds(objects), false, _region is null && ObjectHandlesEnabled); if (ObjectHandlesEnabled && item.Tool == ScreenshotTool.Callout && item.VisibleAt(item.Tail)) DrawHandle(ds, item.Tail, true); }
        }
        if (_draft is { Tool: not ScreenshotTool.Eraser } draft) DrawSelectionBox(ds, draft.Bounds, false, false);
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
        ScreenshotHandleRenderer.Draw(ds, point, (float)_zoom, round);
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
