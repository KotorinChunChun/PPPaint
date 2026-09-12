using ChunPaint.Core;
using ChunPaint.Editing;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;

namespace ChunPaint;

public sealed partial class MainWindow
{
    private PixelSurface? _backgroundOriginal;
    private string? _backgroundDocumentId;
    private string? _backgroundLayerId;

    private PixelSurface GetAdjustmentSource(LayerState layer)
    {
        using var target = new CanvasRenderTarget(PaintCanvas, _imageWidth, _imageHeight, 96);
        using (var drawing = target.CreateDrawingSession())
        {
            drawing.Clear(Microsoft.UI.Colors.Transparent);
            drawing.DrawImage(layer.Surface);
            DrawEditableObjects(drawing, layer);
        }
        return ToPixelSurface(target);
    }

    private bool CanStartAdjustment()
    {
        if (_documentBusy || !CanEditActiveLayer) return false;
        if (_floatingSelectionPixels is null) return true;
        _ = ShowInfoAsync("画像加工", "移動中の選択範囲を適用またはキャンセルしてから画像加工を実行してください。");
        return false;
    }

    private void CommitAdjustment(LayerState layer, PixelSurface result)
    {
        var replacement = ToRenderTarget(result);
        CaptureUndo();
        _objects.RemoveAll(item => item.LayerId == layer.Id);
        if (!_objects.Any(item => item.Id == _activeObjectId)) { _activeObjectId = null; _activeObjectOriginal = null; }
        layer.Surface.Dispose(); layer.Surface = replacement; _surface = replacement;
        RefreshLayersPanel(); FinishEdit();
    }

    private static NumberBox AdjustmentNumber(string label, double min, double max, double value) => new()
    { Header = label, Minimum = min, Maximum = max, Value = value, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };

