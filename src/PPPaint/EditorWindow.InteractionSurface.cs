using System.Numerics;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using PPPaint.Core;
using Windows.Foundation;

namespace PPPaint;

public sealed partial class EditorWindow
{
    private readonly record struct CanvasHit(bool Inside, ScreenshotCanvasEdge? Edge, int Handle);

    private void InvalidateCanvas() { PaintCanvas?.Invalidate(); InteractionCanvas?.Invalidate(); }
    private Point InteractionOrigin => PaintCanvas.TransformToVisual(InteractionCanvas).TransformPoint(new Point(0, 0));
    private void Interaction_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession; ds.Clear(Colors.Transparent);
        var origin = InteractionOrigin;
        ds.Transform = Matrix3x2.CreateScale((float)_zoom) * Matrix3x2.CreateTranslation((float)origin.X, (float)origin.Y);
        DrawSelection(ds); DrawSnapGuides(ds); DrawCanvasCrop(ds); DrawEraserFootprint(ds); DrawPointerBadge(ds);
    }
    private bool IsImagePoint(Vector2 point) => point.X >= 0 && point.Y >= 0 && point.X <= _document.State.Frame.Width && point.Y <= _document.State.Frame.Height;
    private bool ObjectHandlesEnabled => !_pickingColor && _tool is not (ScreenshotTool.Region or ScreenshotTool.Number or ScreenshotTool.Eraser);
    private int HitEditingHandle(Vector2 point) => _pickingColor ? -1 : _tool == ScreenshotTool.Region ? HitRegionHandle(point) : ObjectHandlesEnabled ? HitHandle(point) : -1;
    private CanvasHit HitCanvas(Vector2 point, bool includeCanvasBorder)
    {
        var inside = IsImagePoint(point);
        var edge = includeCanvasBorder ? HitCanvasEdge(point) : null;
        return new(inside, edge, edge is null ? HitEditingHandle(point) : -1);
    }
    private ScreenshotCanvasEdge? HitCanvasEdge(Vector2 point)
    {
        if (CanvasImageBorder.ActualWidth <= 0 || CanvasImageBorder.ActualHeight <= 0 || PaintCanvas.ActualWidth <= 0 || PaintCanvas.ActualHeight <= 0) return null;
        var outer = CanvasImageBorder.TransformToVisual(PaintCanvas).TransformBounds(new Rect(0, 0, CanvasImageBorder.ActualWidth, CanvasImageBorder.ActualHeight));
        return ScreenshotCanvasGeometry.HitBorder(
            new((float)outer.X, (float)outer.Y, (float)outer.Width, (float)outer.Height),
            new(0, 0, (float)PaintCanvas.ActualWidth, (float)PaintCanvas.ActualHeight),
            point * (float)_zoom);
    }
    private static ScreenshotBox ObjectSelectionBounds(ScreenshotAnnotation[] objects) => objects is [{ Tool: ScreenshotTool.Callout } callout]
        ? callout.ClipBounds is null ? callout.Bounds : callout.VisualBounds : ScreenshotGeometry.Bounds(objects);
}
