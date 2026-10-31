using System.Collections.ObjectModel;

namespace PPPaint.Core;

// A read-only snapshot: reading a selection never changes drawing defaults or the document.
public sealed record ScreenshotCapturedStyle(ScreenshotTool Tool, Rgba32? Color,
    IReadOnlyDictionary<ScreenshotStyleProperty, double> Numbers)
{
    public void ApplyTo(ScreenshotToolPreferences preferences)
    {
        foreach (var number in Numbers) preferences.Set(Tool, number.Key, number.Value);
        if (Color is { } color) preferences.Color = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}

public static class ScreenshotStyleCapture
{
    public static bool UsesColor(ScreenshotTool tool) => tool is ScreenshotTool.Pen or ScreenshotTool.Arrow
        or ScreenshotTool.Rectangle or ScreenshotTool.Ellipse or ScreenshotTool.Line or ScreenshotTool.Text
        or ScreenshotTool.Number or ScreenshotTool.Redact or ScreenshotTool.Highlight or ScreenshotTool.Callout
        or ScreenshotTool.Watermark;

    public static bool CanCapture(IReadOnlyList<ScreenshotAnnotation> selection) => selection.Count == 1
        && (UsesColor(selection[0].Tool) || Enum.GetValues<ScreenshotStyleProperty>().Any(property => ScreenshotStyleEditing.Supports(selection[0].Tool, property)));

    public static Rgba32 DisplayColor(Rgba32 drawingColor, IReadOnlyList<ScreenshotAnnotation> selection) =>
        selection.Count == 1 && UsesColor(selection[0].Tool) ? selection[0].Color : drawingColor;

    public static ScreenshotCapturedStyle? Read(IReadOnlyList<ScreenshotAnnotation> selection)
    {
        if (!CanCapture(selection)) return null;
        var item = selection[0];
        var numbers = new Dictionary<ScreenshotStyleProperty, double>();
        foreach (var property in Enum.GetValues<ScreenshotStyleProperty>())
        {
            if (!ScreenshotStyleEditing.Supports(item.Tool, property)) continue;
            numbers[property] = property switch
            {
                ScreenshotStyleProperty.Width => item.Width,
                ScreenshotStyleProperty.FontSize => item.FontSize,
                ScreenshotStyleProperty.BlurRadius => item.BlurRadius,
                ScreenshotStyleProperty.MosaicSize => item.MosaicSize,
                ScreenshotStyleProperty.Opacity => item.Opacity * 100d,
                _ => throw new ArgumentOutOfRangeException(nameof(property))
            };
        }
        return new(item.Tool, UsesColor(item.Tool) ? item.Color with { A = 255 } : null,
            new ReadOnlyDictionary<ScreenshotStyleProperty, double>(numbers));
    }
}
