using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using PPPaint.Core;
using Windows.Foundation;
using Windows.UI;

namespace PPPaint.Imaging;

internal static class ScreenshotShapeDrawing
{
    private static CanvasGeometry Polygon(CanvasDrawingSession ds, IReadOnlyList<Vector2> points)
    {
        using var path = new CanvasPathBuilder(ds);
        path.BeginFigure(points[0]); foreach (var point in points.Skip(1)) path.AddLine(point); path.EndFigure(CanvasFigureLoop.Closed);
        return CanvasGeometry.CreatePath(path);
    }
    public static void Arrow(CanvasDrawingSession ds, ScreenshotAnnotation a)
    {
        if (a.Points.Length < 2) return;
        var start = a.Points[0]; var end = a.Points[^1]; var c = a.OutputColor;
        var color = Color.FromArgb(c.A, c.R, c.G, c.B);
        if (a.ArrowStyle is ScreenshotArrowStyle.Line or ScreenshotArrowStyle.DoubleEnded)
        {
            ds.DrawLine(start, end, color, a.Width); ds.FillCircle(start, a.Width / 2, color); ds.FillCircle(end, a.Width / 2, color);
            void Head(Vector2 from, Vector2 tip)
            {
                var head = ScreenshotGeometry.ArrowHead(from, tip, a.Width);
                if (a.IsFilled) { using var geometry = Polygon(ds, new[] { tip, head.Left, head.Right }); ds.FillGeometry(geometry, color); }
                else { ds.DrawLine(tip, head.Left, color, a.Width); ds.DrawLine(tip, head.Right, color, a.Width); }
            }
            Head(start, end); if (a.ArrowStyle == ScreenshotArrowStyle.DoubleEnded) Head(end, start);
            return;
        }
        var points = ScreenshotArrowGeometry.Block(start, end, a.Width); if (points.Length == 0) return;
        if (a.ArrowStyle == ScreenshotArrowStyle.ThreeDimensional)
        {
            var depth = ScreenshotArrowGeometry.Depth(a.Width);
            var shadow = Color.FromArgb(c.A, (byte)(c.R * .55), (byte)(c.G * .55), (byte)(c.B * .55));
            using var back = Polygon(ds, points.Select(p => p + depth).ToArray());
            if (a.IsFilled) ds.FillGeometry(back, shadow);
            ds.DrawGeometry(back, shadow, a.Width);
            foreach (var point in points) ds.DrawLine(point, point + depth, shadow, Math.Max(1, a.Width / 2));
        }
        using var polygon = Polygon(ds, points);
        if (a.IsFilled) ds.FillGeometry(polygon, color);
        ds.DrawGeometry(polygon, color, a.Width);
    }
    public static Rgba32 Number(CanvasDrawingSession ds, ScreenshotAnnotation a)
    {
        var b = a.Bounds; var c = a.Color; var color = Color.FromArgb(255, c.R, c.G, c.B);
        if (a.NumberStyle == ScreenshotNumberStyle.Plain) return c;
        if (a.NumberStyle == ScreenshotNumberStyle.Square)
        {
            var rect = new Rect(b.X, b.Y, b.Width, b.Height);
            if (a.IsFilled) ds.FillRectangle(rect, color); else ds.DrawRectangle(rect, color, a.Width);
        }
        else
        {
            var center = new Vector2(b.X + b.Width / 2, b.Y + b.Height / 2);
            if (a.IsFilled) ds.FillEllipse(center, b.Width / 2, b.Height / 2, color); else ds.DrawEllipse(center, b.Width / 2, b.Height / 2, color, a.Width);
        }
        return !a.IsFilled ? c : c.R * .299 + c.G * .587 + c.B * .114 > 145 ? Rgba32.Black : Rgba32.White;
    }
}
