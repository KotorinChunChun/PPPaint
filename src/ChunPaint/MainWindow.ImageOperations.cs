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
            if (_selection is not null) MoveSelectionByKeyboard(-1, 0);
            else MoveActiveObjectByKeyboard(-1, 0);
            args.Handled = _selection is not null || HasActiveEditableObject;
        });
        AddAccelerator(VirtualKey.Right, VirtualKeyModifiers.None, (_, args) =>
        {
            if (_selection is not null) MoveSelectionByKeyboard(1, 0);
            else MoveActiveObjectByKeyboard(1, 0);
            args.Handled = _selection is not null || HasActiveEditableObject;
        });
        AddAccelerator(VirtualKey.Up, VirtualKeyModifiers.None, (_, args) =>
        {
            if (_selection is not null) MoveSelectionByKeyboard(0, -1);
            else MoveActiveObjectByKeyboard(0, -1);
            args.Handled = _selection is not null || HasActiveEditableObject;
        });
        AddAccelerator(VirtualKey.Down, VirtualKeyModifiers.None, (_, args) =>
        {
            if (_selection is not null) MoveSelectionByKeyboard(0, 1);
            else MoveActiveObjectByKeyboard(0, 1);
            args.Handled = _selection is not null || HasActiveEditableObject;
        });
        AddAccelerator(VirtualKey.Tab, VirtualKeyModifiers.None, (_, args) =>
        {
            if (_selection is not null) CycleSelectionHandle(1);
            else CycleEditableObjectHandle(1);
            args.Handled = _selection is not null || HasActiveEditableObject;
        });
        AddAccelerator(VirtualKey.Tab, VirtualKeyModifiers.Shift, (_, args) =>
        {
            if (_selection is not null) CycleSelectionHandle(-1);
            else CycleEditableObjectHandle(-1);
            args.Handled = _selection is not null || HasActiveEditableObject;
        });
        AddAccelerator(VirtualKey.F11, VirtualKeyModifiers.None, (_, args) =>
        {
            ToggleFullScreen();
            args.Handled = true;
        });
        AddAccelerator(VirtualKey.Escape, VirtualKeyModifiers.None, (_, args) =>
        {
            args.Handled = CancelShapeDraft() || CancelSelection() || CancelActiveObject() || ExitFullScreen();
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
        var hasObject = HasActiveEditableObject;
        if (CutMenuItem is not null) CutMenuItem.IsEnabled = hasSelection;
        if (CopyMenuItem is not null) CopyMenuItem.IsEnabled = hasSelection;
        if (DeleteMenuItem is not null) DeleteMenuItem.IsEnabled = hasSelection || hasObject;
        if (CropMenuItem is not null) CropMenuItem.IsEnabled = hasSelection;
        if (InvertMenuItem is not null) InvertMenuItem.IsEnabled = hasSelection;
        if (CopyVisibleLayersMenuItem is not null) CopyVisibleLayersMenuItem.IsEnabled = hasSelection;
        if (SelectionApplyMenuItem is not null) SelectionApplyMenuItem.IsEnabled = _floatingSelectionPixels is not null;
        if (SelectionCancelMenuItem is not null) SelectionCancelMenuItem.IsEnabled = _floatingSelectionPixels is not null;
        if (ObjectApplyMenuItem is not null) ObjectApplyMenuItem.IsEnabled = hasObject;
        if (ObjectCancelMenuItem is not null) ObjectCancelMenuItem.IsEnabled = hasObject;
        if (SelectAllButton is not null)
        {
            SelectAllButton.IsEnabled = PaintCanvas is not null && _imageWidth > 0 && _imageHeight > 0;
        }
        if (CancelSelectionButton is not null)
        {
            CancelSelectionButton.IsEnabled = _selection is not null || _floatingSelectionPixels is not null;
        }
        if (DeleteSelectionButton is not null)
        {
            DeleteSelectionButton.IsEnabled = hasSelection || _floatingSelectionPixels is not null;
        }
        if (TransparentSelectionMenuItem is not null) TransparentSelectionMenuItem.IsChecked = _transparentSelection;
        if (TransparentSelectionButton is not null) TransparentSelectionButton.IsChecked = _transparentSelection;
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

        PixelSurface[] transformed;
        try
        {
            transformed = _layers.Select(layer => transform(ToPixelSurface(layer.Surface))).ToArray();
        }
        catch (Exception exception)
        {
            _undo.Pop();
            UpdateHistoryState();
            _ = ShowInfoAsync("画像操作", $"画像操作を適用できませんでした。\n{exception.Message}");
            return;
        }
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

    private bool ApplyImageTransform(Func<PixelSurface, PixelSurface> transform, Func<SelectionRegion, int, int, SelectionRegion>? regionTransform = null)
    {
        ArgumentNullException.ThrowIfNull(transform);
        if (_selection is null)
        {
            ApplyDocumentTransform(transform);
            return true;
        }

        if (!EnsureFloatingSelection() || _floatingSelectionPixels is null)
        {
            return false;
        }

        PixelSurface transformed;
        try
        {
            transformed = transform(_floatingSelectionPixels);
            var transformedRegion = (_selection.Kind == SelectionRegionKind.Mask ? null : regionTransform)?.Invoke(_selection, transformed.Width, transformed.Height)
                ?? CreateSelectionRegionForSize(_selection, transformed.Width, transformed.Height);
            if (transformed.Width != transformedRegion.Bounds.Width || transformed.Height != transformedRegion.Bounds.Height)
            {
                transformed = ImageTransforms.ResizeNearestNeighbor(
                    transformed,
                    transformedRegion.Bounds.Width,
                    transformedRegion.Bounds.Height);
            }
            _selection = transformedRegion;
        }
        catch (Exception exception)
        {
            CancelFloatingSelection();
            _ = ShowInfoAsync("画像操作", $"画像操作を適用できませんでした。\n{exception.Message}");
            return false;
        }
        SetFloatingSelectionPixels(transformed);
        FinishEdit();
        UpdateSelectionStatus();
        return true;
    }

    private SelectionRegion CreateSelectionRegionForSize(SelectionRegion original, int width, int height)
    {
        width = Math.Min(width, _imageWidth);
        height = Math.Min(height, _imageHeight);
        var center = new Point(
            (original.Bounds.X + original.Bounds.Right) / 2d,
            (original.Bounds.Y + original.Bounds.Bottom) / 2d);
        var x = Math.Clamp((int)Math.Round(center.X - (width / 2d)), 0, _imageWidth - width);
        var y = Math.Clamp((int)Math.Round(center.Y - (height / 2d)), 0, _imageHeight - height);
        if (original.Kind is SelectionRegionKind.Rectangle or SelectionRegionKind.Mask)
        {
            return SelectionRegion.CreateRectangle(new PixelRect(x, y, width, height));
        }

        var bounds = original.Bounds;
        var points = original.Points.Select(point => new System.Numerics.Vector2(
            x + ((point.X - bounds.X) / Math.Max(1, bounds.Width) * width),
            y + ((point.Y - bounds.Y) / Math.Max(1, bounds.Height) * height)));
        return SelectionRegion.CreatePolygon(points);
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
        EnsureSurface(PaintCanvas);
        var sourceWidth = _floatingSelectionPixels?.Width ?? _selection?.Bounds.Width ?? _imageWidth;
        var sourceHeight = _floatingSelectionPixels?.Height ?? _selection?.Bounds.Height ?? _imageHeight;
        var mode = new ComboBox { Header = "単位", Width = 180 };
        ConfigureFieldAccessibility(mode, "サイズ変更の単位", "ピクセルまたはパーセントを選択します");
        mode.Items.Add("ピクセル");
        mode.Items.Add("パーセント");
        mode.SelectedIndex = 0;
        var widthBox = new NumberBox
        {
            Header = "水平方向 (ピクセル)",
            Minimum = 1,
            Maximum = 16384,
            Value = sourceWidth,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };
        ConfigureFieldAccessibility(widthBox, "幅", "新しい幅を入力します");
        var heightBox = new NumberBox
        {
            Header = "垂直方向 (ピクセル)",
            Minimum = 1,
            Maximum = 16384,
            Value = sourceHeight,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };
        ConfigureFieldAccessibility(heightBox, "高さ", "新しい高さを入力します");
        var maintainAspect = new CheckBox
        {
            Content = "縦横比を維持する（幅を基準）",
            IsChecked = true
        };
        ConfigureFieldAccessibility(maintainAspect, "縦横比を維持", "幅に合わせて高さを変更します");
        var content = new StackPanel { Spacing = 12, MinWidth = 360 };
        content.Children.Add(mode);
        content.Children.Add(widthBox);
        content.Children.Add(heightBox);
        content.Children.Add(maintainAspect);

        var updatingAspect = false;
        void UpdateHeaders()
        {
            var percent = mode.SelectedIndex == 1;
            widthBox.Header = percent ? "水平方向 (%)" : "水平方向 (ピクセル)";
            heightBox.Header = percent ? "垂直方向 (%)" : "垂直方向 (ピクセル)";
            widthBox.Minimum = percent ? 1 : 1;
            heightBox.Minimum = percent ? 1 : 1;
            widthBox.Maximum = percent ? 100_000 : 16_384;
            heightBox.Maximum = percent ? 100_000 : 16_384;
        }
        mode.SelectionChanged += (_, _) =>
        {
            if (!updatingAspect)
            {
                updatingAspect = true;
                widthBox.Value = mode.SelectedIndex == 1 ? 100 : sourceWidth;
                heightBox.Value = mode.SelectedIndex == 1 ? 100 : sourceHeight;
                updatingAspect = false;
            }
            UpdateHeaders();
        };
        widthBox.ValueChanged += (_, _) =>
        {
            if (updatingAspect || maintainAspect.IsChecked != true || mode.SelectedIndex != 0 || double.IsNaN(widthBox.Value)) return;
            updatingAspect = true;
            heightBox.Value = Math.Round(widthBox.Value * sourceHeight / sourceWidth, 2);
            updatingAspect = false;
        };
        UpdateHeaders();

        var dialog = new ContentDialog
        {
            XamlRoot = PaintCanvas.XamlRoot,
            Title = "サイズ変更",
            Content = content,
            PrimaryButtonText = "OK",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary
        };

        ConfigureDialogAccessibility(dialog);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (double.IsNaN(widthBox.Value) || double.IsNaN(heightBox.Value))
        {
            await ShowInfoAsync("サイズ変更", "幅と高さを数値で指定してください。");
            return;
        }

        if (double.IsInfinity(widthBox.Value) || double.IsInfinity(heightBox.Value))
        {
            await ShowInfoAsync("サイズ変更", "幅と高さは有限の数値で指定してください。");
            return;
        }

        var percentMode = mode.SelectedIndex == 1;
        var newWidth = percentMode
            ? (int)Math.Round(sourceWidth * widthBox.Value / 100d)
            : (int)Math.Round(widthBox.Value);
        var newHeight = maintainAspect.IsChecked == true
            ? (percentMode
                ? (int)Math.Round(sourceHeight * widthBox.Value / 100d)
                : (int)Math.Round((double)newWidth * sourceHeight / sourceWidth))
            : (percentMode
                ? (int)Math.Round(sourceHeight * heightBox.Value / 100d)
                : (int)Math.Round(heightBox.Value));
        if (newWidth is < 1 or > 16384 || newHeight is < 1 or > 16384)
        {
            await ShowInfoAsync("サイズ変更", "幅と高さは1～16384ピクセルで指定してください。");
            return;
        }
        if (newWidth == sourceWidth && newHeight == sourceHeight)
        {
            return;
        }

        ApplyImageTransform(
            surface => ImageTransforms.ResizeNearestNeighbor(surface, newWidth, newHeight),
            (region, width, height) => CreateSelectionRegionForSize(region, width, height));
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
        flyout.Items.Add(new MenuFlyoutSeparator());
        AddRotateItem(flyout, "任意の角度で回転…", "CustomRotate");
        AddRotateItem(flyout, "水平に傾斜…", "SkewHorizontal");
        AddRotateItem(flyout, "垂直に傾斜…", "SkewVertical");
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
                ApplyImageTransform(ImageTransforms.RotateClockwise, (region, _, _) => SelectionTransform.Rotate(region, 90, CanvasPixelBounds));
                break;
            case "CounterClockwise":
                ApplyImageTransform(ImageTransforms.RotateCounterClockwise, (region, _, _) => SelectionTransform.Rotate(region, -90, CanvasPixelBounds));
                break;
            case "Rotate180":
                ApplyImageTransform(ImageTransforms.Rotate180, (region, _, _) => SelectionTransform.Rotate(region, 180, CanvasPixelBounds));
                break;
            case "FlipHorizontal":
                ApplyImageTransform(ImageTransforms.FlipHorizontal);
                break;
            case "FlipVertical":
                ApplyImageTransform(ImageTransforms.FlipVertical);
                break;
            case "CustomRotate":
                _ = ShowAngleTransformDialogAsync(
                    "任意の角度で回転",
                    angle => source => ImageTransforms.RotateNearestNeighbor(source, angle),
                    allowFullCircle: true,
                    regionFactory: angle => (region, _, _) => SelectionTransform.Rotate(region, angle, CanvasPixelBounds));
                break;
            case "SkewHorizontal":
                _ = ShowAngleTransformDialogAsync(
                    "水平に傾斜",
                    angle => source => ImageTransforms.SkewHorizontal(source, angle),
                    regionFactory: angle => (region, _, _) => SelectionTransform.Skew(region, angle, horizontal: true, canvasBounds: CanvasPixelBounds));
                break;
            case "SkewVertical":
                _ = ShowAngleTransformDialogAsync(
                    "垂直に傾斜",
                    angle => source => ImageTransforms.SkewVertical(source, angle),
                    regionFactory: angle => (region, _, _) => SelectionTransform.Skew(region, angle, horizontal: false, canvasBounds: CanvasPixelBounds));
                break;
        }
    }

    private async Task ShowAngleTransformDialogAsync(
        string title,
        Func<float, Func<PixelSurface, PixelSurface>> transformFactory,
        bool allowFullCircle = false,
        Func<float, Func<SelectionRegion, int, int, SelectionRegion>>? regionFactory = null)
    {
        var angleBox = new NumberBox
        {
            Header = "角度 (度)",
            Minimum = allowFullCircle ? -3600 : -89.9,
            Maximum = allowFullCircle ? 3600 : 89.9,
            Value = 0,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };
        ConfigureFieldAccessibility(angleBox, "角度", "回転または傾斜の角度を度数で入力します");
        var dialog = new ContentDialog
        {
            XamlRoot = PaintCanvas.XamlRoot,
            Title = title,
            Content = angleBox,
            PrimaryButtonText = "OK",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary
        };
        ConfigureDialogAccessibility(dialog);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary ||
            double.IsNaN(angleBox.Value) || double.IsInfinity(angleBox.Value))
        {
            return;
        }

        var angle = (float)angleBox.Value;
        if (!allowFullCircle && MathF.Abs(angle) >= 89.9f)
        {
            await ShowInfoAsync(title, "傾斜角度は -89.9～89.9 度で指定してください。");
            return;
        }
        ApplyImageTransform(transformFactory(angle), regionFactory?.Invoke(angle));
    }

    private bool CropToSelection()
    {
        if (GetSelectionPixelRect() is not { } selection)
        {
            return false;
        }

        CommitFloatingSelection();
        ApplyActiveObject();
        ApplyDocumentTransform(surface => ImageTransforms.Crop(surface, selection));
        return true;
    }

    private bool DeleteSelection()
    {
        if (_selection is null && HasActiveEditableObject)
        {
            return CancelActiveObject();
        }
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
        try
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
        catch (Exception exception)
        {
            await ShowInfoAsync("コピー", $"クリップボードへコピーできませんでした。\n{exception.Message}");
        }
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
        try
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
        catch (Exception exception)
        {
            await ShowInfoAsync("貼り付け", $"クリップボードの画像を貼り付けできませんでした。\n{exception.Message}");
        }
    }

    private CancellationTokenSource? _backgroundRemovalCts;

    private async Task RemoveBackgroundAsync()
    {
        if (_backgroundRemovalCts is not null)
        {
            _backgroundRemovalCts.Cancel();
            return;
        }

        EnsureSurface(PaintCanvas);
        var cancellation = new CancellationTokenSource();
        _backgroundRemovalCts = cancellation;
        var previousStatus = PointerStatusText.Text;
        PointerStatusText.Text = "背景を削除しています（再度クリックでキャンセル）";
        try
        {
            if (_selection is not null)
            {
                if (_floatingSelectionPixels is null)
                {
                    var selectedCopies = _layers
                        .Select(layer => (Surface: ClonePixelSurface(ToPixelSurface(layer.Surface)), IsVisible: layer.IsVisible))
                        .ToArray();
                    await Task.Run(
                        () => BackgroundRemovalOperations.RemoveFromVisibleLayers(selectedCopies, _selection, 32, cancellation.Token),
                        cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    CaptureUndo();
                    for (var index = 0; index < _layers.Count; index++)
                    {
                        if (!selectedCopies[index].IsVisible) continue;
                        _layers[index].Surface.Dispose();
                        _layers[index].Surface = ToRenderTarget(selectedCopies[index].Surface);
                    }
                    _surface = _layers[_activeLayerIndex].Surface;
                    FinishEdit();
                    return;
                }

                var source = _floatingSelectionPixels is not null
                    ? ClonePixelSurface(_floatingSelectionPixels)
                    : SelectionOperations.Extract(ToPixelSurface(_surface!), _selection);
                var localRegion = SelectionRegion.CreateRectangle(new PixelRect(0, 0, source.Width, source.Height));
                var result = await Task.Run(
                    () => BackgroundRemovalOperations.RemoveFromEdges(source, localRegion, 32, cancellation.Token),
                    cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (_floatingSelectionPixels is null && !EnsureFloatingSelection())
                {
                    return;
                }
                SetFloatingSelectionPixels(result);
                FinishEdit();
                return;
            }

            var copies = _layers
                .Select(layer => (Surface: ClonePixelSurface(ToPixelSurface(layer.Surface)), IsVisible: layer.IsVisible))
                .ToArray();
            await Task.Run(
                () => BackgroundRemovalOperations.RemoveFromVisibleLayers(copies, cancellationToken: cancellation.Token),
                cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            CaptureUndo();
            for (var index = 0; index < _layers.Count; index++)
            {
                if (!copies[index].IsVisible) continue;
                _layers[index].Surface.Dispose();
                _layers[index].Surface = ToRenderTarget(copies[index].Surface);
            }
            _surface = _layers[_activeLayerIndex].Surface;
            FinishEdit();
        }
        catch (OperationCanceledException)
        {
            // Canceled work only touched private copies, so the document remains unchanged.
        }
        catch (Exception exception)
        {
            await ShowInfoAsync("背景の削除", $"背景を削除できませんでした。\n{exception.Message}");
        }
        finally
        {
            PointerStatusText.Text = previousStatus;
            cancellation.Dispose();
            _backgroundRemovalCts = null;
        }
    }

    private async void Cut_Click(object sender, RoutedEventArgs e) => await CutSelectionAsync();
    private async void Copy_Click(object sender, RoutedEventArgs e) => await CopySelectionAsync();
    private async void Paste_Click(object sender, RoutedEventArgs e) => await PasteAsync();
    private void Crop_Click(object sender, RoutedEventArgs e) => CropToSelection();
    private void Delete_Click(object sender, RoutedEventArgs e) => DeleteSelection();
    private void Invert_Click(object sender, RoutedEventArgs e) => InvertSelectionColors();
    private async void BackgroundRemoval_Click(object sender, RoutedEventArgs e) => await RemoveBackgroundAsync();
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomSlider.Value = Math.Min(ZoomSlider.Maximum, ZoomSlider.Value + 25);
}
