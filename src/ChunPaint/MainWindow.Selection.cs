using System.Numerics;
using ChunPaint.Core;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace ChunPaint;

public sealed partial class MainWindow
{
    private enum SelectionMode
    {
        Rectangle,
        Freeform
    }

    private static readonly SelectionHandle[] SelectionHandles =
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

    private readonly List<Vector2> _freeSelectionPoints = [];
    private SelectionMode _selectionMode = SelectionMode.Rectangle;
    private bool _isMovingSelection;
    private bool _isResizingSelection;
    private bool _isRotatingSelection;
    private bool _selectionInteractionChanged;
    private Vector2 _selectionInteractionStart;
    private float _selectionInteractionStartAngle;
    private SelectionHandle _selectionHandle;
    private SelectionRegion? _selectionInteractionRegion;
    private PixelSurface? _selectionInteractionPixels;
    private PixelSurface? _floatingSelectionPixels;
    private PixelSurface? _floatingOriginalPixels;
    private SelectionRegion? _floatingOriginalRegion;
    private CanvasRenderTarget? _floatingSelectionRenderTarget;
    private bool _floatingUndoCaptured;
    private bool _transparentSelection;
    private int _selectionHandleIndex = -1;

    private bool IsSelectionInteractionActive =>
        _isMovingSelection || _isResizingSelection || _isRotatingSelection;

    private PixelRect CanvasPixelBounds => new(0, 0, _imageWidth, _imageHeight);

    private void HandleSelectionPointerPressed(Vector2 point, PointerRoutedEventArgs e)
    {
        if (_isSelecting || IsSelectionInteractionActive)
        {
            return;
        }

        if (_selection is not null)
        {
            var hit = HitTestSelectionHandle(point, _selection);
            if (hit != SelectionHandle.None)
            {
                BeginSelectionInteraction(point, hit, e);
                return;
            }

            if (_selection.Kind != SelectionRegionKind.Mask && _selection.ContainsPoint(point))
            {
                BeginSelectionInteraction(point, SelectionHandle.None, e);
                return;
            }

            CommitFloatingSelection();
            _selection = null;
        }

        if (_floatingSelectionPixels is not null)
        {
            CommitFloatingSelection();
        }

        _freeSelectionPoints.Clear();
        _freeSelectionPoints.Add(point);
        _isSelecting = true;
        PaintCanvas.CapturePointer(e.Pointer);
        PaintCanvas.Invalidate();
    }

    private bool HandleSelectionPointerMoved(Vector2 point)
    {
        if (_isSelecting)
        {
            var clamped = new Vector2(
                Math.Clamp(point.X, 0, _imageWidth),
                Math.Clamp(point.Y, 0, _imageHeight));
            if (_selectionMode == SelectionMode.Rectangle)
            {
                var bounds = CreateSelectionBounds(_freeSelectionPoints[0], clamped);
                _selection = bounds is { Width: > 0, Height: > 0 }
                    ? SelectionRegion.CreateRectangle(bounds.Value)
                    : null;
            }
            else
            {
                if (_freeSelectionPoints.Count == 0 || Vector2.DistanceSquared(_freeSelectionPoints[^1], clamped) >= 1)
                {
                    _freeSelectionPoints.Add(clamped);
                }
                if (_freeSelectionPoints.Count >= 3)
                {
                    _selection = SelectionRegion.CreatePolygon(_freeSelectionPoints);
                }
            }
            PaintCanvas.Invalidate();
            return true;
        }

        if (!IsSelectionInteractionActive || _selectionInteractionRegion is null)
        {
            return false;
        }

        var delta = point - _selectionInteractionStart;
        if (!_selectionInteractionChanged && delta.LengthSquared() < 0.25f)
        {
            return true;
        }

        if (!EnsureFloatingSelection())
        {
            return true;
        }

        _selectionInteractionPixels ??= ClonePixelSurface(_floatingSelectionPixels!);

        _selectionInteractionChanged = true;
        if (_isMovingSelection)
        {
            _selection = SelectionTransform.Move(_selectionInteractionRegion, delta, CanvasPixelBounds);
            SetFloatingSelectionPixels(_selectionInteractionPixels!);
        }
        else if (_isResizingSelection)
        {
            _selection = SelectionTransform.Resize(_selectionInteractionRegion, _selectionHandle, delta, CanvasPixelBounds);
            SetFloatingSelectionPixels(ImageTransforms.ResizeNearestNeighbor(
                _selectionInteractionPixels!,
                _selection.Bounds.Width,
                _selection.Bounds.Height));
        }
        else if (_isRotatingSelection)
        {
            var center = GetSelectionCenter(_selectionInteractionRegion);
            var angle = MathF.Atan2(point.Y - center.Y, point.X - center.X);
            var degrees = (angle - _selectionInteractionStartAngle) * (180f / MathF.PI);
            _selection = SelectionTransform.Rotate(_selectionInteractionRegion, degrees, CanvasPixelBounds);
            var rotatedPixels = ImageTransforms.RotateNearestNeighbor(_selectionInteractionPixels!, degrees);
            if (rotatedPixels.Width != _selection.Bounds.Width || rotatedPixels.Height != _selection.Bounds.Height)
            {
                rotatedPixels = ImageTransforms.ResizeNearestNeighbor(rotatedPixels, _selection.Bounds.Width, _selection.Bounds.Height);
            }
            SetFloatingSelectionPixels(rotatedPixels);
        }

        PaintCanvas.Invalidate();
        UpdateSelectionStatus();
        return true;
    }

