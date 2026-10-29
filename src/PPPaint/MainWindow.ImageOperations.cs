using PPPaint.Core;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI;

namespace PPPaint;

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
        result.Restore(PixelAlpha.PremultipliedToStraight(source.GetPixelBytes()));
        return result;
    }

    private CanvasRenderTarget ToRenderTarget(PixelSurface source)
    {
        var result = new CanvasRenderTarget(PaintCanvas, source.Width, source.Height, 96);
        result.SetPixelBytes(PixelAlpha.StraightToPremultiplied(source.Snapshot()));
        return result;
    }

    private bool ApplyDocumentTransform(Func<PixelSurface, PixelSurface> transform)
    {
        if (_documentBusy || _layers.Any(layer => layer.IsLocked)) { _ = ShowInfoAsync("画像操作", "ロックしたレイヤーがあるため画像全体を変更できません。"); return false; }
        ArgumentNullException.ThrowIfNull(transform);
        EnsureSurface(PaintCanvas);
        var replacements = new List<CanvasRenderTarget>();
        var masks = new List<byte[]?>();
        try
        {
            var undo = CaptureDocumentSnapshot();
            if (_floatingSelectionPixels is not null && _selection is not null)
            {
                var original = ToPixelSurface(_layers[_activeLayerIndex].Surface);
                SelectionOperations.Apply(original, _selection, _floatingSelectionPixels);
                var savedLayers = undo.Layers.ToArray();
                savedLayers[_activeLayerIndex] = savedLayers[_activeLayerIndex] with { PixelBlocks = PixelBlockSnapshot.Capture(PixelAlpha.StraightToPremultiplied(original.Snapshot())) };
                undo = undo with { Layers = savedLayers };
            }
            var width = 0; var height = 0;
            for (var index = 0; index < _layers.Count; index++)
            {
                var layer = _layers[index];
                using var content = CreateLayerContent(layer);
                var input = ToPixelSurface(content);
                if (index == _activeLayerIndex && _floatingSelectionPixels is not null && _selection is not null)
                    SelectionOperations.Apply(input, _selection, _floatingSelectionPixels);
                var changed = transform(input);
                if (index == 0) { width = changed.Width; height = changed.Height; }
                else if (changed.Width != width || changed.Height != height) throw new InvalidOperationException("変換後のレイヤー寸法が一致しません。");
                byte[]? mask = null;
                if (layer.Mask is not null)
                {
                    var changedMask = transform(MaskSurface(layer.Mask, _imageWidth, _imageHeight));
                    if (changedMask.Width != width || changedMask.Height != height) throw new InvalidOperationException("変換後のマスク寸法が一致しません。");
                    mask = SurfaceMask(changedMask);
                }
                masks.Add(mask); replacements.Add(ToRenderTarget(changed));
            }
            // すべての画像・マスク・GPU資源を準備するまで原画と履歴を変更しない。
            RememberHistory(undo, "画像全体の変換"); _undo.Push(undo); _redo.Clear();
            for (var index = 0; index < _layers.Count; index++)
            {
                var old = _layers[index].Surface; _layers[index].Surface = replacements[index]; _layers[index].Mask = masks[index]; old.Dispose();
            }
            replacements.Clear();
            _objects.Clear(); _activeObjectId = null; _activeObjectOriginal = null;
            ClearFloatingSelectionState(); _selection = null;
            _imageWidth = width; _imageHeight = height;
            _activeLayerIndex = Math.Clamp(_activeLayerIndex, 0, _layers.Count - 1); _surface = _layers[_activeLayerIndex].Surface;
            ApplyCanvasSize(); TrimDocumentHistory(); RefreshLayersPanel(); FinishEdit();
            return true;
        }
        catch (Exception exception)
        {
            _ = ShowInfoAsync("画像操作", $"画像操作を適用できませんでした。\n{exception.Message}");
            return false;
        }
        finally { foreach (var target in replacements) target.Dispose(); }
    }

    private bool ApplyImageTransform(Func<PixelSurface, PixelSurface> transform, Func<SelectionRegion, int, int, SelectionRegion>? regionTransform = null)
    {
        ArgumentNullException.ThrowIfNull(transform);
        if (_documentBusy) return false;
        if (_selection is null) return ApplyDocumentTransform(transform);
        if (!CanEditActiveLayer) return false;
        var originalRegion = _selection;
        CanvasRenderTarget? replacement = null;
        try
        {
            var layer = _layers[_activeLayerIndex];
            PixelSurface? destination = null;
            PixelSurface selected;
            if (_floatingSelectionPixels is not null) selected = _floatingSelectionPixels;
            else
            {
                using var content = CreateLayerContent(layer); destination = ToPixelSurface(content);
                selected = SelectionOperations.Extract(destination, originalRegion, _transparentSelection, ToCoreColor(_secondaryColor));
            }
            var changed = transform(selected);
            var changedRegion = (originalRegion.Kind == SelectionRegionKind.Mask ? null : regionTransform)?.Invoke(originalRegion, changed.Width, changed.Height)
                ?? CreateSelectionRegionForSize(originalRegion, changed.Width, changed.Height);
            if (changed.Width != changedRegion.Bounds.Width || changed.Height != changedRegion.Bounds.Height)
                changed = ImageResampling.Resize(changed, changedRegion.Bounds.Width, changedRegion.Bounds.Height, ResamplingMode.Bilinear);
            if (destination is null)
            {
                replacement = ToRenderTarget(changed);
                _floatingSelectionRenderTarget?.Dispose(); _floatingSelectionRenderTarget = replacement; replacement = null;
                _floatingSelectionPixels = changed; _selection = changedRegion;
            }
            else
            {
                // 選択内容だけを変換する。レイヤーマスクは文書座標に固定して他レイヤーを保持する。
                SelectionOperations.Clear(destination, originalRegion, _activeLayerIndex == 0 ? ToCoreColor(_secondaryColor) : Rgba32.Transparent);
                SelectionOperations.Apply(destination, changedRegion, changed);
                replacement = ToRenderTarget(destination);
                CaptureUndo("選択範囲の変換");
                layer.Surface.Dispose(); layer.Surface = replacement; replacement = null; _surface = layer.Surface;
                _objects.RemoveAll(item => item.LayerId == layer.Id); _activeObjectId = null; _activeObjectOriginal = null;
                _selection = changedRegion;
            }
            FinishEdit(); UpdateSelectionStatus(); return true;
        }
        catch (Exception exception) { _ = ShowInfoAsync("画像操作", $"画像操作を適用できませんでした。\n{exception.Message}"); return false; }
        finally { replacement?.Dispose(); }
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
        if (_documentBusy || !CanEditActiveLayer) return;
        ArgumentNullException.ThrowIfNull(transform);
        EnsureSurface(PaintCanvas);
        CommitFloatingSelection();
        CaptureUndo();
        var layer = _layers[_activeLayerIndex];
        RasterizeObjectsIntoLayer(layer);
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
        var interpolation = new ComboBox { Header = "補間方式" };
        interpolation.Items.Add("最近傍（ドット絵）");
        interpolation.Items.Add("双線形（滑らか）");
        interpolation.Items.Add("面積平均（縮小向け）");
        interpolation.SelectedIndex = 1;
        content.Children.Add(interpolation);
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
        if (await dialog.ShowManagedAsync() != ContentDialogResult.Primary)
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
            surface => ImageResampling.Resize(surface, newWidth, newHeight, (ResamplingMode)interpolation.SelectedIndex),
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
        if (await dialog.ShowManagedAsync() != ContentDialogResult.Primary ||
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
        if (_layers.Any(layer => layer.IsLocked)) { _ = ShowInfoAsync("切り抜き", "ロックしたレイヤーがあるため切り抜きできません。"); return false; }
        if (_documentBusy || !CanEditActiveLayer) return false;
        if (GetSelectionPixelRect() is not { } selection)
        {
            return false;
        }

        return ApplyDocumentTransform(surface => ImageTransforms.Crop(surface, selection));
    }

    private bool DeleteSelection()
    {
        if (_documentBusy || !CanEditActiveLayer) return false;
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
        _surface.SetPixelBytes(PixelAlpha.StraightToPremultiplied(pixels.Snapshot()));
        _selection = null;
        FinishEdit();
        return true;
    }

    private bool InvertSelectionColors()
    {
        if (_documentBusy || !CanEditActiveLayer) return false;
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
        _surface.SetPixelBytes(PixelAlpha.StraightToPremultiplied(pixels.Snapshot()));
        FinishEdit();
        return true;
    }

    private async Task<bool> CopySelectionAsync(bool visibleLayers = false, bool retainBusy = false)
    {
        if (_documentBusy || _selection is null) return false;
        _documentBusy = true; RefreshDocumentTabs();
        try
        {
            EnsureSurface(PaintCanvas);
            var source = CaptureCopySource(visibleLayers);
            var selected = SelectionOperations.Extract(source, _selection, _transparentSelection, ToCoreColor(_secondaryColor));
            using var selectedRenderTarget = ToRenderTarget(selected);
            var stream = new InMemoryRandomAccessStream();
            try
            {
                await selectedRenderTarget.SaveAsync(stream, CanvasBitmapFileFormat.Png);
                stream.Seek(0);
                var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
                package.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
                Clipboard.SetContent(package);
                Clipboard.Flush();
                _clipboardStream?.Dispose();
                _clipboardStream = stream;
            }
            catch { stream.Dispose(); throw; }
            return true;
        }
        catch (Exception exception)
        {
            await ShowInfoAsync("コピー", $"クリップボードへコピーできませんでした。\n{exception.Message}");
            return false;
        }
        finally
        {
            if (!retainBusy) { _documentBusy = false; RefreshDocumentTabs(); }
        }
    }

    private async Task CutSelectionAsync()
    {
        if (_documentBusy || !CanEditActiveLayer || GetSelectionPixelRect() is null) return;
        try
        {
            if (!await CopySelectionAsync(retainBusy: true)) return;
            // awaitを挟まず削除するため、文書切替が割り込むことはない。
            _documentBusy = false;
            DeleteSelection();
        }
        finally { _documentBusy = false; RefreshDocumentTabs(); }
    }
    private async Task PasteAsync()
    {
        if (_documentBusy || !CanEditActiveLayer) return;
        _documentBusy = true; RefreshDocumentTabs();
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
            _activeObjectId = null;
            _activeObjectOriginal = null;
            _selection = SelectionRegion.CreateRectangle(new PixelRect(0, 0, pastedWidth, pastedHeight));
            RefreshLayersPanel();
            _selectionMode = SelectionMode.Rectangle;
            ActivateSelectionTool();
            FinishEdit();
        }
        catch (Exception exception)
        {
            await ShowInfoAsync("貼り付け", $"クリップボードの画像を貼り付けできませんでした。\n{exception.Message}");
        }
        finally { _documentBusy = false; RefreshDocumentTabs(); }
    }

    private Task RemoveBackgroundAsync() => ShowBackgroundRemovalPreviewAsync();

    private async void Cut_Click(object sender, RoutedEventArgs e) => await CutSelectionAsync();
    private async void Copy_Click(object sender, RoutedEventArgs e) => await CopySelectionAsync();
    private async void Paste_Click(object sender, RoutedEventArgs e) => await PasteAsync();
    private void Crop_Click(object sender, RoutedEventArgs e) => CropToSelection();
    private void Delete_Click(object sender, RoutedEventArgs e) => DeleteSelection();
    private void Invert_Click(object sender, RoutedEventArgs e) => InvertSelectionColors();
    private async void BackgroundRemoval_Click(object sender, RoutedEventArgs e) => await RemoveBackgroundAsync();
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomSlider.Value = Math.Min(ZoomSlider.Maximum, ZoomSlider.Value + 25);
}
