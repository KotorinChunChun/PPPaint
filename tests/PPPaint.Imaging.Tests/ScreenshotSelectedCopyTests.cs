using Microsoft.Graphics.Canvas;
using PPPaint.Core;
using PPPaint.Imaging;

namespace PPPaint.Imaging.Tests;

[TestClass]
public sealed class ScreenshotSelectedCopyTests
{
    [TestMethod]
    public void CopyExcludesBackgroundAndUnselectedObjectsAndKeepsTransparentPixels()
    {
        using var device = new CanvasDevice();
        var doc = new ScreenshotDocument(ScreenshotFrame.Blank(100, 80));
        doc.Add(new("other", ScreenshotTool.Redact, new(0, 0, 100, 80), new(0, 0, 255)));
        doc.Add(new("r", ScreenshotTool.Rectangle, new(20, 30, 40, 20), new(255, 0, 0), Width: 2));
        var copy = ScreenshotCopy.Capture(doc.State, null, new HashSet<string> { "r" });
        using var result = ScreenshotRenderer.Render(device, copy.Scene);
        var pixels = result.GetPixelColors();
        Assert.AreEqual(42u, result.SizeInPixels.Width); Assert.AreEqual(22u, result.SizeInPixels.Height);
        Assert.AreEqual((byte)0, pixels[11 * 42 + 21].A); Assert.IsFalse(pixels.Any(pixel => pixel.B > 0));
        Assert.IsTrue(pixels.Any(pixel => pixel.R == 255 && pixel.A > 0));
    }
}
