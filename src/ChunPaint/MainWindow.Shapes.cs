using System.Numerics;
using ChunPaint.Core;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace ChunPaint;

public sealed partial class MainWindow
{
    private const long MinimumShapeDurationMilliseconds = 100;
    private const float MinimumShapeScreenDistance = 6f;

    private ShapeKind _shapeKind = ShapeKind.Line;
    private Button? _selectedShapeButton;
    private Vector2 _shapeStart;
    private Vector2 _shapeEnd;
    private long _shapeStartedAt;
    private bool _isDrawingShape;
    private bool _drawShapeOutline = true;
    private bool _drawShapeFill;
    private ShapeStrokeKind _shapeOutlineKind = ShapeStrokeKind.Solid;
    private ShapeFillKind _shapeFillKind = ShapeFillKind.Solid;

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
        }
        UpdateShapeStyleVisuals();
        UpdateCanvasCursor();
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
        _shapeStart = point;
        _shapeEnd = point;
        _shapeStartedAt = Environment.TickCount64;
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

    private bool CancelShapeDraft()
    {
        if (!_isDrawingShape)
        {
            return false;
        }

        _isDrawingShape = false;
        PaintCanvas.ReleasePointerCaptures();
        PaintCanvas.Invalidate();
        return true;
    }

    private void EndShape(PointerRoutedEventArgs e)
    {
        _isDrawingShape = false;
        PaintCanvas.ReleasePointerCapture(e.Pointer);
        if (_surface is null)
        {
            return;
        }

        if (ShouldCancelShapeDraft())
        {
            PaintCanvas.Invalidate();
            return;
        }

        var bounds = NormalizeShapeBounds(_shapeStart, _shapeEnd);
        var style = new ShapeObjectStyle(
            OutlineColor: ToCoreColor(WithCurrentOpacity(_primaryColor)),
            FillColor: ToCoreColor(WithCurrentOpacity(_secondaryColor)),
            StrokeWidth: Math.Max(1f, (float)StrokeWidthSlider.Value),
            DrawOutline: _drawShapeOutline,
            DrawFill: _drawShapeFill,
            OutlineKind: _shapeOutlineKind,
            FillKind: _shapeFillKind);
        var editable = EditableObject.CreateShape(
            (EditableShapeKind)_shapeKind,
            new ObjectBounds((float)bounds.X, (float)bounds.Y, (float)bounds.Width, (float)bounds.Height),
            style,
            ObjectPoint.FromVector(_shapeStart),
            ObjectPoint.FromVector(_shapeEnd),
            layerId: ActiveLayerId);
        CaptureUndo();
        _objects.Add(editable);
        ActivateEditableObject(editable.Id, isNew: true);
        _selectionMode = SelectionMode.Rectangle;
        ActivateSelectionTool();
        FinishEdit();
    }

    private bool ShouldCancelShapeDraft()
    {
        var elapsedMilliseconds = Environment.TickCount64 - _shapeStartedAt;
        var screenDistance = Vector2.Distance(_shapeStart, _shapeEnd) * (float)_zoom;
        return elapsedMilliseconds < MinimumShapeDurationMilliseconds || screenDistance < MinimumShapeScreenDistance;
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
                DrawShapeLine(session, scaledStart, scaledEnd, outline, strokeWidth, _drawShapeOutline, _shapeOutlineKind);
                return;
            case ShapeKind.Curve:
                DrawCurve(session, scaledStart, scaledEnd, outline, strokeWidth, _drawShapeOutline, _shapeOutlineKind);
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
            case ShapeKind.Heart:
                DrawPolygon(session, CreateHeart(bounds), outline, fill, strokeWidth);
                return;
            case ShapeKind.Lightning:
                DrawPolygon(session, CreateLightning(bounds), outline, fill, strokeWidth);
                return;
        }
    }

    private void DrawEditableShape(CanvasDrawingSession session, EditableObject editable, float scale)
    {
        if (editable.Shape is not { } shape || editable.ShapeStyle is not { } style)
        {
            return;
        }

        var bounds = new Rect(
            editable.Bounds.X * scale,
            editable.Bounds.Y * scale,
            editable.Bounds.Width * scale,
            editable.Bounds.Height * scale);
        var center = new Vector2((float)(bounds.X + bounds.Width / 2), (float)(bounds.Y + bounds.Height / 2));
        var previousTransform = session.Transform;
        session.Transform = Matrix3x2.CreateRotation(editable.RotationDegrees * (MathF.PI / 180f), center) * previousTransform;
        var outline = ToWindowsColor(style.OutlineColor);
        var fill = ToWindowsColor(style.FillColor);
        var strokeWidth = Math.Max(1f, style.StrokeWidth * scale);

        switch (shape)
        {
            case EditableShapeKind.Line:
                if (style.DrawOutline)
                {
                    var start = editable.StartPoint is { } first ? first.ToVector2() * scale : new Vector2((float)bounds.Left, (float)bounds.Top);
                    var end = editable.EndPoint is { } second ? second.ToVector2() * scale : new Vector2((float)bounds.Right, (float)bounds.Bottom);
                    DrawShapeLine(session, start, end, outline, strokeWidth, style.DrawOutline, style.OutlineKind);
                }
                break;
            case EditableShapeKind.Curve:
                if (style.DrawOutline)
                {
                    var start = editable.StartPoint is { } first ? first.ToVector2() * scale : new Vector2((float)bounds.Left, (float)bounds.Top);
                    var end = editable.EndPoint is { } second ? second.ToVector2() * scale : new Vector2((float)bounds.Right, (float)bounds.Bottom);
                    DrawCurve(session, start, end, outline, strokeWidth, style.DrawOutline, style.OutlineKind);
                }
                break;
            case EditableShapeKind.Ellipse:
            case EditableShapeKind.Oval:
                DrawEllipse(session, bounds, outline, fill, strokeWidth, style);
                break;
            case EditableShapeKind.Rectangle:
                DrawRectangle(session, bounds, outline, fill, strokeWidth, 0, style);
                break;
            case EditableShapeKind.RoundedRectangle:
                DrawRectangle(session, bounds, outline, fill, strokeWidth, Math.Max(4, Math.Min(bounds.Width, bounds.Height) / 6), style);
                break;
            case EditableShapeKind.Parallelogram:
                DrawPolygon(session, CreateParallelogram(bounds), outline, fill, strokeWidth, style);
                break;
            case EditableShapeKind.Triangle:
                DrawPolygon(session, CreateTriangle(bounds), outline, fill, strokeWidth, style);
                break;
            case EditableShapeKind.RightTriangle:
                DrawPolygon(session, [new((float)bounds.Left, (float)bounds.Bottom), new((float)bounds.Left, (float)bounds.Top), new((float)bounds.Right, (float)bounds.Bottom)], outline, fill, strokeWidth, style);
                break;
            case EditableShapeKind.Diamond:
                DrawPolygon(session, CreateDiamond(bounds), outline, fill, strokeWidth, style);
                break;
            case EditableShapeKind.Pentagon:
                DrawPolygon(session, CreateRegularPolygon(bounds, 5, -MathF.PI / 2), outline, fill, strokeWidth, style);
                break;
            case EditableShapeKind.Hexagon:
                DrawPolygon(session, CreateRegularPolygon(bounds, 6, 0), outline, fill, strokeWidth, style);
                break;
            case EditableShapeKind.RightArrow:
                DrawPolygon(session, CreateArrow(bounds, ArrowDirection.Right), outline, fill, strokeWidth, style);
                break;
            case EditableShapeKind.LeftArrow:
                DrawPolygon(session, CreateArrow(bounds, ArrowDirection.Left), outline, fill, strokeWidth, style);
                break;
            case EditableShapeKind.UpArrow:
                DrawPolygon(session, CreateArrow(bounds, ArrowDirection.Up), outline, fill, strokeWidth, style);
                break;
            case EditableShapeKind.DownArrow:
                DrawPolygon(session, CreateArrow(bounds, ArrowDirection.Down), outline, fill, strokeWidth, style);
                break;
            case EditableShapeKind.FourPointStar:
                DrawPolygon(session, CreateStar(bounds, 4, 0.28f), outline, fill, strokeWidth, style);
                break;
            case EditableShapeKind.Star:
            case EditableShapeKind.FilledStar:
                DrawPolygon(session, CreateStar(bounds, 5, 0.44f), outline, fill, strokeWidth, style);
                break;
            case EditableShapeKind.Callout:
                DrawPolygon(session, CreateCallout(bounds), outline, fill, strokeWidth, style);
                break;
            case EditableShapeKind.Cloud:
                DrawCloud(session, bounds, outline, fill, strokeWidth, style);
                break;
            case EditableShapeKind.Heart:
                DrawPolygon(session, CreateHeart(bounds), outline, fill, strokeWidth, style);
                break;
            case EditableShapeKind.Lightning:
                DrawPolygon(session, CreateLightning(bounds), outline, fill, strokeWidth, style);
                break;
        }

        session.Transform = previousTransform;
    }

    private static Rect NormalizeShapeBounds(Vector2 start, Vector2 end)
    {
        return new Rect(
            Math.Min(start.X, end.X),
            Math.Min(start.Y, end.Y),
            Math.Max(1, Math.Abs(end.X - start.X)),
            Math.Max(1, Math.Abs(end.Y - start.Y)));
    }

    private void DrawRectangle(CanvasDrawingSession session, Rect bounds, Color outline, Color fill, float strokeWidth, double radius, ShapeObjectStyle? style = null)
    {
        var drawFill = style?.DrawFill ?? _drawShapeFill;
        var drawOutline = style?.DrawOutline ?? _drawShapeOutline;
        var fillKind = style?.FillKind ?? _shapeFillKind;
        var outlineKind = style?.OutlineKind ?? _shapeOutlineKind;
        if (drawFill)
        {
            var fillColor = GetShapeFillColor(fill, fillKind);
            if (radius > 0) session.FillRoundedRectangle(bounds, (float)radius, (float)radius, fillColor);
            else session.FillRectangle(bounds, fillColor);
        }
        if (drawOutline)
        {
            using var strokeStyle = CreateShapeStrokeStyle(outlineKind);
            var outlineColor = GetShapeStrokeColor(outline, outlineKind);
            if (radius > 0) session.DrawRoundedRectangle(bounds, (float)radius, (float)radius, outlineColor, strokeWidth, strokeStyle);
            else session.DrawRectangle(bounds, outlineColor, strokeWidth, strokeStyle);
        }
    }

    private void DrawEllipse(CanvasDrawingSession session, Rect bounds, Color outline, Color fill, float strokeWidth, ShapeObjectStyle? style = null)
    {
        var center = new Vector2((float)(bounds.X + bounds.Width / 2), (float)(bounds.Y + bounds.Height / 2));
        var radiusX = (float)bounds.Width / 2;
        var radiusY = (float)bounds.Height / 2;
        if (style?.DrawFill ?? _drawShapeFill) session.FillEllipse(center, radiusX, radiusY, GetShapeFillColor(fill, style?.FillKind ?? _shapeFillKind));
        if (style?.DrawOutline ?? _drawShapeOutline)
        {
            var outlineKind = style?.OutlineKind ?? _shapeOutlineKind;
            using var strokeStyle = CreateShapeStrokeStyle(outlineKind);
            session.DrawEllipse(center, radiusX, radiusY, GetShapeStrokeColor(outline, outlineKind), strokeWidth, strokeStyle);
        }
    }

    private void DrawShapeLine(
        CanvasDrawingSession session,
        Vector2 start,
        Vector2 end,
        Color outline,
        float strokeWidth,
        bool drawOutline,
        ShapeStrokeKind kind)
    {
        if (!drawOutline) return;
        using var strokeStyle = CreateShapeStrokeStyle(kind);
        session.DrawLine(start, end, GetShapeStrokeColor(outline, kind), strokeWidth, strokeStyle);
    }

    private void DrawCurve(
        CanvasDrawingSession session,
        Vector2 start,
        Vector2 end,
        Color outline,
        float strokeWidth,
        bool drawOutline,
        ShapeStrokeKind kind)
    {
        if (!drawOutline) return;
        using var builder = new CanvasPathBuilder(session.Device);
        var delta = end - start;
        builder.BeginFigure(start);
        builder.AddCubicBezier(start + new Vector2(delta.X * 0.25f, -delta.Y * 0.35f), start + new Vector2(delta.X * 0.75f, delta.Y * 1.35f), end);
        builder.EndFigure(CanvasFigureLoop.Open);
        using var geometry = CanvasGeometry.CreatePath(builder);
        using var strokeStyle = CreateShapeStrokeStyle(kind);
        session.DrawGeometry(geometry, GetShapeStrokeColor(outline, kind), strokeWidth, strokeStyle);
    }

    private void DrawPolygon(CanvasDrawingSession session, Vector2[] points, Color outline, Color fill, float strokeWidth, ShapeObjectStyle? style = null)
    {
        if (points.Length < 3) return;
        using var geometry = CanvasGeometry.CreatePolygon(session.Device, points);
        if (style?.DrawFill ?? _drawShapeFill) session.FillGeometry(geometry, GetShapeFillColor(fill, style?.FillKind ?? _shapeFillKind));
        if (style?.DrawOutline ?? _drawShapeOutline)
        {
            var outlineKind = style?.OutlineKind ?? _shapeOutlineKind;
            using var strokeStyle = CreateShapeStrokeStyle(outlineKind);
            session.DrawGeometry(geometry, GetShapeStrokeColor(outline, outlineKind), strokeWidth, strokeStyle);
        }
    }

    private static CanvasStrokeStyle CreateShapeStrokeStyle(ShapeStrokeKind kind)
    {
        var strokeStyle = new CanvasStrokeStyle();
        if (kind == ShapeStrokeKind.Marker)
        {
            strokeStyle.StartCap = CanvasCapStyle.Square;
            strokeStyle.EndCap = CanvasCapStyle.Square;
        }
        return strokeStyle;
    }

    private static Color GetShapeStrokeColor(Color color, ShapeStrokeKind kind) =>
        WithShapeAlpha(color, kind switch
        {
            ShapeStrokeKind.Crayon => 0.82f,
            ShapeStrokeKind.Marker => 0.58f,
            ShapeStrokeKind.Oil => 0.9f,
            ShapeStrokeKind.Pencil => 0.72f,
            ShapeStrokeKind.Watercolor => 0.48f,
            _ => 1f
        });

    private static Color GetShapeFillColor(Color color, ShapeFillKind kind) =>
        WithShapeAlpha(color, kind switch
        {
            ShapeFillKind.Crayon => 0.82f,
            ShapeFillKind.Marker => 0.58f,
            ShapeFillKind.Oil => 0.9f,
            ShapeFillKind.Pencil => 0.72f,
            ShapeFillKind.Watercolor => 0.48f,
            _ => 1f
        });

    private static Color WithShapeAlpha(Color color, float opacity)
    {
        var alpha = (byte)Math.Clamp((int)MathF.Round(color.A * opacity), 0, 255);
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    private void DrawCloud(CanvasDrawingSession session, Rect bounds, Color outline, Color fill, float strokeWidth, ShapeObjectStyle? style = null)
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
            DrawEllipse(session, lobe, outline, fill, strokeWidth, style);
        }
    }

    private static Vector2[] CreateHeart(Rect bounds) =>
    [
        new((float)(bounds.X + bounds.Width * .5), (float)bounds.Bottom),
        new((float)(bounds.X + bounds.Width * .08), (float)(bounds.Y + bounds.Height * .38)),
        new((float)(bounds.X + bounds.Width * .1), (float)(bounds.Y + bounds.Height * .16)),
        new((float)(bounds.X + bounds.Width * .27), (float)bounds.Top),
        new((float)(bounds.X + bounds.Width * .5), (float)(bounds.Y + bounds.Height * .2)),
        new((float)(bounds.X + bounds.Width * .73), (float)bounds.Top),
        new((float)(bounds.X + bounds.Width * .9), (float)(bounds.Y + bounds.Height * .16)),
        new((float)(bounds.X + bounds.Width * .92), (float)(bounds.Y + bounds.Height * .38))
    ];

    private static Vector2[] CreateLightning(Rect bounds) =>
    [
        new((float)(bounds.X + bounds.Width * .56), (float)bounds.Top),
        new((float)(bounds.X + bounds.Width * .12), (float)(bounds.Y + bounds.Height * .56)),
        new((float)(bounds.X + bounds.Width * .45), (float)(bounds.Y + bounds.Height * .56)),
        new((float)(bounds.X + bounds.Width * .34), (float)bounds.Bottom),
        new((float)(bounds.X + bounds.Width * .88), (float)(bounds.Y + bounds.Height * .3)),
        new((float)(bounds.X + bounds.Width * .55), (float)(bounds.Y + bounds.Height * .3))
    ];

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

    private void OutlineStyleButton_Click(object sender, RoutedEventArgs e)
    {
        var flyout = new MenuFlyout();
        AddOutlineStyleMenuItem(flyout, null, "なし", "╱");
        AddOutlineStyleMenuItem(flyout, ShapeStrokeKind.Solid, "実線（ペン）", "━");
        AddOutlineStyleMenuItem(flyout, ShapeStrokeKind.Crayon, "クレヨン", "〰");
        AddOutlineStyleMenuItem(flyout, ShapeStrokeKind.Marker, "マーカー", "▬");
        AddOutlineStyleMenuItem(flyout, ShapeStrokeKind.Oil, "油彩ブラシ", "≋");
        AddOutlineStyleMenuItem(flyout, ShapeStrokeKind.Pencil, "鉛筆（通常）", "✎");
        AddOutlineStyleMenuItem(flyout, ShapeStrokeKind.Watercolor, "水彩ブラシ", "≈");
        flyout.ShowAt(OutlineStyleButton, new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft });
    }

    private void FillStyleButton_Click(object sender, RoutedEventArgs e)
    {
        var flyout = new MenuFlyout();
        AddFillStyleMenuItem(flyout, null, "なし", "╱");
        AddFillStyleMenuItem(flyout, ShapeFillKind.Solid, "単色（ペンキ）", "▰");
        AddFillStyleMenuItem(flyout, ShapeFillKind.Crayon, "クレヨン", "〰");
        AddFillStyleMenuItem(flyout, ShapeFillKind.Marker, "マーカー", "▬");
        AddFillStyleMenuItem(flyout, ShapeFillKind.Oil, "油彩ブラシ", "≋");
        AddFillStyleMenuItem(flyout, ShapeFillKind.Pencil, "鉛筆（通常）", "✎");
        AddFillStyleMenuItem(flyout, ShapeFillKind.Watercolor, "水彩ブラシ", "≈");
        flyout.ShowAt(FillStyleButton, new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft });
    }

    private void AddOutlineStyleMenuItem(MenuFlyout flyout, ShapeStrokeKind? kind, string label, string glyph)
    {
        var item = new ToggleMenuFlyoutItem
        {
            Text = label,
            Icon = CreateUnifiedIcon("E-outline-" + (kind?.ToString().ToLowerInvariant() ?? "none")),
            IsChecked = kind is null ? !_drawShapeOutline : _drawShapeOutline && _shapeOutlineKind == kind.Value
        };
        item.Click += (_, _) =>
        {
            _drawShapeOutline = kind is not null;
            if (kind is { } selected) _shapeOutlineKind = selected;
            UpdateShapeStyleVisuals();
        };
        flyout.Items.Add(item);
    }

    private void AddFillStyleMenuItem(MenuFlyout flyout, ShapeFillKind? kind, string label, string glyph)
    {
        var item = new ToggleMenuFlyoutItem
        {
            Text = label,
            Icon = CreateUnifiedIcon("E-fill-" + (kind?.ToString().ToLowerInvariant() ?? "none")),
            IsChecked = kind is null ? !_drawShapeFill : _drawShapeFill && _shapeFillKind == kind.Value
        };
        item.Click += (_, _) =>
        {
            _drawShapeFill = kind is not null;
            if (kind is { } selected) _shapeFillKind = selected;
            UpdateShapeStyleVisuals();
        };
        flyout.Items.Add(item);
    }

    private void UpdateShapeStyleVisuals()
    {
        if (OutlineStyleButton is not null) SetUnifiedButtonIcon(OutlineStyleButton, "E-outline-" + (_drawShapeOutline ? _shapeOutlineKind.ToString().ToLowerInvariant() : "none"));
        if (FillStyleButton is not null) SetUnifiedButtonIcon(FillStyleButton, "E-fill-" + (_drawShapeFill ? _shapeFillKind.ToString().ToLowerInvariant() : "none"));
        if (OutlineStyleName is not null)
        {
            OutlineStyleName.Text = _drawShapeOutline ? GetShapeStrokeLabel(_shapeOutlineKind) : "なし";
            OutlineStyleIcon.Text = _drawShapeOutline ? GetShapeStrokeGlyph(_shapeOutlineKind) : "╱";
            OutlineStyleGlyph.Text = _drawShapeOutline ? GetShapeStrokeGlyph(_shapeOutlineKind) : "╱";
            ToolTipService.SetToolTip(OutlineStyleButton, $"輪郭の種類（ペン）: {OutlineStyleName.Text}");
        }
        if (FillStyleName is not null)
        {
            FillStyleName.Text = _drawShapeFill ? GetShapeFillLabel(_shapeFillKind) : "なし";
            FillStyleIcon.Text = _drawShapeFill ? GetShapeFillGlyph(_shapeFillKind) : "╱";
            FillStyleGlyph.Text = _drawShapeFill ? GetShapeFillGlyph(_shapeFillKind) : "╱";
            ToolTipService.SetToolTip(FillStyleButton, $"塗りの種類（ペンキ）: {FillStyleName.Text}");
        }
        PaintCanvas?.Invalidate();
    }

    private static string GetShapeStrokeLabel(ShapeStrokeKind kind) => kind switch
    {
        ShapeStrokeKind.Crayon => "クレヨン",
        ShapeStrokeKind.Marker => "マーカー",
        ShapeStrokeKind.Oil => "油彩",
        ShapeStrokeKind.Pencil => "鉛筆",
        ShapeStrokeKind.Watercolor => "水彩",
        _ => "実線"
    };

    private static string GetShapeStrokeGlyph(ShapeStrokeKind kind) => kind switch
    {
        ShapeStrokeKind.Crayon => "〰",
        ShapeStrokeKind.Marker => "▬",
        ShapeStrokeKind.Oil => "≋",
        ShapeStrokeKind.Pencil => "✎",
        ShapeStrokeKind.Watercolor => "≈",
        _ => "━"
    };

    private static string GetShapeFillLabel(ShapeFillKind kind) => kind switch
    {
        ShapeFillKind.Crayon => "クレヨン",
        ShapeFillKind.Marker => "マーカー",
        ShapeFillKind.Oil => "油彩",
        ShapeFillKind.Pencil => "鉛筆",
        ShapeFillKind.Watercolor => "水彩",
        _ => "ペンキ"
    };

    private static string GetShapeFillGlyph(ShapeFillKind kind) => kind switch
    {
        ShapeFillKind.Crayon => "〰",
        ShapeFillKind.Marker => "▬",
        ShapeFillKind.Oil => "≋",
        ShapeFillKind.Pencil => "✎",
        ShapeFillKind.Watercolor => "≈",
        _ => "▰"
    };

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
        Cloud,
        Heart,
        Lightning
    }
}
