using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PPPaint.Core;
using Windows.Foundation;

namespace PPPaint;

public sealed partial class EditorWindow
{
    private async void CanvasBackground_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var picker = new ColorPicker { Color = ToColor(_document.State.Background), IsAlphaEnabled = true, IsAlphaSliderVisible = true, IsAlphaTextInputVisible = true, IsColorChannelTextInputVisible = true, IsHexInputVisible = true };
        AutomationProperties.SetAutomationId(picker, "CanvasColorPicker");
        var transparent = new CheckBox { Content = KeyLabel("透明", "T"), AccessKey = "T", IsChecked = picker.Color.A == 0, FontSize = 16 };
        AutomationProperties.SetAutomationId(transparent, "CanvasTransparentBox"); AutomationProperties.SetName(transparent, "透明"); ToolTipService.SetToolTip(transparent, "透明 (Alt+T)");
        var synchronizing = false;
        transparent.Checked += (_, _) => { if (!synchronizing) { var color = picker.Color; color.A = 0; picker.Color = color; } };
        transparent.Unchecked += (_, _) => { if (!synchronizing) { var color = picker.Color; color.A = 255; picker.Color = color; } };
        picker.ColorChanged += (_, args) => { synchronizing = true; transparent.IsChecked = args.NewColor.A == 0; synchronizing = false; };
        var white = new Button { FontSize = 16 }; CommandLabel(white, "白", "Alt+W"); white.AccessKey = "W";
        AutomationProperties.SetAutomationId(white, "CanvasWhiteButton"); white.Click += (_, _) => picker.Color = ToColor(Rgba32.White);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; actions.Children.Add(white); actions.Children.Add(transparent);
        var panel = new StackPanel { Spacing = 8 }; panel.Children.Add(picker); panel.Children.Add(actions);
        var dialog = new EditorDialog { Title = "キャンバスの背景色", Content = panel, PrimaryButtonText = "適用", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Primary };
        if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary) return;
        var value = picker.Color; _document.SetBackground(new(value.R, value.G, value.B, value.A)); Refresh();
    });

    private void DrawCanvasBackground(CanvasDrawingSession ds, bool composited)
    {
        var state = _document.State; var color = state.Background;
        if (color.A < 255)
        {
            var origin = PaintCanvas.TransformToVisual(Viewport).TransformPoint(new Point(0, 0));
            var left = (float)Math.Max(0, -origin.X / _zoom); var top = (float)Math.Max(0, -origin.Y / _zoom);
            var right = (float)Math.Min(state.Frame.Width, (Viewport.ActualWidth - origin.X) / _zoom); var bottom = (float)Math.Min(state.Frame.Height, (Viewport.ActualHeight - origin.Y) / _zoom);
            var size = 12f / (float)_zoom;
            for (var y = Math.Max(0, (int)Math.Floor(top / size)); y * size < bottom; y++)
                for (var x = Math.Max(0, (int)Math.Floor(left / size)); x * size < right; x++)
                    ds.FillRectangle(x * size, y * size, Math.Min(size, state.Frame.Width - x * size), Math.Min(size, state.Frame.Height - y * size), (x + y) % 2 == 0 ? Windows.UI.Color.FromArgb(255, 238, 238, 238) : Colors.White);
        }
        if (!composited) ds.FillRectangle(0, 0, state.Frame.Width, state.Frame.Height, ToColor(color));
    }
}
