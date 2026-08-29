using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace ChunPaint;

public sealed partial class MainWindow
{
    private ShapeKind _shapeKind = ShapeKind.Line;
    private Button? _selectedShapeButton;
    private Vector2 _shapeStart;
    private Vector2 _shapeEnd;
    private bool _isDrawingShape;
    private bool _drawShapeOutline = true;
    private bool _drawShapeFill;

    private void ShapeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || !Enum.TryParse(button.Tag?.ToString(), out ShapeKind shape))
        {
            return;
        }

        _shapeKind = shape;
        _tool = PaintTool.Shape;
        foreach (var toolButton in _toolButtons)
        {
            toolButton.IsChecked = false;
        }

        ClearShapeSelectionVisual();
        _selectedShapeButton = button;
        button.Background = new SolidColorBrush(Color.FromArgb(255, 116, 107, 225));
        button.Foreground = new SolidColorBrush(Colors.White);
        if (shape == ShapeKind.FilledStar)
        {
            _drawShapeFill = true;
            UpdateShapeStyleLabels();
        }
    }

    private void ClearShapeSelectionVisual()
    {
        if (_selectedShapeButton is null)
        {
            return;
        }

        _selectedShapeButton.Background = new SolidColorBrush(Colors.Transparent);
        _selectedShapeButton.Foreground = new SolidColorBrush(Colors.Black);
        _selectedShapeButton = null;
    }

    private void BeginShape(Vector2 point, PointerRoutedEventArgs e)
    {
        CaptureUndo();
        _shapeStart = point;
        _shapeEnd = point;
        _isDrawingShape = true;
        PaintCanvas.CapturePointer(e.Pointer);
        PaintCanvas.Invalidate();
    }

    private void UpdateShape(Vector2 point)
    {
        _shapeEnd = new Vector2(
            Math.Clamp(point.X, 0, _imageWidth),
            Math.Clamp(point.Y, 0, _imageHeight));
        PaintCanvas.Invalidate();
    }

    private void EndShape(PointerRoutedEventArgs e)
    {
        _isDrawingShape = false;
        PaintCanvas.ReleasePointerCapture(e.Pointer);
        if (_surface is null)
        {
            return;
        }

        using var session = _surface.CreateDrawingSession();
        DrawCurrentShape(session, _shapeStart, _shapeEnd, 1f);
        FinishEdit();
    }

    private void DrawShapePreview(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (!_isDrawingShape)
        {
            return;
        }

        DrawCurrentShape(args.DrawingSession, _shapeStart, _shapeEnd, (float)_zoom);
    }

    private void DrawCurrentShape(CanvasDrawingSession session, Vector2 start, Vector2 end, float scale)
    {
        var scaledStart = start * scale;
        var scaledEnd = end * scale;
        var bounds = NormalizeShapeBounds(scaledStart, scaledEnd);
        var outline = WithCurrentOpacity(_primaryColor);
        var fill = WithCurrentOpacity(_secondaryColor);
        var strokeWidth = Math.Max(1f, (float)StrokeWidthSlider.Value * scale);

        switch (_shapeKind)
        {
            case ShapeKind.Line:
                if (_drawShapeOutline) session.DrawLine(scaledStart, scaledEnd, outline, strokeWidth);
                return;
            case ShapeKind.Curve:
                DrawCurve(session, scaledStart, scaledEnd, outline, strokeWidth);
                return;
            case ShapeKind.Ellipse:
                DrawEllipse(session, bounds, outline, fill, strokeWidth);
                return;
            case ShapeKind.Rectangle:
                DrawRectangle(session, bounds, outline, fill, strokeWidth, 0);
                return;
            case ShapeKind.RoundedRectangle:
                DrawRectangle(session, bounds, outline, fill, strokeWidth, Math.Max(4, Math.Min(bounds.Width, bounds.Height) / 6));
                return;
            case ShapeKind.Parallelogram:
                DrawPolygon(session, CreateParallelogram(bounds), outline, fill, strokeWidth);
                return;
            case ShapeKind.Triangle:
                DrawPolygon(session, CreateTriangle(bounds), outline, fill, strokeWidth);
                return;
            case ShapeKind.RightTriangle:
                DrawPolygon(session, [new((float)bounds.Left, (float)bounds.Bottom), new((float)bounds.Left, (float)bounds.Top), new((float)bounds.Right, (float)bounds.Bottom)], outline, fill, strokeWidth);
                return;
            case ShapeKind.Diamond:
                DrawPolygon(session, CreateDiamond(bounds), outline, fill, strokeWidth);
                return;
            case ShapeKind.Pentagon:
                DrawPolygon(session, CreateRegularPolygon(bounds, 5, -MathF.PI / 2), outline, fill, strokeWidth);
                return;
            case ShapeKind.Hexagon:
                DrawPolygon(session, CreateRegularPolygon(bounds, 6, 0), outline, fill, strokeWidth);
                return;
            case ShapeKind.RightArrow:
                DrawPolygon(session, CreateArrow(bounds, ArrowDirection.Right), outline, fill, strokeWidth);
                return;
            case ShapeKind.LeftArrow:
                DrawPolygon(session, CreateArrow(bounds, ArrowDirection.Left), outline, fill, strokeWidth);
                return;
            case ShapeKind.UpArrow:
                DrawPolygon(session, CreateArrow(bounds, ArrowDirection.Up), outline, fill, strokeWidth);
                return;
            case ShapeKind.DownArrow:
                DrawPolygon(session, CreateArrow(bounds, ArrowDirection.Down), outline, fill, strokeWidth);
                return;
            case ShapeKind.FourPointStar:
                DrawPolygon(session, CreateStar(bounds, 4, 0.28f), outline, fill, strokeWidth);
                return;
            case ShapeKind.Star:
            case ShapeKind.FilledStar:
                DrawPolygon(session, CreateStar(bounds, 5, 0.44f), outline, fill, strokeWidth);
                return;
            case ShapeKind.Callout:
                DrawPolygon(session, CreateCallout(bounds), outline, fill, strokeWidth);
                return;
            case ShapeKind.Oval:
                DrawEllipse(session, bounds, outline, fill, strokeWidth);
                return;
            case ShapeKind.Cloud:
                DrawCloud(session, bounds, outline, fill, strokeWidth);
                return;
        }
    }

    private static Rect NormalizeShapeBounds(Vector2 start, Vector2 end)
    {
        return new Rect(
            Math.Min(start.X, end.X),
            Math.Min(start.Y, end.Y),
            Math.Max(1, Math.Abs(end.X - start.X)),
            Math.Max(1, Math.Abs(end.Y - start.Y)));
    }

    private void DrawRectangle(CanvasDrawingSession session, Rect bounds, Color outline, Color fill, float strokeWidth, double radius)
    {
        if (_drawShapeFill)
        {
            if (radius > 0) session.FillRoundedRectangle(bounds, (float)radius, (float)radius, fill);
            else session.FillRectangle(bounds, fill);
        }
        if (_drawShapeOutline)
        {
            if (radius > 0) session.DrawRoundedRectangle(bounds, (float)radius, (float)radius, outline, strokeWidth);
            else session.DrawRectangle(bounds, outline, strokeWidth);
        }
    }

    private void DrawEllipse(CanvasDrawingSession session, Rect bounds, Color outline, Color fill, float strokeWidth)
    {
        var center = new Vector2((float)(bounds.X + bounds.Width / 2), (float)(bounds.Y + bounds.Height / 2));
        var radiusX = (float)bounds.Width / 2;
        var radiusY = (float)bounds.Height / 2;
        if (_drawShapeFill) session.FillEllipse(center, radiusX, radiusY, fill);
        if (_drawShapeOutline) session.DrawEllipse(center, radiusX, radiusY, outline, strokeWidth);
    }

    private void DrawCurve(CanvasDrawingSession session, Vector2 start, Vector2 end, Color outline, float strokeWidth)
    {
        if (!_drawShapeOutline) return;
        using var builder = new CanvasPathBuilder(session.Device);
        var delta = end - start;
        builder.BeginFigure(start);
        builder.AddCubicBezier(start + new Vector2(delta.X * 0.25f, -delta.Y * 0.35f), start + new Vector2(delta.X * 0.75f, delta.Y * 1.35f), end);
        builder.EndFigure(CanvasFigureLoop.Open);
        using var geometry = CanvasGeometry.CreatePath(builder);
        session.DrawGeometry(geometry, outline, strokeWidth);
    }

    private void DrawPolygon(CanvasDrawingSession session, Vector2[] points, Color outline, Color fill, float strokeWidth)
    {
        if (points.Length < 3) return;
        using var geometry = CanvasGeometry.CreatePolygon(session.Device, points);
        if (_drawShapeFill) session.FillGeometry(geometry, fill);
        if (_drawShapeOutline) session.DrawGeometry(geometry, outline, strokeWidth);
    }

    private void DrawCloud(CanvasDrawingSession session, Rect bounds, Color outline, Color fill, float strokeWidth)
    {
        var lobes = new[]
        {
            new Rect(bounds.X, bounds.Y + bounds.Height * .35, bounds.Width * .45, bounds.Height * .55),
            new Rect(bounds.X + bounds.Width * .18, bounds.Y + bounds.Height * .05, bounds.Width * .45, bounds.Height * .65),
            new Rect(bounds.X + bounds.Width * .48, bounds.Y + bounds.Height * .18, bounds.Width * .45, bounds.Height * .62),
            new Rect(bounds.X + bounds.Width * .28, bounds.Y + bounds.Height * .4, bounds.Width * .55, bounds.Height * .5)
        };
        foreach (var lobe in lobes)
        {
            DrawEllipse(session, lobe, outline, fill, strokeWidth);
        }
    }

    private static Vector2[] CreateParallelogram(Rect bounds)
    {
        var offset = (float)bounds.Width * .22f;
        return [new((float)bounds.Left + offset, (float)bounds.Top), new((float)bounds.Right, (float)bounds.Top), new((float)bounds.Right - offset, (float)bounds.Bottom), new((float)bounds.Left, (float)bounds.Bottom)];
    }

    private static Vector2[] CreateTriangle(Rect bounds) =>
        [new((float)(bounds.X + bounds.Width / 2), (float)bounds.Top), new((float)bounds.Right, (float)bounds.Bottom), new((float)bounds.Left, (float)bounds.Bottom)];

    private static Vector2[] CreateDiamond(Rect bounds) =>
        [new((float)(bounds.X + bounds.Width / 2), (float)bounds.Top), new((float)bounds.Right, (float)(bounds.Y + bounds.Height / 2)), new((float)(bounds.X + bounds.Width / 2), (float)bounds.Bottom), new((float)bounds.Left, (float)(bounds.Y + bounds.Height / 2))];

    private static Vector2[] CreateRegularPolygon(Rect bounds, int sides, float rotation)
    {
        var center = new Vector2((float)(bounds.X + bounds.Width / 2), (float)(bounds.Y + bounds.Height / 2));
        var radiusX = (float)bounds.Width / 2;
        var radiusY = (float)bounds.Height / 2;
        return Enumerable.Range(0, sides)
            .Select(index =>
            {
                var angle = rotation + (MathF.Tau * index / sides);
                return center + new Vector2(MathF.Cos(angle) * radiusX, MathF.Sin(angle) * radiusY);
            })
            .ToArray();
    }

    private static Vector2[] CreateStar(Rect bounds, int points, float innerRatio)
    {
        var center = new Vector2((float)(bounds.X + bounds.Width / 2), (float)(bounds.Y + bounds.Height / 2));
        var radiusX = (float)bounds.Width / 2;
        var radiusY = (float)bounds.Height / 2;
        var result = new Vector2[points * 2];
        for (var index = 0; index < result.Length; index++)
        {
            var ratio = index % 2 == 0 ? 1f : innerRatio;
            var angle = -MathF.PI / 2 + (MathF.PI * index / points);
            result[index] = center + new Vector2(MathF.Cos(angle) * radiusX * ratio, MathF.Sin(angle) * radiusY * ratio);
        }
        return result;
    }

    private static Vector2[] CreateArrow(Rect bounds, ArrowDirection direction)
    {
        Vector2[] normalized =
        [
            new(0, .3f), new(.62f, .3f), new(.62f, 0), new(1, .5f),
            new(.62f, 1), new(.62f, .7f), new(0, .7f)
        ];
        return normalized.Select(point =>
        {
            var transformed = direction switch
            {
                ArrowDirection.Left => new Vector2(1 - point.X, point.Y),
                ArrowDirection.Up => new Vector2(point.Y, 1 - point.X),
                ArrowDirection.Down => new Vector2(point.Y, point.X),
                _ => point
            };
            return new Vector2((float)bounds.X + transformed.X * (float)bounds.Width, (float)bounds.Y + transformed.Y * (float)bounds.Height);
        }).ToArray();
    }

    private static Vector2[] CreateCallout(Rect bounds)
    {
        return
        [
            new((float)bounds.Left, (float)bounds.Top),
            new((float)bounds.Right, (float)bounds.Top),
            new((float)bounds.Right, (float)(bounds.Y + bounds.Height * .72)),
            new((float)(bounds.X + bounds.Width * .62), (float)(bounds.Y + bounds.Height * .72)),
            new((float)(bounds.X + bounds.Width * .45), (float)bounds.Bottom),
            new((float)(bounds.X + bounds.Width * .48), (float)(bounds.Y + bounds.Height * .72)),
            new((float)bounds.Left, (float)(bounds.Y + bounds.Height * .72))
        ];
    }

    private void OutlineStyle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement anchor) return;
        var flyout = new MenuFlyout();
        var solid = new ToggleMenuFlyoutItem { Text = "単色", IsChecked = _drawShapeOutline };
        var none = new ToggleMenuFlyoutItem { Text = "輪郭なし", IsChecked = !_drawShapeOutline };
        solid.Click += (_, _) => { _drawShapeOutline = true; UpdateShapeStyleLabels(); };
        none.Click += (_, _) => { _drawShapeOutline = false; UpdateShapeStyleLabels(); };
        flyout.Items.Add(solid);
        flyout.Items.Add(none);
        flyout.ShowAt(anchor);
    }

    private void FillStyle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement anchor) return;
        var flyout = new MenuFlyout();
        var solid = new ToggleMenuFlyoutItem { Text = "単色", IsChecked = _drawShapeFill };
        var none = new ToggleMenuFlyoutItem { Text = "塗りつぶしなし", IsChecked = !_drawShapeFill };
        solid.Click += (_, _) => { _drawShapeFill = true; UpdateShapeStyleLabels(); };
        none.Click += (_, _) => { _drawShapeFill = false; UpdateShapeStyleLabels(); };
        flyout.Items.Add(solid);
        flyout.Items.Add(none);
        flyout.ShowAt(anchor);
    }

    private void UpdateShapeStyleLabels()
    {
        if (OutlineStyleText is not null) OutlineStyleText.Text = _drawShapeOutline ? "単色" : "なし";
        if (FillStyleText is not null) FillStyleText.Text = _drawShapeFill ? "単色" : "なし";
    }

    private enum ArrowDirection
    {
        Right,
        Left,
        Up,
        Down
    }

    private enum ShapeKind
    {
        Line,
        Curve,
        Ellipse,
        Rectangle,
        RoundedRectangle,
        Parallelogram,
        Triangle,
        RightTriangle,
        Diamond,
        Pentagon,
        Hexagon,
        RightArrow,
        LeftArrow,
        UpArrow,
        DownArrow,
        FourPointStar,
        Star,
        FilledStar,
        Callout,
        Oval,
        Cloud
    }
}
