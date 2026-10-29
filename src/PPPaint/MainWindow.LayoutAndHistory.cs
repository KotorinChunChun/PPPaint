using System.Numerics;
using System.Runtime.CompilerServices;
using PPPaint.Core;
using PPPaint.Editing;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace PPPaint;

public sealed partial class MainWindow
{
    private sealed record HistoryLabel(string Text);
    private readonly ConditionalWeakTable<DocumentSnapshot, HistoryLabel> _historyLabels = new();
    private bool _guidesVisible;
    private bool _snapToGuides;
    private double _guideX = .5, _guideY = .5;

    private void RememberHistory(DocumentSnapshot snapshot, string operation)
    {
        var name = operation switch
        {
            "PaintCanvas_PointerPressed" => "描画・塗りつぶし",
            "CommitAdjustment" => "画像加工",
            "ApplyDocumentTransform" => "キャンバス変形",
            "ApplyImageTransform" => "画像変形",
            "ToggleActiveLayerLock" => "レイヤーロック",
            "ChangeActiveLayerOpacityAsync" => "レイヤー不透明度",
            "AddLayer_Click" => "レイヤー追加",
            "BeginShape" => "図形",
            "EnsureFloatingSelection" => "選択範囲の編集",
            "CommitEditableObject" => "文字・図形の編集",
            _ => operation.EndsWith("Async") || operation.Contains('_') ? "編集: " + operation.Replace("_Click", "").Replace("Async", "") : operation
        };
        _historyLabels.Remove(snapshot);
        _historyLabels.Add(snapshot, new HistoryLabel(name));
    }

    private string HistoryName(DocumentSnapshot snapshot) => _historyLabels.TryGetValue(snapshot, out var label) ? label.Text : "編集";

    private async void HistoryPanel_Click(object sender, RoutedEventArgs e)
    {
        if (_documentBusy || _isDrawing) return;
        CommitFloatingSelection(); CommitEditableObjectForDocumentSwitch();
        var past = _undo.Reverse().ToArray(); var future = _redo.ToArray();
        var list = new ListView { Height = 330, MinWidth = 420, SelectionMode = ListViewSelectionMode.Single };
        list.Items.Add("0: 保持されている最初の状態");
        for (var i = 0; i < past.Length; i++) list.Items.Add($"{i + 1}: {HistoryName(past[i])}" + (i + 1 == past.Length ? " ← 現在" : ""));
        for (var i = 0; i < future.Length; i++) list.Items.Add($"{past.Length + i + 1}: {HistoryName(future[i])}（やり直し）");
        list.SelectedIndex = past.Length;
        var dialog = new ContentDialog { XamlRoot = PaintCanvas.XamlRoot, Title = "操作履歴", Content = list, PrimaryButtonText = "この状態へ移動", CloseButtonText = "閉じる" };
        ConfigureDialogAccessibility(dialog);
        _documentBusy = true;
        ContentDialogResult result;
        try { result = await dialog.ShowManagedAsync(); }
        finally { _documentBusy = false; }
        if (result != ContentDialogResult.Primary || list.SelectedIndex < 0) return;
        var delta = list.SelectedIndex - past.Length;
        for (var i = 0; i < Math.Abs(delta); i++) { if (delta < 0) Undo(); else Redo(); }
    }

    private void DrawGuides(CanvasDrawingSession drawing)
    {
        if (!_guidesVisible) return;
        var color = Color.FromArgb(190, 0, 150, 220);
        drawing.DrawLine((float)(_imageWidth * _guideX * _zoom), 0, (float)(_imageWidth * _guideX * _zoom), (float)(_imageHeight * _zoom), color, 1);
        drawing.DrawLine(0, (float)(_imageHeight * _guideY * _zoom), (float)(_imageWidth * _zoom), (float)(_imageHeight * _guideY * _zoom), color, 1);
    }

    private EditableObject SnapObjectToGuides(EditableObject item)
    {
        if (!_snapToGuides) return item;
        var b = item.Bounds; var threshold = 8f / (float)_zoom;
        static float Delta(float start, float length, float target, float threshold)
        {
            var delta = new[] { target - start, target - (start + length / 2), target - (start + length) }.OrderBy(Math.Abs).First();
            return Math.Abs(delta) <= threshold ? delta : 0;
        }
        return item.MoveBy(new Vector2(Delta(b.X, b.Width, (float)(_imageWidth * _guideX), threshold),
            Delta(b.Y, b.Height, (float)(_imageHeight * _guideY), threshold)), CanvasObjectBounds);
    }

    private async void Guides_Click(object sender, RoutedEventArgs e)
    {
        if (_documentBusy) return;
        var show = new CheckBox { Content = "ガイドを表示", IsChecked = _guidesVisible };
        var snap = new CheckBox { Content = "オブジェクトをガイドに吸着", IsChecked = _snapToGuides };
        var x = AdjustmentNumber("縦ガイドの位置 (%)", 0, 100, _guideX * 100);
        var y = AdjustmentNumber("横ガイドの位置 (%)", 0, 100, _guideY * 100);
        var panel = new StackPanel { Spacing = 10, MinWidth = 350 };
        panel.Children.Add(show); panel.Children.Add(snap); panel.Children.Add(x); panel.Children.Add(y);
        var dialog = new ContentDialog { XamlRoot = PaintCanvas.XamlRoot, Title = "ガイドと吸着", Content = panel, PrimaryButtonText = "適用", CloseButtonText = "キャンセル" };
        ConfigureDialogAccessibility(dialog);
        if (await dialog.ShowManagedAsync() != ContentDialogResult.Primary || !double.IsFinite(x.Value) || !double.IsFinite(y.Value)) return;
        _guidesVisible = show.IsChecked == true; _snapToGuides = snap.IsChecked == true;
        _guideX = Math.Clamp(x.Value, 0, 100) / 100; _guideY = Math.Clamp(y.Value, 0, 100) / 100;
        PaintCanvas.Invalidate();
    }

