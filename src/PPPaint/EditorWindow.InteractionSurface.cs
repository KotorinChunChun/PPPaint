using System.Numerics;
using Microsoft.Graphics.Canvas.UI.Xaml;
using PPPaint.Core;
using Windows.Foundation;

namespace PPPaint;

public sealed partial class EditorWindow
{
    private readonly record struct CanvasHit(bool Inside, ScreenshotCanvasHandle? Edge, int Handle, ScreenshotAnnotation? HandleObject = null);

    private void InvalidateCanvas() { PaintCanvas?.Invalidate(); InteractionCanvas?.Invalidate(); CanvasResizeOverlay?.Invalidate(); }
    private Point InteractionOrigin => PaintCanvas.TransformToVisual(InteractionCanvas).TransformPoint(new Point(0, 0));
    private void Interaction_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession; ds.Clear(Colors.Transparent);
        var origin = InteractionOrigin;
        ds.Transform = Matrix3x2.CreateScale((float)_zoom) * Matrix3x2.CreateTranslation((float)origin.X, (float)origin.Y);
        DrawSelection(ds); DrawSnapGuides(ds); DrawPenFootprint(ds); DrawEraserFootprint(ds); DrawPointerBadge(ds);
    }
    private bool IsImagePoint(Vector2 point) => point.X >= 0 && point.Y >= 0 && point.X <= _document.State.Frame.Width && point.Y <= _document.State.Frame.Height;
    private bool ObjectHandlesEnabled => !_pickingColor && _tool is not (ScreenshotTool.Region or ScreenshotTool.Number or ScreenshotTool.Eraser);
    private int HitEditingHandle(Vector2 point) => _pickingColor ? -1 : _tool == ScreenshotTool.Region ? HitRegionHandle(point) : ObjectHandlesEnabled ? HitHandle(point) : -1;
    private CanvasHit HitCanvas(Vector2 point, bool includeCanvasBorder)
    {
        var inside = IsImagePoint(point);
        var edge = includeCanvasBorder ? HitCanvasEdge(point) : null;
        if (edge is not null) return new(inside, edge, -1);
        var handle = HitEditingHandle(point);
        if (handle >= 0 || !_showObjectHandles || _tool != ScreenshotTool.Select || _pickingColor || _gesture != Gesture.None)
            return new(inside, null, handle);

        var objects = _document.State.Annotations;
        for (var index = objects.Length - 1; index >= 0; index--)
        {
            var item = objects[index];
            if (!item.VisualBounds.Contains(point, ScreenshotHandleGeometry.RoundRadius / (float)_zoom)) continue;
            handle = HitObjectHandle(item, point);
            if (handle >= 0) return new(inside, null, handle, item);
        }
        return new(inside, null, -1);
    }
    private ScreenshotCanvasHandle? HitCanvasEdge(Vector2 point)
    {
        if (PaintCanvas.ActualWidth <= 0 || PaintCanvas.ActualHeight <= 0) return null;
        var position = new Point(point.X * _zoom, point.Y * _zoom);
        if (new Rect(0, 0, PaintCanvas.ActualWidth, PaintCanvas.ActualHeight).Contains(position)) return null;
        foreach (var (element, handle) in _canvasGrips)
        {
            var bounds = element.TransformToVisual(PaintCanvas).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            if (bounds.Contains(position)) return handle;
        }
        return null;
    }
    private static ScreenshotBox ObjectSelectionBounds(ScreenshotAnnotation[] objects) => objects is [var item]
        ? ObjectSelectionBounds(item) : ScreenshotGeometry.Bounds(objects);

    private static ScreenshotBox ObjectSelectionBounds(ScreenshotAnnotation item) =>
        item is { Tool: ScreenshotTool.Callout, ClipBounds: null } ? item.Bounds : item.VisualBounds;
}
