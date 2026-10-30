using System.Collections.Immutable;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using PPPaint.Core;
using PPPaint.Imaging;
using Windows.Foundation;

namespace PPPaint;

public sealed partial class EditorWindow
{
    private bool _fitting;
    private void Zoom_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        _zoom = e.NewValue / 100; if (ZoomLabel is null) return; ZoomLabel.Content = KeyLabel($"{e.NewValue:0}%", "Alt+D"); Refresh();
    }
    private void FitImage()
    {
        if (Viewport is null || Viewport.ActualWidth < 1 || Viewport.ActualHeight < 1) return;
        _fitting = true;
        try { ZoomSlider.Value = Math.Clamp(Math.Min((Viewport.ActualWidth - 44) / _document.State.Frame.Width, (Viewport.ActualHeight - 44) / _document.State.Frame.Height) * 100, 10, 100); }
        finally { _fitting = false; }
    }
    private void Fit_Click(object sender, RoutedEventArgs e) { if (!_busy) FitImage(); }
    private void ZoomReset_Click(object sender, RoutedEventArgs e) { if (!_busy) ZoomSlider.Value = 100; }
    private void Viewport_SizeChanged(object sender, SizeChangedEventArgs e) { if (!_fitting && !_hasImage && !Dirty) FitImage(); }
    private void Crop_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _region is not { } region) return;
        ClampRegion(); region = _region!.Value;
        var x = (int)region.X; var y = (int)region.Y;
        _document.Crop(x, y, Math.Max(1, (int)region.Width), Math.Max(1, (int)region.Height));
        _region = null; _selectedIds.Clear(); _hasImage = true; Refresh(); FitImage();
    }
    private async void Resize_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var f = _document.State.Frame;
        var width = new NumberBox { Header = "幅 (px)", Minimum = 1, Maximum = 16384, Value = f.Width, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var height = new NumberBox { Header = "高さ (px)", Minimum = 1, Maximum = 16384, Value = f.Height, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(width, "画像の幅"); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(height, "画像の高さ");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(width, "DialogImageWidth"); Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(height, "DialogImageHeight");
        DialogField(width, "幅 (px)", "W"); DialogField(height, "高さ (px)", "H");
        var ratio = new CheckBox { Content = "縦横比を固定", IsChecked = true, FontSize = 16 }; var updating = false;
        CommandLabel(ratio, "縦横比を固定", "Alt+R"); ratio.AccessKey = "R";
        width.ValueChanged += (_, e) => { if (updating || ratio.IsChecked != true || !double.IsFinite(e.NewValue)) return; updating = true; height.Value = Math.Clamp(Math.Round(e.NewValue * f.Height / f.Width), 1, 16384); updating = false; };
        height.ValueChanged += (_, e) => { if (updating || ratio.IsChecked != true || !double.IsFinite(e.NewValue)) return; updating = true; width.Value = Math.Clamp(Math.Round(e.NewValue * f.Width / f.Height), 1, 16384); updating = false; };
        var error = new TextBlock { FontSize = 16, TextWrapping = TextWrapping.Wrap };
        var panel = new StackPanel { Spacing = 8 }; panel.Children.Add(width); panel.Children.Add(height); panel.Children.Add(ratio); panel.Children.Add(error);
        var dialog = new EditorDialog { Title = "画像サイズ", Content = panel, PrimaryButtonText = "適用", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Primary };
        dialog.PrimaryButtonClick += (_, e) =>
        {
            try
            {
                if (!double.IsFinite(width.Value) || !double.IsFinite(height.Value) || width.Value != Math.Truncate(width.Value) || height.Value != Math.Truncate(height.Value)) throw new ArgumentException("幅と高さを整数で入力してください。");
                ScreenshotFrame.ValidateSize((int)width.Value, (int)height.Value);
            }
            catch (ArgumentException ex) { error.Text = ex.Message; e.Cancel = true; }
        };
        if (await ShowDialogAsync(dialog, width) != ContentDialogResult.Primary) return;
        var w = (int)width.Value; var h = (int)height.Value;
        if (w == f.Width && h == f.Height) return;
        using var bitmap = ScreenshotRenderer.Bitmap(PaintCanvas, f); using var target = new CanvasRenderTarget(PaintCanvas, w, h, 96);
        using (var ds = target.CreateDrawingSession()) { ds.Clear(Colors.Transparent); ds.DrawImage(bitmap, new Rect(0, 0, w, h), new Rect(0, 0, f.Width, f.Height), 1, CanvasImageInterpolation.HighQualityCubic); }
        var sx = (float)w / f.Width; var sy = (float)h / f.Height;
        var annotations = _document.State.Annotations.Select(a => a.Resize(new(a.Bounds.X * sx, a.Bounds.Y * sy, a.Bounds.Width * sx, a.Bounds.Height * sy)) with { Width = Math.Max(1, a.Width * Math.Min(sx, sy)) }).ToImmutableArray();
        _document.Apply(_document.State with { Frame = new(w, h, target.GetPixelBytes()), Annotations = annotations });
        _region = null; _selectedIds.Clear(); _hasImage = true; Refresh(); FitImage();
    });
    private void Transform_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || sender is not MenuFlyoutItem item) return;
        CancelGesture();
        var frame = _document.State.Frame; var source = new PixelSurface(frame.Width, frame.Height, Rgba32.Transparent); source.Restore(frame.Pixels.Span);
        var operation = item.Tag?.ToString();
        var output = operation switch { "Rotate" => ImageTransforms.RotateClockwise(source), "FlipX" => ImageTransforms.FlipHorizontal(source), _ => ImageTransforms.FlipVertical(source) };
        Vector2 Transform(Vector2 p) => operation switch { "Rotate" => new(frame.Height - p.Y, p.X), "FlipX" => new(frame.Width - p.X, p.Y), _ => new(p.X, frame.Height - p.Y) };
        var annotations = _document.State.Annotations.Select(a =>
        {
            var points = Corners(a.Bounds).Select(Transform).ToArray(); var xs = points.Select(p => p.X); var ys = points.Select(p => p.Y);
            return a with { Bounds = new(xs.Min(), ys.Min(), xs.Max() - xs.Min(), ys.Max() - ys.Min()), Points = a.Points.IsDefault ? a.Points : a.Points.Select(Transform).ToImmutableArray(), Rotation = operation == "Rotate" ? (a.Rotation + 90) % 360 : -a.Rotation,
                MirrorX = a.Tool == ScreenshotTool.Image && operation == "FlipX" ? !a.MirrorX : a.MirrorX, MirrorY = a.Tool == ScreenshotTool.Image && operation == "FlipY" ? !a.MirrorY : a.MirrorY };
        }).ToImmutableArray();
        _document.Apply(_document.State with { Frame = new(output.Width, output.Height, output.Snapshot()), Annotations = annotations });
        _region = null; _selectedIds.Clear(); _hasImage = true; Refresh(); FitImage();
    }
}
