using PPPaint.Core;
using PPPaint.Editing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PPPaint;

public sealed partial class MainWindow
{
    private bool CanModifyLayerProperties => (uint)_activeLayerIndex < _layers.Count && !_layers[_activeLayerIndex].IsLocked;

    private static string DescribeAdvancedLayer(LayerState layer) =>
        (layer.Group is null ? "" : $"[{layer.Group}] ") +
        (layer.Mask is null ? "" : "[マスク] ") + (layer.Adjustment is null ? "" : "[調整] ") +
        $"{(layer.IsLocked ? "🔒 " : "")}{layer.Name} ({layer.Opacity:P0})" +
        (layer.BlendMode == LayerBlendMode.Normal ? "" : $" [{BlendModeLabel(layer.BlendMode)}]");

    private static string BlendModeLabel(LayerBlendMode mode) => mode switch
    {
        LayerBlendMode.Multiply => "乗算", LayerBlendMode.Screen => "スクリーン", LayerBlendMode.Overlay => "オーバーレイ", _ => "通常"
    };

    private void AddAdvancedLayerMenuItems(MenuFlyout flyout)
    {
        var properties = new MenuFlyoutItem { Text = "描画モード・グループ…", Icon = CreateUnifiedIcon("A-layers") };
        properties.Click += async (_, _) => await ShowAdvancedLayerPropertiesAsync();
        flyout.Items.Add(properties);
        var hasMask = _layers.Count > 0 && _layers[_activeLayerIndex].Mask is not null;
        AddLayerMenuItem(flyout, "マスクを追加（選択範囲）", () => ModifyActiveMask("Add"), CanModifyLayerProperties);
        AddLayerMenuItem(flyout, "マスクを反転", () => ModifyActiveMask("Invert"), CanModifyLayerProperties && hasMask);
        AddLayerMenuItem(flyout, "マスクを削除", () => ModifyActiveMask("Remove"), CanModifyLayerProperties && hasMask);
        AddLayerMenuItem(flyout, "マスクを塗って隠す", () => StartAdvancedTool("MaskHide"), CanModifyLayerProperties);
        AddLayerMenuItem(flyout, "マスクを塗って戻す", () => StartAdvancedTool("MaskReveal"), CanModifyLayerProperties);
        var group = new MenuFlyoutItem { Text = "グループ操作…", IsEnabled = _layers.Count > 0 && _layers[_activeLayerIndex].Group is not null };
        group.Click += async (_, _) => await ShowLayerGroupOperationsAsync();
        flyout.Items.Add(group);
    }

    private void ModifyActiveMask(string action)
    {
        if (_documentBusy || !CanModifyLayerProperties) return;
        CommitFloatingSelection();
        var layer = _layers[_activeLayerIndex];
        if (action != "Add" && layer.Mask is null) return;
        byte[]? mask = null;
        if (action == "Add")
        {
            mask = new byte[checked(_imageWidth * _imageHeight)];
            for (var y = 0; y < _imageHeight; y++)
            for (var x = 0; x < _imageWidth; x++) mask[y * _imageWidth + x] = _selection?.GetCoverage(x, y) ?? 255;
        }
        else if (action == "Invert") mask = layer.Mask!.Select(value => (byte)(255 - value)).ToArray();
        CaptureUndo();
        layer.Mask = mask;
        FinishEdit();
    }