    private bool HandleSelectionPointerReleased(PointerRoutedEventArgs e)
    {
        if (_isSelecting)
        {
            _isSelecting = false;
            PaintCanvas.ReleasePointerCapture(e.Pointer);
            if (_selectionMode == SelectionMode.Freeform && _freeSelectionPoints.Count < 3)
            {
                _selection = null;
            }
            if (_selection is null)
            {
                _freeSelectionPoints.Clear();
            }
            UpdateSelectionStatus();
            UpdateSelectionCommandState();
            PaintCanvas.Invalidate();
            return true;
        }

        if (IsSelectionInteractionActive)
        {
            PaintCanvas.ReleasePointerCapture(e.Pointer);
            _isMovingSelection = false;
            _isResizingSelection = false;
            _isRotatingSelection = false;
            _selectionInteractionRegion = null;
            _selectionInteractionPixels = null;
            _selectionHandle = SelectionHandle.None;
            return true;
        }

        return false;
    }

    private void BeginSelectionInteraction(Vector2 point, SelectionHandle handle, PointerRoutedEventArgs e)
    {
        if (_selection is null)
        {
            return;
        }

        _selectionInteractionStart = point;
        _selectionInteractionRegion = _selection;
        _selectionInteractionPixels = _floatingSelectionPixels is null
            ? null
            : ClonePixelSurface(_floatingSelectionPixels);
        _selectionInteractionChanged = false;
        _selectionHandle = handle;
        _isRotatingSelection = handle == SelectionHandle.Rotation;
        _isResizingSelection = handle is not SelectionHandle.None and not SelectionHandle.Rotation;
        _isMovingSelection = handle == SelectionHandle.None;
        if (_isRotatingSelection)
        {
            var center = GetSelectionCenter(_selection);
            _selectionInteractionStartAngle = MathF.Atan2(point.Y - center.Y, point.X - center.X);
        }
        _selectionHandleIndex = Array.IndexOf(SelectionHandles, handle);
        PaintCanvas.CapturePointer(e.Pointer);
    }

    private bool EnsureFloatingSelection(bool captureUndo = true)
    {
        if (_floatingSelectionPixels is not null)
        {
            return true;
        }
        if (_selection is null || _surface is null)
        {
            return false;
        }

        var source = ToPixelSurface(_surface);
        _floatingOriginalRegion = _selection;
        _floatingOriginalPixels = SelectionOperations.Extract(source, _selection);
        _floatingSelectionPixels = SelectionOperations.Extract(
            source,
            _selection,
            _transparentSelection,
            ToCoreColor(_secondaryColor));
        if (captureUndo)
        {
            CaptureUndo();
            _floatingUndoCaptured = true;
        }
        var clearColor = _activeLayerIndex == 0 ? ToCoreColor(_secondaryColor) : Rgba32.Transparent;
        SelectionOperations.Clear(source, _selection, clearColor);
        _surface.SetPixelBytes(source.Snapshot());
        UpdateFloatingSelectionRenderTarget();
        return true;
    }

