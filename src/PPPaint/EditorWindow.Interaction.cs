using System.Collections.Immutable;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using PPPaint.Core;
using PPPaint.Imaging;
using Windows.System;

namespace PPPaint;

public sealed partial class EditorWindow
{
    private enum Gesture { None, Draw, Move, Resize, Endpoint, Marquee, RegionCreate, RegionMove, RegionResize, Pan, CanvasCrop, CalloutTail, NumberPlace, RegionLift, PickColor }
    private uint? _pointerId;
    private Gesture _gesture;
    private Vector2 _start, _viewportStart, _lastPoint;
    private Vector2 _handleCenter;
    private ScreenshotAnnotation? _draft;
    private ScreenshotBox? _region;
    private ScreenshotBox _gestureBox;
    private ScreenshotBox? _regionBefore;
    private ImmutableArray<ScreenshotAnnotation> _gestureItems = [];
    private HashSet<string> _selectionBefore = [];
    private readonly Dictionary<string, ScreenshotAnnotation> _previews = [];
    private int _handle = -1;
    private bool _dragged, _emptySelectionPress;
    private bool _extendSelection, _duplicating;
    private string? _clickSelectionId, _primaryBefore;
    private ImmutableArray<ScreenshotAnnotation> _duplicateItems = [];
    private float? _snapX, _snapY;
    private double _panX, _panY;
    private ScreenshotNumberPlacement? _numberPlacement;
    private ScreenshotRegionLift? _lift;
    private ScreenshotState? _liftPreview;