    private async Task ShowAdvancedLayerPropertiesAsync()
    {
        if (_documentBusy || _layers.Count == 0) return;
        CommitFloatingSelection();
        var indices = GetSelectedLayerIndices();
        if (indices.Length == 0) indices = [_activeLayerIndex];
        var selected = indices.Select(index => _layers[index]).ToArray();
        var layer = _layers[_activeLayerIndex];
        var mode = new ComboBox { Header = "描画モード", MinWidth = 280 };
        foreach (var value in Enum.GetValues<LayerBlendMode>()) mode.Items.Add(BlendModeLabel(value));
        mode.SelectedIndex = (int)layer.BlendMode;
        var group = new TextBox { Header = "グループ名（空欄で解除）", Text = layer.Group ?? "", MaxLength = 128 };
        var panel = new StackPanel { Spacing = 12 };
        ToolTipService.SetToolTip(panel, $"選択中の{selected.Length}レイヤーへ適用します。");
        panel.Children.Add(mode); panel.Children.Add(group);
        var dialog = new ContentDialog { XamlRoot = PaintCanvas.XamlRoot, Title = "描画モード・グループ", Content = panel,
            PrimaryButtonText = "適用", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Primary };
        ConfigureDialogAccessibility(dialog);
        _documentBusy = true; RefreshDocumentTabs();
        try
        {
            if (await dialog.ShowManagedAsync() != ContentDialogResult.Primary) return;
            var name = string.IsNullOrWhiteSpace(group.Text) ? null : group.Text.Trim();
            if (name?.Any(char.IsControl) == true) { await ShowInfoAsync("グループ", "名前に改行や制御文字は使用できません。"); return; }
            var selectedMode = (LayerBlendMode)Math.Clamp(mode.SelectedIndex, 0, 3);
            if (selected.All(item => item.Group == name && item.BlendMode == selectedMode)) return;
            CaptureUndo();
            foreach (var item in selected) { item.BlendMode = selectedMode; item.Group = name; }
            FinishEdit();
        }
        finally { _documentBusy = false; RefreshDocumentTabs(); }
    }

    private async Task ShowLayerGroupOperationsAsync()
    {
        if (_documentBusy || _layers.Count == 0 || _layers[_activeLayerIndex].Group is not { } groupName) return;
        CommitFloatingSelection();
        var members = _layers.Where(layer => layer.Group == groupName).ToArray();
        var operation = new ComboBox { Header = "一括操作", MinWidth = 280 };
        foreach (var name in new[] { "表示", "非表示", "ロック", "ロック解除", "不透明度を設定", "上へ移動", "下へ移動", "グループ解除" }) operation.Items.Add(name);
        operation.SelectedIndex = 0;
        var opacity = new Slider { Header = "不透明度 (%)", Minimum = 0, Maximum = 100, Value = members[0].Opacity * 100 };
        var panel = new StackPanel { Spacing = 12 }; panel.Children.Add(operation); panel.Children.Add(opacity);
        var dialog = new ContentDialog { XamlRoot = PaintCanvas.XamlRoot, Title = $"グループ: {groupName} ({members.Length}枚)", Content = panel,
            PrimaryButtonText = "適用", CloseButtonText = "キャンセル" };
        ConfigureDialogAccessibility(dialog);
        _documentBusy = true; RefreshDocumentTabs();
        try
        {
            if (await dialog.ShowManagedAsync() != ContentDialogResult.Primary) return;
            var action = operation.SelectedIndex;
            if (action is 5 or 6 && members.Any(layer => layer.IsLocked))
            { await ShowInfoAsync("グループ", "移動する前にグループのロックを解除してください。"); return; }
            CaptureUndo();
            var activeId = _layers[_activeLayerIndex].Id;
            foreach (var layer in members)
            {
                switch (action)
                {
                    case 0: layer.IsVisible = true; break;
                    case 1: layer.IsVisible = false; break;
                    case 2: layer.IsLocked = true; break;
                    case 3: layer.IsLocked = false; break;
                    case 4: layer.Opacity = (float)(opacity.Value / 100); break;
                    case 7: layer.Group = null; break;
                }
            }
            if (action == 5)
            {
                for (var index = _layers.Count - 2; index >= 0; index--)
                    if (_layers[index].Group == groupName && _layers[index + 1].Group != groupName)
                        (_layers[index], _layers[index + 1]) = (_layers[index + 1], _layers[index]);
            }
            else if (action == 6)
            {
                for (var index = 1; index < _layers.Count; index++)
                    if (_layers[index].Group == groupName && _layers[index - 1].Group != groupName)
                        (_layers[index], _layers[index - 1]) = (_layers[index - 1], _layers[index]);
            }
            _activeLayerIndex = _layers.FindIndex(layer => layer.Id == activeId);
            _surface = _layers[_activeLayerIndex].Surface;
            FinishEdit();
        }
        finally { _documentBusy = false; RefreshDocumentTabs(); }
    }
}
