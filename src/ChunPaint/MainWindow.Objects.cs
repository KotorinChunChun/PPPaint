using System.Numerics;
using ChunPaint.Core;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.UI;

namespace ChunPaint;

public sealed partial class MainWindow
{
    private readonly List<EditableObject> _objects = [];
    private string? _activeObjectId;
    private EditableObject? _activeObjectOriginal;
    private bool _activeObjectIsNew;
    private bool _isMovingObject;
    private bool _isResizingObject;
    private bool _isRotatingObject;
    private bool _objectInteractionChanged;
    private Vector2 _objectInteractionStart;
    private float _objectInteractionStartAngle;
    private SelectionHandle _objectHandle;
    private EditableObject? _objectInteractionOriginal;
    private int _objectHandleIndex = -1;

    private ObjectBounds CanvasObjectBounds => new(0, 0, _imageWidth, _imageHeight);

    private void DrawEditableObjects(CanvasDrawingSession session)
    {
        foreach (var editableObject in _objects)
        {
            DrawEditableObject(session, editableObject, (float)_zoom);
        }
    }

    private void DrawEditableObject(CanvasDrawingSession session, EditableObject editableObject, float scale)
    {
        if (editableObject.Kind == EditableObjectKind.Text)
        {
            DrawEditableText(session, editableObject, scale);
        }
        else
        {
            DrawEditableShape(session, editableObject, scale);
        }
    }

    private void DrawEditableText(CanvasDrawingSession session, EditableObject editableObject, float scale)
    {
        if (editableObject.Text is null || editableObject.TextStyle is not { } style)
        {
            return;
        }

        var bounds = new Rect(
            editableObject.Bounds.X * scale,
            editableObject.Bounds.Y * scale,
            editableObject.Bounds.Width * scale,
            editableObject.Bounds.Height * scale);
        var center = new Vector2((float)(bounds.X + bounds.Width / 2), (float)(bounds.Y + bounds.Height / 2));
        var previousTransform = session.Transform;
        session.Transform = Matrix3x2.CreateRotation(editableObject.RotationDegrees * (MathF.PI / 180f), center) * previousTransform;
        var background = ToWindowsColor(style.Background);
        if (style.HasBackground)
        {
            session.FillRectangle(bounds, background);
        }

        using var format = new CanvasTextFormat
        {
            FontFamily = style.FontFamily,
            FontSize = style.FontSize * scale,
            FontWeight = style.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = style.Italic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal,
            HorizontalAlignment = style.Alignment switch
            {
                EditableTextAlignment.Center => CanvasHorizontalAlignment.Center,
                EditableTextAlignment.Right => CanvasHorizontalAlignment.Right,
                _ => CanvasHorizontalAlignment.Left
            },
            WordWrapping = CanvasWordWrapping.Wrap
        };
        using var layout = new CanvasTextLayout(
            session,
            editableObject.Text,
            format,
            Math.Max(1f, (float)bounds.Width),
            Math.Max(1f, (float)bounds.Height));
        if (style.Underline)
        {
            layout.SetUnderline(0, editableObject.Text.Length, true);
        }
        if (style.Strikethrough)
        {
            layout.SetStrikethrough(0, editableObject.Text.Length, true);
        }
        session.DrawTextLayout(layout, new Vector2((float)bounds.X, (float)bounds.Y), ToWindowsColor(style.Foreground));
        session.Transform = previousTransform;
    }

