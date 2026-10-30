using System.Numerics;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using PPPaint.Core;
using Windows.Foundation;

namespace PPPaint;

public sealed partial class EditorWindow
{
    private void InvalidateCanvas() { PaintCanvas?.Invalidate(); InteractionCanvas?.Invalidate(); }
    private Point InteractionOrigin => PaintCanvas.TransformToVisual(InteractionCanvas).TransformPoint(new Point(0, 0));
    private void Interaction_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession; ds.Clear(Colors.Transparent);
        if (_canvasResizePreview is not null) return;
        var origin = InteractionOrigin;
        ds.Transform = Matrix3x2.CreateScale((float)_zoom) * Matrix3x2.CreateTranslation((float)origin.X, (float)origin.Y);
        DrawSelection(ds); DrawSnapGuides(ds); DrawCanvasCrop(ds); DrawEraserFootprint(ds); DrawPointerBadge(ds);
    }
    private bool IsImagePoint(Vector2 point) => point.X >= 0 && point.Y >= 0 && point.X <= _document.State.Frame.Width && point.Y <= _document.State.Frame.Height;
    private bool ObjectHandlesEnabled => !_pickingColor && _tool is not (ScreenshotTool.Region or ScreenshotTool.Number or ScreenshotTool.Eraser);
    private int HitEditingHandle(Vector2 point) => _pickingColor ? -1 : _tool == ScreenshotTool.Region ? HitRegionHandle(point) : ObjectHandlesEnabled ? HitHandle(point) : -1;
    private ScreenshotCanvasEdge? HitCanvasEdge(Vector2 point)
    {
        var pixel = new Point(point.X * _zoom, point.Y * _zoom);
        foreach (var grip in CanvasEdges)
        {
            if (grip.ActualWidth <= 0 || grip.ActualHeight <= 0) continue;
            var rectangle = grip.TransformToVisual(PaintCanvas).TransformBounds(new Rect(0, 0, grip.ActualWidth, grip.ActualHeight));
            if (rectangle.Contains(pixel) && Enum.TryParse<ScreenshotCanvasEdge>(grip.Tag?.ToString(), out var edge)) return edge;
        }
        return null;
    }
    private static ScreenshotBox ObjectSelectionBounds(ScreenshotAnnotation[] objects) => objects is [{ Tool: ScreenshotTool.Callout } callout]
        ? callout.ClipBounds is null ? callout.Bounds : callout.VisualBounds : ScreenshotGeometry.Bounds(objects);
}
