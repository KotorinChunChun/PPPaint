using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace ChunPaint;

public sealed partial class MainWindow
{
    private bool _updatingZoomControls;

    private void HandleZoomSliderChanged(double percentage)
    {
        _zoom = percentage / 100d;
        if (PaintCanvas is null)
        {
            return;
        }

        ApplyCanvasSize();
        PaintCanvas.Invalidate();
        HorizontalRuler?.Invalidate();
        VerticalRuler?.Invalidate();
        UpdateZoomComboBox(percentage);
    }

    private void UpdateZoomComboBox(double percentage)
    {
        if (ZoomComboBox is null || _updatingZoomControls)
        {
            return;
        }

        _updatingZoomControls = true;
        var rounded = (int)Math.Round(percentage);
        var matchingIndex = -1;
        for (var index = 0; index < ZoomComboBox.Items.Count; index++)
        {
            if (ZoomComboBox.Items[index] is ComboBoxItem { Tag: string tag } && int.TryParse(tag, out var value) && value == rounded)
            {
                matchingIndex = index;
                break;
            }
        }
        ZoomComboBox.SelectedIndex = matchingIndex;
        ZoomComboBox.Text = $"{rounded}%";
        _updatingZoomControls = false;
    }

    private void ZoomComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingZoomControls || ZoomComboBox?.SelectedItem is not ComboBoxItem { Tag: string tag } || !double.TryParse(tag, out var percentage))
        {
            return;
        }
        ZoomSlider.Value = Math.Clamp(percentage, ZoomSlider.Minimum, ZoomSlider.Maximum);
    }

    private void ToggleRulers(ToggleMenuFlyoutItem? item)
    {
        if (HorizontalRuler is null || VerticalRuler is null || RulerCorner is null)
        {
            return;
        }
        var isVisible = item?.IsChecked ?? HorizontalRuler.Visibility != Visibility.Visible;
        var visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        RulerCorner.Visibility = visibility;
        HorizontalRuler.Visibility = visibility;
        VerticalRuler.Visibility = visibility;
        UpdateRulerDimensions();
        HorizontalRuler.Invalidate();
        VerticalRuler.Invalidate();
    }

    private void UpdateRulerDimensions()
    {
        if (HorizontalRuler is null || VerticalRuler is null || PaintCanvas is null)
        {
            return;
        }
        HorizontalRuler.Width = PaintCanvas.Width;
        VerticalRuler.Height = PaintCanvas.Height;
    }

    private void HorizontalRuler_Draw(CanvasControl sender, CanvasDrawEventArgs args) =>
        DrawRuler(args, horizontal: true, (float)sender.ActualWidth);

    private void VerticalRuler_Draw(CanvasControl sender, CanvasDrawEventArgs args) =>
        DrawRuler(args, horizontal: false, (float)sender.ActualHeight);

    private void DrawRuler(CanvasDrawEventArgs args, bool horizontal, float displayLength)
    {
        args.DrawingSession.Clear(Color.FromArgb(255, 248, 248, 248));
        if (_zoom <= 0 || displayLength <= 0)
        {
            return;
        }

        var majorStep = GetRulerMajorStep(_zoom);
        var minorStep = majorStep / 5d;
        var imageLength = horizontal ? _imageWidth : _imageHeight;
        var borderColor = Color.FromArgb(255, 190, 190, 190);
        var tickColor = Color.FromArgb(255, 75, 75, 75);
        using var format = new CanvasTextFormat { FontFamily = "Segoe UI", FontSize = 9 };

        if (horizontal)
        {
            args.DrawingSession.DrawLine(0, 23, displayLength, 23, borderColor, 1);
        }
        else
        {
            args.DrawingSession.DrawLine(31, 0, 31, displayLength, borderColor, 1);
        }

        for (var value = 0d; value <= imageLength; value += minorStep)
        {
            var position = (float)(value * _zoom);
            var major = Math.Abs((value / majorStep) - Math.Round(value / majorStep)) < 0.001;
            var tickLength = major ? 9f : 5f;
            if (horizontal)
            {
                args.DrawingSession.DrawLine(position, 24 - tickLength, position, 23, tickColor, 1);
                if (major)
                {
                    args.DrawingSession.DrawText(((int)Math.Round(value)).ToString(), position + 2, 1, tickColor, format);
                }
            }
            else
            {
                args.DrawingSession.DrawLine(32 - tickLength, position, 31, position, tickColor, 1);
                if (major)
                {
                    args.DrawingSession.DrawText(((int)Math.Round(value)).ToString(), 2, position + 1, tickColor, format);
                }
            }
        }
    }

    private static double GetRulerMajorStep(double zoom)
    {
        foreach (var step in new[] { 10d, 20d, 50d, 100d, 200d, 500d, 1000d, 2000d, 5000d })
        {
            if (step * zoom >= 55d)
            {
                return step;
            }
        }
        return 10_000d;
    }
}
