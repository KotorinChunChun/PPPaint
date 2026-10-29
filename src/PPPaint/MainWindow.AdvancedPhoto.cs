using PPPaint.Core;
using PPPaint.Editing;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System.Numerics;
using Windows.Foundation;

namespace PPPaint;

public sealed partial class MainWindow
{
    private (StackPanel Controls, Func<PhotoAdjustmentSettings> Read) CreatePhotoControls(PixelSurface source, PhotoAdjustmentSettings settings)
    {
        var controls = new StackPanel { Spacing = 8 };
        controls.Children.Add(new TextBlock { Text = "輝度ヒストグラム（透明画素を除外）" });
        var histogram = PhotoAdjustments.Histogram(source); var maximum = Math.Max(1, histogram.Max());
        var graph = new Canvas { Width = 400, Height = 80, Background = new SolidColorBrush(Microsoft.UI.Colors.Black) };
        var line = new Polyline { Stroke = new SolidColorBrush(Microsoft.UI.Colors.LightSkyBlue), StrokeThickness = 1.5 };
        for (var i = 0; i < histogram.Length; i++) line.Points.Add(new Point(i * 399d / 255, 79 - histogram[i] * 78d / maximum));
        graph.Children.Add(line); controls.Children.Add(graph);
        var black = AdjustmentNumber("黒点 (0～0.99)", 0, .99, settings.BlackPoint);
        var white = AdjustmentNumber("白点 (0.01～1)", .01, 1, settings.WhitePoint);
        var gamma = AdjustmentNumber("ガンマ (0.1～10)", .1, 10, settings.Gamma);
        var temperature = AdjustmentNumber("色温度 (-1～1)", -1, 1, settings.Temperature);
        var tint = AdjustmentNumber("色かぶり (-1～1)", -1, 1, settings.Tint);
        var shadows = AdjustmentNumber("カーブ: シャドウ (-0.5～0.5)", -.5, .5, settings.Shadows);
        var midtones = AdjustmentNumber("カーブ: 中間調 (-0.5～0.5)", -.5, .5, settings.Midtones);
        var highlights = AdjustmentNumber("カーブ: ハイライト (-0.5～0.5)", -.5, .5, settings.Highlights);
        foreach (var field in new[] { black, white, gamma, temperature, tint, shadows, midtones, highlights }) controls.Children.Add(field);
        PhotoAdjustmentSettings Read()
        {
            var result = new PhotoAdjustmentSettings(black.Value, white.Value, gamma.Value, temperature.Value, tint.Value, shadows.Value, midtones.Value, highlights.Value);
            PhotoAdjustments.Validate(result); return result;
        }
        return (controls, Read);
    }

    private async void AdvancedPhoto_Click(object sender, RoutedEventArgs e)
    {
        if (!CanStartAdjustment()) return;
        try
        {
            var layer = _layers[_activeLayerIndex]; var source = GetAdjustmentSource(layer); var region = _selection;
            var fields = CreatePhotoControls(source, new());
            await ShowAdjustmentPreviewAsync("写真調整・レベル・トーンカーブ", layer, source, fields.Controls,
                token => { var settings = fields.Read(); return Task.Run(() => PhotoAdjustments.Apply(source, settings, region, token), token); });
        }
        catch (Exception ex) { await ShowInfoAsync("写真調整", ex.Message); }
    }