    private void DrawEditableObjectOverlay(CanvasDrawingSession session)
    {
        var editableObject = GetActiveEditableObject();
        if (editableObject is null)
        {
            return;
        }

        using var style = new CanvasStrokeStyle { DashStyle = CanvasDashStyle.Dash };
        var corners = new[]
        {
            GetObjectHandlePosition(editableObject, SelectionHandle.TopLeft),
            GetObjectHandlePosition(editableObject, SelectionHandle.TopRight),
            GetObjectHandlePosition(editableObject, SelectionHandle.BottomRight),
            GetObjectHandlePosition(editableObject, SelectionHandle.BottomLeft)
        };
        for (var index = 0; index < corners.Length; index++)
        {
            var first = corners[index] * (float)_zoom;
            var second = corners[(index + 1) % corners.Length] * (float)_zoom;
            session.DrawLine(first, second, Colors.Black, 1, style);
            session.DrawLine(first + Vector2.One, second + Vector2.One, Colors.White, 1, style);
        }

        foreach (var handle in ObjectHandles)
        {
            var position = GetObjectHandlePosition(editableObject, handle) * (float)_zoom;
            var size = Math.Max(6, 8 * _zoom);
            var rect = new Rect(position.X - (size / 2), position.Y - (size / 2), size, size);
            session.FillRectangle(rect, handle == _objectHandle ? Color.FromArgb(255, 116, 107, 225) : Colors.White);
            session.DrawRectangle(rect, Colors.Black, 1);
        }

        var rotation = GetObjectRotationHandlePosition(editableObject) * (float)_zoom;
        var top = GetObjectHandlePosition(editableObject, SelectionHandle.Top) * (float)_zoom;
        session.DrawLine(top, rotation, Colors.Black, 1);
        var radius = (float)Math.Max(4, 5 * _zoom);
        session.FillEllipse(rotation, radius, radius, _objectHandle == SelectionHandle.Rotation ? Color.FromArgb(255, 116, 107, 225) : Colors.White);
        session.DrawEllipse(rotation, radius, radius, Colors.Black, 1);
    }

    private static readonly SelectionHandle[] ObjectHandles =
    [
        SelectionHandle.TopLeft,
        SelectionHandle.Top,
        SelectionHandle.TopRight,
        SelectionHandle.Right,
        SelectionHandle.BottomRight,
        SelectionHandle.Bottom,
        SelectionHandle.BottomLeft,
        SelectionHandle.Left,
        SelectionHandle.Rotation
    ];

    private bool HandleEditableObjectPointerPressed(Vector2 point, PointerRoutedEventArgs e)
    {
        if (GetActiveEditableObject() is { } active)
        {
            var activeHandle = HitTestObjectHandle(point, active);
            if (activeHandle != SelectionHandle.None)
            {
                BeginEditableObjectInteraction(point, activeHandle, e);
                return true;
            }
        }

        var hit = FindEditableObject(point);
        if (hit is null)
        {
            _activeObjectId = null;
            _activeObjectOriginal = null;
            return false;
        }

        CommitFloatingSelection();
        ActivateEditableObject(hit.Id, isNew: false);
        var handle = HitTestObjectHandle(point, hit);
        BeginEditableObjectInteraction(point, handle, e);
        return true;
    }

    private bool HandleEditableObjectPointerMoved(Vector2 point)
    {
        if ((!_isMovingObject && !_isResizingObject && !_isRotatingObject) || _objectInteractionOriginal is null)
        {
            return false;
        }

        var delta = point - _objectInteractionStart;
        if (!_objectInteractionChanged && delta.LengthSquared() < 0.25f)
        {
            return true;
        }

        if (!_objectInteractionChanged)
        {
            CaptureUndo();
            _objectInteractionChanged = true;
        }

        var updated = _objectInteractionOriginal;
        if (_isMovingObject)
        {
            updated = updated.MoveBy(delta, CanvasObjectBounds);
        }
        else if (_isResizingObject)
        {
            updated = updated.Resize(_objectHandle, delta, CanvasObjectBounds);
        }
        else if (_isRotatingObject)
        {
            var center = _objectInteractionOriginal.Bounds.Center;
            var angle = MathF.Atan2(point.Y - center.Y, point.X - center.X);
            var degrees = (angle - _objectInteractionStartAngle) * (180f / MathF.PI);
            updated = updated.RotateBy(degrees);
        }

        ReplaceEditableObject(updated);
        PointerStatusText.Text = $"{Math.Round(updated.Bounds.Width)} × {Math.Round(updated.Bounds.Height)} px";
        PaintCanvas.Invalidate();
        return true;
    }

    private bool HandleEditableObjectPointerReleased(PointerRoutedEventArgs e)
    {
        if (!_isMovingObject && !_isResizingObject && !_isRotatingObject)
        {
            return false;
        }

        PaintCanvas.ReleasePointerCapture(e.Pointer);
        _isMovingObject = false;
        _isResizingObject = false;
        _isRotatingObject = false;
        _objectInteractionOriginal = null;
        _objectHandle = SelectionHandle.None;
        if (_objectInteractionChanged)
        {
            FinishEdit();
        }
        _objectInteractionChanged = false;
        UpdateSelectionCommandState();
        PaintCanvas.Invalidate();
        return true;
    }

