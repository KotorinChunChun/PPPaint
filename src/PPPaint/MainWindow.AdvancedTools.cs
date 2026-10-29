using System.Numerics;
using PPPaint.Core;
using PPPaint.Editing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace PPPaint;

public sealed partial class MainWindow
{
    private string? _advancedTool;
    private SelectionCombineMode _selectionCombine;
    private byte _wandTolerance = 24;
    private bool _wandContiguous = true;
    private int _selectionFeather;
    private float _retouchRadius = 16, _retouchHardness = .7f, _retouchOpacity = 1;
    private bool _advancedDragging;
    private Vector2 _advancedStart;
    private SelectionRegion? _advancedPreviousSelection;
    private DocumentSnapshot? _advancedOriginal;
    private PixelSurface? _retouchOriginal;
    private Vector2? _cloneAnchor;
    private string? _cloneLayer;
    private readonly List<Vector2> _retouchPoints = [];
    private byte[]? _maskOriginal, _maskCoverage;

    private void StartAdvancedTool(string tool)
    {
        CancelAdvancedStroke();
        CommitFloatingSelection();
        ActivateSelectionTool();
        _advancedTool = tool;
        _cloneAnchor = null;
        _retouchOriginal = null;
        PointerStatusText.Text = tool switch
        {
            "Ellipse" => "楕円選択: ドラッグして範囲を指定",
            "Wand" => "自動選択: 選択したい色をクリック",
            "Clone" => "複製ブラシ: Alt+クリックで参照点、ドラッグで複製",
            "Heal" => "修復ブラシ: Alt+クリックで参照点、ドラッグで修復",
            "MaskHide" => "マスク: ドラッグして隠す（原画は保持）",
            _ => "マスク: ドラッグして表示を戻す"
        };
    }

    private async void AdvancedSelection_Click(object sender, RoutedEventArgs e)
    {
        if (_documentBusy || _isDrawing) return;
        var tag = (sender as FrameworkElement)?.Tag as string;
        if (tag is "Ellipse" or "Wand") { StartAdvancedTool(tag); return; }
        var combine = new ComboBox { Header = "選択の合成", ItemsSource = new[] { "置き換え", "追加", "差し引き" }, SelectedIndex = (int)_selectionCombine };
        var tolerance = AdjustmentNumber("自動選択の色差 (0～255)", 0, 255, _wandTolerance);
        var feather = AdjustmentNumber("境界をぼかす半径 (px)", 0, 64, _selectionFeather);
        var contiguous = new CheckBox { Content = "つながった領域だけを選択", IsChecked = _wandContiguous };
        var controls = new StackPanel { Spacing = 8 };
        foreach (var item in new FrameworkElement[] { combine, tolerance, contiguous, feather }) controls.Children.Add(item);
        var dialog = new ContentDialog { XamlRoot = PaintCanvas.XamlRoot, Title = tag == "Feather" ? "選択境界をぼかす" : "選択オプション", Content = controls, PrimaryButtonText = "適用", CloseButtonText = "キャンセル" };
        ConfigureDialogAccessibility(dialog);
        _documentBusy = true;
        try
        {
            if (await dialog.ShowManagedAsync() != ContentDialogResult.Primary) return;
            _selectionCombine = (SelectionCombineMode)Math.Max(0, combine.SelectedIndex);
            _wandTolerance = (byte)Math.Clamp(double.IsFinite(tolerance.Value) ? tolerance.Value : 24, 0, 255);
            _selectionFeather = (int)Math.Clamp(double.IsFinite(feather.Value) ? feather.Value : 0, 0, 64);
            _wandContiguous = contiguous.IsChecked == true;
            if (tag == "Feather" && _selection is not null)
            {
                CommitFloatingSelection();
                _selection = SelectionEditing.Feather(_selection, _imageWidth, _imageHeight, _selectionFeather);
                UpdateSelectionStatus(); PaintCanvas.Invalidate();
            }
        }
        finally { _documentBusy = false; UpdateHistoryState(); }
    }

