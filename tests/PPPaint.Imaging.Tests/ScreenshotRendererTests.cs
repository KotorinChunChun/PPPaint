using Microsoft.Graphics.Canvas;
using PPPaint.Core;
using PPPaint.Imaging;
using Windows.Storage.Streams;

namespace PPPaint.Imaging.Tests;

[TestClass]
public sealed class ScreenshotRendererTests
{
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
        doc.Add(new("highlight", ScreenshotTool.Highlight, new(5, 5, 20, 20), new(255, 220, 0)));
        using (var target = ScreenshotRenderer.Render(device, doc.State))
        {
            var c = target.GetPixelColors()[10 * 60 + 10]; Assert.IsTrue(c.B > 100 && c.B < 255); Assert.AreEqual((byte)255, c.A);
        }
        doc.Add(new("erase", ScreenshotTool.Eraser, new(30, 10, 10, 10), Rgba32.Transparent, Width: 12, Points: [new(35, 15), new(40, 15)]));
        using var erased = ScreenshotRenderer.Render(device, doc.State);
        Assert.AreEqual((byte)0, erased.GetPixelColors()[15 * 60 + 36].A);
    }
}