    private void BeginEditableObjectInteraction(Vector2 point, SelectionHandle handle, PointerRoutedEventArgs e)
    {
        var editableObject = GetActiveEditableObject();
        if (editableObject is null)
        {
            return;
        }

        _objectInteractionStart = point;
        _objectInteractionOriginal = editableObject;
        _objectInteractionChanged = false;
        _objectHandle = handle;
        _isRotatingObject = handle == SelectionHandle.Rotation;
        _isResizingObject = handle is not SelectionHandle.None and not SelectionHandle.Rotation;
        _isMovingObject = handle == SelectionHandle.None;
        _objectHandleIndex = Array.IndexOf(ObjectHandles, handle);
        if (_isRotatingObject)
        {
            var center = editableObject.Bounds.Center;
            _objectInteractionStartAngle = MathF.Atan2(point.Y - center.Y, point.X - center.X);
        }
        PaintCanvas.CapturePointer(e.Pointer);
    }

    private EditableObject? FindEditableObject(Vector2 point)
    {
        for (var index = _objects.Count - 1; index >= 0; index--)
        {
            if (ContainsObjectPoint(_objects[index], point))
            {
                return _objects[index];
            }
        }
        return null;
    }

    private static bool ContainsObjectPoint(EditableObject editableObject, Vector2 point)
    {
        var local = RotatePoint(point, editableObject.Bounds.Center, -editableObject.RotationDegrees);
        return editableObject.Bounds.Contains(local);
    }

    private SelectionHandle HitTestObjectHandle(Vector2 point, EditableObject editableObject)
    {
        var tolerance = Math.Max(6, 10 / Math.Max(_zoom, 0.01));
        if (Vector2.Distance(point, GetObjectRotationHandlePosition(editableObject)) <= tolerance)
        {
            return SelectionHandle.Rotation;
        }

        foreach (var handle in ObjectHandles.Where(handle => handle != SelectionHandle.Rotation))
        {
            if (Vector2.Distance(point, GetObjectHandlePosition(editableObject, handle)) <= tolerance)
            {
                return handle;
            }
        }
        return SelectionHandle.None;
    }

    private static Vector2 GetObjectHandlePosition(EditableObject editableObject, SelectionHandle handle)
    {
        var bounds = editableObject.Bounds;
        var position = handle switch
        {
            SelectionHandle.TopLeft => new Vector2(bounds.X, bounds.Y),
            SelectionHandle.Top => new Vector2(bounds.X + bounds.Width / 2, bounds.Y),
            SelectionHandle.TopRight => new Vector2(bounds.Right, bounds.Y),
            SelectionHandle.Right => new Vector2(bounds.Right, bounds.Y + bounds.Height / 2),
            SelectionHandle.BottomRight => new Vector2(bounds.Right, bounds.Bottom),
            SelectionHandle.Bottom => new Vector2(bounds.X + bounds.Width / 2, bounds.Bottom),
            SelectionHandle.BottomLeft => new Vector2(bounds.X, bounds.Bottom),
            SelectionHandle.Left => new Vector2(bounds.X, bounds.Y + bounds.Height / 2),
            _ => new Vector2(bounds.X + bounds.Width / 2, bounds.Y)
        };
        return RotatePoint(position, bounds.Center, editableObject.RotationDegrees);
    }

    private static Vector2 GetObjectRotationHandlePosition(EditableObject editableObject)
    {
        var bounds = editableObject.Bounds;
        var position = new Vector2(bounds.X + bounds.Width / 2, bounds.Y - 24);
        return RotatePoint(position, bounds.Center, editableObject.RotationDegrees);
    }

    private static Vector2 RotatePoint(Vector2 point, Vector2 center, float degrees)
    {
        var radians = degrees * (MathF.PI / 180f);
        var relative = point - center;
        return center + new Vector2(
            (relative.X * MathF.Cos(radians)) - (relative.Y * MathF.Sin(radians)),
            (relative.X * MathF.Sin(radians)) + (relative.Y * MathF.Cos(radians)));
    }

    private EditableObject? GetActiveEditableObject() =>
        _activeObjectId is null ? null : _objects.FirstOrDefault(item => item.Id == _activeObjectId);