    private void SetFloatingSelectionPixels(PixelSurface pixels)
    {
        _floatingSelectionPixels = pixels;
        UpdateFloatingSelectionRenderTarget();
    }

    private void UpdateFloatingSelectionRenderTarget()
    {
        _floatingSelectionRenderTarget?.Dispose();
        _floatingSelectionRenderTarget = _floatingSelectionPixels is null
            ? null
            : ToRenderTarget(_floatingSelectionPixels);
    }

    private void DrawFloatingSelection(CanvasDrawingSession session)
    {
        if (_floatingSelectionRenderTarget is null || _selection is null)
        {
            return;
        }

        var bounds = _selection.Bounds;
        session.DrawImage(
            _floatingSelectionRenderTarget,
            new Rect(bounds.X * _zoom, bounds.Y * _zoom, bounds.Width * _zoom, bounds.Height * _zoom),
            new Rect(0, 0, _floatingSelectionRenderTarget.SizeInPixels.Width, _floatingSelectionRenderTarget.SizeInPixels.Height));
    }

    private void DrawSelectionOverlay(CanvasDrawingSession session)
    {
        if (_selection is null)
        {
            return;
        }

        using var style = new CanvasStrokeStyle { DashStyle = CanvasDashStyle.Dash };
        if (_selection.Kind is SelectionRegionKind.Rectangle or SelectionRegionKind.Mask)
        {
            var bounds = _selection.Bounds;
            var scaled = new Rect(bounds.X * _zoom, bounds.Y * _zoom, bounds.Width * _zoom, bounds.Height * _zoom);
            session.DrawRectangle(scaled, Colors.Black, 1, style);
            session.DrawRectangle(new Rect(scaled.X + 1, scaled.Y + 1, Math.Max(0, scaled.Width - 2), Math.Max(0, scaled.Height - 2)), Colors.White, 1, style);
        }
        else
        {
            for (var index = 0; index < _selection.Points.Count; index++)
            {
                var first = _selection.Points[index] * (float)_zoom;
                var second = _selection.Points[(index + 1) % _selection.Points.Count] * (float)_zoom;
                session.DrawLine(first, second, Colors.Black, 1, style);
                session.DrawLine(first + Vector2.One, second + Vector2.One, Colors.White, 1, style);
            }
        }

        if (_selection.Kind == SelectionRegionKind.Mask)
        {
            return;
        }

        foreach (var handle in SelectionHandles.Where(handle => handle != SelectionHandle.Rotation))
        {
            var position = _selection.GetHandlePosition(handle) * (float)_zoom;
            var size = Math.Max(6, 8 * _zoom);
            var rect = new Rect(position.X - (size / 2), position.Y - (size / 2), size, size);
            session.FillRectangle(rect, handle == _selectionHandle ? Color.FromArgb(255, 116, 107, 225) : Colors.White);
            session.DrawRectangle(rect, Colors.Black, 1);
        }

        var rotation = _selection.GetRotationHandlePosition() * (float)_zoom;
        var top = _selection.GetHandlePosition(SelectionHandle.Top) * (float)_zoom;
        session.DrawLine(top, rotation, Colors.Black, 1);
        var rotationRadius = (float)Math.Max(4, 5 * _zoom);
        session.FillEllipse(rotation, rotationRadius, rotationRadius, _selectionHandle == SelectionHandle.Rotation ? Color.FromArgb(255, 116, 107, 225) : Colors.White);
        session.DrawEllipse(rotation, rotationRadius, rotationRadius, Colors.Black, 1);
    }

    private SelectionHandle HitTestSelectionHandle(Vector2 point, SelectionRegion region)
    {
        if (region.Kind == SelectionRegionKind.Mask)
        {
            return SelectionHandle.None;
        }
        var tolerance = Math.Max(6, 10 / Math.Max(_zoom, 0.01));
        var rotation = region.GetRotationHandlePosition();
        if (Vector2.Distance(point, rotation) <= tolerance)
        {
            return SelectionHandle.Rotation;
        }

        foreach (var handle in SelectionHandles.Where(handle => handle != SelectionHandle.Rotation))
        {
            if (Vector2.Distance(point, region.GetHandlePosition(handle)) <= tolerance)
            {
                return handle;
            }
        }
        return SelectionHandle.None;
    }