    private async void AdvancedRetouch_Click(object sender, RoutedEventArgs e)
    {
        if (_documentBusy || _isDrawing) return;
        var tag = (sender as FrameworkElement)?.Tag as string;
        if (tag is "Clone" or "Heal") { StartAdvancedTool(tag); return; }
        var radius = AdjustmentNumber("半径 (px)", 1, 512, _retouchRadius);
        var hardness = AdjustmentNumber("硬さ (0～1)", 0, 1, _retouchHardness);
        var opacity = AdjustmentNumber("強さ (0～1)", 0, 1, _retouchOpacity);
        var controls = new StackPanel { Spacing = 8 };
        foreach (var item in new[] { radius, hardness, opacity }) controls.Children.Add(item);
        var dialog = new ContentDialog { XamlRoot = PaintCanvas.XamlRoot, Title = "修復・複製・マスクブラシ設定", Content = controls, PrimaryButtonText = "適用", CloseButtonText = "キャンセル" };
        ConfigureDialogAccessibility(dialog); _documentBusy = true;
        try
        {
            if (await dialog.ShowManagedAsync() == ContentDialogResult.Primary)
            {
                _retouchRadius = (float)Math.Clamp(double.IsFinite(radius.Value) ? radius.Value : 16, 1, 512);
                _retouchHardness = (float)Math.Clamp(double.IsFinite(hardness.Value) ? hardness.Value : .7, 0, 1);
                _retouchOpacity = (float)Math.Clamp(double.IsFinite(opacity.Value) ? opacity.Value : 1, 0, 1);
            }
        }
        finally { _documentBusy = false; UpdateHistoryState(); }
    }

    private bool AdvancedPointerPressed(Vector2 point, PointerRoutedEventArgs e)
    {
        if (_advancedTool is null) return false;
        e.Handled = true;
        if (!e.GetCurrentPoint(PaintCanvas).Properties.IsLeftButtonPressed || _layers.Count == 0) return true;
        if (_advancedTool == "Wand")
        {
            CommitFloatingSelection();
            var incoming = SelectionEditing.MagicWand(GetAdjustmentSource(_layers[_activeLayerIndex]), (int)point.X, (int)point.Y, _wandTolerance, _wandContiguous);
            CompleteAdvancedSelection(incoming, _selection); return true;
        }
        _advancedStart = point; _advancedPreviousSelection = _selection;
        if (_advancedTool != "Ellipse")
        {
            var layer = _layers[_activeLayerIndex];
            if (layer.IsLocked || (layer.Adjustment is not null && _advancedTool is "Clone" or "Heal")) return true;
            if (_advancedTool is "Clone" or "Heal")
            {
                if ((e.KeyModifiers & VirtualKeyModifiers.Menu) != 0)
                {
                    _cloneAnchor = point; _cloneLayer = layer.Id; PointerStatusText.Text = $"参照点: {(int)point.X}, {(int)point.Y} px"; return true;
                }
                if (_cloneAnchor is null || _cloneLayer != layer.Id) { PointerStatusText.Text = "先に Alt+クリックで参照点を指定してください"; return true; }
            }
            _advancedOriginal = CaptureDocumentSnapshot();
            if (_advancedTool is "Clone" or "Heal")
            {
                _retouchOriginal = GetAdjustmentSource(layer);
                _objects.RemoveAll(item => item.LayerId == layer.Id); _activeObjectId = null;
            }
            else
            {
                _maskOriginal = layer.Mask?.ToArray() ?? Enumerable.Repeat((byte)255, checked(_imageWidth * _imageHeight)).ToArray();
                _maskCoverage = new byte[_maskOriginal.Length];
            }
            _retouchPoints.Clear(); _retouchPoints.Add(point);
        }
        _advancedDragging = true; _isDrawing = true; PaintCanvas.CapturePointer(e.Pointer);
        RenderAdvancedStroke(point); return true;
    }

    private bool AdvancedPointerMoved(Vector2 point, PointerRoutedEventArgs e)
    {
        if (!_advancedDragging) return false;
        point = Vector2.Clamp(point, Vector2.Zero, new Vector2(_imageWidth - 1, _imageHeight - 1));
        if (_retouchPoints.Count < 50000 && (_retouchPoints.Count == 0 || Vector2.Distance(_retouchPoints[^1], point) >= .5f)) _retouchPoints.Add(point);
        RenderAdvancedStroke(point); e.Handled = true; return true;
    }