    private Vector2 ImagePoint(PointerRoutedEventArgs e, bool clamp = false)
    {
        var p = e.GetCurrentPoint(PaintCanvas).Position; var frame = _document.State.Frame;
        var v = new Vector2((float)(p.X / _zoom), (float)(p.Y / _zoom));
        return clamp ? Vector2.Clamp(v, Vector2.Zero, new(frame.Width, frame.Height)) : v;
    }
    private void SetSelection(IEnumerable<string> ids)
    {
        _selectedIds.Clear(); foreach (var id in ids) _selectedIds.Add(id); _primaryId = _selectedIds.LastOrDefault();
        ShowSelectedOptions(); Refresh();
    }
    private int HitHandle(Vector2 point)
    {
        if (_selectedIds.Count == 0) return -1;
        var selected = SelectedObjects; var single = selected.Length == 1 ? selected[0] : null;
        if (single?.Tool == ScreenshotTool.Callout && single.VisibleAt(single.Tail) && ScreenshotHandleGeometry.Contains(single.Tail, point, (float)_zoom, true)) return 8;
        var round = HasEndpointHandles(single);
        var points = round ? single!.Points.ToArray() : ScreenshotGeometry.Handles(ObjectSelectionBounds(selected));
        return ScreenshotHandleGeometry.Hit(points, point, (float)_zoom, round);
    }
    private static bool HasEndpointHandles(ScreenshotAnnotation? item) => item is { Tool: ScreenshotTool.Arrow or ScreenshotTool.Line } && item.Points.Length == 2 && item.Points.All(item.VisibleAt);
    private int HitRegionHandle(Vector2 point)
    {
        if (_region is not { } region) return -1;
        var handles = ScreenshotGeometry.Handles(region);
        return ScreenshotHandleGeometry.Hit(handles, point, (float)_zoom);
    }
    private ScreenshotAnnotation[] Hits(Vector2 point) => _document.State.Annotations.Reverse().Where(a => a.Hit(point, (float)(5 / _zoom))).ToArray();
    private void Canvas_Pressed(object sender, PointerRoutedEventArgs e)
    {
        if (_busy || _pointerId is not null) return;
        var pointer = e.GetCurrentPoint(PaintCanvas); var properties = pointer.Properties; var point = ImagePoint(e);
        var canvasHit = HitCanvas(point, includeCanvasBorder: true);
        var inside = canvasHit.Inside; var edge = canvasHit.Edge; var editingHandle = canvasHit.Handle;
        var panning = properties.IsMiddleButtonPressed || KeyDown(VirtualKey.Space);
        if (!inside && editingHandle < 0 && edge is null && !panning) return;
        if (properties.IsRightButtonPressed)
        {
            var hit = Hits(point).FirstOrDefault();
            var target = ScreenshotContextTarget.Resolve(_region, SelectedObjects, hit, point);
            _region = target.Region;
            if (_selectedIds.SetEquals(target.SelectedIds)) { ShowSelectedOptions(); Refresh(); }
            else SetSelection(target.SelectedIds);
            ShowObjectMenu(point, pointer.Position); e.Handled = true; return;
        }
        if (!properties.IsLeftButtonPressed && !properties.IsMiddleButtonPressed) return;
        if (_pickingColor && properties.IsLeftButtonPressed && !panning) { if (inside) BeginColorCapture(point, e); e.Handled = true; return; }
        InteractionCanvas.Focus(FocusState.Pointer); _start = _lastPoint = point; _handle = -1; _dragged = false; _emptySelectionPress = false;
        _selectionBefore = new(_selectedIds); _regionBefore = _region; _gestureItems = []; _previews.Clear();
        _primaryBefore = _primaryId; _clickSelectionId = null; _duplicateItems = []; _duplicating = false; _snapX = _snapY = null;
        _extendSelection = KeyDown(VirtualKey.Shift) || KeyDown(VirtualKey.Control);
        var viewPoint = e.GetCurrentPoint(Viewport).Position; _viewportStart = new((float)viewPoint.X, (float)viewPoint.Y);
        if (properties.IsMiddleButtonPressed || KeyDown(VirtualKey.Space))
        {
            _gesture = Gesture.Pan; _panX = Viewport.HorizontalOffset; _panY = Viewport.VerticalOffset;
        }
        else if (edge is { } canvasEdge)
        {
            BeginCanvasCrop(canvasEdge, e); e.Handled = true; return;
        }
        else if (_tool == ScreenshotTool.Number)
        {
            if (_document.State.NextNumber == int.MaxValue) { _ = RunAsync(() => MessageAsync("番号", "次の番号を小さくしてください。")); e.Handled = true; return; }
            _selectedIds.Clear(); _region = null; ShowSelectedOptions();
            _numberPlacement = new(_document.State, point, CurrentFontSize, _color); _draft = _numberPlacement.Preview;
            _gestureBox = _draft.Bounds; _gesture = Gesture.NumberPlace;
        }
        else if (_tool == ScreenshotTool.Region)
        {
            _handle = HitRegionHandle(point);
            if (_handle >= 0) { _gesture = Gesture.RegionResize; _gestureBox = _region!.Value; _handleCenter = ScreenshotGeometry.Handles(_gestureBox)[_handle]; }
            else if (_region is { } region && region.Contains(point))
            {
                _emptySelectionPress = Hits(point).Length == 0;
                _gesture = Gesture.RegionLift; _gestureBox = region;
            }
            else { _gesture = Gesture.RegionCreate; _region = new(point.X, point.Y, 0, 0); if (!_extendSelection) _selectedIds.Clear(); }
        }
        else if (_tool == ScreenshotTool.Select && _region is { } pixels && pixels.Contains(point))
        {
            _gesture = Gesture.RegionLift; _gestureBox = pixels; _emptySelectionPress = Hits(point).Length == 0;
        }
        else if (_tool != ScreenshotTool.Eraser && (_handle = HitHandle(point)) >= 0)
        {
            _gestureItems = SelectedObjects.ToImmutableArray(); _gestureBox = ScreenshotGeometry.Bounds(_gestureItems);
            if (Selected?.Tool == ScreenshotTool.Callout) _gestureBox = Selected.ClipBounds is null ? Selected.Bounds : Selected.VisualBounds;
            _gesture = _handle == 8 ? Gesture.CalloutTail : HasEndpointHandles(Selected) ? Gesture.Endpoint : Gesture.Resize;
            _handleCenter = _handle == 8 ? Selected!.Tail : _gesture == Gesture.Endpoint ? Selected!.Points[_handle] : ScreenshotGeometry.Handles(_gestureBox)[_handle];
        }
        else if (_tool == ScreenshotTool.Select)
        {
            var hits = Hits(point); ScreenshotAnnotation? hit = hits.FirstOrDefault();
            var selectedZ = _document.State.Annotations.Select((a, i) => (a, i)).Where(x => _selectedIds.Contains(x.a.Id)).Select(x => x.i).DefaultIfEmpty(-1).Max();
            var moveGroup = _selectedIds.Count > 0 && ScreenshotGeometry.Bounds(SelectedObjects).Contains(point)
                && (hit is null || _selectedIds.Contains(hit.Id) || _document.State.Annotations.IndexOf(hit) < selectedZ)
                && (hit is null || !_extendSelection || !_selectedIds.Contains(hit.Id));
            if (hit is not null || moveGroup)
            {
                _emptySelectionPress = hit is null;
                if (moveGroup && _extendSelection && hit is not null) _clickSelectionId = hit.Id;
                if (!moveGroup && hit is not null)
                {
                    _clickSelectionId = hit.Id;
                    // Defer toggling until release so Ctrl-click and Ctrl-drag are distinct.
                    if (!_selectedIds.Contains(hit.Id)) { if (!_extendSelection) _selectedIds.Clear(); _selectedIds.Add(hit.Id); }
                    _primaryId = hit.Id;
                }
                _gestureItems = SelectedObjects.ToImmutableArray(); _gesture = Gesture.Move; _gestureBox = ScreenshotGeometry.Bounds(_gestureItems); _region = null;
            }
            else
            {
                _gesture = Gesture.Marquee; _region = new(point.X, point.Y, 0, 0);
                if (!_extendSelection) _selectedIds.Clear();
            }
        }
        else
        {
            _selectedIds.Clear(); _region = null; ShowSelectedOptions(); _gesture = Gesture.Draw;
            var font = CurrentFontSize; var text = string.IsNullOrWhiteSpace(TextInput.Text) ? "テキスト" : TextInput.Text;
            var box = _tool is ScreenshotTool.Text ? new ScreenshotBox(point.X, point.Y, Math.Max(font * 5, font * text.Length), font * 2) : new(point.X, point.Y, 0, 0);
            _draft = new(Guid.NewGuid().ToString("N"), _tool, box, _color, CurrentWidth, font, text,
                _tool is ScreenshotTool.Pen or ScreenshotTool.Eraser or ScreenshotTool.Line or ScreenshotTool.Arrow ? [point] : default, BlurRadius: CurrentBlurRadius, MosaicSize: CurrentMosaicSize, Opacity: CurrentOpacity);
            if (_tool is ScreenshotTool.Text)
            {
                // Text is placed on press and edited in the text box, not dragged out.
                _document.Add(_draft); var id = _draft.Id; _draft = null; _hasImage = true; _gesture = Gesture.None;
                SetCurrentTool(ScreenshotTool.Select); SetSelection([id]);
                TextInput.Focus(FocusState.Programmatic); TextInput.SelectAll();
                e.Handled = true; return;
            }
        }
        _pointerId = e.Pointer.PointerId; InteractionCanvas.CapturePointer(e.Pointer); UpdateCursor(point); ShowSelectedOptions(); Refresh(); e.Handled = true;
    }
    private void Canvas_Moved(object sender, PointerRoutedEventArgs e)
    {
        var point = ImagePoint(e);
        if (_pointerId != e.Pointer.PointerId) { UpdateCursor(point); return; }
        _lastPoint = point;
        if (_gesture == Gesture.Pan)
        {
            var vp = e.GetCurrentPoint(Viewport).Position; Viewport.ChangeView(_panX - (vp.X - _viewportStart.X), _panY - (vp.Y - _viewportStart.Y), null, true);
        }
        else if (_gesture == Gesture.PickColor) PreviewPickedColor(point);
        else if (_gesture == Gesture.CanvasCrop) PreviewCanvasCrop(e);
        else PreviewGesture(point);
        UpdateCursor(point); InvalidateCanvas(); e.Handled = true;
    }
    private IEnumerable<ScreenshotBox> SnapTargets()
    {
        var frame = _document.State.Frame; yield return new(0, 0, frame.Width, frame.Height);
        foreach (var item in _document.State.Annotations)
            if (!_selectedIds.Contains(item.Id) && !_gestureItems.Any(a => a.Id == item.Id)) yield return item.Bounds;
    }
    private Vector2 SnapPointer(Vector2 point, bool horizontal = true, bool vertical = true)
    {
        if (!KeyDown(VirtualKey.Menu)) return point;
        var snap = ScreenshotManipulation.SnapPoint(point, SnapTargets(), (float)(6 / _zoom), horizontal, vertical);
        _snapX = snap.X; _snapY = snap.Y; return snap.Point;
    }
    private Vector2 ConstrainAndSnapMove(Vector2 delta, bool shift)
    {
        delta = ScreenshotManipulation.ConstrainMove(delta, shift);
        if (KeyDown(VirtualKey.Menu))
        {
            var snap = ScreenshotManipulation.SnapMove(_gestureBox, delta, SnapTargets(), (float)(6 / _zoom), shift);
            delta = snap.Point; _snapX = snap.X; _snapY = snap.Y;
        }
        return delta;
    }
    private void ValidateGuides(ScreenshotBox box)
    {
        if (_snapX is { } x && !ScreenshotManipulation.IsAligned(box, x, true)) _snapX = null;
        if (_snapY is { } y && !ScreenshotManipulation.IsAligned(box, y, false)) _snapY = null;
    }
    private void PreviewGesture(Vector2 point)
    {
        if (_gesture == Gesture.CanvasCrop) { PreviewCanvasCrop(); return; }
        var frame = _document.State.Frame;
        var bounded = Vector2.Clamp(point, Vector2.Zero, new(frame.Width, frame.Height));
        var handlePoint = ScreenshotHandleGeometry.DragPoint(_handleCenter, _start, point);
        var delta = point - _start; if (delta.Length() * _zoom >= 3) _dragged = true;
        var shift = KeyDown(VirtualKey.Shift); var ctrl = KeyDown(VirtualKey.Control); _snapX = _snapY = null;
        switch (_gesture)
        {
            case Gesture.RegionLift:
                if (!_dragged || ScreenshotCrop.RegionArea(_gestureBox, frame.Width, frame.Height) is not { } area) break;
                _lift ??= ScreenshotRegionLifting.Create(PaintCanvas, _document.State, area);
                delta = ScreenshotManipulation.WholePixels(ConstrainAndSnapMove(delta, shift));
                _liftPreview = _lift.Preview(delta, ctrl);
                _selectedIds.Clear(); _region = _gestureBox with { X = _gestureBox.X + delta.X, Y = _gestureBox.Y + delta.Y };
                ValidateGuides(_region.Value); break;
            case Gesture.NumberPlace:
                delta = ConstrainAndSnapMove(delta, shift);
                _numberPlacement?.Move(_start + delta); _draft = _numberPlacement?.Preview; break;
            // Rectangular selections stay on the pixel grid, so the frame shown is exactly what delete/cut/crop process.
            case Gesture.Marquee:
            case Gesture.RegionCreate:
                var origin = ScreenshotManipulation.WholePixels(_start);
                var corner = ScreenshotManipulation.WholePixels(SnapPointer(bounded));
                _region = ScreenshotManipulation.FitToCanvas(ScreenshotManipulation.CreateBox(origin, corner, shift, ctrl), origin, frame.Width, frame.Height, shift).SnapToPixels();
                ValidateGuides(_region.Value); PreviewMarquee(); break;
            case Gesture.RegionMove:
                delta = ScreenshotManipulation.WholePixels(ConstrainAndSnapMove(delta, shift));
                _region = (_gestureBox with
                {
                    X = Math.Clamp(_gestureBox.X + delta.X, 0, Math.Max(0, frame.Width - _gestureBox.Width)),
                    Y = Math.Clamp(_gestureBox.Y + delta.Y, 0, Math.Max(0, frame.Height - _gestureBox.Height))
                }).SnapToPixels();
                ValidateGuides(_region.Value); PreviewMarquee(); break;
            case Gesture.RegionResize:
                var anchor = ctrl ? new Vector2(_gestureBox.X + _gestureBox.Width / 2, _gestureBox.Y + _gestureBox.Height / 2) : ScreenshotGeometry.Handles(_gestureBox)[(_handle + 4) % 8];
                var resizePoint = ScreenshotManipulation.WholePixels(SnapPointer(Vector2.Clamp(handlePoint, Vector2.Zero, new(frame.Width, frame.Height)), _handle is not (1 or 5), _handle is not (3 or 7)));
                _region = ScreenshotManipulation.FitToCanvas(ScreenshotGeometry.Resize(_gestureBox, _handle, resizePoint, shift, ctrl), anchor, frame.Width, frame.Height, shift).SnapToPixels();
                ValidateGuides(_region.Value); PreviewMarquee(); break;
            case Gesture.Move:
                delta = ScreenshotManipulation.AlignMoveToPixels(ConstrainAndSnapMove(delta, shift), _gestureItems, _regionBefore is not null && _tool == ScreenshotTool.Region);
                ValidateGuides(_gestureBox with { X = _gestureBox.X + delta.X, Y = _gestureBox.Y + delta.Y });
                _duplicating = _dragged && ctrl;
                if (_duplicating && _duplicateItems.IsEmpty) _duplicateItems = ScreenshotManipulation.Duplicate(_gestureItems);
                for (var i = 0; i < _gestureItems.Length; i++) _previews[_gestureItems[i].Id] = (_duplicating ? _duplicateItems[i] : _gestureItems[i]).Move(delta);
                if (_regionBefore is { } r && _tool == ScreenshotTool.Region) _region = r with { X = r.X + delta.X, Y = r.Y + delta.Y };
                break;
            case Gesture.Endpoint:
                var line = _gestureItems[0]; var end = SnapPointer(handlePoint);
                var changed = ScreenshotGeometry.MoveEndpoint(line, _handle, end, shift);
                if (ctrl)
                {
                    var center = (line.Points[0] + line.Points[1]) / 2;
                    var moved = shift ? ScreenshotGeometry.SnapDirection(center, end) : end;
                    changed = ScreenshotGeometry.MoveEndpoint(line, _handle, moved);
                    changed = ScreenshotGeometry.MoveEndpoint(changed, 1 - _handle, 2 * center - moved);
                }
                _previews[line.Id] = changed; ValidateGuides(changed.Bounds); break;
            case Gesture.CalloutTail:
                var callout = _gestureItems[0]; var tip = SnapPointer(handlePoint);
                if (shift) tip = ScreenshotGeometry.SnapDirection(new(callout.Bounds.X + callout.Bounds.Width / 2, callout.Bounds.Y + callout.Bounds.Height / 2), tip);
                _previews[callout.Id] = callout with { CalloutTip = tip }; _snapX = _snapY = null; break;
            case Gesture.Resize:
                var target = ScreenshotGeometry.Resize(_gestureBox, _handle, SnapPointer(handlePoint, _handle is not (1 or 5), _handle is not (3 or 7)), shift, ctrl); ValidateGuides(target);
                foreach (var item in ScreenshotGeometry.ResizeGroup(_gestureItems, _gestureBox, target)) _previews[item.Id] = item;
                break;
            case Gesture.Draw:
                if (_draft is not { } draft) break;
                point = bounded;
                if (draft.Tool is ScreenshotTool.Pen or ScreenshotTool.Eraser) _draft = ScreenshotEditing.AppendStrokePoint(draft, point);
                else
                {
                    point = SnapPointer(point);
                    if (draft.Tool is ScreenshotTool.Line or ScreenshotTool.Arrow)
                    {
                        if (shift) point = ScreenshotGeometry.SnapDirection(_start, point);
                        var start = ctrl ? 2 * _start - point : _start;
                        _draft = draft with { Bounds = ScreenshotBox.Between(start, point), Points = [start, point] };
                    }
                    else _draft = draft with { Bounds = ScreenshotManipulation.CreateBox(_start, point, shift, ctrl) };
                    ValidateGuides(_draft.Bounds);
                }
                break;
        }
    }
    private void PreviewMarquee()
    {
        _selectedIds.Clear(); if (_extendSelection) _selectedIds.UnionWith(_selectionBefore);
        if (_region is { } region) _selectedIds.UnionWith(_document.State.Annotations.Where(a => region.Contains(a.VisualBounds)).Select(a => a.Id));
    }
    private void Canvas_Released(object sender, PointerRoutedEventArgs e)
    {
        if (_pointerId != e.Pointer.PointerId) return;
        if (_gesture == Gesture.PickColor) { FinishColorCapture(ImagePoint(e), e); return; }
        if (_gesture == Gesture.CanvasCrop) { FinishCanvasCrop(e); e.Handled = true; return; }
        Canvas_Moved(sender, e); _pointerId = null; InteractionCanvas.ReleasePointerCapture(e.Pointer);
        if (_lift is not null && _liftPreview is not null)
        {
            _lift.Commit(_document, _liftPreview); _region = null; _selectedIds.Clear(); _selectedIds.Add(_lift.Image.Id); _primaryId = _lift.Image.Id;
            _hasImage = true; SetCurrentTool(ScreenshotTool.Select);
        }
        var editCallout = _draft?.Tool == ScreenshotTool.Callout && _dragged;
        if (_gesture == Gesture.NumberPlace && _numberPlacement is { } number)
        {
            var id = number.Commit(_document); _selectedIds.Clear(); _selectedIds.Add(id); _primaryId = id; _hasImage = true; _draft = null; _numberPlacement = null;
        }
        if (_draft is { } draft && (draft.Tool is ScreenshotTool.Pen or ScreenshotTool.Eraser || _dragged && (draft.Bounds.Width >= 2 || draft.Bounds.Height >= 2)))
        {
            _document.Add(draft); _selectedIds.Clear(); _selectedIds.Add(draft.Id); _primaryId = draft.Id; _hasImage = true;
            if (draft.Tool is not (ScreenshotTool.Pen or ScreenshotTool.Eraser)) SetCurrentTool(ScreenshotTool.Select);
            else _selectedIds.Clear();
        }
        if (_dragged && _previews.Count > 0)
        {
            if (_duplicating) { _document.AddMany(_previews.Values); _selectedIds.Clear(); _selectedIds.UnionWith(_previews.Values.Select(a => a.Id)); _primaryId = _selectedIds.LastOrDefault(); }
            else _document.ReplaceMany(_previews.Values);
        }
        if (!_dragged && _clickSelectionId is { } clicked) { var selection = ScreenshotManipulation.ClickSelection(_selectionBefore, clicked, _extendSelection); _selectedIds.Clear(); _selectedIds.UnionWith(selection); _primaryId = _selectedIds.Contains(clicked) ? clicked : _selectedIds.LastOrDefault(); }
        if (!_dragged && _emptySelectionPress) { _region = null; _selectedIds.Clear(); _primaryId = null; }
        if (_gesture is Gesture.Marquee or Gesture.RegionCreate && !_dragged) { _region = null; _selectedIds.Clear(); if (_extendSelection) _selectedIds.UnionWith(_selectionBefore); }
        ClearGesturePreview(); ClearMovePreview();
        ShowSelectedOptions(); Refresh(); UpdateCursor(_hoverPoint); e.Handled = true;
        if (editCallout) { TextInput.Focus(FocusState.Programmatic); TextInput.SelectAll(); }
    }
    private void Canvas_Canceled(object sender, PointerRoutedEventArgs e) { if (_pointerId == e.Pointer.PointerId) { CancelGesture(); ShowSelectedOptions(); Refresh(); } }
    private void Canvas_CaptureLost(object sender, PointerRoutedEventArgs e) { if (_pointerId == e.Pointer.PointerId) { CancelGesture(); ShowSelectedOptions(); Refresh(); } }
    private void CancelGesture()
    {
        var picking = _gesture == Gesture.PickColor;
        if (_pointerId is not null) { _region = _regionBefore; _selectedIds.Clear(); _selectedIds.UnionWith(_selectionBefore); _primaryId = _primaryBefore; }
        _pointerId = null; ClearGesturePreview();
        _numberPlacement?.Cancel(); _numberPlacement = null;
        ClearMovePreview();
        _canvasCrop = null; if (CanvasLeftGrip is not null) foreach (var grip in CanvasEdges) grip.ReleasePointerCaptures();
        InteractionCanvas?.ReleasePointerCaptures(); InvalidateCanvas();
        if (picking) SetColorPicking(false);
    }
    private void ClearGesturePreview()
    {
        _draft = null; _previews.Clear(); _gestureItems = []; _gesture = Gesture.None; _handle = -1; _emptySelectionPress = false;
        _lift = null; _liftPreview = null; ClearColorCapture();
    }
    private void ClearMovePreview()
    {
        _duplicateItems = []; _duplicating = false; _snapX = _snapY = null;
    }
    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _dialogOpen) return;
        CancelGesture(); SetCurrentTool(ScreenshotTool.Region); var frame = _document.State.Frame; _region = new(0, 0, frame.Width, frame.Height);
        SetSelection(_document.State.Annotations.Select(a => a.Id));
    }
}
