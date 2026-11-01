using Microsoft.Graphics.Canvas;
using PPPaint.Core;
using Windows.Storage.Streams;

namespace PPPaint.Imaging.Tests;

[TestClass]
public sealed class ScreenshotRendererTests
{
    [TestMethod]
    public async Task RectangularBlurChangesOnlyItsRegionRespectsOrderAndSurvivesPng()
    {
        using var device = new CanvasDevice();
        var pixels = new byte[64 * 48 * 4];
        for (var y = 0; y < 48; y++) for (var x = 0; x < 64; x++) { var index = (y * 64 + x) * 4; pixels[index] = pixels[index + 1] = pixels[index + 2] = (byte)(x % 4 < 2 ? 0 : 255); pixels[index + 3] = 255; }
        var doc = ScreenshotDocument.FromImage(new(64, 48, pixels)); var initial = doc.State;
        doc.Add(new("blur", ScreenshotTool.Blur, new(16, 12, 32, 24), Rgba32.Black, BlurRadius: 4));
        using (var blurred = ScreenshotRenderer.Render(device, doc.State))
        {
            var colors = blurred.GetPixelColors(); Assert.AreEqual((byte)0, colors[0].R);
            Assert.IsTrue(colors[20 * 64 + 24].R is > 40 and < 215); Assert.AreEqual((byte)255, colors[20 * 64 + 24].A);
            using var encoded = new InMemoryRandomAccessStream(); await Win2DImageCodec.SaveAsync(blurred, encoded, DocumentFileFormat.Png);
            using var decoded = await Win2DImageCodec.LoadAsync(device, encoded); Assert.AreEqual(colors[20 * 64 + 24], decoded.GetPixelColors()[20 * 64 + 24]);
        }
        doc.Add(new("top", ScreenshotTool.Redact, new(20, 16, 8, 8), new(255, 0, 0)));
        using (var top = ScreenshotRenderer.Render(device, doc.State)) { var p = top.GetPixelColors()[20 * 64 + 24]; Assert.AreEqual((byte)255, p.R); Assert.AreEqual((byte)0, p.B); }
        doc.Undo(); doc.Reorder(new HashSet<string> { "blur" }, ScreenshotStackOrder.Back);
        using (var back = ScreenshotRenderer.Render(device, doc.State)) Assert.AreEqual((byte)0, back.GetPixelColors()[20 * 64 + 24].R);
        doc.Undo(); doc.Undo(); Assert.AreSame(initial, doc.State);
    }
    [TestMethod]
    public void ImageMirroringAndQuarterTurnPreservePixelOrientation()
    {
        using var device = new CanvasDevice();
        var frame = new ScreenshotFrame(2, 1, new byte[] { 0, 0, 255, 255, 255, 0, 0, 255 });
        var image = ScreenshotDocument.ImageObject(frame, System.Numerics.Vector2.Zero);
        using (var mirrored = ScreenshotRenderer.Render(device, new(ScreenshotFrame.Transparent(2, 1), [image with { MirrorX = true }])))
        {
            Assert.AreEqual((byte)255, mirrored.GetPixelColors()[0].B); Assert.AreEqual((byte)255, mirrored.GetPixelColors()[1].R);
        }
        using var rotated = ScreenshotRenderer.Render(device, new(ScreenshotFrame.Transparent(1, 2), [image with { Bounds = new(0, 0, 1, 2), Rotation = 90 }]));
        Assert.AreEqual((byte)255, rotated.GetPixelColors()[0].R); Assert.AreEqual((byte)255, rotated.GetPixelColors()[1].B);
    }
    [TestMethod]
    public void CropKeepsRasterEditableButExportClipsToCanvasAndUndoRestoresFullImage()
    {
        using var device = new CanvasDevice();
        var bytes = Enumerable.Repeat(new byte[] { 0, 0, 255, 255 }, 20).SelectMany(p => p).ToArray();
        var doc = ScreenshotDocument.FromImage(new(5, 4, bytes)); var before = doc.State;
        doc.Crop(1, 1, 3, 2);
        Assert.AreEqual(-1f, doc.State.Annotations[0].Bounds.X);
        using var target = ScreenshotRenderer.Render(device, doc.State);
        Assert.AreEqual(3u, target.SizeInPixels.Width); Assert.AreEqual(2u, target.SizeInPixels.Height);
        Assert.IsTrue(target.GetPixelColors().All(c => c.R == 255 && c.A == 255));
        doc.Undo(); Assert.AreSame(before, doc.State);
    }
    [TestMethod]
    public void RasterAndShapesRespectSharedStackOrderAndExportContainsOnlyCompositedPixels()
    {
        using var device = new CanvasDevice();
        var doc = ScreenshotDocument.FromImage(ScreenshotFrame.Blank(40, 30));
        doc.Add(new("red", ScreenshotTool.Redact, new(0, 0, 30, 30), new(255, 0, 0)));
        var bluePixels = Enumerable.Repeat(new byte[] { 255, 0, 0, 255 }, 100).SelectMany(p => p).ToArray();
        var image = doc.InsertImage(new(10, 10, bluePixels), new(5, 5));
        using (var front = ScreenshotRenderer.Render(device, doc.State)) Assert.AreEqual((byte)255, front.GetPixelColors()[6 * 40 + 6].B);
        doc.Reorder(new HashSet<string> { image }, ScreenshotStackOrder.Back);
        using var back = ScreenshotRenderer.Render(device, doc.State); var pixel = back.GetPixelColors()[6 * 40 + 6];
        Assert.AreEqual((byte)255, pixel.R); Assert.AreEqual((byte)0, pixel.B);
        doc.Undo(); Assert.AreEqual(ScreenshotTool.Image, doc.State.Annotations[^1].Tool);
    }
    [TestMethod]
    public async Task RedactionSurvivesPngRoundTripAsOpaquePixels()
    {
        using var device = new CanvasDevice();
        var doc = new ScreenshotDocument(ScreenshotFrame.Blank(60, 40));
        doc.Add(new("secret", ScreenshotTool.Redact, new(10, 10, 30, 20), new(0, 0, 0, 1)));
        using var rendered = ScreenshotRenderer.Render(device, doc.State);
        using var stream = new InMemoryRandomAccessStream();
        await Win2DImageCodec.SaveAsync(rendered, stream, DocumentFileFormat.Png);
        using var loaded = await Win2DImageCodec.LoadAsync(device, stream);
        var color = loaded.GetPixelColors()[20 * 60 + 20];
        Assert.AreEqual((byte)255, color.A); Assert.AreEqual((byte)0, color.R); Assert.AreEqual((byte)0, color.G); Assert.AreEqual((byte)0, color.B);
        var outside = loaded.GetPixelColors()[0]; Assert.AreEqual((byte)255, outside.R);
    }
    [TestMethod]
    public void HighlightLeavesUnderlyingPixelsVisibleAndEraserClearsAlpha()
    {
        using var device = new CanvasDevice();
        var doc = new ScreenshotDocument(ScreenshotFrame.Blank(60, 40));
        doc.SetBackground(Rgba32.Transparent);
        doc.Add(new("highlight", ScreenshotTool.Highlight, new(5, 5, 20, 20), new(255, 220, 0)));
        using (var target = ScreenshotRenderer.Render(device, doc.State))
        {
            var c = target.GetPixelColors()[10 * 60 + 10]; Assert.IsTrue(c.B > 100 && c.B < 255); Assert.AreEqual((byte)255, c.A);
        }
        doc.Add(new("erase", ScreenshotTool.Eraser, new(30, 10, 10, 10), Rgba32.Transparent, Width: 12, Points: [new(35, 15), new(40, 15)]));
        using var erased = ScreenshotRenderer.Render(device, doc.State);
        Assert.AreEqual((byte)0, erased.GetPixelColors()[15 * 60 + 36].A);
        doc.SetBackground(Rgba32.White);
        using var white = ScreenshotRenderer.Render(device, doc.State); Assert.AreEqual((byte)255, white.GetPixelColors()[15 * 60 + 36].A); Assert.AreEqual((byte)255, white.GetPixelColors()[15 * 60 + 36].R);
    }
}