    private void RenderAdvancedStroke(Vector2 point)
    {
        if (_advancedTool == "Ellipse")
        {
            var left = (int)Math.Min(_advancedStart.X, point.X); var top = (int)Math.Min(_advancedStart.Y, point.Y);
            _selection = SelectionEditing.Ellipse(_imageWidth, _imageHeight, new PixelRect(left, top, Math.Max(1, (int)Math.Abs(point.X - _advancedStart.X) + 1), Math.Max(1, (int)Math.Abs(point.Y - _advancedStart.Y) + 1)));
        }
        else if (_advancedTool is "Clone" or "Heal" && _retouchOriginal is not null && _cloneAnchor is { } anchor)
        {
            // 各プレビューをストローク開始時の原画から再計算し、移動イベントごとの強さの累積を防ぐ。
            var result = RetouchOperations.Stroke(_retouchOriginal, _retouchOriginal, anchor, _advancedStart, _retouchPoints, _retouchRadius, _retouchHardness, _retouchOpacity, _advancedTool == "Heal", _selection);
            var replacement = ToRenderTarget(result); var layer = _layers[_activeLayerIndex];
            layer.Surface.Dispose(); layer.Surface = replacement; _surface = replacement;
        }
        else if (_maskOriginal is not null && _maskCoverage is not null)
        {
            var from = _retouchPoints.Count > 1 ? _retouchPoints[^2] : point;
            var steps = Math.Max(1, (int)Math.Ceiling(Vector2.Distance(from, point) / Math.Max(.5f, _retouchRadius / 3)));
            for (var step = 0; step <= steps; step++)
            {
                var center = Vector2.Lerp(from, point, step / (float)steps);
                for (var y = Math.Max(0, (int)(center.Y - _retouchRadius)); y <= Math.Min(_imageHeight - 1, (int)(center.Y + _retouchRadius)); y++)
                for (var x = Math.Max(0, (int)(center.X - _retouchRadius)); x <= Math.Min(_imageWidth - 1, (int)(center.X + _retouchRadius)); x++)
                {
                    var d = Vector2.Distance(new Vector2(x + .5f, y + .5f), center) / _retouchRadius;
                    if (d >= 1) continue;
                    var edge = d <= _retouchHardness || _retouchHardness == 1 ? 1 : (1 - d) / (1 - _retouchHardness);
                    var index = y * _imageWidth + x;
                    _maskCoverage[index] = Math.Max(_maskCoverage[index], (byte)Math.Clamp(MathF.Round(edge * _retouchOpacity * (_selection?.GetCoverage(x, y) ?? 255)), 0, 255));
                }
            }
            var mask = _maskOriginal.ToArray();
            for (var i = 0; i < mask.Length; i++) mask[i] = (byte)((_maskOriginal[i] * (255 - _maskCoverage[i]) + (_advancedTool == "MaskHide" ? 0 : 255) * _maskCoverage[i] + 127) / 255);
            _layers[_activeLayerIndex].Mask = mask;
        }
        _stackRevision++; PaintCanvas.Invalidate();
    }

    private bool AdvancedPointerReleased(PointerRoutedEventArgs e)
    {
        if (!_advancedDragging) return false;
        AdvancedPointerMoved(ToImagePoint(e.GetCurrentPoint(PaintCanvas).Position), e);
        _advancedDragging = false; _isDrawing = false; PaintCanvas.ReleasePointerCapture(e.Pointer);
        if (_advancedTool == "Ellipse" && _selection is { } incoming) CompleteAdvancedSelection(incoming, _advancedPreviousSelection);
        else if (_advancedOriginal is { } original)
        {
            RememberHistory(original, _advancedTool switch { "Clone" => "複製ブラシ", "Heal" => "修復ブラシ", _ => "マスクブラシ" });
            _undo.Push(original); _redo.Clear(); TrimDocumentHistory(); FinishEdit();
        }
        _advancedOriginal = null; _retouchOriginal = null; _maskOriginal = null; _maskCoverage = null; _retouchPoints.Clear();
        return true;
    }

    private void CompleteAdvancedSelection(SelectionRegion incoming, SelectionRegion? previous)
    {
        if (_selectionFeather > 0) incoming = SelectionEditing.Feather(incoming, _imageWidth, _imageHeight, _selectionFeather);
        _selection = SelectionEditing.Combine(previous, incoming, _imageWidth, _imageHeight, _selectionCombine);
        UpdateSelectionStatus(); UpdateSelectionCommandState(); PaintCanvas.Invalidate();
    }

    private bool CancelAdvancedStroke()
    {
        if (!_advancedDragging) return false;
        _advancedDragging = false; _isDrawing = false;
        if (_advancedOriginal is { } original) RestoreDocumentSnapshot(original);
        _selection = _advancedPreviousSelection;
        _advancedOriginal = null; _retouchOriginal = null; _maskOriginal = null; _maskCoverage = null; _retouchPoints.Clear();
        PaintCanvas.ReleasePointerCaptures(); _stackRevision++; PaintCanvas.Invalidate(); UpdateHistoryState(); return true;
    }

    private void PaintCanvas_PointerCanceled(object sender, PointerRoutedEventArgs e) => CancelAdvancedStroke();
}
