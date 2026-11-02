using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using PPPaint.Core;

namespace PPPaint.Imaging.Tests;

[TestClass]
public sealed class CalloutTextSizingTests
{
    [TestMethod]
    [DataRow("複数行の説明です。\n二行目もはみ出さずに表示します。", 24f)]
    [DataRow("Long annotation text that wraps around the available width.\nSecond line.", 36f)]
    public void CalloutGrowsForTextAndKeepsItsTargetPoint(string text, float font)
    {
        using var device = new CanvasDevice();
        var item = new ScreenshotAnnotation("c", ScreenshotTool.Callout, new(40, 100, 70, 30), new(220, 30, 20), FontSize: font, CalloutTip: new(70, 170));
        var fitted = ScreenshotCalloutText.Fit(device, item, text, 360);
        Assert.AreEqual(item.Tail, fitted.Tail); Assert.AreEqual(text, fitted.Text);
        Assert.IsTrue(fitted.Bounds.Width >= item.Bounds.Width); Assert.IsTrue(fitted.Bounds.Height > item.Bounds.Height);
        Assert.IsTrue(fitted.Bounds.Right <= 360.01f);
        using var format = new CanvasTextFormat { FontFamily = "Yu Gothic UI", FontSize = font, WordWrapping = CanvasWordWrapping.Wrap };
        using var layout = new CanvasTextLayout(device, text, format, fitted.Bounds.Width - 16, 16384);
        Assert.IsTrue(layout.LayoutBounds.Height <= fitted.Bounds.Height - 12 + .01);
        using var rendered = ScreenshotRenderer.Render(device, new(ScreenshotFrame.Transparent(400, 400), [fitted]));
        Assert.IsTrue(rendered.GetPixelColors().Any(c => c.A > 0));
    }
    [TestMethod]
    public void WideTailAndOppositeSideTipRenderWithoutHolesInTheBody()
    {
        using var device = new CanvasDevice();
        var callout = new ScreenshotAnnotation("c", ScreenshotTool.Callout, new(30, 30, 80, 60), Rgba32.Black,
            CalloutTip: new(60, 10), CalloutEdge: 2, CalloutStart: .1f, CalloutEnd: .9f);
        using var image = ScreenshotRenderer.Render(device, new(ScreenshotFrame.Transparent(140, 120), [callout]));
        var pixels = image.GetPixelColors();
        Assert.AreEqual((byte)255, pixels[55 * 140 + 60].A); Assert.AreEqual((byte)255, pixels[55 * 140 + 60].R);
    }
}
