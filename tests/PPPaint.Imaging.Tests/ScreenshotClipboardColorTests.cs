using Microsoft.Graphics.Canvas;
using PPPaint.Core;
using PPPaint.Imaging;
using Windows.Storage.Streams;

namespace PPPaint.Imaging.Tests;

[TestClass]
public sealed class ScreenshotClipboardColorTests
{
    [TestMethod]
    public async Task WhiteAndTransparentCanvasColorsSurvivePngAndEditablePaste()
    {
        using var device = new CanvasDevice(); var doc = new ScreenshotDocument(ScreenshotFrame.Transparent(40, 30));
        doc.Add(new("red", ScreenshotTool.Redact, new(8, 8, 12, 12), new(255, 0, 0)));
        foreach (var background in new[] { Rgba32.White, new Rgba32(40, 120, 200), Rgba32.Transparent, new Rgba32(40, 120, 200, 128) })
        {
            doc.SetBackground(background);
            using var original = ScreenshotRenderer.Render(device, doc.State); var colors = original.GetPixelColors(); Assert.AreEqual(background.A, colors[0].A);
            if (background.A != 0) { Assert.AreEqual((byte)((background.R * background.A + 127) / 255), colors[0].R); Assert.AreEqual((byte)((background.G * background.A + 127) / 255), colors[0].G); Assert.AreEqual((byte)((background.B * background.A + 127) / 255), colors[0].B); }
            var payload = ScreenshotEditing.Capture(doc.State, new(0, 0, 40, 30));
            using var pasted = ScreenshotRenderer.Render(device, new(ScreenshotFrame.Transparent(40, 30), payload.ObjectsAt(System.Numerics.Vector2.Zero)));
            CollectionAssert.AreEqual(colors, pasted.GetPixelColors()); Assert.IsTrue(payload.Scene.Annotations.Any(a => a.Tool == ScreenshotTool.Redact));
            using var png = new InMemoryRandomAccessStream(); await Win2DImageCodec.SaveAsync(original, png, DocumentFileFormat.Png);
            using var decoded = await Win2DImageCodec.LoadAsync(device, png); CollectionAssert.AreEqual(colors, decoded.GetPixelColors());
        }
    }
    [TestMethod]
    public void CuttingRangeLeavesOutsideExactlyAndUsesCanvasBackgroundOrAlpha()
    {
        using var device = new CanvasDevice(); var pixels = Enumerable.Repeat(new byte[] { 0, 0, 255, 255 }, 40 * 30).SelectMany(b => b).ToArray();
        var doc = ScreenshotDocument.FromImage(new(40, 30, pixels)); doc.Add(new("blue", ScreenshotTool.Redact, new(5, 5, 25, 20), new(0, 0, 255)));
        foreach (var background in new[] { new Rgba32(0, 200, 0), Rgba32.Transparent })
        {
            doc.SetBackground(background); var before = doc.State;
            using var original = ScreenshotRenderer.Render(device, before); var old = original.GetPixelColors();
            doc.DeleteRange(new(10, 10, 10, 10)); using var cut = ScreenshotRenderer.Render(device, doc.State); var next = cut.GetPixelColors();
            for (var y = 0; y < 30; y++) for (var x = 0; x < 40; x++)
            {
                var actual = next[y * 40 + x];
                if (x is >= 10 and < 20 && y is >= 10 and < 20) { Assert.AreEqual(background.A, actual.A); if (background.A != 0) Assert.AreEqual(background.G, actual.G); }
                else Assert.AreEqual(old[y * 40 + x], actual);
            }
            doc.Undo(); Assert.AreSame(before, doc.State);
        }
    }
    [TestMethod]
    public void ClippedCopiedImageAndTransparentEraserNeverRevealOutsidePixels()
    {
        using var device = new CanvasDevice(); var doc = ScreenshotDocument.FromImage(ScreenshotFrame.Blank(40, 30)); doc.SetBackground(Rgba32.Transparent);
        doc.Add(new("erase", ScreenshotTool.Eraser, new(5, 15, 30, 0), Rgba32.Black, Width: 8, Points: [new(5, 15), new(35, 15)]));
        var payload = ScreenshotEditing.Capture(doc.State, new(10, 5, 20, 20));
        using var expected = ScreenshotRenderer.Render(device, doc.State); var source = expected.GetPixelColors();
        using var pasted = ScreenshotRenderer.Render(device, new(ScreenshotFrame.Transparent(40, 30), payload.ObjectsAt(new(5, 5)))); var colors = pasted.GetPixelColors();
        Assert.AreEqual((byte)0, colors[0].A); Assert.AreEqual((byte)0, colors[15 * 40 + 15].A); Assert.AreEqual(source[7 * 40 + 12], colors[7 * 40 + 7]);
        Assert.AreEqual((byte)0, colors[7 * 40 + 27].A);
    }
    [TestMethod]
    public void CutoutsRemainTransparentThroughBlurAndMosaic()
    {
        using var device = new CanvasDevice(); var doc = ScreenshotDocument.FromImage(ScreenshotFrame.Blank(40, 30)); doc.SetBackground(Rgba32.Transparent);
        doc.Add(new("blur", ScreenshotTool.Blur, new(2, 2, 35, 25), Rgba32.Black));
        doc.Add(new("mosaic", ScreenshotTool.Mosaic, new(3, 3, 32, 24), Rgba32.Black));
        doc.DeleteRange(new(10, 10, 10, 10));
        using var result = ScreenshotRenderer.Render(device, doc.State); Assert.AreEqual((byte)0, result.GetPixelColors()[15 * 40 + 15].A);
    }
}
