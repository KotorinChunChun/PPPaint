using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PPPaint.Core;
using PPPaint.Imaging;
using Windows.Foundation;

namespace PPPaint;

public sealed partial class EditorWindow
{
    private ScreenshotCanvasResize? _canvasResizePreview;
    private CanvasRenderTarget? _canvasResizePreviewImage;
    private CanvasRenderTarget CanvasResizePreviewImage() => _canvasResizePreviewImage ??= ScreenshotRenderer.RenderCanvasPreview(PaintCanvas, _document.State, _canvasResizePreview!, _renderCache);

    private async void Resize_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var state = _document.State; var f = state.Frame;
        var oldZoom = ZoomSlider.Value; var oldX = Viewport.HorizontalOffset; var oldY = Viewport.VerticalOffset;
        var width = new NumberBox { Minimum = 1, Maximum = 16384, Value = f.Width, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var height = new NumberBox { Minimum = 1, Maximum = 16384, Value = f.Height, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        AutomationProperties.SetAutomationId(width, "DialogImageWidth"); AutomationProperties.SetAutomationId(height, "DialogImageHeight");
        DialogField(width, "幅 (px)", "W"); DialogField(height, "高さ (px)", "H");
        var ratio = new CheckBox { IsChecked = true, FontSize = 16 };
        CommandLabel(ratio, "縦横比を固定", "Alt+R"); ratio.AccessKey = "R"; AutomationProperties.SetAutomationId(ratio, "DialogImageRatio");
        var centered = new CheckBox { IsChecked = true, FontSize = 16 };
        CommandLabel(centered, "両端を連動", "Alt+B"); centered.AccessKey = "B"; AutomationProperties.SetAutomationId(centered, "DialogCanvasCentered");
        ToolTipService.SetToolTip(centered, "中心を固定して左右・上下を増減 (Alt+B)");
        var thumbnail = new CanvasControl { Width = 320, Height = 160, CustomDevice = PaintCanvas.Device };
        AutomationProperties.SetAutomationId(thumbnail, "CanvasResizeThumbnail"); AutomationProperties.SetName(thumbnail, "変更後のキャンバス");
        thumbnail.Draw += (control, args) =>
        {
            var ds = args.DrawingSession; ds.Clear(Colors.White);
            if (_canvasResizePreview is not { } layout) return;
            var scale = (float)Math.Min((control.ActualWidth - 8) / layout.Width, (control.ActualHeight - 8) / layout.Height);
            var x = (float)(control.ActualWidth - layout.Width * scale) / 2; var y = (float)(control.ActualHeight - layout.Height * scale) / 2;
            if (_document.State.Background.A < 255)
                for (var py = 0; py < control.ActualHeight; py += 8) for (var px = 0; px < control.ActualWidth; px += 8)
                    ds.FillRectangle(px, py, 8, 8, (px / 8 + py / 8) % 2 == 0 ? Colors.White : Colors.LightGray);
            ds.Transform = Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation(x, y);
            ds.DrawImage(CanvasResizePreviewImage());
            ds.DrawRectangle(new Rect(0, 0, layout.Width, layout.Height), Colors.DodgerBlue, 1 / scale);
        };
        var error = new TextBlock { FontSize = 16, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(error, "CanvasResizeError");
        var panel = new StackPanel { Spacing = 8 }; panel.Children.Add(width); panel.Children.Add(height); panel.Children.Add(ratio); panel.Children.Add(centered); panel.Children.Add(thumbnail); panel.Children.Add(error);
        var dialog = new EditorDialog { Title = "キャンバスサイズ", Content = panel, PrimaryButtonText = "適用", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Primary };
        var updating = false; var committed = false;
        void UpdatePreview()
        {
            if (updating) return;
            try
            {
                if (!double.IsFinite(width.Value) || !double.IsFinite(height.Value) || width.Value != Math.Truncate(width.Value) || height.Value != Math.Truncate(height.Value)) throw new ArgumentException("幅と高さを整数で入力してください。");
                var next = ScreenshotCanvasResize.Create(state, checked((int)width.Value), checked((int)height.Value), centered.IsChecked == true);
                error.Text = ""; dialog.IsPrimaryButtonEnabled = true;
                if (_canvasResizePreview == next) return;
                _canvasResizePreview = next; _canvasResizePreviewImage?.Dispose(); _canvasResizePreviewImage = null;
                var name = $"プレビュー: {next.Width} × {next.Height} px、画像の移動 {next.OffsetX}, {next.OffsetY} px";
                AutomationProperties.SetItemStatus(PaintCanvas, name); AutomationProperties.SetName(thumbnail, name);
                Refresh(); FitImage(); thumbnail.Invalidate();
            }
            catch (Exception ex) when (ex is ArgumentException or OverflowException) { error.Text = ex.Message; dialog.IsPrimaryButtonEnabled = false; }
        }
        width.ValueChanged += (_, e) =>
        {
            if (updating) return;
            if (ratio.IsChecked == true && double.IsFinite(e.NewValue)) { updating = true; height.Value = Math.Clamp(Math.Round(e.NewValue * f.Height / f.Width), 1, 16384); updating = false; }
            UpdatePreview();
        };
        height.ValueChanged += (_, e) =>
        {
            if (updating) return;
            if (ratio.IsChecked == true && double.IsFinite(e.NewValue)) { updating = true; width.Value = Math.Clamp(Math.Round(e.NewValue * f.Width / f.Height), 1, 16384); updating = false; }
            UpdatePreview();
        };
        centered.Checked += (_, _) => UpdatePreview(); centered.Unchecked += (_, _) => UpdatePreview();
        dialog.PrimaryButtonClick += (_, args) => { UpdatePreview(); if (!dialog.IsPrimaryButtonEnabled) args.Cancel = true; };
        try
        {
            UpdatePreview();
            if (await ShowDialogAsync(dialog, width) != ContentDialogResult.Primary || _canvasResizePreview is not { } layout) return;
            if (layout.Width == f.Width && layout.Height == f.Height) return;
            if (!ReferenceEquals(state, _document.State)) throw new InvalidOperationException("編集中の画像が変わったため適用できません。");
            _document.ResizeCanvas(layout.Width, layout.Height, centered.IsChecked == true);
            _region = null; _selectedIds.Clear(); _hasImage = true; committed = true;
        }
        finally
        {
            _canvasResizePreview = null; _canvasResizePreviewImage?.Dispose(); _canvasResizePreviewImage = null;
            thumbnail.RemoveFromVisualTree(); AutomationProperties.SetItemStatus(PaintCanvas, "");
            if (!committed) ZoomSlider.Value = oldZoom;
            Refresh();
            if (committed) FitImage();
            else { Viewport.UpdateLayout(); Viewport.ChangeView(oldX, oldY, null, true); }
        }
    });
}
