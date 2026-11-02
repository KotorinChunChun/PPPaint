using System.Collections.Immutable;
using Microsoft.Graphics.Canvas;
using PPPaint.Core;

namespace PPPaint.Imaging;

public static class ScreenshotRegionLifting
{
    public static ScreenshotRegionLift Create(ICanvasResourceCreator device, ScreenshotState source, PixelRect area)
    {
        using var rendered = ScreenshotRenderer.Render(device, source);
        var pixels = ScreenshotRenderer.Frame(rendered).Crop(area.X, area.Y, area.Width, area.Height);
        var prepared = source;
        // Freeze dependent effects so cutting their source pixels cannot change
        // the visible result outside the selected rectangle. Other objects stay editable.
        var items = source.Annotations.ToBuilder();
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.Tool is not (ScreenshotTool.Blur or ScreenshotTool.Mosaic)) continue;
            var bounds = ScreenshotEditing.Intersection(item.Bounds, new(0, 0, source.Frame.Width, source.Frame.Height));
            if (bounds is not { } box) continue;
            var left = (int)Math.Floor(box.X); var top = (int)Math.Floor(box.Y);
            var right = (int)Math.Ceiling(box.Right); var bottom = (int)Math.Ceiling(box.Bottom);
            using var effect = ScreenshotRenderer.Render(device, source with { Annotations = source.Annotations.Take(i + 1).ToImmutableArray() });
            var image = ScreenshotRenderer.Frame(effect).Crop(left, top, right - left, bottom - top);
            items[i] = ScreenshotDocument.ImageObject(image, new(left, top), "効果の画像") with
            { Id = item.Id, ClipBounds = item.ClipBounds, Cutouts = item.Cutouts, ReplacePixels = true };
        }
        prepared = prepared with { Annotations = items.ToImmutable() };
        return new(source, prepared, area, pixels);
    }
}