    private async Task<bool> ShowAdjustmentPreviewAsync(string title, LayerState layer, PixelSurface source,
        StackPanel controls, Func<CancellationToken, Task<PixelSurface>> calculate, Action? applied = null, Action<PixelSurface>? commit = null)
    {
        using var cancellation = new CancellationTokenSource();
        _documentBusy = true; RefreshDocumentTabs();
        var preview = new Image { Width = 440, Height = 260, Stretch = Stretch.Uniform };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var update = new Button { Content = "プレビューを更新" };
        var panel = new StackPanel { Spacing = 10, MinWidth = 440 };
        panel.Children.Add(new TextBlock { Text = "対象: アクティブレイヤー（選択範囲がある場合は範囲内）", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(controls); panel.Children.Add(update); panel.Children.Add(preview); panel.Children.Add(status);
        var dialog = new ContentDialog { XamlRoot = PaintCanvas.XamlRoot, Title = title, Content = new ScrollViewer { Content = panel, MaxHeight = 640 }, PrimaryButtonText = "適用", CloseButtonText = "キャンセル", IsPrimaryButtonEnabled = false };
        PixelSurface? calculated = null;
        var calculating = false;
        async Task RefreshPreview()
        {
            if (calculating) return;
            calculating = true; update.IsEnabled = false; dialog.IsPrimaryButtonEnabled = false;
            try
            {
                // 設定を読み取ってからバックグラウンドで原本を加工する。
                calculated = await calculate(cancellation.Token);
                var scale = Math.Min(1d, Math.Min(440d / calculated.Width, 260d / calculated.Height));
                var small = await Task.Run(() => ImageResampling.Resize(calculated, Math.Max(1, (int)(calculated.Width * scale)), Math.Max(1, (int)(calculated.Height * scale)), ResamplingMode.Area, cancellation.Token));
                var bitmap = new WriteableBitmap(small.Width, small.Height);
                using (var stream = bitmap.PixelBuffer.AsStream()) { var bytes = PixelAlpha.StraightToPremultiplied(small.Snapshot()); await stream.WriteAsync(bytes); }
                bitmap.Invalidate(); preview.Source = bitmap; status.Text = "設定変更後はプレビューを更新してください。適用時にも現在の設定で再計算します。";
                dialog.IsPrimaryButtonEnabled = true;
            }
            catch (OperationCanceledException) { calculated = null; dialog.Hide(); }
            catch (Exception ex) { calculated = null; status.Text = ex.Message; }
            finally { calculating = false; update.IsEnabled = true; }
        }
        dialog.Closing += (_, args) => { if (calculating && !cancellation.IsCancellationRequested) { args.Cancel = true; cancellation.Cancel(); } };
        update.Click += async (_, _) => await RefreshPreview();
        try
        {
            dialog.Opened += async (_, _) => await RefreshPreview();
            ConfigureDialogAccessibility(dialog);
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return false;
            calculated = await calculate(cancellation.Token);
            if (commit is null) CommitAdjustment(layer, calculated); else commit(calculated); applied?.Invoke(); return true;
        }
        catch (Exception ex) { await ShowInfoAsync(title, $"画像加工を適用できませんでした。\n{ex.Message}"); return false; }
        finally { _documentBusy = false; RefreshDocumentTabs(); }
    }

    private async void ImageAdjustments_Click(object sender, RoutedEventArgs e)
    {
        if (!CanStartAdjustment()) return;
        var layer = _layers[_activeLayerIndex]; var source = GetAdjustmentSource(layer); var region = _selection;
        var tag = (sender as FrameworkElement)?.Tag?.ToString() ?? "Adjust";
        var controls = new StackPanel { Spacing = 8 };
        var amount = AdjustmentNumber(tag == "Mosaic" ? "ブロックサイズ (px)" : "ぼかし半径 (px)", 1, tag == "Mosaic" ? 256 : 64, tag == "Mosaic" ? 12 : 4);
        var brightness = AdjustmentNumber("明るさ (-100～100)", -100, 100, 0);
        var contrast = AdjustmentNumber("コントラスト (-100～100)", -100, 100, 0);
        var saturation = AdjustmentNumber("彩度 (0～200%)", 0, 200, 100);
        if (tag == "Adjust") { controls.Children.Add(brightness); controls.Children.Add(contrast); controls.Children.Add(saturation); }
        else if (tag != "Blackout") controls.Children.Add(amount);
        var title = tag switch { "Mosaic" => "モザイク", "Blur" => "ぼかし", "Blackout" => "黒塗り", _ => "色調整" };
        await ShowAdjustmentPreviewAsync(title, layer, source, controls, token => { var size = checked((int)amount.Value); var b = brightness.Value / 100; var c = contrast.Value / 100; var saturationValue = saturation.Value / 100; return Task.Run(() => tag switch
        {
            "Mosaic" => ImageAdjustments.Mosaic(source, size, region, token),
            "Blur" => ImageAdjustments.Blur(source, size, region, token),
            "Blackout" => ImageAdjustments.Blackout(source, region, token),
            _ => ImageAdjustments.Adjust(source, b, c, saturationValue, region, token)
        }); });
    }

    private async Task ShowBackgroundRemovalPreviewAsync()
    {
        if (!CanStartAdjustment()) return;
        var layer = _layers[_activeLayerIndex]; var source = GetAdjustmentSource(layer); var region = _selection;
        var targets = _layers.Where(item => item.IsVisible && !item.IsLocked).ToArray();
        var originals = targets.ToDictionary(item => item.Id, GetAdjustmentSource);
        var tolerance = AdjustmentNumber("背景色の許容差 (0～255)", 0, 255, 32);
        var scope = new ComboBox { Header = "対象" };
        scope.Items.Add("可視レイヤー全体（ロックを除く）"); scope.Items.Add("アクティブレイヤー"); scope.SelectedIndex = 0;
        var controls = new StackPanel { Spacing = 8 }; controls.Children.Add(scope); controls.Children.Add(tolerance);
        Dictionary<string, PixelSurface>? results = null;
        await ShowAdjustmentPreviewAsync("背景の削除（プレビューはアクティブレイヤー）", layer, source, controls,
            token =>
            {
                var selected = scope.SelectedIndex == 0 ? targets : new[] { layer };
                var toleranceValue = checked((byte)tolerance.Value);
                return Task.Run(() => { results = selected.ToDictionary(item => item.Id, item => BackgroundRemovalOperations.RemoveFromEdges(
                    originals.TryGetValue(item.Id, out var pixels) ? pixels : source, region, toleranceValue, token));
                return results.TryGetValue(layer.Id, out var active) ? active : source; });
            },
            () => { if (results!.ContainsKey(layer.Id)) { _backgroundOriginal = source; _backgroundDocumentId = _activeDocument?.Id; _backgroundLayerId = layer.Id; } },
            _ =>
            {
                var replacements = new Dictionary<string, CanvasRenderTarget>();
                try { foreach (var item in results!) replacements[item.Key] = ToRenderTarget(item.Value); }
                catch { foreach (var item in replacements.Values) item.Dispose(); throw; }
                CaptureUndo();
                foreach (var item in _layers.Where(item => replacements.ContainsKey(item.Id)))
                {
                    _objects.RemoveAll(obj => obj.LayerId == item.Id);
                    item.Surface.Dispose(); item.Surface = replacements[item.Id];
                }
                if (!_objects.Any(item => item.Id == _activeObjectId)) { _activeObjectId = null; _activeObjectOriginal = null; }
                _surface = layer.Surface; RefreshLayersPanel(); FinishEdit();
            });
    }
    private async void BackgroundRefine_Click(object sender, RoutedEventArgs e)
    {
        if (!CanStartAdjustment()) return;
        var layer = _layers[_activeLayerIndex]; var original = _backgroundOriginal;
        if (original is null || _backgroundDocumentId != _activeDocument?.Id || _backgroundLayerId != layer.Id || original.Width != _imageWidth || original.Height != _imageHeight)
        { await ShowInfoAsync("背景の調整", "このレイヤーで背景を削除してから調整してください。原画はこの起動中に保持されます。"); return; }
        var source = GetAdjustmentSource(layer); var region = _selection;
        var tag = (sender as FrameworkElement)?.Tag?.ToString() ?? "Refine";
        if (tag != "Refine" && region is null) { await ShowInfoAsync("背景の手直し", "復元または削除したい範囲を選択してください。"); return; }
        var expansion = AdjustmentNumber("残す範囲の拡張 (+)・収縮 (-) px", -32, 32, 0);
        var feather = AdjustmentNumber("境界ぼかし (px)", 0, 64, 0);
        var controls = new StackPanel { Spacing = 8 };
        if (tag == "Refine") { controls.Children.Add(expansion); controls.Children.Add(feather); }
        await ShowAdjustmentPreviewAsync("背景の調整", layer, source, controls, token =>
        {
            var expandValue = checked((int)expansion.Value); var featherValue = checked((int)feather.Value);
            return Task.Run(() => { if (tag != "Refine") return BackgroundMaskOperations.Retouch(original, source, region!, tag == "Restore", token);
            var refined = BackgroundMaskOperations.Refine(original, source, expandValue, featherValue, token);
            if (region is not null)
                for (var y = 0; y < source.Height; y++) for (var x = 0; x < source.Width; x++) if (!region.ContainsPixel(x, y)) refined[x, y] = source[x, y];
            return refined; });
        });
    }
}