    private void UpdateSelectionStatus()
    {
        if (_selection is { } selection)
        {
            PointerStatusText.Text = $"{selection.Bounds.Width} × {selection.Bounds.Height} px";
        }
    }

    private void MoveSelectionByKeyboard(int horizontal, int vertical)
    {
        if (_selection is null || _selection.Kind == SelectionRegionKind.Mask)
        {
            return;
        }
        if (!EnsureFloatingSelection())
        {
            return;
        }

        _selection = SelectionTransform.Move(_selection, new Vector2(horizontal, vertical), CanvasPixelBounds);
        PaintCanvas.Invalidate();
        UpdateSelectionStatus();
    }

    private void CycleSelectionHandle(int direction)
    {
        if (_selection is null)
        {
            return;
        }
        _selectionHandleIndex = (_selectionHandleIndex + direction + SelectionHandles.Length) % SelectionHandles.Length;
        _selectionHandle = SelectionHandles[_selectionHandleIndex];
        PointerStatusText.Text = _selectionHandle == SelectionHandle.Rotation
            ? "回転ハンドル"
            : $"{_selectionHandle} ハンドル";
        PaintCanvas.Invalidate();
    }

    private void ApplySelection()
    {
        if (_floatingSelectionPixels is not null)
        {
            CommitFloatingSelection();
            return;
        }
        ApplyActiveObject();
    }

    private bool CancelSelection()
    {
        if (_floatingSelectionPixels is not null)
        {
            var cancelled = CancelFloatingSelection();
            UpdateSelectionCommandState();
            return cancelled;
        }
        if (_selection is null)
        {
            return false;
        }
        _selection = null;
        _freeSelectionPoints.Clear();
        UpdateSelectionCommandState();
        PaintCanvas.Invalidate();
        return true;
    }

    private void ActivateSelectionMode(SelectionMode mode)
    {
        CommitFloatingSelection();
        _selectionMode = mode;
        _tool = PaintTool.Select;
        ActivateSelectionTool();
        UpdateSelectionModeButtonState();
        PaintCanvas.Invalidate();
    }

    private void ActivateSelectionTool()
    {
        _tool = PaintTool.Select;
        foreach (var button in _toolButtons)
        {
            button.IsChecked = false;
        }
        ClearShapeSelectionVisual();
        UpdateSelectionModeButtonState();
        UpdateSelectionCommandState();
        UpdateCanvasCursor();
    }

    private void SelectAll()
    {
        EnsureSurface(PaintCanvas);
        CommitFloatingSelection();
        _tool = PaintTool.Select;
        _selectionMode = SelectionMode.Rectangle;
        foreach (var button in _toolButtons)
        {
            button.IsChecked = false;
        }
        ClearShapeSelectionVisual();
        UpdateSelectionModeButtonState();
        UpdateCanvasCursor();
        _selection = SelectionRegion.CreateRectangle(CanvasPixelBounds);
        UpdateSelectionStatus();
        UpdateSelectionCommandState();
        PaintCanvas.Invalidate();
    }

    private void InvertSelectionRegion()
    {
        if (_selection is null)
        {
            return;
        }
        CommitFloatingSelection();
        var mask = _selection.CreateMask(_imageWidth, _imageHeight);
        for (var index = 0; index < mask.Length; index++)
        {
            mask[index] = !mask[index];
        }
        _selection = SelectionRegion.CreateMask(_imageWidth, _imageHeight, mask);
        UpdateSelectionStatus();
        UpdateSelectionCommandState();
        PaintCanvas.Invalidate();
    }

    private void ToggleTransparentSelection()
    {
        SetTransparentSelection(!_transparentSelection);
    }

    private void SetTransparentSelection(bool value)
    {
        _transparentSelection = value;
        if (TransparentSelectionMenuItem is not null)
        {
            TransparentSelectionMenuItem.IsChecked = value;
        }
        if (TransparentSelectionButton is not null)
        {
            TransparentSelectionButton.IsChecked = value;
        }
        UpdateSelectionCommandState();
    }

