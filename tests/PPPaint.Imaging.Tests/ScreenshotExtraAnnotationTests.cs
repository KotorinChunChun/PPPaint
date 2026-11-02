using Microsoft.Graphics.Canvas;
using PPPaint.Core;
using Windows.Storage.Streams;

namespace PPPaint.Imaging.Tests;

[TestClass]
public sealed class ScreenshotExtraAnnotationTests
{
    [TestMethod]
    public async Task MosaicAffectsOnlyItsRectangleAndLeavesLaterAnnotationsClearInPng()
    {
        using var device = new CanvasDevice(); var bytes = new byte[64 * 48 * 4];
        for (var y = 0; y < 48; y++) for (var x = 0; x < 64; x++) { var i = (y * 64 + x) * 4; bytes[i] = bytes[i + 1] = bytes[i + 2] = (byte)(x % 2 == 0 ? 0 : 255); bytes[i + 3] = 255; }
        var doc = ScreenshotDocument.FromImage(new(64, 48, bytes)); var original = doc.State;
        doc.Add(new("mosaic", ScreenshotTool.Mosaic, new(8, 8, 32, 24), Rgba32.Black, MosaicSize: 8));
        using (var target = ScreenshotRenderer.Render(device, doc.State))
        {
            var pixels = target.GetPixelColors(); Assert.AreEqual((byte)0, pixels[0].R); Assert.AreEqual((byte)255, pixels[1].R);
            Assert.IsTrue(pixels[10 * 64 + 10].R is > 20 and < 235);
            Assert.AreEqual(pixels[10 * 64 + 10], pixels[10 * 64 + 14]); Assert.AreEqual(pixels[10 * 64 + 10], pixels[14 * 64 + 14]);
            using var encoded = new InMemoryRandomAccessStream(); await Win2DImageCodec.SaveAsync(target, encoded, DocumentFileFormat.Png);
            using var decoded = await Win2DImageCodec.LoadAsync(device, encoded); CollectionAssert.AreEqual(pixels, decoded.GetPixelColors());
        }
        doc.Add(new("top", ScreenshotTool.Redact, new(12, 12, 8, 8), new(255, 0, 0)));
        using (var top = ScreenshotRenderer.Render(device, doc.State)) Assert.AreEqual((byte)255, top.GetPixelColors()[14 * 64 + 14].R);
        doc.Undo(); doc.Reorder(new HashSet<string> { "mosaic" }, ScreenshotStackOrder.Back);
        using (var back = ScreenshotRenderer.Render(device, doc.State)) Assert.AreEqual((byte)0, back.GetPixelColors()[10 * 64 + 10].R);
        doc.Undo(); doc.Undo(); Assert.AreSame(original, doc.State);
    }

    [TestMethod]
    public async Task TextOpacityAndCalloutTailAreFlattenedIntoJpegWithWhiteTransparency()
    {
        using var device = new CanvasDevice(); var background = ScreenshotFrame.Transparent(200, 120);
        var text = new ScreenshotAnnotation("text", ScreenshotTool.Text, new(10, 10, 160, 55), Rgba32.Black, FontSize: 42, Text: "TEST", Opacity: .25f);
        using (var translucent = ScreenshotRenderer.Render(device, new(background, [text])))
        {
            var alpha = translucent.GetPixelColors().Max(p => p.A); Assert.IsTrue(alpha is > 20 and <= 64);
        }
        var callout = new ScreenshotAnnotation("callout", ScreenshotTool.Callout, new(20, 20, 100, 50), new(255, 0, 0), Text: "注釈", CalloutTip: new(60, 105));
        using var rendered = ScreenshotRenderer.Render(device, new(background, [callout]));
        var colors = rendered.GetPixelColors(); Assert.IsTrue(colors[96 * 200 + 60].A > 0); Assert.AreEqual((byte)0, colors[96 * 200 + 170].A);
        using var stream = new InMemoryRandomAccessStream(); await Win2DImageCodec.SaveAsync(rendered, stream, DocumentFileFormat.Jpeg);
        using var jpeg = await Win2DImageCodec.LoadAsync(device, stream); Assert.AreEqual(200u, jpeg.SizeInPixels.Width); Assert.AreEqual(120u, jpeg.SizeInPixels.Height);
        var corner = jpeg.GetPixelColors()[0]; Assert.AreEqual((byte)255, corner.A); Assert.IsTrue(corner.R > 245 && corner.G > 245 && corner.B > 245);
    }
}