    private async void ObjectLayout_Click(object sender, RoutedEventArgs e)
    {
        if (_documentBusy) return;
        CommitFloatingSelection();
        var available = _objects.Where(item => FindObjectLayer(item) is { IsLocked: false, IsVisible: true }).ToArray();
        if (available.Length == 0) { await ShowInfoAsync("配置・整列", "編集可能な文字または図形を作成してください。"); return; }
        var list = new ListView { SelectionMode = ListViewSelectionMode.Multiple, Height = 160 };
        foreach (var item in available) list.Items.Add(new ListViewItem { Content = $"{(item.Kind == EditableObjectKind.Text ? item.Text : item.Shape?.ToString())} ({item.Bounds.X:0}, {item.Bounds.Y:0})", Tag = item });
        var initial = Array.FindIndex(available, item => item.Id == _activeObjectId);
        list.SelectedIndex = Math.Max(0, initial);
        var operation = new ComboBox { Header = "操作", ItemsSource = new[] { "位置と寸法を指定（1個）", "左揃え", "横中央揃え", "右揃え", "上揃え", "縦中央揃え", "下揃え", "横に等間隔", "縦に等間隔" }, SelectedIndex = 0 };
        var b = available[Math.Max(0, initial)].Bounds;
        var x = AdjustmentNumber("X (px)", 0, _imageWidth, b.X); var y = AdjustmentNumber("Y (px)", 0, _imageHeight, b.Y);
        var width = AdjustmentNumber("幅 (px)", 1, _imageWidth, b.Width); var height = AdjustmentNumber("高さ (px)", 1, _imageHeight, b.Height);
        var panel = new StackPanel { Spacing = 8, MinWidth = 420 };
        ToolTipService.SetToolTip(operation, "1個の整列はキャンバス基準、複数は選択したオブジェクトの外接枠基準です。");
        foreach (var control in new FrameworkElement[] { list, operation, x, y, width, height }) panel.Children.Add(control);
        var dialog = new ContentDialog { XamlRoot = PaintCanvas.XamlRoot, Title = "配置・整列", Content = new ScrollViewer { Content = panel, MaxHeight = 550 }, PrimaryButtonText = "適用", CloseButtonText = "キャンセル" };
        ConfigureDialogAccessibility(dialog);
        if (await dialog.ShowManagedAsync() != ContentDialogResult.Primary) return;
        var selected = list.SelectedItems.Cast<ListViewItem>().Select(item => (EditableObject)item.Tag).ToArray();
        if (selected.Length == 0) return;
        var updated = new List<EditableObject>();
        if (operation.SelectedIndex == 0)
        {
            if (selected.Length != 1 || new[] { x.Value, y.Value, width.Value, height.Value }.Any(v => !double.IsFinite(v))) { await ShowInfoAsync("配置", "数値指定では1個だけ選択し、有限の数値を入力してください。"); return; }
            var item = selected[0];
            var w = (float)Math.Clamp(width.Value, 1, _imageWidth); var h = (float)Math.Clamp(height.Value, 1, _imageHeight);
            var resized = item.Resize(SelectionHandle.BottomRight, new Vector2(w - item.Bounds.Width, h - item.Bounds.Height), CanvasObjectBounds);
            updated.Add(resized.MoveBy(new Vector2((float)x.Value - resized.Bounds.X, (float)y.Value - resized.Bounds.Y), CanvasObjectBounds));
        }
        else
        {
            var left = selected.Length == 1 ? 0 : selected.Min(item => item.Bounds.X); var top = selected.Length == 1 ? 0 : selected.Min(item => item.Bounds.Y);
            var right = selected.Length == 1 ? _imageWidth : selected.Max(item => item.Bounds.Right); var bottom = selected.Length == 1 ? _imageHeight : selected.Max(item => item.Bounds.Bottom);
            if (operation.SelectedIndex >= 7 && selected.Length < 3) { await ShowInfoAsync("等間隔", "3個以上のオブジェクトを選択してください。"); return; }
            var sorted = (operation.SelectedIndex == 8 ? selected.OrderBy(item => item.Bounds.Y) : selected.OrderBy(item => item.Bounds.X)).ToArray();
            var horizontalGap = selected.Length > 1 ? (right - left - selected.Sum(item => item.Bounds.Width)) / (selected.Length - 1) : 0;
            var verticalGap = selected.Length > 1 ? (bottom - top - selected.Sum(item => item.Bounds.Height)) / (selected.Length - 1) : 0;
            float cursorX = left, cursorY = top;
            foreach (var item in sorted)
            {
                var bounds = item.Bounds;
                var dx = operation.SelectedIndex switch { 1 => left - bounds.X, 2 => (left + right - bounds.Width) / 2 - bounds.X, 3 => right - bounds.Right, 7 => cursorX - bounds.X, _ => 0 };
                var dy = operation.SelectedIndex switch { 4 => top - bounds.Y, 5 => (top + bottom - bounds.Height) / 2 - bounds.Y, 6 => bottom - bounds.Bottom, 8 => cursorY - bounds.Y, _ => 0 };
                updated.Add(item.MoveBy(new Vector2(dx, dy), CanvasObjectBounds)); cursorX += bounds.Width + horizontalGap; cursorY += bounds.Height + verticalGap;
            }
        }
        CaptureUndo("配置・整列");
        foreach (var item in updated) ReplaceEditableObject(item);
        _activeObjectOriginal = null; FinishEdit();
    }
}
