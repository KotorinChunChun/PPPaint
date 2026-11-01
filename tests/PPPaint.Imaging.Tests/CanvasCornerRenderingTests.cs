using Microsoft.Graphics.Canvas;
using PPPaint.Core;
using PPPaint.Imaging;

namespace PPPaint.Imaging.Tests;

[TestClass]
public sealed class CanvasCornerRenderingTests
{
    [TestMethod]
    public void EightHandlePreviewsAndCommittedReframesMatchEveryPixelWithAlphaAndEffects()
    {
        using var device = new CanvasDevice();
        var bytes = new byte[40 * 30 * 4];
        for (var y = 0; y < 30; y++) for (var x = 0; x < 40; x++)
        {
            var i = (y * 40 + x) * 4; var alpha = x < 10 ? 128 : 255;
            bytes[i] = (byte)(x * 4 * alpha / 255); bytes[i + 1] = (byte)(y * 5 * alpha / 255); bytes[i + 2] = (byte)(150 * alpha / 255); bytes[i + 3] = (byte)alpha;
        }
        var doc = new ScreenshotDocument(new(40, 30, bytes));
        doc.Add(new("shape", ScreenshotTool.Redact, new(4, 4, 30, 20), new(255, 0, 0), ClipBounds: new(5, 5, 27, 17), Cutouts: [new(10, 10, 4, 4)]));
        doc.Add(new("erase", ScreenshotTool.Eraser, new(0, 12, 40, 0), Rgba32.Black, Width: 3, Points: [new(0, 12), new(40, 12)]));
        doc.Add(new("blur", ScreenshotTool.Blur, new(2, 2, 16, 25), Rgba32.Black, BlurRadius: 2));
        doc.Add(new("mosaic", ScreenshotTool.Mosaic, new(22, 0, 17, 25), Rgba32.Black, MosaicSize: 4));
        foreach (var background in new[] { Rgba32.White, Rgba32.Transparent, new Rgba32(20, 60, 120, 128) })
        {
            doc.SetBackground(background); var before = doc.State;
            foreach (var handle in Enum.GetValues<ScreenshotCanvasHandle>()) foreach (var delta in new[] { -3d, 3d }) foreach (var centered in new[] { false, true })
            {
                var bounds = ScreenshotCanvasGeometry.ResizeFromDrag(40, 30, handle, 10, 20, 10 + delta, 20 + delta, 1, centered);
                var layout = ScreenshotCanvasResize.FromBounds(before, bounds);
                using var preview = ScreenshotRenderer.RenderCanvasPreview(device, before, layout);
                using var committed = ScreenshotRenderer.Render(device, layout.Apply(before));
                CollectionAssert.AreEqual(committed.GetPixelBytes(), preview.GetPixelBytes(), $"{background}/{handle}/{delta}/{centered}");
                Assert.AreSame(before, doc.State);
            }
        }
    }

    [TestMethod]
    public void TopLeftExtensionAddsBackgroundAndMovesTheOriginalPixelsWithoutResampling()
    {
        using var device = new CanvasDevice();
        var doc = ScreenshotDocument.FromImage(ScreenshotFrame.Blank(20, 10));
        doc.Add(new("red", ScreenshotTool.Pen, new(0, 0, 0, 0), new(255, 0, 0), Width: 1, Points: [new(.1f, .1f)]));
        foreach (var background in new[] { Rgba32.Transparent, Rgba32.White, new Rgba32(0, 100, 200) })
        {
            doc.SetBackground(background); var before = doc.State;
            var layout = ScreenshotCanvasResize.FromBounds(before, new(-2, -3, 24, 16));
            using var original = ScreenshotRenderer.Render(device, before);
            using var extended = ScreenshotRenderer.Render(device, layout.Apply(before));
            var originalBytes = original.GetPixelBytes(); var extendedBytes = extended.GetPixelBytes();
            for (var y = 0; y < 10; y++)
                CollectionAssert.AreEqual(originalBytes.AsSpan(y * 20 * 4, 20 * 4).ToArray(), extendedBytes.AsSpan(((y + 3) * 24 + 2) * 4, 20 * 4).ToArray());
            Assert.AreEqual(background, Rgba32.FromPremultipliedBgra(extended.GetPixelBytes(0, 0, 1, 1)));
            Assert.AreEqual(new Rgba32(255, 0, 0), Rgba32.FromPremultipliedBgra(extended.GetPixelBytes(2, 3, 1, 1)));
            Assert.AreSame(before, doc.State);
        }
    }
}
