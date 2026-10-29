using Microsoft.Graphics.Canvas;
using PPPaint.Core;

namespace PPPaint.Imaging;

public sealed class ScreenshotRenderCache(ICanvasResourceCreator device) : IDisposable
{
    private readonly Dictionary<ScreenshotFrame, CanvasBitmap> _bitmaps = [];
    public CanvasBitmap Get(ScreenshotFrame frame)
    {
        if (!_bitmaps.TryGetValue(frame, out var bitmap)) _bitmaps.Add(frame, bitmap = ScreenshotRenderer.Bitmap(device, frame));
        return bitmap;
    }
    public void Retain(IEnumerable<ScreenshotAnnotation> objects)
    {
        var frames = objects.Where(a => a.Raster is not null).Select(a => a.Raster!).ToHashSet();
        foreach (var frame in _bitmaps.Keys.Where(f => !frames.Contains(f)).ToArray()) { _bitmaps[frame].Dispose(); _bitmaps.Remove(frame); }
    }
    public void Dispose() { foreach (var bitmap in _bitmaps.Values) bitmap.Dispose(); _bitmaps.Clear(); }
}
