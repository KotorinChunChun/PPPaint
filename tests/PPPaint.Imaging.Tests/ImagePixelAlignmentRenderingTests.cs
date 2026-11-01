using System.Numerics;
using Microsoft.Graphics.Canvas;
using PPPaint.Core;

namespace PPPaint.Imaging.Tests;

// v0.2.30: why moved/pasted images must stay on whole pixels.
[TestClass]
public sealed class ImagePixelAlignmentRenderingTests
{
    [TestMethod]
    public void WholePixelImageKeepsEveryPixelWhileHalfPixelImageBlurs()
    {
        using var device = new CanvasDevice();
        var stripes = new byte[8 * 4 * 4];
        for (var y = 0; y < 4; y++) for (var x = 0; x < 8; x++)
        {
            var index = (y * 8 + x) * 4; var value = (byte)(x % 2 == 0 ? 0 : 255);
            stripes[index] = stripes[index + 1] = stripes[index + 2] = value; stripes[index + 3] = 255;
        }
        var image = ScreenshotDocument.ImageObject(new ScreenshotFrame(8, 4, stripes), Vector2.Zero);
        var canvas = ScreenshotFrame.Transparent(16, 8);

        var origin = ScreenshotManipulation.WholePixels(new(4.6f, 2.4f));
        using (var aligned = ScreenshotRenderer.Render(device, new(canvas, [image.Move(origin)])))
        {
            var colors = aligned.GetPixelColors();
            for (var y = 0; y < 4; y++) for (var x = 0; x < 8; x++)
                Assert.AreEqual(stripes[(y * 8 + x) * 4], colors[((int)origin.Y + y) * 16 + (int)origin.X + x].R, $"({x}, {y})");
        }
        using var shifted = ScreenshotRenderer.Render(device, new(canvas, [image.Move(new(4.5f, 2.5f))]));
        var blurred = shifted.GetPixelColors()[(3 * 16) + 6];
        Assert.IsTrue(blurred.R is > 20 and < 235, "半画素の位置では補間で縞が混ざること。");
    }
}
