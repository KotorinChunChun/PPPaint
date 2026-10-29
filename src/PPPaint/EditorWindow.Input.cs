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
    private uint? _pointerId;
    private Vector2 _start;
    private ScreenshotAnnotation? _draft, _original;
    private ScreenshotBox? _region;
    private int _handle = -1;
    private Vector2 ImagePoint(PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(PaintCanvas).Position; var frame = _document.State.Frame;
        return new((float)Math.Clamp(p.X / _zoom, 0, frame.Width), (float)Math.Clamp(p.Y / _zoom, 0, frame.Height));
    }
    private static bool KeyDown(VirtualKey key) => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
    private void Canvas_Pressed(object sender, PointerRoutedEventArgs e)
    {
        if (_busy || _pointerId is not null || !e.GetCurrentPoint(PaintCanvas).Properties.IsLeftButtonPressed) return;
        PaintCanvas.Focus(FocusState.Pointer); _start = ImagePoint(e); _handle = -1; _original = null;
        if (_tool == ScreenshotTool.Select)
        {
            if (Selected is { } active)
            {
                var corners = Corners(active.Bounds);
                for (var i = 0; i < corners.Length; i++) if (Vector2.Distance(corners[i], _start) <= 9 / _zoom) { _handle = i; _original = active; break; }
            }
            if (_original is null) _original = _document.State.Annotations.LastOrDefault(a => a.Hit(_start, (float)(5 / _zoom)));
            _selectedId = _original?.Id; _draft = _original; ShowSelectedOptions();
        }
        else if (_tool == ScreenshotTool.Region) { _selectedId = null; _region = new(_start.X, _start.Y, 0, 0); }
        else
        {
            var width = (float)(double.IsFinite(WidthBox.Value) ? WidthBox.Value : 3);
            var font = (float)(double.IsFinite(FontBox.Value) ? FontBox.Value : 24);
            var text = _tool == ScreenshotTool.Number ? _document.State.NextNumber.ToString() : string.IsNullOrWhiteSpace(TextInput.Text) ? "テキスト" : TextInput.Text;
            var b = _tool == ScreenshotTool.Number ? new ScreenshotBox(_start.X, _start.Y, font * 1.8f, font * 1.8f)
                : _tool == ScreenshotTool.Text ? new ScreenshotBox(_start.X, _start.Y, Math.Max(font * 5, font * text.Length), font * 2) : new ScreenshotBox(_start.X, _start.Y, 0, 0);
            _draft = new(Guid.NewGuid().ToString("N"), _tool, b, _color, _tool == ScreenshotTool.Eraser ? Math.Max(12, width) : width, font, text,
                _tool is ScreenshotTool.Pen or ScreenshotTool.Eraser or ScreenshotTool.Line or ScreenshotTool.Arrow ? [_start] : default);
            if (_tool is ScreenshotTool.Number or ScreenshotTool.Text)
            {
                _document.Add(_draft); _selectedId = _draft.Id; _draft = null; _hasImage = true; ShowSelectedOptions(); Refresh();
                if (_tool == ScreenshotTool.Text) { TextInput.Focus(FocusState.Programmatic); TextInput.SelectAll(); }
                e.Handled = true; return;
            }
        }
        _pointerId = e.Pointer.PointerId; PaintCanvas.CapturePointer(e.Pointer); Refresh(); e.Handled = true;
    }
    private void Canvas_Moved(object sender, PointerRoutedEventArgs e)
    {
        if (_pointerId != e.Pointer.PointerId) return;
        var point = ImagePoint(e);
        if (_tool == ScreenshotTool.Region) _region = ScreenshotBox.Between(_start, point);
        else if (_original is { } original)
        {
            if (_handle >= 0)
            {
                var anchor = Corners(original.Bounds)[(_handle + 2) % 4];
                var box = ScreenshotBox.Between(anchor, point);
                _draft = original.Resize(box with { Width = Math.Max(2, box.Width), Height = Math.Max(2, box.Height) });
            }
            else
            {
                var f = _document.State.Frame; var b = original.Bounds; var delta = point - _start;
                // Cropped annotations can extend beyond the canvas; do not force a jump when grabbing them.
                if (b.X >= 0 && b.Right <= f.Width) delta.X = Math.Clamp(delta.X, -b.X, f.Width - b.Right);
                if (b.Y >= 0 && b.Bottom <= f.Height) delta.Y = Math.Clamp(delta.Y, -b.Y, f.Height - b.Bottom);
                _draft = original.Move(delta);
            }
        }
        else if (_draft is { } draft)
        {
            if (_tool is ScreenshotTool.Pen or ScreenshotTool.Eraser)
            {
                if (Vector2.Distance(draft.Points[^1], point) >= .5f)
                {
                    var points = draft.Points.Add(point); var xs = points.Select(p => p.X); var ys = points.Select(p => p.Y);
                    _draft = draft with { Points = points, Bounds = new(xs.Min(), ys.Min(), Math.Max(1, xs.Max() - xs.Min()), Math.Max(1, ys.Max() - ys.Min())) };
                }
            }
            else _draft = draft with { Bounds = ScreenshotBox.Between(_start, point), Points = _tool is ScreenshotTool.Line or ScreenshotTool.Arrow ? [_start, point] : default };
        }
        PaintCanvas.Invalidate(); e.Handled = true;
    }
    private void Canvas_Released(object sender, PointerRoutedEventArgs e)
    {
        if (_pointerId != e.Pointer.PointerId) return;
        Canvas_Moved(sender, e); _pointerId = null; PaintCanvas.ReleasePointerCapture(e.Pointer);
        if (_draft is { } draft)
        {
            if (_original is not null)
            {
                if (draft != _original) _document.Replace(draft);
            }
            else if (draft.Tool is ScreenshotTool.Pen or ScreenshotTool.Eraser || draft.Bounds.Width >= 2 || draft.Bounds.Height >= 2)
            {
                _document.Add(draft); _selectedId = draft.Id; _hasImage = true;
            }
        }
        if (_region is { Width: < 1 } || _region is { Height: < 1 }) _region = null;
        _draft = null; _original = null; _handle = -1; Refresh(); e.Handled = true;
    }
    private void Canvas_Canceled(object sender, PointerRoutedEventArgs e) { if (_pointerId == e.Pointer.PointerId) CancelGesture(); }
    private void Canvas_CaptureLost(object sender, PointerRoutedEventArgs e) { if (_pointerId == e.Pointer.PointerId) CancelGesture(); }
    private void CancelGesture() { _pointerId = null; _draft = null; _original = null; _handle = -1; PaintCanvas?.ReleasePointerCaptures(); PaintCanvas?.Invalidate(); }
    private void ShowSelectedOptions()
    {
        _updatingControls = true;
        try
        {
            var a = Selected;
            TextOptions.Visibility = a?.Tool is ScreenshotTool.Text or ScreenshotTool.Number || _tool is ScreenshotTool.Text or ScreenshotTool.Number ? Visibility.Visible : Visibility.Collapsed;
            if (a is null) return;
            _color = a.Color; WidthBox.Value = Math.Clamp(a.Width, 1, 100); FontBox.Value = Math.Clamp(a.FontSize, 8, 200); TextInput.Text = a.Text;
        }
        finally { _updatingControls = false; }
    }
    private async void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_busy || _dialogOpen) return;
        var editingText = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(Root.XamlRoot) is TextBox;
        var ctrl = KeyDown(VirtualKey.Control);
        if (ctrl && e.Key == VirtualKey.S) { e.Handled = true; await RunAsync(async () => await SaveImageAsync()); }
        else if (ctrl && e.Key == VirtualKey.O) { e.Handled = true; await RunAsync(async () => { var path = await PickPathAsync(false); if (path is not null) await OpenPathAsync(path); }); }
        else if (editingText) return;
        else if (ctrl && e.Key == VirtualKey.V) { e.Handled = true; await RunAsync(PasteImageAsync); }
        else if (ctrl && e.Key == VirtualKey.C) { e.Handled = true; await RunAsync(CopyImageAsync); }
        else if (ctrl && e.Key == VirtualKey.Z) { e.Handled = true; Undo_Click(sender, new()); }
        else if (ctrl && e.Key == VirtualKey.Y) { e.Handled = true; Redo_Click(sender, new()); }
        else if (ctrl && e.Key == VirtualKey.N) { e.Handled = true; App.Instance.CreateWindow(); }
        else if (e.Key == VirtualKey.Delete) { e.Handled = true; Delete_Click(sender, new()); }
        else if (e.Key == VirtualKey.Escape) { CancelGesture(); _region = null; _selectedId = null; Refresh(); e.Handled = true; }
    }
}
