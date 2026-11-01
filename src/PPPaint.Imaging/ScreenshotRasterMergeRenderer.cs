using Microsoft.Graphics.Canvas;
using PPPaint.Core;

namespace PPPaint.Imaging;

public static class ScreenshotRasterMergeRenderer
{
    public static ScreenshotRasterMergeImage Render(ICanvasResourceCreator device, ScreenshotRasterMerge merge, ScreenshotRenderCache? cache = null)
    {
        ArgumentNullException.ThrowIfNull(device); ArgumentNullException.ThrowIfNull(merge);
        using var target = ScreenshotRenderer.Render(device, merge.Scene, cache);
        var pixels = target.GetPixelBytes();
        var width = merge.Scene.Frame.Width; var height = merge.Scene.Frame.Height;
        var left = width; var top = height; var right = 0; var bottom = 0;
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            if (pixels[(y * width + x) * 4 + 3] == 0) continue;
            left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1);
        }
        var bounds = right > left && bottom > top ? new PixelRect(left, top, right - left, bottom - top) : merge.Bounds;
        var cropped = new byte[checked(bounds.Width * bounds.Height * 4)];
        for (var row = 0; row < bounds.Height; row++)
            pixels.AsSpan(((bounds.Y + row) * width + bounds.X) * 4, bounds.Width * 4).CopyTo(cropped.AsSpan(row * bounds.Width * 4));
        return new(new ScreenshotFrame(bounds.Width, bounds.Height, cropped), bounds);
    }
}
