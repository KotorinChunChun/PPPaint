using Microsoft.Graphics.Canvas;
using PPPaint.Core;
using Windows.Storage.Streams;

namespace PPPaint.Imaging.Tests;

[TestClass]
public sealed class TextOpacityTests
{
    [TestMethod]
    [DataRow(1f, 255)]
    [DataRow(.5f, 128)]
    [DataRow(.25f, 64)]
    public async Task TextOpacityIsRenderedAndPreservedThroughPng(float opacity, int expectedAlpha)
    {
        using var device = new CanvasDevice();
        var text = new ScreenshotAnnotation("t", ScreenshotTool.Text, new(10, 10, 180, 70), Rgba32.Black, FontSize: 42, Text: "TEST", Opacity: opacity);
        using var rendered = ScreenshotRenderer.Render(device, new(ScreenshotFrame.Transparent(200, 100), [text]));
        var colors = rendered.GetPixelColors();
        Assert.AreEqual(expectedAlpha, (int)colors.Max(c => c.A));
        Assert.AreEqual((byte)0, colors[0].A);
        using var stream = new InMemoryRandomAccessStream();
        await Win2DImageCodec.SaveAsync(rendered, stream, DocumentFileFormat.Png);
        using var reopened = await Win2DImageCodec.LoadAsync(device, stream);
        CollectionAssert.AreEqual(colors, reopened.GetPixelColors());
    }

    [TestMethod]
    public void TextDefaultsToTheSameOpaqueRenderingAsExplicitFullOpacity()
    {
        using var device = new CanvasDevice();
        var text = new ScreenshotAnnotation("t", ScreenshotTool.Text, new(10, 10, 180, 70), Rgba32.Black, FontSize: 42, Text: "TEST");
        var frame = ScreenshotFrame.Transparent(200, 100);
        using var defaults = ScreenshotRenderer.Render(device, new(frame, [text]));
        using var explicitOpacity = ScreenshotRenderer.Render(device, new(frame, [text with { Opacity = 1 }]));
        var colors = defaults.GetPixelColors();
        Assert.AreEqual((byte)255, colors.Max(c => c.A));
        CollectionAssert.AreEqual(colors, explicitOpacity.GetPixelColors());
    }
}
