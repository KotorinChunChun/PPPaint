using System.Numerics;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class ScreenshotEditingHelpersTests
{
    [TestMethod]
    public void DescribeShowsContentOrToolName()
    {
        Assert.AreEqual("画像: 画面.png", ScreenshotCommands.Describe(ScreenshotDocument.ImageObject(ScreenshotFrame.Transparent(1, 1), Vector2.Zero, "画面.png")));
        Assert.AreEqual("番号: 3", ScreenshotCommands.Describe(new("n", ScreenshotTool.Number, default, Rgba32.Black, Text: "3")));
        Assert.AreEqual("吹き出し", ScreenshotCommands.Describe(new("c", ScreenshotTool.Callout, default, Rgba32.Black, Text: "本文")));
        Assert.AreEqual("ペン", ScreenshotCommands.Describe(new("p", ScreenshotTool.Pen, default, Rgba32.Black)));
        var longText = ScreenshotCommands.Describe(new("t", ScreenshotTool.Text, default, Rgba32.Black, Text: new string('あ', 60)));
        Assert.AreEqual(49, longText.Length); Assert.EndsWith("…", longText);
    }

    [TestMethod]
    public void FitTextBoundsGrowsToTheLongestLineWithinTheCanvas()
    {
        var text = new ScreenshotAnnotation("t", ScreenshotTool.Text, new(10, 10, 50, 20), Rgba32.Black, FontSize: 10);
        Assert.AreEqual(new ScreenshotBox(10, 10, 80, 32), ScreenshotEditing.FitTextBounds(text, "12345678\r\nab", 200));
        // Lines longer than the space to the right edge wrap into extra rows.
        Assert.AreEqual(new ScreenshotBox(10, 10, 40, 48), ScreenshotEditing.FitTextBounds(text with { Bounds = new(10, 10, 20, 20) }, "123456789012", 50));
        var number = text with { Tool = ScreenshotTool.Number };
        Assert.AreEqual(number.Bounds, ScreenshotEditing.FitTextBounds(number, "123456789", 200));
    }

    [TestMethod]
    public void AppendStrokePointSkipsJitterAndKeepsBoundsAroundAllPoints()
    {
        var pen = new ScreenshotAnnotation("p", ScreenshotTool.Pen, new(5, 5, 0, 0), Rgba32.Black, Points: [new(5, 5)]);
        Assert.AreSame(pen, ScreenshotEditing.AppendStrokePoint(pen, new(5.3f, 5.2f)));
        var next = ScreenshotEditing.AppendStrokePoint(ScreenshotEditing.AppendStrokePoint(pen, new(2, 9)), new(5, 5.5f));
        Assert.HasCount(3, next.Points);
        Assert.AreEqual(new ScreenshotBox(2, 5, 3, 4), next.Bounds);
        var flat = ScreenshotEditing.AppendStrokePoint(pen, new(9, 5));
        Assert.AreEqual(new ScreenshotBox(5, 5, 4, 1), flat.Bounds);
    }
}
