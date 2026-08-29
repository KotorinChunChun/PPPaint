using ChunPaint.Editing;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace ChunPaint;

public sealed partial class MainWindow
{
    private int _layerCount = 1;

    private void RefreshLayersPanel()
    {
        if (LayersList is null || _updatingLayersPanel)
            return;

        _updatingLayersPanel = true;
        LayersList.Items.Clear();
        for (var index = _layers.Count - 1; index >= 0; index--)
        {
            var layer = _layers[index];
            var row = new Grid { Width = 102, Height = 76, ColumnSpacing = 5 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var eye = new TextBlock
            {
                Text = layer.IsVisible ? "●" : "○",
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var thumbnail = new Border
            {
                Width = 48,
                Height = 48,
                Background = new SolidColorBrush(Microsoft.UI.Colors.White),
                BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.LightGray),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4)
            };
            Grid.SetColumn(thumbnail, 1);
            var name = new TextBlock
            {
                Text = layer.Name,
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(name, 2);
            row.Children.Add(eye);
            row.Children.Add(thumbnail);
            row.Children.Add(name);

            LayersList.Items.Add(new ListViewItem
            {
                Content = row,
                Tag = index,
                IsSelected = index == _activeLayerIndex,
                Padding = new Thickness(2),
                Margin = new Thickness(0, 2, 0, 2)
            });
        }
        _updatingLayersPanel = false;
    }

    private void LayersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingLayersPanel || LayersList.SelectedItem is not ListViewItem { Tag: int index })
            return;
        if ((uint)index >= _layers.Count)
            return;

        _activeLayerIndex = index;
        _surface = _layers[index].Surface;
        PaintCanvas.Invalidate();
    }

    private void LayersList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var flyout = new MenuFlyout();
        AddLayerMenuItem(flyout, _layers.Count > 0 && _layers[_activeLayerIndex].IsVisible ? "非表示" : "表示", ToggleActiveLayerVisibility);
        AddLayerMenuItem(flyout, "複製", DuplicateActiveLayer);
        AddLayerMenuItem(flyout, "削除", DeleteActiveLayer, _layers.Count > 1);
        flyout.ShowAt(LayersList, new FlyoutShowOptions { Position = e.GetPosition(LayersList) });
        e.Handled = true;
    }

    private static void AddLayerMenuItem(MenuFlyout flyout, string text, Action action, bool enabled = true)
    {
        var item = new MenuFlyoutItem { Text = text, IsEnabled = enabled };
        item.Click += (_, _) => action();
        flyout.Items.Add(item);
    }

    private void ToggleActiveLayerVisibility()
    {
        if (_layers.Count == 0) return;
        CaptureUndo();
        _layers[_activeLayerIndex].IsVisible = !_layers[_activeLayerIndex].IsVisible;
        RefreshLayersPanel();
        FinishEdit();
    }

    private void DuplicateActiveLayer()
    {
        if (_layers.Count == 0) return;
        CaptureUndo();
        var source = _layers[_activeLayerIndex];
        var surface = new CanvasRenderTarget(PaintCanvas, _imageWidth, _imageHeight, 96);
        surface.SetPixelBytes(source.Surface.GetPixelBytes());
        _layers.Insert(_activeLayerIndex + 1, new LayerState($"{source.Name} のコピー", surface, source.IsVisible));
        _activeLayerIndex++;
        _surface = surface;
        RefreshLayersPanel();
        FinishEdit();
    }

    private void DeleteActiveLayer()
    {
        if (_layers.Count <= 1) return;
        CaptureUndo();
        _layers[_activeLayerIndex].Dispose();
        _layers.RemoveAt(_activeLayerIndex);
        _activeLayerIndex = Math.Clamp(_activeLayerIndex, 0, _layers.Count - 1);
        _surface = _layers[_activeLayerIndex].Surface;
        RefreshLayersPanel();
        FinishEdit();
    }

    private void PaintCanvas_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var hasSelection = _selection is not null;
        var flyout = new MenuFlyout();
        flyout.Items.Add(new MenuFlyoutItem { Text = "切り取り", IsEnabled = hasSelection });
        flyout.Items.Add(new MenuFlyoutItem { Text = "コピー", IsEnabled = hasSelection });
        flyout.Items.Add(new MenuFlyoutItem { Text = "貼り付け", IsEnabled = false });
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(new MenuFlyoutItem { Text = "トリミング", IsEnabled = hasSelection });
        flyout.Items.Add(new MenuFlyoutItem { Text = "削除", IsEnabled = hasSelection });
        flyout.Items.Add(new MenuFlyoutItem { Text = "色の反転", IsEnabled = hasSelection });
        flyout.ShowAt(PaintCanvas, new FlyoutShowOptions { Position = e.GetPosition(PaintCanvas) });
        e.Handled = true;
    }
}
