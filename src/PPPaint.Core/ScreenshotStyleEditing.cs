namespace PPPaint.Core;

public enum ScreenshotStyleProperty { Width, FontSize, BlurRadius, MosaicSize, Opacity }

public static class ScreenshotStyleEditing
{
    public static bool Supports(ScreenshotTool tool, ScreenshotStyleProperty property) => property switch
    {
        ScreenshotStyleProperty.Width => tool is ScreenshotTool.Pen or ScreenshotTool.Eraser or ScreenshotTool.Arrow or ScreenshotTool.Line or ScreenshotTool.Rectangle or ScreenshotTool.Ellipse or ScreenshotTool.Callout,
        ScreenshotStyleProperty.FontSize => tool is ScreenshotTool.Text or ScreenshotTool.Number or ScreenshotTool.Callout or ScreenshotTool.Watermark,
        ScreenshotStyleProperty.BlurRadius => tool == ScreenshotTool.Blur,
        ScreenshotStyleProperty.MosaicSize => tool == ScreenshotTool.Mosaic,
        ScreenshotStyleProperty.Opacity => tool == ScreenshotTool.Watermark,
        _ => false
    };

    public static ScreenshotAnnotation Apply(ScreenshotAnnotation annotation, ScreenshotStyleProperty property, float value)
    {
        if (!Supports(annotation.Tool, property)) return annotation;
        if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        return property switch
        {
            ScreenshotStyleProperty.Width => annotation with { Width = Math.Clamp(value, 1, 100) },
            ScreenshotStyleProperty.FontSize => annotation with { FontSize = Math.Clamp(value, 8, 200) },
            ScreenshotStyleProperty.BlurRadius => annotation with { BlurRadius = Math.Clamp(value, 1, 64) },
            ScreenshotStyleProperty.MosaicSize => annotation with { MosaicSize = (int)Math.Clamp(Math.Round(value), 2, 128) },
            ScreenshotStyleProperty.Opacity => annotation with { Opacity = Math.Clamp(value, .05f, 1) },
            _ => annotation
        };
    }
}
