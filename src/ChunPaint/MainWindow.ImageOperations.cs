using ChunPaint.Core;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI;

namespace ChunPaint;

public sealed partial class MainWindow
{
    private InMemoryRandomAccessStream? _clipboardStream;

    private void ConfigureWidgetAccelerators()
    {
        AddAccelerator(VirtualKey.C, VirtualKeyModifiers.Control, async (_, args) =>
        {
            args.Handled = true;
            await CopySelectionAsync();
        });
        AddAccelerator(VirtualKey.C, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, async (_, args) =>
        {
            args.Handled = true;
            await CopySelectionAsync(visibleLayers: true);
        });
        AddAccelerator(VirtualKey.A, VirtualKeyModifiers.Control, (_, args) =>
        {
            SelectAll();
            args.Handled = true;
        });
        AddAccelerator(VirtualKey.X, VirtualKeyModifiers.Control, async (_, args) =>
        {
            args.Handled = true;
            await CutSelectionAsync();
        });
        AddAccelerator(VirtualKey.V, VirtualKeyModifiers.Control, async (_, args) =>
        {
            args.Handled = true;
            await PasteAsync();
        });
        AddAccelerator(VirtualKey.P, VirtualKeyModifiers.Control, async (_, args) =>
        {
            args.Handled = true;
            await ShowPrintUiAsync();
        });
        AddAccelerator(VirtualKey.Delete, VirtualKeyModifiers.None, (_, args) =>
        {
            args.Handled = DeleteSelection();
        });
        AddAccelerator(VirtualKey.Space, VirtualKeyModifiers.Control, (_, args) =>
        {
            ApplySelection();
            args.Handled = true;
        });
        AddAccelerator(VirtualKey.Left, VirtualKeyModifiers.None, (_, args) =>
        {
            MoveSelectionByKeyboard(-1, 0);
            args.Handled = _selection is not null;
        });
        AddAccelerator(VirtualKey.Right, VirtualKeyModifiers.None, (_, args) =>
        {
            MoveSelectionByKeyboard(1, 0);
            args.Handled = _selection is not null;
        });
        AddAccelerator(VirtualKey.Up, VirtualKeyModifiers.None, (_, args) =>
        {
            MoveSelectionByKeyboard(0, -1);
            args.Handled = _selection is not null;
        });
        AddAccelerator(VirtualKey.Down, VirtualKeyModifiers.None, (_, args) =>
        {
            MoveSelectionByKeyboard(0, 1);
            args.Handled = _selection is not null;
        });
        AddAccelerator(VirtualKey.Tab, VirtualKeyModifiers.None, (_, args) =>
        {
            CycleSelectionHandle(1);
            args.Handled = _selection is not null;
        });
        AddAccelerator(VirtualKey.Tab, VirtualKeyModifiers.Shift, (_, args) =>
        {
            CycleSelectionHandle(-1);
            args.Handled = _selection is not null;
        });
        AddAccelerator(VirtualKey.F11, VirtualKeyModifiers.None, (_, args) =>
        {
            ToggleFullScreen();
            args.Handled = true;
        });
        AddAccelerator(VirtualKey.Escape, VirtualKeyModifiers.None, (_, args) =>
        {
            args.Handled = CancelSelection() || ExitFullScreen();
        });
    }

    private Color WithCurrentOpacity(Color color)
    {
        var opacity = OpacitySlider is null ? 1d : OpacitySlider.Value / 100d;
        return Color.FromArgb((byte)Math.Clamp(Math.Round(color.A * opacity), 0, 255), color.R, color.G, color.B);
    }

    private PixelRect? GetSelectionPixelRect()
    {
        if (_selection is not { } selection)
        {
            return null;
        }

        var bounds = selection.Bounds;
        return bounds.X >= 0 && bounds.Y >= 0 && bounds.Right <= _imageWidth && bounds.Bottom <= _imageHeight
            ? bounds
            : null;
    }

