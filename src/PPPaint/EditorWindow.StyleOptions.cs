using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PPPaint.Core;
using Windows.UI;

namespace PPPaint;

public sealed partial class EditorWindow
{
    private Rgba32 _color = new(220, 38, 38);
    private Rgba32 _drawingColor = new(220, 38, 38);
    private float CurrentWidth => (float)(double.IsFinite(WidthBox.Value) ? Math.Clamp(WidthBox.Value, 1, 100) : 3);
    private float CurrentFontSize => (float)(double.IsFinite(FontBox.Value) ? Math.Clamp(FontBox.Value, 8, 200) : 24);
    private float CurrentBlurRadius => (float)(BlurRadiusBox is not null && double.IsFinite(BlurRadiusBox.Value) ? Math.Clamp(BlurRadiusBox.Value, 1, 64) : 8);
    private int CurrentMosaicSize => (int)(MosaicSizeBox is not null && double.IsFinite(MosaicSizeBox.Value) ? Math.Clamp(Math.Round(MosaicSizeBox.Value), 2, 128) : 12);
    private float CurrentOpacity => (float)(OpacityBox is not null && double.IsFinite(OpacityBox.Value) ? Math.Clamp(OpacityBox.Value / 100, .05, 1) : .25);
    private void Style_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_updatingControls || _busy || !double.IsFinite(args.NewValue) || PaintCanvas is null) return;
        var property = sender == WidthBox ? ScreenshotStyleProperty.Width : sender == FontBox ? ScreenshotStyleProperty.FontSize
            : sender == BlurRadiusBox ? ScreenshotStyleProperty.BlurRadius : sender == MosaicSizeBox ? ScreenshotStyleProperty.MosaicSize : ScreenshotStyleProperty.Opacity;
        var value = (float)(property == ScreenshotStyleProperty.Opacity ? args.NewValue / 100 : args.NewValue);
        var selected = SelectedObjects;
        _document.ReplaceMany(selected.Select(a => ScreenshotStyleEditing.Apply(a, property, value)));
        if (selected.Length == 0)
        {
            RememberDrawingNumber(_tool, property, args.NewValue);
            SavePreferences();
        }
        ShowSelectedOptions(); Refresh();
    }
    private void NumberStart_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_updatingControls || _busy || _pointerId is not null || !double.IsFinite(args.NewValue) || args.NewValue != Math.Truncate(args.NewValue) || args.NewValue is < 1 or > int.MaxValue) return;
        _document.SetNextNumber((int)args.NewValue); Refresh();
    }
    private void TextInput_Changed(object sender, TextChangedEventArgs e)
    {
        if (_updatingControls || _busy) return;
        if (Selected is { Tool: ScreenshotTool.Text or ScreenshotTool.Number or ScreenshotTool.Callout or ScreenshotTool.Watermark } selected)
        {
            var bounds = ScreenshotEditing.FitTextBounds(selected, TextInput.Text, _document.State.Frame.Width);
            // One undo step per typing session; leaving the text box starts the next one.
            _document.Replace(selected with { Text = TextInput.Text, Bounds = bounds }, "text:" + selected.Id); Refresh();
        }
    }
    private void SetColor(Rgba32 color)
    {
        color = color with { A = 255 };
        var selected = SelectedObjects;
        if (selected.Length == 0) SetDrawingColor(color);
        else
        {
            _document.ReplaceMany(selected.Where(a => ScreenshotStyleCapture.UsesColor(a.Tool)).Select(a => a with { Color = color }));
            ShowSelectedOptions(); Refresh();
        }
    }
    private void SetDrawingColor(Rgba32 color)
    {
        _drawingColor = color with { A = 255 };
        SavePreferences(); ShowSelectedOptions(); Refresh();
    }
    private void AdoptStyle_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _dialogOpen || _pointerId is not null || ScreenshotStyleCapture.Read(SelectedObjects) is not { } style) return;
        foreach (var number in style.Numbers) RememberDrawingNumber(style.Tool, number.Key, number.Value);
        if (style.Color is { } color) { _toolPreferences.Color = ColorHex(color); _drawingColor = color; }
        SavePreferences(); ShowSelectedOptions(); Refresh();
    }
    private static Color ToColor(Rgba32 c) => Color.FromArgb(c.A, c.R, c.G, c.B);
    private static Rgba32 ParseColor(string text)
    {
        text = text.Trim().TrimStart('#');
        if (text.Length != 6 || !uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var c)) throw new ArgumentException("色は6桁の16進数で指定してください。");
        return new((byte)(c >> 16), (byte)(c >> 8), (byte)c);
    }
    private void ShowSelectedOptions()
    {
        _updatingControls = true;
        try
        {
            var selected = SelectedObjects; var multiple = selected.Length > 1;
            var a = selected.Length == 1 ? selected[0] : null; var tool = a?.Tool ?? _tool;
            _color = ScreenshotStyleCapture.DisplayColor(_drawingColor, selected);
            AdoptStyleButton.IsEnabled = ScreenshotStyleCapture.CanCapture(selected);
            var defaults = _toolPreferences.Get(_tool);
            bool Available(ScreenshotStyleProperty property) => multiple ? selected.Any(item => ScreenshotStyleEditing.Supports(item.Tool, property)) : ScreenshotStyleEditing.Supports(tool, property);
            void NumberValue(NumberBox box, ScreenshotStyleProperty property, double fallback, Func<ScreenshotAnnotation, double> getValue)
            {
                box.PlaceholderText = ""; Microsoft.UI.Xaml.Automation.AutomationProperties.SetItemStatus(box, "");
                if (multiple)
                {
                    var values = selected.Where(item => ScreenshotStyleEditing.Supports(item.Tool, property)).Select(getValue).Distinct().Take(2).ToArray();
                    if (values.Length == 0) { box.Value = fallback; return; }
                    box.Value = values.Length == 1 ? values[0] : double.NaN;
                    if (values.Length > 1) { box.PlaceholderText = "混在"; Microsoft.UI.Xaml.Automation.AutomationProperties.SetItemStatus(box, "混在"); }
                }
                else box.Value = a is not null && ScreenshotStyleEditing.Supports(a.Tool, property) ? getValue(a) : fallback;
            }
            NumberValue(WidthBox, ScreenshotStyleProperty.Width, defaults.Width, item => item.Width);
            NumberValue(FontBox, ScreenshotStyleProperty.FontSize, defaults.FontSize, item => item.FontSize);
            NumberValue(BlurRadiusBox, ScreenshotStyleProperty.BlurRadius, defaults.BlurRadius, item => item.BlurRadius);
            NumberValue(MosaicSizeBox, ScreenshotStyleProperty.MosaicSize, defaults.MosaicSize, item => item.MosaicSize);
            NumberValue(OpacityBox, ScreenshotStyleProperty.Opacity, defaults.Opacity, item => item.Opacity * 100);
            WidthCaption.Text = tool == ScreenshotTool.Eraser ? "幅 (px)" : "太さ";
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(WidthBox, tool == ScreenshotTool.Eraser ? "消しゴムの幅 (px)" : "線の太さ");
            WidthBox.IsEnabled = Available(ScreenshotStyleProperty.Width) || !multiple && tool is ScreenshotTool.Select or ScreenshotTool.Region;
            WidthOptions.Visibility = WidthBox.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
            BlurOptions.Visibility = Available(ScreenshotStyleProperty.BlurRadius) ? Visibility.Visible : Visibility.Collapsed;
            MosaicOptions.Visibility = Available(ScreenshotStyleProperty.MosaicSize) ? Visibility.Visible : Visibility.Collapsed;
            OpacityOptions.Visibility = Available(ScreenshotStyleProperty.Opacity) ? Visibility.Visible : Visibility.Collapsed;
            NumberOptions.Visibility = tool == ScreenshotTool.Number || _tool == ScreenshotTool.Number ? Visibility.Visible : Visibility.Collapsed;
            NumberStartBox.Value = _document.State.NextNumber;
            TextOptions.Visibility = Available(ScreenshotStyleProperty.FontSize) ? Visibility.Visible : Visibility.Collapsed;
            TextInput.Visibility = multiple || tool == ScreenshotTool.Number && _tool == ScreenshotTool.Number ? Visibility.Collapsed : Visibility.Visible;
            if (a is null || a.Tool == ScreenshotTool.Image) return;
            TextInput.Text = a.Text;
        }
        finally { _updatingControls = false; }
    }
}
