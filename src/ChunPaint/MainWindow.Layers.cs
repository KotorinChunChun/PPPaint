using ChunPaint.Editing;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
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
                CornerRadius = new CornerRadius(4),
                Child = new Image
                {
                    Source = CreateLayerThumbnail(layer),
                    Stretch = Stretch.Uniform
                }
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

    private WriteableBitmap CreateLayerThumbnail(LayerState layer)
    {
        const int size = 48;
        const int checkerSize = 6;
        var sourcePixels = layer.Surface.GetPixelBytes();
        var pixels = new byte[size * size * 4];
        var scale = Math.Min(size / (double)_imageWidth, size / (double)_imageHeight);
        var drawWidth = Math.Max(1, (int)Math.Round(_imageWidth * scale));
        var drawHeight = Math.Max(1, (int)Math.Round(_imageHeight * scale));
        var offsetX = (size - drawWidth) / 2;
        var offsetY = (size - drawHeight) / 2;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var checker = ((x / checkerSize) + (y / checkerSize)) % 2 == 0 ? (byte)232 : (byte)255;
                var targetOffset = ((y * size) + x) * 4;
                pixels[targetOffset] = checker;
                pixels[targetOffset + 1] = checker;
                pixels[targetOffset + 2] = checker;
                pixels[targetOffset + 3] = 255;

                if (x < offsetX || y < offsetY || x >= offsetX + drawWidth || y >= offsetY + drawHeight)
                {
                    continue;
                }

                var sourceX = Math.Min(_imageWidth - 1, (int)((x - offsetX) * _imageWidth / (double)drawWidth));
                var sourceY = Math.Min(_imageHeight - 1, (int)((y - offsetY) * _imageHeight / (double)drawHeight));
                var sourceOffset = ((sourceY * _imageWidth) + sourceX) * 4;
                var alpha = sourcePixels[sourceOffset + 3];
                pixels[targetOffset] = BlendThumbnailChannel(sourcePixels[sourceOffset], checker, alpha);
                pixels[targetOffset + 1] = BlendThumbnailChannel(sourcePixels[sourceOffset + 1], checker, alpha);
                pixels[targetOffset + 2] = BlendThumbnailChannel(sourcePixels[sourceOffset + 2], checker, alpha);
            }
        }

        var bitmap = new WriteableBitmap(size, size);
        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            stream.Write(pixels, 0, pixels.Length);
        }
        bitmap.Invalidate();
        return bitmap;
    }

    private static byte BlendThumbnailChannel(byte foreground, byte background, byte alpha) =>
        (byte)(((foreground * alpha) + (background * (255 - alpha)) + 127) / 255);


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
        flyout.Items.Add(new MenuFlyoutSeparator());
        AddLayerMenuItem(flyout, "上へ移動", MoveActiveLayerUp, _activeLayerIndex + 1 < _layers.Count);
        AddLayerMenuItem(flyout, "下へ移動", MoveActiveLayerDown, _activeLayerIndex > 0);
        AddLayerMenuItem(flyout, "下のレイヤーと結合", MergeActiveLayerDown, _activeLayerIndex > 0);
        var rename = new MenuFlyoutItem { Text = "名前の変更" };
        rename.Click += RenameLayer_Click;
        flyout.Items.Add(rename);
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

    private void MoveActiveLayerUp()
    {
        if (_activeLayerIndex + 1 >= _layers.Count) return;
        CaptureUndo();
        var nextIndex = _activeLayerIndex + 1;
        (_layers[_activeLayerIndex], _layers[nextIndex]) = (_layers[nextIndex], _layers[_activeLayerIndex]);
        _activeLayerIndex = nextIndex;
        _surface = _layers[_activeLayerIndex].Surface;
        RefreshLayersPanel();
        FinishEdit();
    }

    private void MoveActiveLayerDown()
    {
        if (_activeLayerIndex <= 0) return;
        CaptureUndo();
        var nextIndex = _activeLayerIndex - 1;
        (_layers[_activeLayerIndex], _layers[nextIndex]) = (_layers[nextIndex], _layers[_activeLayerIndex]);
        _activeLayerIndex = nextIndex;
        _surface = _layers[_activeLayerIndex].Surface;
        RefreshLayersPanel();
        FinishEdit();
    }

    private void MergeActiveLayerDown()
    {
        if (_activeLayerIndex <= 0) return;
        CaptureUndo();
        var upper = _layers[_activeLayerIndex];
        var lower = _layers[_activeLayerIndex - 1];
        using (var session = lower.Surface.CreateDrawingSession())
        {
            if (upper.IsVisible)
            {
                session.DrawImage(upper.Surface);
            }
        }
        upper.Dispose();
        _layers.RemoveAt(_activeLayerIndex);
        _activeLayerIndex--;
        _surface = lower.Surface;
        RefreshLayersPanel();
        FinishEdit();
    }

    private async Task RenameActiveLayerAsync()
    {
        if (_layers.Count == 0) return;
        var textBox = new TextBox
        {
            Text = _layers[_activeLayerIndex].Name,
            MinWidth = 300
        };
        var dialog = new ContentDialog
        {
            XamlRoot = LayersList.XamlRoot,
            Title = "レイヤー名の変更",
            Content = textBox,
            PrimaryButtonText = "OK",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(textBox.Text))
        {
            return;
        }
        CaptureUndo();
        _layers[_activeLayerIndex].Name = textBox.Text.Trim();
        RefreshLayersPanel();
        FinishEdit();
    }

    private void MoveLayerUp_Click(object sender, RoutedEventArgs e) => MoveActiveLayerUp();
    private void MoveLayerDown_Click(object sender, RoutedEventArgs e) => MoveActiveLayerDown();
    private void MergeLayerDown_Click(object sender, RoutedEventArgs e) => MergeActiveLayerDown();
    private async void RenameLayer_Click(object sender, RoutedEventArgs e) => await RenameActiveLayerAsync();

    private void PaintCanvas_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var hasSelection = _selection is not null;
        var flyout = new MenuFlyout();
        var cut = new MenuFlyoutItem { Text = "切り取り", IsEnabled = hasSelection };
        var copy = new MenuFlyoutItem { Text = "コピー", IsEnabled = hasSelection };
        var paste = new MenuFlyoutItem { Text = "貼り付け", IsEnabled = CanPasteBitmap() };
        cut.Click += Cut_Click;
        copy.Click += Copy_Click;
        paste.Click += Paste_Click;
        flyout.Items.Add(cut);
        flyout.Items.Add(copy);
        flyout.Items.Add(paste);
        flyout.Items.Add(new MenuFlyoutSeparator());
        var crop = new MenuFlyoutItem { Text = "トリミング", IsEnabled = hasSelection };
        var delete = new MenuFlyoutItem { Text = "削除", IsEnabled = hasSelection };
        var invert = new MenuFlyoutItem { Text = "色の反転", IsEnabled = hasSelection };
        crop.Click += Crop_Click;
        delete.Click += Delete_Click;
        invert.Click += Invert_Click;
        flyout.Items.Add(crop);
        flyout.Items.Add(delete);
        flyout.Items.Add(invert);
        flyout.ShowAt(PaintCanvas, new FlyoutShowOptions { Position = e.GetPosition(PaintCanvas) });
        e.Handled = true;
    }
}
