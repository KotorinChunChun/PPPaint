using System.Numerics;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class ScreenshotCalloutTests
{
    [TestMethod]
    public void TailIsSelectableIncludedInCopyBoundsAndTransformsWithBody()
    {
        var item = new ScreenshotAnnotation("callout", ScreenshotTool.Callout, new(20, 20, 80, 40), Rgba32.Black, CalloutTip: new(40, 100));
        Assert.IsTrue(item.Hit(new(40, 95), 1)); Assert.IsFalse(item.Hit(new(90, 95), 1));
        Assert.AreEqual(100f, ScreenshotGeometry.Bounds([item]).Bottom);
        var moved = item.Move(new(10, 5)); Assert.AreEqual(new Vector2(50, 105), moved.Tail);
        var resized = item.Resize(new(40, 40, 160, 80)); Assert.AreEqual(new Vector2(80, 200), resized.Tail);
        var doc = new ScreenshotDocument(ScreenshotFrame.Blank(200, 200)); doc.Add(item); var before = doc.State;
        doc.Replace(item with { CalloutTip = new(150, 30) }); doc.Undo(); Assert.AreSame(before, doc.State);
    }
    [TestMethod]
    public void TailAttachesToAllFourSidesAndInsideTipCreatesPlainBody()
    {
        var box = new ScreenshotBox(20, 20, 80, 40);
        foreach (var tip in new[] { new Vector2(60, 0), new(120, 40), new(60, 100), new(0, 40) })
        { var polygon = ScreenshotGeometry.CalloutOutline(box, tip); Assert.HasCount(7, polygon); Assert.Contains(tip, polygon); }
        Assert.HasCount(4, ScreenshotGeometry.CalloutOutline(box, new(40, 40)));
    }
}
