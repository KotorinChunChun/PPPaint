using Microsoft.Graphics.Canvas;
using PPPaint.Core;
using Windows.Storage.Streams;

namespace PPPaint.Imaging.Tests;

[TestClass]
public sealed class ShapeStyleRenderingTests
{
    [TestMethod]
    [DataRow(ScreenshotArrowStyle.Line, false)]
    [DataRow(ScreenshotArrowStyle.Line, true)]
    [DataRow(ScreenshotArrowStyle.DoubleEnded, false)]
    [DataRow(ScreenshotArrowStyle.DoubleEnded, true)]
    [DataRow(ScreenshotArrowStyle.Block, false)]
    [DataRow(ScreenshotArrowStyle.Block, true)]
    [DataRow(ScreenshotArrowStyle.ThreeDimensional, false)]
    [DataRow(ScreenshotArrowStyle.ThreeDimensional, true)]
    public async Task ArrowStylesAndFillArePreservedInPng(ScreenshotArrowStyle style, bool fill)
    {
        using var device = new CanvasDevice();
        var a = new ScreenshotAnnotation("a", ScreenshotTool.Arrow, new(15, 50, 95, 0), new(220, 30, 20), Points: [new(15, 50), new(110, 50)], Filled: fill, ArrowStyle: style);
        using var image = ScreenshotRenderer.Render(device, new(ScreenshotFrame.Transparent(140, 100), [a]));
        Assert.IsTrue(image.GetPixelColors().Any(c => c.A > 0));
        using var stream = new InMemoryRandomAccessStream(); await Win2DImageCodec.SaveAsync(image, stream, DocumentFileFormat.Png);
        using var read = await Win2DImageCodec.LoadAsync(device, stream);
        var before = image.GetPixelColors(); var after = read.GetPixelColors();
        for (var i = 0; i < before.Length; i++)
        {
            Assert.AreEqual(before[i].A, after[i].A);
            // PNG stores straight alpha; premultiply/unpremultiply can round a
            // partially covered edge by one channel value, but never move it.
            var tolerance = before[i].A is 0 or 255 ? 0 : 1;
            Assert.IsTrue(Math.Abs(before[i].R - after[i].R) <= tolerance && Math.Abs(before[i].G - after[i].G) <= tolerance && Math.Abs(before[i].B - after[i].B) <= tolerance);
        }
    }
    [TestMethod]
    public void RectangleFillIsOpaqueAndEachArrowAndNumberStyleLooksDifferent()
    {
        using var device = new CanvasDevice(); var frame = ScreenshotFrame.Transparent(140, 100);
        var rectangle = new ScreenshotAnnotation("r", ScreenshotTool.Rectangle, new(10, 10, 40, 40), new(255, 0, 0, 5), Filled: true);
        using (var image = ScreenshotRenderer.Render(device, new(frame, [rectangle]))) Assert.AreEqual((byte)255, image.GetPixelColors()[30 * 140 + 30].A);
        var arrows = new HashSet<string>();
        foreach (var style in Enum.GetValues<ScreenshotArrowStyle>())
        {
            var arrow = new ScreenshotAnnotation("a", ScreenshotTool.Arrow, new(15, 50, 95, 0), new(220, 30, 20), Points: [new(15, 50), new(110, 50)], Filled: true, ArrowStyle: style);
            using var image = ScreenshotRenderer.Render(device, new(frame, [arrow])); arrows.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(image.GetPixelBytes())));
        }
        Assert.HasCount(4, arrows);
        var numbers = new HashSet<string>();
        foreach (var style in Enum.GetValues<ScreenshotNumberStyle>()) foreach (var fill in new[] { false, true })
        {
            var number = new ScreenshotAnnotation("n", ScreenshotTool.Number, new(20, 20, 50, 50), new(220, 30, 20), Text: "7", NumberStyle: style, Filled: fill);
            using var image = ScreenshotRenderer.Render(device, new(frame, [number])); numbers.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(image.GetPixelBytes())));
        }
        Assert.HasCount(5, numbers); // Plain has no fill in either setting.
    }
}
