using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace ChunPaint;

public sealed partial class MainWindow
{
    private enum ColorTarget
    {
        Primary,
        Secondary
    }

    private readonly List<Color> _customColors = [];
    private ColorTarget _activeColorTarget = ColorTarget.Primary;

    private Color GetActiveColor() => _activeColorTarget == ColorTarget.Primary ? _primaryColor : _secondaryColor;

    private void SetActiveColor(Color color, bool addCustomColor = false)
    {
        SetColorTarget(_activeColorTarget, color);
        if (addCustomColor)
        {
            AddCustomColor(color);
        }
    }

    private void SetColorTarget(ColorTarget target, Color color, bool activateTarget = false)
    {
        if (activateTarget)
        {
            _activeColorTarget = target;
        }

        if (target == ColorTarget.Primary)
        {
            _primaryColor = color;
        }
        else
        {
            _secondaryColor = color;
        }

        UpdateColorVisuals();
    }

    private void ColorTarget_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || !TryGetColorTarget(element, out var target))
        {
            return;
        }

        _activeColorTarget = target;
        UpdateColorVisuals();
        RebuildCustomColors();
        ColorPaletteFlyout?.ShowAt(element);
    }

    private static bool TryGetColorTarget(FrameworkElement element, out ColorTarget target)
    {
        var value = element.Tag?.ToString() ?? element.Name;
        if (value.Contains("Secondary", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Background", StringComparison.OrdinalIgnoreCase))
        {
            target = ColorTarget.Secondary;
            return true;
        }
        if (value.Contains("Primary", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Pen", StringComparison.OrdinalIgnoreCase))
        {
            target = ColorTarget.Primary;
            return true;
        }

        target = default;
        return false;
    }

    private void UpdateColorVisuals()
    {
        var primaryBrush = new SolidColorBrush(_primaryColor);
        var secondaryBrush = new SolidColorBrush(_secondaryColor);
        ApplyColorPreview(PrimaryColorPreview, primaryBrush);
        ApplyColorPreview(SecondaryColorPreview, secondaryBrush);
        PrimaryColorBox.Background = primaryBrush;
        SecondaryColorBox.Background = secondaryBrush;

        var activeBorder = new SolidColorBrush(Color.FromArgb(255, 116, 107, 225));
        var inactiveBorder = new SolidColorBrush(Color.FromArgb(255, 170, 170, 170));
        if (PrimaryColorButton is not null)
        {
            PrimaryColorButton.BorderBrush = _activeColorTarget == ColorTarget.Primary ? activeBorder : inactiveBorder;
            PrimaryColorButton.BorderThickness = new Thickness(_activeColorTarget == ColorTarget.Primary ? 2 : 1);
        }
        if (SecondaryColorButton is not null)
        {
            SecondaryColorButton.BorderBrush = _activeColorTarget == ColorTarget.Secondary ? activeBorder : inactiveBorder;
            SecondaryColorButton.BorderThickness = new Thickness(_activeColorTarget == ColorTarget.Secondary ? 2 : 1);
        }
    }

    private static void ApplyColorPreview(FrameworkElement preview, SolidColorBrush brush)
    {
        switch (preview)
        {
            case Border border:
                border.Background = brush;
                break;
            case Shape shape:
                shape.Fill = brush;
                break;
            case Control control:
                control.Background = brush;
                break;
        }
    }

    private void AddCustomColor(Color color)
    {
        if (_customColors.Any(existing => existing.Equals(color)))
        {
            RebuildCustomColors();
            return;
        }

        if (_customColors.Count >= 10)
        {
            _customColors.RemoveAt(0);
        }
        _customColors.Add(color);
        RebuildCustomColors();
    }

    private void RebuildCustomColors()
    {
        if (CustomColorsPanel is null)
        {
            return;
        }

        CustomColorsPanel.Children.Clear();
        foreach (var color in _customColors)
        {
            var swatch = new Button
            {
                Width = 24,
                Height = 24,
                Padding = new Thickness(0),
                Margin = new Thickness(2),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 170, 170, 170)),
                BorderThickness = new Thickness(1),
                Tag = ToColorTag(color),
                Content = new Border
                {
                    Width = 18,
                    Height = 18,
                    Background = new SolidColorBrush(color),
                    BorderBrush = new SolidColorBrush(color),
                    BorderThickness = new Thickness(1)
                }
            };
            swatch.Click += Palette_Click;
            AutomationProperties.SetName(swatch, $"作成色 {ToColorTag(color)}");
            ToolTipService.SetToolTip(swatch, $"作成色 {ToColorTag(color)}");
            CustomColorsPanel.Children.Add(swatch);
        }
    }

    private static string ToColorTag(Color color) =>
        $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
}
