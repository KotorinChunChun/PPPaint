using Microsoft.Graphics.Canvas;
using PPPaint.Core;

namespace PPPaint.Imaging;

public sealed class ScreenshotRenderCache(ICanvasResourceCreator device) : IDisposable
{
    private readonly Dictionary<ScreenshotFrame, CanvasBitmap> _bitmaps = [];
    private ScreenshotState? _scene;
    private CanvasRenderTarget? _rendered;
    // The cache owns the returned image. Callers must not dispose it.
    public CanvasRenderTarget GetScene(ScreenshotState state)
    {
        if (_rendered is not null && ScreenshotEditing.SameImage(_scene, state)) return _rendered;
        _rendered?.Dispose(); _rendered = null; _scene = null;
        _rendered = ScreenshotRenderer.Render(device, state, this); _scene = state;
        return _rendered;
    }
    public CanvasBitmap Get(ScreenshotFrame frame)
    {
        if (!_bitmaps.TryGetValue(frame, out var bitmap)) _bitmaps.Add(frame, bitmap = ScreenshotRenderer.Bitmap(device, frame));
        return bitmap;
    }
    public void Retain(IEnumerable<ScreenshotAnnotation> objects, ScreenshotFrame? background = null)
    {
        var frames = objects.Where(a => a.Raster is not null).Select(a => a.Raster!).ToHashSet();
        if (background is not null) frames.Add(background);
        foreach (var frame in _bitmaps.Keys.Where(f => !frames.Contains(f)).ToArray()) { _bitmaps[frame].Dispose(); _bitmaps.Remove(frame); }
    }
    public void Dispose() { _rendered?.Dispose(); _rendered = null; _scene = null; foreach (var bitmap in _bitmaps.Values) bitmap.Dispose(); _bitmaps.Clear(); }
}
