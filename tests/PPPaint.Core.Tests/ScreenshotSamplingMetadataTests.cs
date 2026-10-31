using System.Numerics;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class ScreenshotSamplingMetadataTests
{
    [TestMethod]
    public void MoveAndResizeTransformSamplingWithClipAndKeepEraserBackground()
    {
        var annotation = new ScreenshotAnnotation("sample", ScreenshotTool.Blur, new(10, 20, 20, 40), Rgba32.Black,
            ClipBounds: new(5, 10, 40, 60), SamplingBounds: new(5, 10, 40, 60), EraserBackground: new(30, 70, 110, 128));
        var moved = annotation.Move(new Vector2(-4, 7));
        Assert.AreEqual(new ScreenshotBox(1, 17, 40, 60), moved.SamplingBounds); Assert.AreEqual(moved.ClipBounds, moved.SamplingBounds);
        Assert.AreEqual(annotation.EraserBackground, moved.EraserBackground); Assert.AreEqual(new ScreenshotBox(5, 10, 40, 60), annotation.SamplingBounds);
        var resized = annotation.Resize(new(30, 60, 40, 20));
        Assert.AreEqual(new ScreenshotBox(20, 55, 80, 30), resized.SamplingBounds); Assert.AreEqual(resized.ClipBounds, resized.SamplingBounds);
        Assert.AreEqual(annotation.EraserBackground, resized.EraserBackground);
        var legacy = new ScreenshotAnnotation("legacy", ScreenshotTool.Mosaic, new(0, 0, 10, 10), Rgba32.Black);
        Assert.IsNull(legacy.Move(new(1, 2)).SamplingBounds); Assert.IsNull(legacy.Resize(new(1, 2, 20, 20)).SamplingBounds);
        Assert.IsNull(legacy.EraserBackground);
    }
}