    private void ActivateEditableObject(string id, bool isNew)
    {
        var editableObject = _objects.FirstOrDefault(item => item.Id == id);
        if (editableObject is null)
        {
            return;
        }

        _activeObjectId = id;
        _activeObjectOriginal = editableObject;
        _activeObjectIsNew = isNew;
        _selection = null;
        UpdateSelectionCommandState();
        PaintCanvas.Invalidate();
    }

    private void ReplaceEditableObject(EditableObject editableObject)
    {
        var index = _objects.FindIndex(item => item.Id == editableObject.Id);
        if (index >= 0)
        {
            _objects[index] = editableObject;
        }
    }

    private void MoveActiveObjectByKeyboard(int horizontal, int vertical)
    {
        var editableObject = GetActiveEditableObject();
        if (editableObject is null)
        {
            return;
        }

        CaptureUndo();
        ReplaceEditableObject(editableObject.MoveBy(new Vector2(horizontal, vertical), CanvasObjectBounds));
        FinishEdit();
    }

    private void CycleEditableObjectHandle(int direction)
    {
        if (GetActiveEditableObject() is null)
        {
            return;
        }
        _objectHandleIndex = (_objectHandleIndex + direction + ObjectHandles.Length) % ObjectHandles.Length;
        _objectHandle = ObjectHandles[_objectHandleIndex];
        PointerStatusText.Text = _objectHandle == SelectionHandle.Rotation ? "回転ハンドル" : $"{_objectHandle} ハンドル";
        PaintCanvas.Invalidate();
    }

    private bool ApplyActiveObject()
    {
        var editableObject = GetActiveEditableObject();
        if (editableObject is null || _surface is null)
        {
            return false;
        }

        CaptureUndo();
        using (var session = _surface.CreateDrawingSession())
        {
            DrawEditableObject(session, editableObject, 1f);
        }
        _objects.RemoveAll(item => item.Id == editableObject.Id);
        _activeObjectId = null;
        _activeObjectOriginal = null;
        FinishEdit();
        return true;
    }

    private bool CancelActiveObject()
    {
        var editableObject = GetActiveEditableObject();
        if (editableObject is null)
        {
            return false;
        }

        if (!_activeObjectIsNew && _activeObjectOriginal is not null && editableObject != _activeObjectOriginal)
        {
            CaptureUndo();
            ReplaceEditableObject(_activeObjectOriginal);
            FinishEdit();
        }
        else if (_activeObjectIsNew)
        {
            CaptureUndo();
            _objects.RemoveAll(item => item.Id == editableObject.Id);
            FinishEdit();
        }

        _activeObjectId = null;
        _activeObjectOriginal = null;
        _objectHandle = SelectionHandle.None;
        PaintCanvas.Invalidate();
        UpdateSelectionCommandState();
        return true;
    }

    private bool HasActiveEditableObject => GetActiveEditableObject() is not null;

    private void CommitEditableObjectForDocumentSwitch() => ApplyAllEditableObjects();

    private bool ApplyAllEditableObjects()
    {
        if (_objects.Count == 0 || _surface is null)
        {
            return false;
        }

        CaptureUndo();
        using (var session = _surface.CreateDrawingSession())
        {
            foreach (var editableObject in _objects)
            {
                DrawEditableObject(session, editableObject, 1f);
            }
        }
        _objects.Clear();
        _activeObjectId = null;
        _activeObjectOriginal = null;
        FinishEdit();
        return true;
    }

    private CanvasRenderTarget CreateCompositeWithObjects()
    {
        return CreateComposite();
    }

    private static Color ToWindowsColor(Rgba32 color) => Color.FromArgb(color.A, color.R, color.G, color.B);

    private async void PaintCanvas_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var point = ToImagePoint(e.GetPosition(PaintCanvas));
        var editableObject = FindEditableObject(point);
        if (editableObject?.Kind == EditableObjectKind.Text)
        {
            ActivateEditableObject(editableObject.Id, isNew: false);
            await EditTextObjectAsync(editableObject);
        }
    }

    private async Task EditTextObjectAsync(EditableObject editableObject)
    {
        await ShowTextEditorAsync(editableObject, editableObject.Bounds.Center);
    }

    private void ApplyObject_Click(object sender, RoutedEventArgs e) => ApplyActiveObject();

    private void CancelObject_Click(object sender, RoutedEventArgs e) => CancelActiveObject();
}