    private void UpdateSelectionCommandState()
    {
        var hasSelection = GetSelectionPixelRect() is not null;
        if (CutMenuItem is not null) CutMenuItem.IsEnabled = hasSelection;
        if (CopyMenuItem is not null) CopyMenuItem.IsEnabled = hasSelection;
        if (DeleteMenuItem is not null) DeleteMenuItem.IsEnabled = hasSelection;
        if (CropMenuItem is not null) CropMenuItem.IsEnabled = hasSelection;
        if (InvertMenuItem is not null) InvertMenuItem.IsEnabled = hasSelection;
        if (CopyVisibleLayersMenuItem is not null) CopyVisibleLayersMenuItem.IsEnabled = hasSelection;
        if (SelectionApplyMenuItem is not null) SelectionApplyMenuItem.IsEnabled = _floatingSelectionPixels is not null;
        if (SelectionCancelMenuItem is not null) SelectionCancelMenuItem.IsEnabled = _floatingSelectionPixels is not null;
        if (TransparentSelectionMenuItem is not null) TransparentSelectionMenuItem.IsChecked = _transparentSelection;
    }

    private static bool CanPasteBitmap()
    {
        try
        {
            return Clipboard.GetContent().Contains(StandardDataFormats.Bitmap);
        }
        catch
        {
            return false;
        }
    }

    private PixelSurface ToPixelSurface(CanvasRenderTarget source)
    {
        var result = new PixelSurface((int)source.SizeInPixels.Width, (int)source.SizeInPixels.Height, Rgba32.Transparent);
        result.Restore(source.GetPixelBytes());
        return result;
    }

    private CanvasRenderTarget ToRenderTarget(PixelSurface source)
    {
        var result = new CanvasRenderTarget(PaintCanvas, source.Width, source.Height, 96);
        result.SetPixelBytes(source.Snapshot());
        return result;
    }

    private void ApplyDocumentTransform(Func<PixelSurface, PixelSurface> transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        EnsureSurface(PaintCanvas);
        CommitFloatingSelection();
        CaptureUndo();

        var transformed = _layers.Select(layer => transform(ToPixelSurface(layer.Surface))).ToArray();
        for (var index = 0; index < _layers.Count; index++)
        {
            _layers[index].Surface.Dispose();
            _layers[index].Surface = ToRenderTarget(transformed[index]);
        }

        _imageWidth = transformed[0].Width;
        _imageHeight = transformed[0].Height;
        _activeLayerIndex = Math.Clamp(_activeLayerIndex, 0, _layers.Count - 1);
        _surface = _layers[_activeLayerIndex].Surface;
        _selection = null;
        ApplyCanvasSize();
        RefreshLayersPanel();
        FinishEdit();
    }

    private void ApplyActiveLayerTransform(Func<PixelSurface, PixelSurface> transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        EnsureSurface(PaintCanvas);
        CommitFloatingSelection();
        CaptureUndo();

        var layer = _layers[_activeLayerIndex];
        var transformed = transform(ToPixelSurface(layer.Surface));
        layer.Surface.Dispose();
        layer.Surface = ToRenderTarget(transformed);
        _surface = layer.Surface;
        RefreshLayersPanel();
        FinishEdit();
    }