    private async void Gradient_Click(object sender, RoutedEventArgs e)
    {
        if (!CanStartAdjustment()) return;
        try
        {
            var layer = _layers[_activeLayerIndex]; var source = GetAdjustmentSource(layer); var region = _selection;
            var controls = new StackPanel { Spacing = 8 };
            var radial = new CheckBox { Content = "放射状（オフは線形）" };
            var transparent = new CheckBox { Content = "終点を透明にする" };
            var sx = AdjustmentNumber("始点 X", 0, source.Width - 1, 0); var sy = AdjustmentNumber("始点 Y", 0, source.Height - 1, 0);
            var ex = AdjustmentNumber("終点 X", 0, Math.Max(1, source.Width - 1), Math.Max(1, source.Width - 1)); var ey = AdjustmentNumber("終点 Y", 0, source.Height - 1, 0);
            ToolTipService.SetToolTip(controls, "始点色は主色、終点色は副色を使います。");
            foreach (var item in new FrameworkElement[] { radial, transparent, sx, sy, ex, ey }) controls.Children.Add(item);
            var first = ToCoreColor(_primaryColor); var last = ToCoreColor(_secondaryColor);
            await ShowAdjustmentPreviewAsync("グラデーション", layer, source, controls, token =>
            {
                var start = new Vector2((float)sx.Value, (float)sy.Value); var end = new Vector2((float)ex.Value, (float)ey.Value);
                var endColor = transparent.IsChecked == true ? new Rgba32(last.R, last.G, last.B, 0) : last; var circular = radial.IsChecked == true;
                return Task.Run(() => GradientOperations.Apply(source, start, end, first, endColor, circular, region, token), token);
            });
        }
        catch (Exception ex) { await ShowInfoAsync("グラデーション", ex.Message); }
    }

    private async void PrecisionCrop_Click(object sender, RoutedEventArgs e)
    {
        if (_documentBusy || _layers.Count == 0) return;
        if (_floatingSelectionPixels is not null) { await ShowInfoAsync("精密切り抜き", "移動中の選択を適用または取消してから実行してください。"); return; }
        if (_layers.Any(layer => layer.IsLocked)) { await ShowInfoAsync("精密切り抜き", "ロックしたレイヤーを解除してから画像全体を変更してください。"); return; }
        try
        {
            using var composite = CreateStackComposite(_layers.ToArray()); var source = ToPixelSurface(composite); var layer = _layers[_activeLayerIndex];
            var controls = new StackPanel { Spacing = 8 };
            ToolTipService.SetToolTip(controls, "全レイヤーを同じ範囲で切り抜きます。回転後の画像を基準に範囲を指定してください。");
            var ratio = new ComboBox { Header = "縦横比" }; foreach (var value in new[] { "自由", "1:1", "4:3", "16:9" }) ratio.Items.Add(value); ratio.SelectedIndex = 0;
            var x = AdjustmentNumber("左 X", 0, 100000, _selection?.Bounds.X ?? 0); var y = AdjustmentNumber("上 Y", 0, 100000, _selection?.Bounds.Y ?? 0);
            var width = AdjustmentNumber("幅", 1, 16384, _selection?.Bounds.Width ?? source.Width); var height = AdjustmentNumber("高さ", 1, 16384, _selection?.Bounds.Height ?? source.Height);
            var outputWidth = AdjustmentNumber("出力幅", 1, 16384, width.Value); var outputHeight = AdjustmentNumber("出力高さ", 1, 16384, height.Value);
            var angle = AdjustmentNumber("傾き補正角度 (-45～45度)", -45, 45, 0);
            foreach (var item in new FrameworkElement[] { ratio, x, y, width, height, outputWidth, outputHeight, angle }) controls.Children.Add(item);
            Func<PixelSurface, PixelSurface>? operation = null;
            await ShowAdjustmentPreviewAsync("精密切り抜き・傾き補正", layer, source, controls, token =>
            {
                var factor = ratio.SelectedIndex switch { 1 => 1d, 2 => 4d / 3, 3 => 16d / 9, _ => 0d };
                var w = checked((int)width.Value); var h = factor == 0 ? checked((int)height.Value) : checked((int)Math.Round(w / factor));
                var ow = checked((int)outputWidth.Value); var oh = factor == 0 ? checked((int)outputHeight.Value) : checked((int)Math.Round(ow / factor));
                var rect = new PixelRect(checked((int)x.Value), checked((int)y.Value), w, h); var degrees = (float)angle.Value;
                if (!float.IsFinite(degrees) || degrees < -45 || degrees > 45) throw new ArgumentOutOfRangeException("角度");
                operation = pixels =>
                {
                    token.ThrowIfCancellationRequested();
                    var rotated = degrees == 0 ? pixels : ImageTransforms.RotateNearestNeighbor(pixels, degrees);
                    var cropped = ImageTransforms.Crop(rotated, rect);
                    return ImageResampling.Resize(cropped, ow, oh, ResamplingMode.Area, token);
                };
                return Task.Run(() => operation(source), token);
            }, commit: _ =>
            {
                // モーダルの確定後、この同期処理だけ既存の全体変換ガードを解除する。
                var busy = _documentBusy; _documentBusy = false;
                try { ApplyDocumentTransform(operation!); }
                finally { _documentBusy = busy; }
            });
        }
        catch (Exception ex) { await ShowInfoAsync("精密切り抜き", ex.Message); }
    }

