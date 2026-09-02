using System.Numerics;
using ChunPaint.Core;
using Microsoft.UI.Input;

namespace ChunPaint;

public sealed partial class MainWindow
{
    private void UpdateCanvasCursor(Vector2? point = null)
    {
        var cursor = ResolveCanvasCursor(point);
        if (cursor.ResourceName is { } resourceName)
        {
            try
            {
                PaintCanvasCursorHost.SetCursorResource(resourceName);
            }
            catch (Exception)
            {
                // 配布物や開発中の出力にカーソルDLLやリソースがない場合でも起動を継続する。
                PaintCanvasCursorHost.SetCursorShape(InputSystemCursorShape.Arrow);
            }
        }
        else
        {
            PaintCanvasCursorHost.SetCursorShape(cursor.SystemShape ?? InputSystemCursorShape.Arrow);
        }
    }

    private CanvasCursorDefinition ResolveCanvasCursor(Vector2? point)
    {
        if (_isMovingObject || _isMovingSelection)
        {
            return CanvasCursorDefinition.System(InputSystemCursorShape.SizeAll);
        }
        if (_isResizingObject)
        {
            return CanvasCursorDefinition.System(CursorForHandle(_objectHandle));
        }
        if (_isResizingSelection)
        {
            return CanvasCursorDefinition.System(CursorForHandle(_selectionHandle));
        }
        if (_isRotatingObject || _isRotatingSelection)
        {
            return CanvasCursorDefinition.System(InputSystemCursorShape.Hand);
        }

        if (_tool == PaintTool.Select && point is { } imagePoint)
        {
            if (GetActiveEditableObject() is { } editableObject)
            {
                var objectHandle = HitTestObjectHandle(imagePoint, editableObject);
                if (objectHandle != SelectionHandle.None)
                {
                    return CanvasCursorDefinition.System(CursorForHandle(objectHandle));
                }
            }

            if (FindEditableObject(imagePoint) is not null)
            {
                return CanvasCursorDefinition.System(InputSystemCursorShape.SizeAll);
            }

            if (_selection is not null)
            {
                var selectionHandle = HitTestSelectionHandle(imagePoint, _selection);
                if (selectionHandle != SelectionHandle.None)
                {
                    return CanvasCursorDefinition.System(CursorForHandle(selectionHandle));
                }
                if (_selection.ContainsPoint(imagePoint))
                {
                    return CanvasCursorDefinition.System(InputSystemCursorShape.SizeAll);
                }
            }
        }

        return _tool switch
        {
            PaintTool.Fill => CanvasCursorDefinition.Resource("ChunPaintCursorFill"),
            PaintTool.Pencil => CanvasCursorDefinition.Resource("ChunPaintCursorPencil"),
            PaintTool.Brush => CanvasCursorDefinition.Resource(BrushCursorResource()),
            PaintTool.Eraser => CanvasCursorDefinition.Resource("ChunPaintCursorEraser"),
            PaintTool.Picker => CanvasCursorDefinition.Resource("ChunPaintCursorPicker"),
            PaintTool.Shape => CanvasCursorDefinition.Resource("ChunPaintCursorShape"),
            PaintTool.Zoom => CanvasCursorDefinition.Resource("ChunPaintCursorZoom"),
            PaintTool.Text => CanvasCursorDefinition.System(InputSystemCursorShape.IBeam),
            PaintTool.Select => CanvasCursorDefinition.System(InputSystemCursorShape.Arrow),
            _ => CanvasCursorDefinition.System(InputSystemCursorShape.Arrow)
        };
    }

    private string BrushCursorResource() => _brushKind switch
    {
        BrushKind.Watercolor => "ChunPaintCursorWatercolor",
        BrushKind.Marker => "ChunPaintCursorMarker",
        BrushKind.NaturalPencil => "ChunPaintCursorNaturalPencil",
        BrushKind.Airbrush => "ChunPaintCursorAirbrush",
        BrushKind.Calligraphy => "ChunPaintCursorCalligraphy",
        BrushKind.Crayon => "ChunPaintCursorNaturalPencil",
        BrushKind.Flat => "ChunPaintCursorCalligraphy",
        BrushKind.Pixel => "ChunPaintCursorPencil",
        _ => "ChunPaintCursorOil"
    };

    private static InputSystemCursorShape CursorForHandle(SelectionHandle handle) => handle switch
    {
        SelectionHandle.TopLeft or SelectionHandle.BottomRight => InputSystemCursorShape.SizeNorthwestSoutheast,
        SelectionHandle.TopRight or SelectionHandle.BottomLeft => InputSystemCursorShape.SizeNortheastSouthwest,
        SelectionHandle.Top or SelectionHandle.Bottom => InputSystemCursorShape.SizeNorthSouth,
        SelectionHandle.Left or SelectionHandle.Right => InputSystemCursorShape.SizeWestEast,
        SelectionHandle.Rotation => InputSystemCursorShape.Hand,
        _ => InputSystemCursorShape.SizeAll
    };

    private readonly record struct CanvasCursorDefinition(
        InputSystemCursorShape? SystemShape,
        string? ResourceName)
    {
        public static CanvasCursorDefinition System(InputSystemCursorShape shape) => new(shape, null);

        public static CanvasCursorDefinition Resource(string resourceName) => new(null, resourceName);
    }
}