    private async Task ShowResizeDialogAsync()
    {
        var widthBox = new NumberBox
        {
            Header = "水平方向 (ピクセル)",
            Minimum = 1,
            Maximum = 16384,
            Value = _imageWidth,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };
        var heightBox = new NumberBox
        {
            Header = "垂直方向 (ピクセル)",
            Minimum = 1,
            Maximum = 16384,
            Value = _imageHeight,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };
        var maintainAspect = new CheckBox
        {
            Content = "縦横比を維持する（幅を基準）",
            IsChecked = true
        };
        var content = new StackPanel { Spacing = 12, MinWidth = 360 };
        content.Children.Add(widthBox);
        content.Children.Add(heightBox);
        content.Children.Add(maintainAspect);

        var dialog = new ContentDialog
        {
            XamlRoot = PaintCanvas.XamlRoot,
            Title = "サイズ変更",
            Content = content,
            PrimaryButtonText = "OK",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (double.IsNaN(widthBox.Value) || double.IsNaN(heightBox.Value))
        {
            await ShowInfoAsync("サイズ変更", "幅と高さを数値で指定してください。");
            return;
        }

        var newWidth = (int)Math.Round(widthBox.Value);
        var newHeight = maintainAspect.IsChecked == true
            ? Math.Max(1, (int)Math.Round((double)newWidth * _imageHeight / _imageWidth))
            : (int)Math.Round(heightBox.Value);
        if (newWidth is < 1 or > 16384 || newHeight is < 1 or > 16384)
        {
            await ShowInfoAsync("サイズ変更", "幅と高さは1～16384ピクセルで指定してください。");
            return;
        }
        if (newWidth == _imageWidth && newHeight == _imageHeight)
        {
            return;
        }

        ApplyDocumentTransform(surface => ImageTransforms.ResizeNearestNeighbor(surface, newWidth, newHeight));
    }

    private void ShowRotateMenu(FrameworkElement? anchor)
    {
        if (anchor is null)
        {
            return;
        }

        var flyout = new MenuFlyout();
        AddRotateItem(flyout, "右へ90°回転", "Clockwise");
        AddRotateItem(flyout, "左へ90°回転", "CounterClockwise");
        AddRotateItem(flyout, "180°回転", "Rotate180");
        flyout.Items.Add(new MenuFlyoutSeparator());
        AddRotateItem(flyout, "左右反転", "FlipHorizontal");
        AddRotateItem(flyout, "上下反転", "FlipVertical");
        flyout.ShowAt(anchor);
    }

    private void AddRotateItem(MenuFlyout flyout, string text, string command)
    {
        var item = new MenuFlyoutItem { Text = text, Tag = command };
        item.Click += RotateCommand_Click;
        flyout.Items.Add(item);
    }

    private void RotateCommand_Click(object sender, RoutedEventArgs e)
    {
        switch ((sender as FrameworkElement)?.Tag?.ToString())
        {
            case "Clockwise":
                ApplyDocumentTransform(ImageTransforms.RotateClockwise);
                break;
            case "CounterClockwise":
                ApplyDocumentTransform(ImageTransforms.RotateCounterClockwise);
                break;
            case "Rotate180":
                ApplyDocumentTransform(ImageTransforms.Rotate180);
                break;
            case "FlipHorizontal":
                ApplyDocumentTransform(ImageTransforms.FlipHorizontal);
                break;
            case "FlipVertical":
                ApplyDocumentTransform(ImageTransforms.FlipVertical);
                break;
        }
    }

    private bool CropToSelection()
    {
        if (GetSelectionPixelRect() is not { } selection)
        {
            return false;
        }

        CommitFloatingSelection();
        ApplyDocumentTransform(surface => ImageTransforms.Crop(surface, selection));
        return true;
    }

    private bool DeleteSelection()
    {
        if (_floatingSelectionPixels is not null)
        {
            DeleteFloatingSelection();
            return true;
        }
        if (_surface is null || _selection is null)
        {
            return false;
        }

        CaptureUndo();
        var pixels = ToPixelSurface(_surface);
        var replacement = _activeLayerIndex == 0 ? ToCoreColor(_secondaryColor) : Rgba32.Transparent;
        SelectionOperations.Clear(pixels, _selection, replacement);
        _surface.SetPixelBytes(pixels.Snapshot());
        _selection = null;
        FinishEdit();
        return true;
    }

    private bool InvertSelectionColors()
    {
        if (_floatingSelectionPixels is not null)
        {
            var inverted = ImageTransforms.InvertColors(_floatingSelectionPixels);
            SetFloatingSelectionPixels(inverted);
            PaintCanvas.Invalidate();
            return true;
        }
        if (_surface is null || _selection is null)
        {
            return false;
        }

        CaptureUndo();
        var pixels = ToPixelSurface(_surface);
        SelectionOperations.InvertColors(pixels, _selection);
        _surface.SetPixelBytes(pixels.Snapshot());
        FinishEdit();
        return true;
    }

    private async Task CopySelectionAsync(bool visibleLayers = false)
    {
        if (_selection is null)
        {
            return;
        }

        PixelSurface selected;
        if (_floatingSelectionPixels is not null && !visibleLayers)
        {
            selected = ClonePixelSurface(_floatingSelectionPixels);
        }
        else
        {
            EnsureSurface(PaintCanvas);
            var source = visibleLayers
                ? SelectionOperations.CompositeVisibleLayers(_layers.Select(layer => (ToPixelSurface(layer.Surface), layer.IsVisible)))
                : ToPixelSurface(_surface!);
            selected = SelectionOperations.Extract(
                source,
                _selection,
                _transparentSelection,
                ToCoreColor(_secondaryColor));
        }

        _clipboardStream?.Dispose();
        _clipboardStream = new InMemoryRandomAccessStream();
        using var selectedRenderTarget = ToRenderTarget(selected);
        await selectedRenderTarget.SaveAsync(_clipboardStream, CanvasBitmapFileFormat.Png);
        _clipboardStream.Seek(0);

        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetBitmap(RandomAccessStreamReference.CreateFromStream(_clipboardStream));
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    private async Task CutSelectionAsync()
    {
        if (GetSelectionPixelRect() is null)
        {
            return;
        }

        await CopySelectionAsync();
        DeleteSelection();
    }

    private async Task PasteAsync()
    {
        CommitFloatingSelection();
        var content = Clipboard.GetContent();
        if (!content.Contains(StandardDataFormats.Bitmap))
        {
            await ShowInfoAsync("貼り付け", "クリップボードに貼り付け可能な画像がありません。");
            return;
        }

        var reference = await content.GetBitmapAsync();
        using var stream = await reference.OpenReadAsync();
        using var bitmap = await CanvasBitmap.LoadAsync(PaintCanvas, stream);
        EnsureSurface(PaintCanvas);
        CaptureUndo();

        var pastedWidth = Math.Min(_imageWidth, (int)bitmap.SizeInPixels.Width);
        var pastedHeight = Math.Min(_imageHeight, (int)bitmap.SizeInPixels.Height);
        var surface = new CanvasRenderTarget(PaintCanvas, _imageWidth, _imageHeight, 96);
        using (var session = surface.CreateDrawingSession())
        {
            session.Clear(Colors.Transparent);
            session.DrawImage(
                bitmap,
                new Rect(0, 0, pastedWidth, pastedHeight),
                new Rect(0, 0, pastedWidth, pastedHeight));
        }

        _layerCount++;
        _layers.Add(new Editing.LayerState($"貼り付け {_layerCount}", surface));
        _activeLayerIndex = _layers.Count - 1;
        _surface = surface;
        _selection = SelectionRegion.CreateRectangle(new PixelRect(0, 0, pastedWidth, pastedHeight));
        RefreshLayersPanel();
        FinishEdit();
    }

    private void RemoveBackground()
    {
        ApplyActiveLayerTransform(surface => ImageTransforms.RemoveBackgroundFromEdges(surface, 32));
    }

    private async void Cut_Click(object sender, RoutedEventArgs e) => await CutSelectionAsync();
    private async void Copy_Click(object sender, RoutedEventArgs e) => await CopySelectionAsync();
    private async void Paste_Click(object sender, RoutedEventArgs e) => await PasteAsync();
    private void Crop_Click(object sender, RoutedEventArgs e) => CropToSelection();
    private void Delete_Click(object sender, RoutedEventArgs e) => DeleteSelection();
    private void Invert_Click(object sender, RoutedEventArgs e) => InvertSelectionColors();
    private void BackgroundRemoval_Click(object sender, RoutedEventArgs e) => RemoveBackground();
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomSlider.Value = Math.Min(ZoomSlider.Maximum, ZoomSlider.Value + 25);
}