    private async void AdjustmentLayer_Click(object sender, RoutedEventArgs e)
    {
        if (_documentBusy || _layers.Count == 0 || _floatingSelectionPixels is not null) return;
        var edit = string.Equals((sender as FrameworkElement)?.Tag?.ToString(), "Edit", StringComparison.Ordinal);
        var layer = _layers[_activeLayerIndex];
        if (edit && (layer.IsLocked || layer.Adjustment is null)) { await ShowInfoAsync("調整レイヤー", "編集する調整レイヤーを選び、ロックを解除してください。"); return; }
        try
        {
            using var composite = CreateStackComposite(edit ? _layers.Take(_activeLayerIndex).ToArray() : _layers.ToArray());
            var source = ToPixelSurface(composite); var fields = CreatePhotoControls(source, edit ? layer.Adjustment! : new());
            ToolTipService.SetToolTip(fields.Controls, edit ? "この調整レイヤーと下層をプレビューします。上層は含みません。" : "最上位に調整レイヤーを追加します。元のレイヤーは保持されます。");
            var mask = edit ? layer.Mask?.ToArray() : null; var opacity = edit ? layer.Opacity : 1f; var visible = !edit || layer.IsVisible;
            var blendMode = edit ? layer.BlendMode : LayerBlendMode.Normal;
            PhotoAdjustmentSettings? selected = null;
            await ShowAdjustmentPreviewAsync(edit ? "調整レイヤーの再編集" : "調整レイヤーを追加", layer, source, fields.Controls, token =>
            {
                selected = fields.Read(); var settings = selected;
                return Task.Run(() =>
                {
                    var adjusted = PhotoAdjustments.Apply(source, settings, token: token);
                    for (var y = 0; y < source.Height; y++)
                    {
                        token.ThrowIfCancellationRequested();
                        for (var x = 0; x < source.Width; x++)
                        {
                            var amount = visible ? opacity * (mask is null ? 1d : mask[y * source.Width + x] / 255d) : 0;
                            var before = source[x, y]; var after = adjusted[x, y];
                            byte Mix(byte a, byte b)
                            {
                                var cb = a / 255d; var cf = b / 255d;
                                var blended = blendMode switch
                                {
                                    LayerBlendMode.Multiply => cb * cf,
                                    LayerBlendMode.Screen => cb + cf - cb * cf,
                                    LayerBlendMode.Overlay => cb <= .5 ? 2 * cb * cf : 1 - 2 * (1 - cb) * (1 - cf),
                                    _ => cf
                                };
                                return (byte)Math.Clamp(Math.Round((cb + (blended - cb) * amount) * 255), 0, 255);
                            }
                            adjusted[x, y] = new(Mix(before.R, after.R), Mix(before.G, after.G), Mix(before.B, after.B), before.A);
                        }
                    }
                    return adjusted;
                }, token);
            }, commit: _ =>
            {
                if (edit) { CaptureUndo("調整レイヤーの編集"); layer.Adjustment = selected; }
                else
                {
                    var surface = new CanvasRenderTarget(PaintCanvas, _imageWidth, _imageHeight, 96);
                    try
                    {
                        using (var drawing = surface.CreateDrawingSession()) drawing.Clear(Microsoft.UI.Colors.Transparent);
                        var added = new LayerState("写真調整", surface) { Adjustment = selected };
                        CaptureUndo("調整レイヤーの追加"); _layers.Add(added); _activeLayerIndex = _layers.Count - 1; _surface = surface;
                    }
                    catch { surface.Dispose(); throw; }
                }
                RefreshLayersPanel(); FinishEdit();
            });
        }
        catch (Exception ex) { await ShowInfoAsync("調整レイヤー", ex.Message); }
    }
}
