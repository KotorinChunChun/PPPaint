using System.Collections.Immutable;
using System.Numerics;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PPPaint.Core;
using Windows.System;
using Windows.UI.Core;

namespace PPPaint;

public sealed partial class EditorWindow
{
    private enum Gesture { None, Draw, Move, Resize, Endpoint, Marquee, RegionCreate, RegionMove, RegionResize, Pan, CanvasCrop }
    private uint? _pointerId;
    private Gesture _gesture;
    private Vector2 _start, _viewportStart;
    private ScreenshotAnnotation? _draft;
    private ScreenshotBox? _region;
    private ScreenshotBox _gestureBox;
    private ScreenshotBox? _regionBefore;
    private ImmutableArray<ScreenshotAnnotation> _gestureItems = [];
    private HashSet<string> _selectionBefore = [];
    private readonly Dictionary<string, ScreenshotAnnotation> _previews = [];
    private int _handle = -1;
    private bool _dragged;
    private double _panX, _panY;

    private Vector2 ImagePoint(PointerRoutedEventArgs e, bool clamp = false)
    {
        var p = e.GetCurrentPoint(PaintCanvas).Position; var frame = _document.State.Frame;
        var v = new Vector2((float)(p.X / _zoom), (float)(p.Y / _zoom));
        return clamp ? Vector2.Clamp(v, Vector2.Zero, new(frame.Width, frame.Height)) : v;
    }
    private static bool KeyDown(VirtualKey key) => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
    private void SetSelection(IEnumerable<string> ids)
    {
        _selectedIds.Clear(); foreach (var id in ids) _selectedIds.Add(id); _primaryId = _selectedIds.LastOrDefault();
        ShowSelectedOptions(); Refresh();
    }
    private int HitHandle(Vector2 point)
    {
        if (_selectedIds.Count == 0) return -1;
        var selected = SelectedObjects; var single = selected.Length == 1 ? selected[0] : null;
        var points = single is { Tool: ScreenshotTool.Arrow or ScreenshotTool.Line } && single.Points.Length == 2 ? single.Points.ToArray() : ScreenshotGeometry.Handles(ScreenshotGeometry.Bounds(selected));
        return ScreenshotGeometry.HitHandle(points, point, (float)(7 / _zoom));
    }
    private int HitRegionHandle(Vector2 point)
    {
        if (_region is not { } region) return -1;
        var handles = ScreenshotGeometry.Handles(region);
        return ScreenshotGeometry.HitHandle(handles, point, (float)(7 / _zoom));
    }
    private ScreenshotAnnotation[] Hits(Vector2 point) => _document.State.Annotations.Reverse().Where(a => a.Hit(point, (float)(5 / _zoom))).ToArray();
    private void Canvas_Pressed(object sender, PointerRoutedEventArgs e)
    {
        if (_busy || _pointerId is not null) return;
        var pointer = e.GetCurrentPoint(PaintCanvas); var properties = pointer.Properties; var point = ImagePoint(e);
        if (properties.IsRightButtonPressed)
        {
            var hit = Hits(point).FirstOrDefault();
            if (hit is not null && !_selectedIds.Contains(hit.Id)) SetSelection([hit.Id]);
            ShowObjectMenu(point, pointer.Position); e.Handled = true; return;
        }
        if (!properties.IsLeftButtonPressed && !properties.IsMiddleButtonPressed) return;
        PaintCanvas.Focus(FocusState.Pointer); _start = point; _handle = -1; _dragged = false;
        _selectionBefore = new(_selectedIds); _regionBefore = _region; _gestureItems = []; _previews.Clear();
        var viewPoint = e.GetCurrentPoint(Viewport).Position; _viewportStart = new((float)viewPoint.X, (float)viewPoint.Y);
        if (properties.IsMiddleButtonPressed || KeyDown(VirtualKey.Space))
        {
            _gesture = Gesture.Pan; _panX = Viewport.HorizontalOffset; _panY = Viewport.VerticalOffset;
        }
        else if (_tool == ScreenshotTool.Region)
        {
            _handle = HitRegionHandle(point);
            if (_handle >= 0) { _gesture = Gesture.RegionResize; _gestureBox = _region!.Value; }
            else if (_region is { } region && region.Contains(point))
            {
                _gesture = _selectedIds.Count > 0 ? Gesture.Move : Gesture.RegionMove; _gestureBox = region; _gestureItems = SelectedObjects.ToImmutableArray();
            }
            else { _gesture = Gesture.RegionCreate; _region = new(point.X, point.Y, 0, 0); if (!KeyDown(VirtualKey.Shift)) _selectedIds.Clear(); }
        }
        else if (!KeyDown(VirtualKey.Control) && (_handle = HitHandle(point)) >= 0)
        {
            _gestureItems = SelectedObjects.ToImmutableArray(); _gestureBox = ScreenshotGeometry.Bounds(_gestureItems);
            _gesture = Selected is { Tool: ScreenshotTool.Arrow or ScreenshotTool.Line } ? Gesture.Endpoint : Gesture.Resize;
        }
        else if (_tool == ScreenshotTool.Select)
        {
            var hits = Hits(point); ScreenshotAnnotation? hit = hits.FirstOrDefault();
            if (KeyDown(VirtualKey.Menu) && hits.Length > 0) { var index = Array.FindIndex(hits, a => a.Id == _primaryId); hit = hits[(index + 1) % hits.Length]; }
            if (KeyDown(VirtualKey.Control)) hit = null;
            var selectedZ = _document.State.Annotations.Select((a, i) => (a, i)).Where(x => _selectedIds.Contains(x.a.Id)).Select(x => x.i).DefaultIfEmpty(-1).Max();
            var moveGroup = _selectedIds.Count > 0 && ScreenshotGeometry.Bounds(SelectedObjects).Contains(point)
                && (hit is null || _selectedIds.Contains(hit.Id) || _document.State.Annotations.IndexOf(hit) < selectedZ)
                && !KeyDown(VirtualKey.Shift) && !KeyDown(VirtualKey.Control) && !KeyDown(VirtualKey.Menu);
            if (hit is not null || moveGroup)
            {
                if (!moveGroup && hit is not null)
                {
                    if (KeyDown(VirtualKey.Shift)) { if (!_selectedIds.Add(hit.Id)) _selectedIds.Remove(hit.Id); }
                    else if (!_selectedIds.Contains(hit.Id)) { _selectedIds.Clear(); _selectedIds.Add(hit.Id); }
                    _primaryId = hit.Id;
                }
                _gestureItems = SelectedObjects.ToImmutableArray(); _gesture = Gesture.Move; _gestureBox = ScreenshotGeometry.Bounds(_gestureItems); _region = null;
            }
            else
            {
                _gesture = Gesture.Marquee; _region = new(point.X, point.Y, 0, 0);
                if (!KeyDown(VirtualKey.Shift)) _selectedIds.Clear();
            }
        }
        else
        {
            _selectedIds.Clear(); _region = null; _gesture = Gesture.Draw;
            var font = CurrentFontSize; var text = _tool == ScreenshotTool.Number ? _document.State.NextNumber.ToString() : string.IsNullOrWhiteSpace(TextInput.Text) ? "テキスト" : TextInput.Text;
            var box = _tool == ScreenshotTool.Number ? new ScreenshotBox(point.X, point.Y, font * 1.8f, font * 1.8f) : _tool == ScreenshotTool.Text ? new ScreenshotBox(point.X, point.Y, Math.Max(font * 5, font * text.Length), font * 2) : new(point.X, point.Y, 0, 0);
            _draft = new(Guid.NewGuid().ToString("N"), _tool, box, _color, _tool == ScreenshotTool.Eraser ? Math.Max(12, CurrentWidth) : CurrentWidth, font, text,
                _tool is ScreenshotTool.Pen or ScreenshotTool.Eraser or ScreenshotTool.Line or ScreenshotTool.Arrow ? [point] : default, BlurRadius: CurrentBlurRadius);
            if (_tool is ScreenshotTool.Text or ScreenshotTool.Number)
            {
                var tool = _tool; _document.Add(_draft); var id = _draft.Id; _draft = null; _hasImage = true; _gesture = Gesture.None;
                if (tool == ScreenshotTool.Text) SetCurrentTool(ScreenshotTool.Select);
                SetSelection([id]); if (tool == ScreenshotTool.Text) { TextInput.Focus(FocusState.Programmatic); TextInput.SelectAll(); }
                e.Handled = true; return;
            }
        }
        _pointerId = e.Pointer.PointerId; PaintCanvas.CapturePointer(e.Pointer); UpdateCursor(point); ShowSelectedOptions(); Refresh(); e.Handled = true;
    }
    private void Canvas_Moved(object sender, PointerRoutedEventArgs e)
    {
        var point = ImagePoint(e);
        if (_pointerId != e.Pointer.PointerId) { UpdateCursor(point); return; }
        var delta = point - _start; if (delta.Length() * _zoom >= 3) _dragged = true;
        switch (_gesture)
        {
            case Gesture.Pan:
                var vp = e.GetCurrentPoint(Viewport).Position; Viewport.ChangeView(_panX - (vp.X - _viewportStart.X), _panY - (vp.Y - _viewportStart.Y), null, true); break;
            case Gesture.Marquee:
            case Gesture.RegionCreate:
                _region = ScreenshotBox.Between(_start, ImagePoint(e, true)); PreviewMarquee(); break;
            case Gesture.RegionMove:
                var canvas = _document.State.Frame;
                _region = _gestureBox with { X = Math.Clamp(_gestureBox.X + delta.X, 0, Math.Max(0, canvas.Width - _gestureBox.Width)), Y = Math.Clamp(_gestureBox.Y + delta.Y, 0, Math.Max(0, canvas.Height - _gestureBox.Height)) }; PreviewMarquee(); break;
            case Gesture.RegionResize:
                _region = ScreenshotGeometry.Resize(_gestureBox, _handle, ImagePoint(e, true), KeyDown(VirtualKey.Shift)); ClampRegion(); PreviewMarquee(); break;
            case Gesture.Move:
                if (KeyDown(VirtualKey.Shift)) delta = Math.Abs(delta.X) > Math.Abs(delta.Y) ? new(delta.X, 0) : new(0, delta.Y);
                foreach (var item in _gestureItems) _previews[item.Id] = item.Move(delta);
                if (_regionBefore is { } r && _tool == ScreenshotTool.Region) _region = r with { X = r.X + delta.X, Y = r.Y + delta.Y };
                break;
            case Gesture.Endpoint:
                _previews[_gestureItems[0].Id] = ScreenshotGeometry.MoveEndpoint(_gestureItems[0], _handle, point, KeyDown(VirtualKey.Shift)); break;
            case Gesture.Resize:
                var target = ScreenshotGeometry.Resize(_gestureBox, _handle, point, KeyDown(VirtualKey.Shift));
                foreach (var item in ScreenshotGeometry.ResizeGroup(_gestureItems, _gestureBox, target)) _previews[item.Id] = item;
                break;
            case Gesture.Draw:
                if (_draft is not { } draft) break;
                point = ImagePoint(e, true);
                if (draft.Tool is ScreenshotTool.Pen or ScreenshotTool.Eraser)
                {
                    if (Vector2.Distance(draft.Points[^1], point) < .5f) break;
                    var points = draft.Points.Add(point); var xs = points.Select(p => p.X); var ys = points.Select(p => p.Y);
                    _draft = draft with { Points = points, Bounds = new(xs.Min(), ys.Min(), Math.Max(1, xs.Max() - xs.Min()), Math.Max(1, ys.Max() - ys.Min())) };
                }
                else
                {
                    if (KeyDown(VirtualKey.Shift)) point = draft.Tool is ScreenshotTool.Line or ScreenshotTool.Arrow ? ScreenshotGeometry.SnapDirection(_start, point)
                        : _start + new Vector2(MathF.CopySign(Math.Max(Math.Abs(delta.X), Math.Abs(delta.Y)), delta.X), MathF.CopySign(Math.Max(Math.Abs(delta.X), Math.Abs(delta.Y)), delta.Y));
                    _draft = draft with { Bounds = ScreenshotBox.Between(_start, point), Points = draft.Tool is ScreenshotTool.Line or ScreenshotTool.Arrow ? [_start, point] : default };
                }
                break;
        }
        UpdateCursor(point); PaintCanvas.Invalidate(); e.Handled = true;
    }
    private void PreviewMarquee()
    {
        _selectedIds.Clear(); if (KeyDown(VirtualKey.Shift)) _selectedIds.UnionWith(_selectionBefore);
        if (_region is { } region) _selectedIds.UnionWith(_document.State.Annotations.Where(a => region.Contains(a.Bounds)).Select(a => a.Id));
    }
    private void ClampRegion()
    {
        if (_region is not { } r) return; var f = _document.State.Frame;
        var x = Math.Clamp(r.X, 0, f.Width - 1); var y = Math.Clamp(r.Y, 0, f.Height - 1);
        var right = Math.Clamp(r.Right, x + 1, f.Width); var bottom = Math.Clamp(r.Bottom, y + 1, f.Height);
        _region = new(x, y, right - x, bottom - y);
    }
    private void Canvas_Released(object sender, PointerRoutedEventArgs e)
    {
        if (_pointerId != e.Pointer.PointerId) return;
        Canvas_Moved(sender, e); _pointerId = null; PaintCanvas.ReleasePointerCapture(e.Pointer);
        if (_draft is { } draft && (draft.Tool is ScreenshotTool.Pen or ScreenshotTool.Eraser || _dragged && (draft.Bounds.Width >= 2 || draft.Bounds.Height >= 2)))
        {
            _document.Add(draft); _selectedIds.Clear(); _selectedIds.Add(draft.Id); _primaryId = draft.Id; _hasImage = true;
            if (draft.Tool is not (ScreenshotTool.Pen or ScreenshotTool.Eraser)) SetCurrentTool(ScreenshotTool.Select);
            else _selectedIds.Clear();
        }
        if (_dragged && _previews.Count > 0) _document.ReplaceMany(_previews.Values);
        if (_gesture is Gesture.Marquee or Gesture.RegionCreate && !_dragged) { _region = null; _selectedIds.Clear(); }
        _draft = null; _previews.Clear(); _gestureItems = []; _gesture = Gesture.None; _handle = -1;
        ShowSelectedOptions(); Refresh(); e.Handled = true;
    }
    private void Canvas_Canceled(object sender, PointerRoutedEventArgs e) { if (_pointerId == e.Pointer.PointerId) CancelGesture(); }
    private void Canvas_CaptureLost(object sender, PointerRoutedEventArgs e) { if (_pointerId == e.Pointer.PointerId) CancelGesture(); }
    private void CancelGesture()
    {
        if (_pointerId is not null) { _region = _regionBefore; _selectedIds.Clear(); _selectedIds.UnionWith(_selectionBefore); }
        _pointerId = null; _draft = null; _previews.Clear(); _gestureItems = []; _gesture = Gesture.None; _handle = -1;
        _canvasCrop = null; if (CanvasLeftGrip is not null) foreach (var grip in CanvasEdges) grip.ReleasePointerCaptures();
        PaintCanvas?.ReleasePointerCaptures(); PaintCanvas?.Invalidate();
    }
    private void ShowSelectedOptions()
    {
        _updatingControls = true;
        try
        {
            var a = Selected; var tool = a?.Tool ?? _tool;
            WidthBox.IsEnabled = tool is not (ScreenshotTool.Redact or ScreenshotTool.Highlight or ScreenshotTool.Number or ScreenshotTool.Text or ScreenshotTool.Image or ScreenshotTool.Blur);
            BlurOptions.Visibility = tool == ScreenshotTool.Blur ? Visibility.Visible : Visibility.Collapsed;
            TextOptions.Visibility = a?.Tool is ScreenshotTool.Text or ScreenshotTool.Number || _tool is ScreenshotTool.Text or ScreenshotTool.Number ? Visibility.Visible : Visibility.Collapsed;
            if (a is null || a.Tool == ScreenshotTool.Image) return;
            _color = a.Color; WidthBox.Value = Math.Clamp(a.Width, 1, 100); FontBox.Value = Math.Clamp(a.FontSize, 8, 200); BlurRadiusBox.Value = Math.Clamp(a.BlurRadius, 1, 64); TextInput.Text = a.Text;
        }
        finally { _updatingControls = false; }
    }
    private async void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_busy || _dialogOpen) return;
        var editingText = FocusManager.GetFocusedElement(Root.XamlRoot) is TextBox; var ctrl = KeyDown(VirtualKey.Control);
        if (ctrl && e.Key == VirtualKey.S) { e.Handled = true; await RunAsync(async () => await SaveImageAsync(KeyDown(VirtualKey.Shift))); }
        else if (ctrl && e.Key == VirtualKey.O) { e.Handled = true; await RunAsync(async () => { var path = await PickPathAsync(false); if (path is not null) await OpenPathAsync(path); }); }
        else if (editingText) return;
        else if (ctrl && e.Key == VirtualKey.V) { e.Handled = true; await RunAsync(PasteImageAsync); }
        else if (ctrl && e.Key == VirtualKey.C) { e.Handled = true; await RunAsync(CopyImageAsync); }
        else if (ctrl && e.Key == VirtualKey.Z) { e.Handled = true; Undo_Click(sender, new()); }
        else if (ctrl && e.Key == VirtualKey.Y) { e.Handled = true; Redo_Click(sender, new()); }
        else if (ctrl && e.Key == VirtualKey.N) { e.Handled = true; App.Instance.CreateWindow(); }
        else if (_pointerId is null && ScreenshotCommands.TryTool((char)e.Key, ctrl, KeyDown(VirtualKey.Menu), KeyDown(VirtualKey.Shift), editingText, out var tool)) { SelectTool(tool); PaintCanvas.Focus(FocusState.Programmatic); e.Handled = true; }
        else if (!ctrl && !KeyDown(VirtualKey.Menu) && (e.Key == VirtualKey.C || e.Key == VirtualKey.Enter) && _region is not null) { Crop_Click(sender, new()); e.Handled = true; }
        else if (ctrl && e.Key == VirtualKey.A) { SetCurrentTool(ScreenshotTool.Select); SetSelection(_document.State.Annotations.Select(a => a.Id)); e.Handled = true; }
        else if (ctrl && e.Key is VirtualKey.PageUp or VirtualKey.PageDown)
        {
            _document.Reorder(_selectedIds, e.Key == VirtualKey.PageUp ? KeyDown(VirtualKey.Shift) ? ScreenshotStackOrder.Front : ScreenshotStackOrder.Forward : KeyDown(VirtualKey.Shift) ? ScreenshotStackOrder.Back : ScreenshotStackOrder.Backward); Refresh(); e.Handled = true;
        }
        else if (e.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down && _selectedIds.Count > 0)
        {
            var step = KeyDown(VirtualKey.Shift) ? 10 : 1; var offset = e.Key switch { VirtualKey.Left => new Vector2(-step, 0), VirtualKey.Right => new(step, 0), VirtualKey.Up => new(0, -step), _ => new(0, step) };
            _document.ReplaceMany(SelectedObjects.Select(a => a.Move(offset))); _region = null; Refresh(); e.Handled = true;
        }
        else if (e.Key == VirtualKey.Delete) { e.Handled = true; Delete_Click(sender, new()); }
        else if (e.Key == VirtualKey.Escape) { CancelGesture(); _region = null; _selectedIds.Clear(); SetCurrentTool(ScreenshotTool.Select); ShowSelectedOptions(); Refresh(); e.Handled = true; }
    }
}
