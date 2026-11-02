using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using PPPaint.Core;

namespace PPPaint.Imaging;

public static class ScreenshotCalloutText
{
    public static ScreenshotAnnotation Fit(ICanvasResourceCreator device, ScreenshotAnnotation item, string text, int canvasWidth)
    {
        if (item.Tool != ScreenshotTool.Callout) return item;
        const float horizontal = 16, vertical = 12;
        using var format = new CanvasTextFormat { FontFamily = "Yu Gothic UI", FontSize = item.FontSize, WordWrapping = CanvasWordWrapping.NoWrap };
        using var natural = new CanvasTextLayout(device, text, format, 16384, 16384);
        var edge = ScreenshotCalloutGeometry.Controls(item).Edge;
        var available = Math.Max(32, edge == 1 ? item.Bounds.Right : canvasWidth - item.Bounds.X);
        var width = Math.Min(Math.Max(item.Bounds.Width, (float)Math.Ceiling(natural.LayoutBounds.Width) + horizontal), available);
        format.WordWrapping = CanvasWordWrapping.Wrap;
        using var wrapped = new CanvasTextLayout(device, text, format, Math.Max(1, width - horizontal), 16384);
        var height = Math.Max(item.Bounds.Height, (float)Math.Ceiling(wrapped.LayoutBounds.Height) + vertical);
        var x = edge == 1 ? Math.Max(0, item.Bounds.Right - width) : item.Bounds.X;
        var y = edge == 2 ? Math.Max(0, item.Bounds.Bottom - height) : item.Bounds.Y;
        return item with { Bounds = new(x, y, width, height), Text = text, CalloutTip = item.Tail };
    }
}
