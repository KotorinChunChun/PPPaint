using Microsoft.Graphics.Canvas;
using PPPaint.Core;
using PPPaint.Imaging;

namespace PPPaint.Imaging.Tests;

[TestClass]
public sealed class CanvasResizeRenderingTests
{
    [TestMethod]
    public void CanvasPreviewMatchesCommittedPixelsIncludingAlphaMasksAndEffects()
    {
        using var device = new CanvasDevice();
        var bytes = new byte[40 * 30 * 4];
        for (var y = 0; y < 30; y++) for (var x = 0; x < 40; x++)
        {
            var i = (y * 40 + x) * 4; var alpha = x < 10 ? 128 : 255;
            bytes[i] = (byte)((x * 5 * alpha + 127) / 255); bytes[i + 1] = (byte)((y * 7 * alpha + 127) / 255); bytes[i + 2] = (byte)((180 * alpha + 127) / 255); bytes[i + 3] = (byte)alpha;
        }
        var doc = new ScreenshotDocument(new(40, 30, bytes));
        doc.Add(new("shape", ScreenshotTool.Redact, new(3, 3, 30, 20), new(255, 0, 0), ClipBounds: new(3, 3, 28, 18), Cutouts: [new(8, 8, 6, 6)]));
        doc.Add(new("erase", ScreenshotTool.Eraser, new(0, 12, 40, 0), Rgba32.Black, Width: 3, Points: [new(0, 12), new(40, 12)]));
        doc.Add(new("blur", ScreenshotTool.Blur, new(2, 2, 20, 25), Rgba32.Black, BlurRadius: 3));
        doc.Add(new("mosaic", ScreenshotTool.Mosaic, new(22, 0, 17, 25), Rgba32.Black, MosaicSize: 4));
        foreach (var background in new[] { Rgba32.White, Rgba32.Transparent, new Rgba32(40, 100, 180, 128) })
        {
            doc.SetBackground(background); var before = doc.State;
            foreach (var size in new[] { (60, 50), (20, 10), (50, 14), (17, 41), (1, 1) }) foreach (var centered in new[] { true, false })
            {
                var layout = ScreenshotCanvasResize.Create(before, size.Item1, size.Item2, centered);
                using var preview = ScreenshotRenderer.RenderCanvasPreview(device, before, layout);
                using var committed = ScreenshotRenderer.Render(device, layout.Apply(before));
                CollectionAssert.AreEqual(committed.GetPixelBytes(), preview.GetPixelBytes(), $"{background}/{size}/{centered}");
                Assert.AreSame(before, doc.State);
            }
        }
    }
    [TestMethod]
    public void AddedCanvasUsesItsBackgroundAndOriginalImageKeepsPixelSize()
    {
        using var device = new CanvasDevice(); var doc = ScreenshotDocument.FromImage(ScreenshotFrame.Blank(20, 10));
        doc.Add(new("red", ScreenshotTool.Redact, new(5, 2, 5, 4), new(255, 0, 0)));
        doc.SetBackground(new(0, 120, 200)); doc.ResizeCanvas(30, 20, true);
        using var result = ScreenshotRenderer.Render(device, doc.State);
        Assert.AreEqual(new Rgba32(0, 120, 200), Rgba32.FromPremultipliedBgra(result.GetPixelBytes(0, 0, 1, 1)));
        Assert.AreEqual(Rgba32.White, Rgba32.FromPremultipliedBgra(result.GetPixelBytes(5, 5, 1, 1)));
        Assert.AreEqual(new Rgba32(255, 0, 0), Rgba32.FromPremultipliedBgra(result.GetPixelBytes(10, 7, 1, 1)));
        Assert.AreEqual(new ScreenshotBox(10, 7, 5, 4), doc.State.Annotations[^1].Bounds);
    }
}
