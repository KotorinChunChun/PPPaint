using Microsoft.Graphics.Canvas;
using PPPaint.Core;

namespace PPPaint.Imaging.Tests;

[TestClass]
public sealed class RegionLiftRenderingTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void LiftingRenderedRangePreservesOutsidePixelsAndTransparentBackground(bool transparent)
    {
        using var device = new CanvasDevice();
        var document = new ScreenshotDocument(ScreenshotFrame.Transparent(100, 80));
        document.SetBackground(transparent ? Rgba32.Transparent : Rgba32.White);
        document.Add(new("ink", ScreenshotTool.Pen, new(2, 10, 90, 40), new(255, 0, 0), Width: 3, Points: [new(2, 10), new(92, 50)]));
        document.Add(new("rect", ScreenshotTool.Redact, new(15, 15, 45, 40), new(20, 150, 220, 128)));
        document.Add(new("blur", ScreenshotTool.Blur, new(10, 10, 50, 50), Rgba32.Black, BlurRadius: 3));
        document.Add(new("mosaic", ScreenshotTool.Mosaic, new(12, 12, 44, 42), Rgba32.Black, MosaicSize: 4));
        var before = document.State;
        using var original = ScreenshotRenderer.Render(device, before); var source = original.GetPixelColors();
        var plan = ScreenshotRegionLifting.Create(device, before, new(20, 20, 20, 20));
        var moved = plan.Preview(new(50, 25), false);
        using var result = ScreenshotRenderer.Render(device, moved); var output = result.GetPixelColors();
        for (var y = 0; y < 80; y++) for (var x = 0; x < 100; x++)
        {
            var cut = x is >= 20 and < 40 && y is >= 20 and < 40;
            var destination = x is >= 70 and < 90 && y is >= 45 and < 65;
            if (!cut && !destination) Assert.AreEqual(source[y * 100 + x], output[y * 100 + x], $"Outside changed: {x}/{y}");
            if (cut) Assert.AreEqual(transparent ? (byte)0 : (byte)255, output[y * 100 + x].A);
        }
        Assert.AreEqual(ScreenshotTool.Pen, moved.Annotations.Single(a => a.Id == "ink").Tool);
        Assert.AreSame(before, document.State);
        plan.Commit(document, moved); document.Undo(); Assert.AreSame(before, document.State);
    }
}
