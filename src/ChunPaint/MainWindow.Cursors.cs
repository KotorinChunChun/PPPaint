using System.Numerics;
using ChunPaint.Core;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChunPaint;

public sealed partial class MainWindow
{
    private void UpdateCanvasCursor(Vector2? point = null)
    {
        PaintCanvasCursorHost.SetCursorShape(ResolveCanvasCursor(point));
        UpdateCanvasPenCursorIndicator(point);
    }

    private void UpdateCanvasPenCursorIndicator(Vector2? point)
    {
        var glyph = ResolveCanvasPenGlyph();
        if (CanvasPenCursorIndicator is null || CanvasPenCursorGlyph is null || point is not { } imagePoint || !IsInside(imagePoint) || glyph is null)
        {
            if (CanvasPenCursorIndicator is not null)
            {
                CanvasPenCursorIndicator.Visibility = Visibility.Collapsed;
            }
            return;
        }

        CanvasPenCursorGlyph.Text = glyph;
        CanvasPenCursorIndicator.Visibility = Visibility.Visible;
        Canvas.SetLeft(CanvasPenCursorIndicator, imagePoint.X * _zoom - CanvasPenCursorIndicator.Width / 2);
        Canvas.SetTop(CanvasPenCursorIndicator, imagePoint.Y * _zoom - CanvasPenCursorIndicator.Height / 2);
    }

    private string? ResolveCanvasPenGlyph() => _tool switch
    {
        PaintTool.Pencil => "✎",
        PaintTool.Brush => _brushKind switch
        {
            BrushKind.Watercolor => "≈",
            BrushKind.Marker => "▬",
            BrushKind.NaturalPencil => "✎",
            BrushKind.Airbrush => "✺",
            BrushKind.Calligraphy => "◢",
            _ => "≋"
        },
        _ => null
    };

    private InputSystemCursorShape ResolveCanvasCursor(Vector2? point)
    {
        if (_isMovingObject || _isMovingSelection)
        {
            return InputSystemCursorShape.SizeAll;
        }
        if (_isResizingObject)
        {
            return CursorForHandle(_objectHandle);
        }
        if (_isResizingSelection)
        {
            return CursorForHandle(_selectionHandle);
        }
        if (_isRotatingObject || _isRotatingSelection)
        {
            return InputSystemCursorShape.Hand;
        }

        if (_tool == PaintTool.Select && point is { } imagePoint)
        {
            if (GetActiveEditableObject() is { } editableObject)
            {
                var objectHandle = HitTestObjectHandle(imagePoint, editableObject);
                if (objectHandle != SelectionHandle.None)
                {
                    return CursorForHandle(objectHandle);
                }
            }

            if (FindEditableObject(imagePoint) is not null)
            {
                return InputSystemCursorShape.SizeAll;
            }

            if (_selection is not null)
            {
                var selectionHandle = HitTestSelectionHandle(imagePoint, _selection);
                if (selectionHandle != SelectionHandle.None)
                {
                    return CursorForHandle(selectionHandle);
                }
                if (_selection.ContainsPoint(imagePoint))
                {
                    return InputSystemCursorShape.SizeAll;
                }
            }
        }

        return _tool switch
        {
            PaintTool.Fill => InputSystemCursorShape.Hand,
            PaintTool.Text => InputSystemCursorShape.IBeam,
            PaintTool.Select => InputSystemCursorShape.Arrow,
            PaintTool.Pencil or PaintTool.Brush or PaintTool.Eraser or PaintTool.Picker or PaintTool.Shape or PaintTool.Zoom
                => InputSystemCursorShape.Cross,
            _ => InputSystemCursorShape.Arrow
        };
    }

    private static InputSystemCursorShape CursorForHandle(SelectionHandle handle) => handle switch
    {
        SelectionHandle.TopLeft or SelectionHandle.BottomRight => InputSystemCursorShape.SizeNorthwestSoutheast,
        SelectionHandle.TopRight or SelectionHandle.BottomLeft => InputSystemCursorShape.SizeNortheastSouthwest,
        SelectionHandle.Top or SelectionHandle.Bottom => InputSystemCursorShape.SizeNorthSouth,
        SelectionHandle.Left or SelectionHandle.Right => InputSystemCursorShape.SizeWestEast,
        SelectionHandle.Rotation => InputSystemCursorShape.Hand,
        _ => InputSystemCursorShape.SizeAll
    };
}
