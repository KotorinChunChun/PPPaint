using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PPPaint.Core;
using PPPaint.Imaging;
using System.Numerics;
using Windows.Foundation;

namespace PPPaint;

public sealed partial class EditorWindow
{
    private void DrawSelection(CanvasDrawingSession ds)
    {
        DrawObjectGuides(ds);
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
            DrawObjectHandles(ds, objects[0], ObjectHandlesEnabled);
        }
        if (_draft is { Tool: not (ScreenshotTool.Eraser or ScreenshotTool.Pen) } draft) DrawSelectionBox(ds, draft.Bounds, false, false);
    }
    private void DrawObjectGuides(CanvasDrawingSession ds)
    {
        if (!_showObjectHandles) return;
        var single = _selectedIds.Count == 1 && _region is null && ObjectHandlesEnabled ? _selectedIds.First() : null;
        foreach (var item in (_liftPreview ?? _document.State).Annotations)
            if (item.Id != single)
                DrawObjectHandles(ds, _previews.GetValueOrDefault(item.Id, item), enabled: true, muted: true);
    }
    private void DrawObjectHandles(CanvasDrawingSession ds, ScreenshotAnnotation item, bool enabled, bool muted = false)
    {
        var endpoints = enabled && HasEndpointHandles(item);
        if (!endpoints || muted)
            DrawSelectionBox(ds, ObjectSelectionBounds(item), false, enabled && !endpoints && (muted || _region is null), muted);
        if (endpoints)
            foreach (var point in item.Points) DrawHandle(ds, point, true, muted);
        if (enabled && item.Tool == ScreenshotTool.Callout)
        {
            foreach (var point in ScreenshotCalloutGeometry.Points(item))
                if (item.VisibleAt(point))
                {
                    var radius = ScreenshotHandleGeometry.RoundRadius / (float)_zoom;
                    ds.FillCircle(point, radius, muted ? Colors.SlateGray : Colors.Gold);
                    ds.DrawCircle(point, radius, Colors.Black, 1 / (float)_zoom);
                }
        }
    }
    private void DrawSnapGuides(CanvasDrawingSession ds)
    {
        var frame = _document.State.Frame;
        if (_snapX is { } x) ds.DrawLine(x, 0, x, frame.Height, Colors.Magenta, 1 / (float)_zoom);
        if (_snapY is { } y) ds.DrawLine(0, y, frame.Width, y, Colors.Magenta, 1 / (float)_zoom);
    }
    private void DrawSelectionBox(CanvasDrawingSession ds, ScreenshotBox b, bool fill, bool handles, bool muted = false)
    {
        var rect = new Rect(b.X, b.Y, Math.Max(1, b.Width), Math.Max(1, b.Height)); var scale = (float)_zoom;
        if (fill) ds.FillRectangle(rect, Windows.UI.Color.FromArgb(25, 0, 120, 215));
        using var style = new CanvasStrokeStyle { DashStyle = CanvasDashStyle.Dash };
        ds.DrawRectangle(rect, Colors.White, (muted ? 2 : 3) / scale);
        ds.DrawRectangle(rect, muted ? Colors.SlateGray : Colors.DodgerBlue, (muted ? 1 : 1.5f) / scale, style);
        if (handles) foreach (var point in ScreenshotGeometry.Handles(b)) DrawHandle(ds, point, false, muted);
    }
    private void DrawHandle(CanvasDrawingSession ds, Vector2 point, bool round, bool muted = false)
    {
        ScreenshotHandleRenderer.Draw(ds, point, (float)_zoom, round, muted ? Colors.SlateGray : null);
    }
    private void Order_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement item && Enum.TryParse<ScreenshotStackOrder>(item.Tag?.ToString(), out var order))
            ReorderObjects(order);
    }
    private void ReorderObjects(ScreenshotStackOrder order)
    {
        if (_busy || _dialogOpen || _pointerId is not null || _selectedIds.Count == 0) return;
        _document.Reorder(_selectedIds, order);
        Refresh();
        if (ObjectPanel.Visibility == Visibility.Visible && ObjectList.SelectedItems.Count == 1)
            ObjectList.ScrollIntoView(ObjectList.SelectedItems[0]);
    }
    private void AddOrderItems(MenuFlyout flyout, bool enabled)
    {
        foreach (var (label, order) in new[] { ("最前面へ", ScreenshotStackOrder.Front), ("1つ前へ", ScreenshotStackOrder.Forward), ("1つ後ろへ", ScreenshotStackOrder.Backward), ("最背面へ", ScreenshotStackOrder.Back) })
        {
            var item = new MenuFlyoutItem { Text = label, Tag = order.ToString(), IsEnabled = enabled };
            item.Click += Order_Click;
            flyout.Items.Add(item);
        }
    }
    private void ShowObjectMenu(Vector2 point, Point position)
    {
        var flyout = new MenuFlyout();
        var target = new ScreenshotContextTarget(_region, [.. _selectedIds]);
        AddOrderItems(flyout, target.CanReorder);
        var hits = Hits(point);
        var merge = new MenuFlyoutItem { Text = "選択を画像に統合", IsEnabled = target.CanReorder, KeyboardAcceleratorTextOverride = "Ctrl+E" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(merge, "ObjectContextMerge");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAcceleratorKey(merge, "Ctrl+E");
        ToolTipService.SetToolTip(merge, "選択図形だけを透過背景の画像に統合。元に戻すで復元 (Ctrl+E)");
        merge.Click += MergeSelection_Click; flyout.Items.Add(merge);
        if (hits.Length > 0)
        {
            flyout.Items.Add(new MenuFlyoutSeparator()); var targets = new MenuFlyoutSubItem { Text = "選択する対象" };
            foreach (var hit in hits)
            {
                var item = new MenuFlyoutItem { Text = ScreenshotCommands.Describe(hit) };
                item.Click += (_, _) => SelectObjectTargets([hit.Id]);
                targets.Items.Add(item);
            }
            flyout.Items.Add(targets);
        }
        flyout.Items.Add(new MenuFlyoutSeparator()); var delete = new MenuFlyoutItem { Text = target.Region is null ? "削除" : "範囲を削除", IsEnabled = target.CanDelete };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(delete, "ObjectContextDelete");
        delete.Click += Delete_Click; flyout.Items.Add(delete);
        flyout.ShowAt(PaintCanvas, new FlyoutShowOptions { Position = position });
    }
}
