namespace PPPaint.Core.Tests;

[TestClass]
public sealed class ScreenshotCopyTests
{
    [TestMethod]
    public void SelectedObjectsHaveTightTransparentSceneAndEditableOriginalPositions()
    {
        var doc = new ScreenshotDocument(ScreenshotFrame.Blank(300, 200));
        doc.Add(new("selected", ScreenshotTool.Rectangle, new(20, 30, 40, 20), Rgba32.Black, Width: 2));
        doc.Add(new("other", ScreenshotTool.Rectangle, new(10, 10, 200, 100), Rgba32.Black));
        var before = doc.State; var transfer = ScreenshotCopy.Capture(before, null, new HashSet<string> { "selected" });
        Assert.AreEqual(42, transfer.Scene.Frame.Width); Assert.AreEqual(22, transfer.Scene.Frame.Height);
        Assert.AreEqual(new System.Numerics.Vector2(19, 29), transfer.Origin);
        Assert.AreEqual(Rgba32.Transparent, transfer.Scene.Background); Assert.IsFalse(transfer.Scene.Frame.HasVisiblePixels());
        Assert.HasCount(1, transfer.Scene.Annotations); Assert.AreEqual("selected", transfer.Scene.Annotations[0].Id);
        var pasted = transfer.ObjectsAt(transfer.Origin); Assert.HasCount(1, pasted);
        Assert.AreEqual(before.Annotations[0].Bounds, pasted[0].Bounds); Assert.AreNotEqual("selected", pasted[0].Id);
        Assert.AreSame(before, doc.State);
    }
    [TestMethod]
    public void MultipleObjectsIncludeAllBoundsAndEmptyOrRegionSelectionKeepsFullCanvas()
    {
        var doc = new ScreenshotDocument(ScreenshotFrame.Transparent(300, 200));
        doc.Add(new("a", ScreenshotTool.Redact, new(20, 30, 10, 10), Rgba32.Black, Width: 2));
        doc.Add(new("b", ScreenshotTool.Redact, new(70, 80, 10, 10), Rgba32.Black, Width: 2));
        var ids = new HashSet<string> { "a", "b" };
        var selected = ScreenshotCopy.Capture(doc.State, null, ids);
        Assert.AreEqual(62, selected.Scene.Frame.Width); Assert.AreEqual(62, selected.Scene.Frame.Height); Assert.HasCount(2, selected.Scene.Annotations);
        foreach (var full in new[] { ScreenshotCopy.Capture(doc.State, null, new HashSet<string>()), ScreenshotCopy.Capture(doc.State, new(0, 0, 300, 200), ids) })
        { Assert.AreEqual(300, full.Scene.Frame.Width); Assert.AreEqual(200, full.Scene.Frame.Height); }
    }
    [TestMethod]
    public void OnePixelAndPartlyClippedObjectsCopyWithoutCanvasMutation()
    {
        var doc = new ScreenshotDocument(ScreenshotFrame.Transparent(100, 80));
        doc.Add(new("dot", ScreenshotTool.Pen, new(10, 20, 0, 0), Rgba32.Black, Width: 1, Points: [new(10, 20)]));
        var dot = ScreenshotCopy.Capture(doc.State, null, new HashSet<string> { "dot" });
        Assert.AreEqual(1, dot.Scene.Frame.Width); Assert.AreEqual(1, dot.Scene.Frame.Height); Assert.HasCount(1, dot.Scene.Annotations);
        Assert.ThrowsExactly<InvalidOperationException>(() => ScreenshotCopy.Capture(doc.State, null, new HashSet<string> { "missing" }));
    }
}