    private void UpdateSelectionModeButtonState()
    {
        var selectionToolActive = _tool == PaintTool.Select;
        for (var index = 0; index < _selectionModeButtons.Count; index++)
        {
            _selectionModeButtons[index].IsChecked = selectionToolActive &&
                ((index == 0 && _selectionMode == SelectionMode.Rectangle) ||
                 (index == 1 && _selectionMode == SelectionMode.Freeform));
        }
    }

    private void ClearFloatingSelectionState()
    {
        _floatingSelectionRenderTarget?.Dispose();
        _floatingSelectionRenderTarget = null;
        _floatingSelectionPixels = null;
        _floatingOriginalPixels = null;
        _floatingOriginalRegion = null;
        _floatingUndoCaptured = false;
        _isMovingSelection = false;
        _isResizingSelection = false;
        _isRotatingSelection = false;
        _selectionInteractionRegion = null;
        _selectionInteractionPixels = null;
        _selectionHandle = SelectionHandle.None;
    }

    private bool CommitFloatingSelection()
    {
        if (_floatingSelectionPixels is null || _selection is null || _surface is null)
        {
            return false;
        }

        var destination = ToPixelSurface(_surface);
        SelectionOperations.Apply(destination, _selection, _floatingSelectionPixels);
        _surface.SetPixelBytes(destination.Snapshot());
        ClearFloatingSelectionState();
        FinishEdit();
        UpdateSelectionStatus();
        return true;
    }

    private bool CancelFloatingSelection()
    {
        if (_floatingOriginalPixels is null || _floatingOriginalRegion is null || _surface is null)
        {
            return false;
        }

        var destination = ToPixelSurface(_surface);
        SelectionOperations.Apply(destination, _floatingOriginalRegion, _floatingOriginalPixels);
        _surface.SetPixelBytes(destination.Snapshot());
        var originalRegion = _floatingOriginalRegion;
        if (_floatingUndoCaptured && _undo.Count > 0)
        {
            _undo.Pop();
        }
        ClearFloatingSelectionState();
        _selection = originalRegion;
        UpdateHistoryState();
        PaintCanvas.Invalidate();
        return true;
    }

    private void DeleteFloatingSelection()
    {
        if (_floatingSelectionPixels is null)
        {
            return;
        }
        ClearFloatingSelectionState();
        _selection = null;
        FinishEdit();
    }

    private static PixelRect? CreateSelectionBounds(Vector2 first, Vector2 second)
    {
        var left = (int)MathF.Floor(MathF.Min(first.X, second.X));
        var top = (int)MathF.Floor(MathF.Min(first.Y, second.Y));
        var right = (int)MathF.Ceiling(MathF.Max(first.X, second.X));
        var bottom = (int)MathF.Ceiling(MathF.Max(first.Y, second.Y));
        return right > left && bottom > top
            ? new PixelRect(left, top, right - left, bottom - top)
            : null;
    }

    private static Vector2 GetSelectionCenter(SelectionRegion region) =>
        new((region.Bounds.X + region.Bounds.Right) / 2f, (region.Bounds.Y + region.Bounds.Bottom) / 2f);

    private static PixelSurface ClonePixelSurface(PixelSurface source)
    {
        var result = new PixelSurface(source.Width, source.Height, Rgba32.Transparent);
        result.Restore(source.Snapshot());
        return result;
    }

    private static Rgba32 ToCoreColor(Color color) => new(color.R, color.G, color.B, color.A);

    private void RectangleSelection_Click(object sender, RoutedEventArgs e) => ActivateSelectionMode(SelectionMode.Rectangle);
    private void FreeSelection_Click(object sender, RoutedEventArgs e) => ActivateSelectionMode(SelectionMode.Freeform);
    private void SelectAll_Click(object sender, RoutedEventArgs e) => SelectAll();
    private void InvertSelectionRegion_Click(object sender, RoutedEventArgs e) => InvertSelectionRegion();
    private void ToggleTransparentSelection_Click(object sender, RoutedEventArgs e) => ToggleTransparentSelection();
    private async void CopyVisibleLayers_Click(object sender, RoutedEventArgs e)
    {
        CommitFloatingSelection();
        await CopySelectionAsync(visibleLayers: true);
    }
    private void ApplySelection_Click(object sender, RoutedEventArgs e) => ApplySelection();
    private void CancelSelection_Click(object sender, RoutedEventArgs e) => CancelSelection();
}
