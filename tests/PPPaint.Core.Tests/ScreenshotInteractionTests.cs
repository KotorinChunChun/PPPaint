using System.Numerics;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class ScreenshotInteractionTests
{
    [TestMethod]
    public void SmallObjectsChooseNearestHandleAndArrowheadCanBeReselected()
    {
        Assert.AreEqual(1, ScreenshotGeometry.HitHandle(new[] { new Vector2(10, 10), new Vector2(15, 10) }, new(14, 10), 7));
        var arrow = new ScreenshotAnnotation("arrow", ScreenshotTool.Arrow, new(0, 20, 100, 0), Rgba32.Black, Width: 8, Points: [new(0, 20), new(100, 20)]);
        var wing = ScreenshotGeometry.ArrowHead(arrow.Points[0], arrow.Points[1], arrow.Width);
        Assert.IsTrue(arrow.Hit(wing.Left, 1)); Assert.IsFalse(arrow.Hit(new(50, 50), 1));
    }
    private static ScreenshotAnnotation Rect(string id, float x = 10) => new(id, ScreenshotTool.Rectangle, new(x, 10, 50, 40), Rgba32.Black);
    [TestMethod]
    public void OriginalAndPastedImagesRemainSeparateAndUndoInsertionRestoresCanvas()
    {
        var doc = ScreenshotDocument.FromImage(ScreenshotFrame.Blank(80, 60)); var initial = doc.State;
        var id = doc.InsertImage(ScreenshotFrame.Blank(40, 30), new(60, 50));
        Assert.HasCount(2, doc.State.Annotations); Assert.AreEqual(ScreenshotTool.Image, doc.State.Annotations[0].Tool);
        Assert.AreEqual(100, doc.State.Frame.Width); Assert.AreEqual(80, doc.State.Frame.Height);
        doc.Replace(doc.State.Annotations[1].Move(new(-10, -20)));
        Assert.AreEqual(50f, doc.State.Annotations[1].Bounds.X); Assert.AreEqual(0f, doc.State.Annotations[0].Bounds.X);
        doc.Undo(); doc.Undo(); Assert.AreSame(initial, doc.State); Assert.IsNotNull(id);
    }
    [TestMethod]
    public void HollowShapesAndTransparentImagePixelsAllowSelectionBehind()
    {
        var rectangle = Rect("r"); Assert.IsTrue(rectangle.Hit(new(10, 25), 2)); Assert.IsFalse(rectangle.Hit(new(35, 30), 2));
        var ellipse = rectangle with { Tool = ScreenshotTool.Ellipse }; Assert.IsTrue(ellipse.Hit(new(10, 30), 2)); Assert.IsFalse(ellipse.Hit(new(35, 30), 2));
        var image = ScreenshotDocument.ImageObject(new(2, 1, new byte[] { 0, 0, 0, 0, 0, 0, 255, 255 }), Vector2.Zero).Resize(new(0, 0, 100, 50));
        Assert.IsFalse(image.Hit(new(20, 25))); Assert.IsTrue(image.Hit(new(80, 25)));
        Assert.IsTrue((image with { MirrorX = true }).Hit(new(20, 25)));
    }
    [TestMethod]
    public void HorizontalAndVerticalLineEndpointsCanChangeDirectionWithoutMovingOtherEnd()
    {
        var line = new ScreenshotAnnotation("line", ScreenshotTool.Arrow, new(10, 20, 100, 0), Rgba32.Black, Points: [new(10, 20), new(110, 20)]);
        var changed = ScreenshotGeometry.MoveEndpoint(line, 1, new(10, 120));
        Assert.AreEqual(new Vector2(10, 20), changed.Points[0]); Assert.AreEqual(0f, changed.Bounds.Width); Assert.AreEqual(100f, changed.Bounds.Height);
        var changedAgain = ScreenshotGeometry.MoveEndpoint(changed, 0, new(50, 20));
        Assert.AreEqual(new Vector2(10, 120), changedAgain.Points[1]); Assert.IsTrue(changedAgain.Hit(new(30, 70)));
        var snapped = ScreenshotGeometry.MoveEndpoint(line, 1, new(90, 23), true); Assert.AreEqual(20f, snapped.Points[1].Y, .001f);
    }
    [TestMethod]
    public void EdgeAndCornerResizingKeepsOppositeEdgeAndSupportsCrossing()
    {
        var b = new ScreenshotBox(10, 20, 100, 50);
        var top = ScreenshotGeometry.Resize(b, 1, new(100, 5)); Assert.AreEqual(new ScreenshotBox(10, 5, 100, 65), top);
        var left = ScreenshotGeometry.Resize(b, 7, new(120, 20)); Assert.AreEqual(110f, left.X); Assert.AreEqual(10f, left.Width);
        var proportional = ScreenshotGeometry.Resize(b, 4, new(210, 150), true); Assert.AreEqual(2f, proportional.Width / proportional.Height, .001f);
    }
    [TestMethod]
    public void ReorderingMultipleObjectsPreservesRelativeOrderAndUndo()
    {
        var doc = new ScreenshotDocument(ScreenshotFrame.Blank(200, 100));
        foreach (var id in new[] { "a", "b", "c", "d", "e" }) doc.Add(Rect(id)); var initial = doc.State;
        doc.Reorder(new HashSet<string> { "b", "c" }, ScreenshotStackOrder.Forward);
        CollectionAssert.AreEqual(new[] { "a", "d", "b", "c", "e" }, doc.State.Annotations.Select(a => a.Id).ToArray());
        doc.Reorder(new HashSet<string> { "b", "c" }, ScreenshotStackOrder.Back);
        CollectionAssert.AreEqual(new[] { "b", "c", "a", "d", "e" }, doc.State.Annotations.Select(a => a.Id).ToArray());
        doc.Undo(); doc.Undo(); Assert.AreSame(initial, doc.State);
    }
    [TestMethod]
    public void GroupResizeAndMoveCommitAsOneUndoAndSelectionBoundsContainMembers()
    {
        var doc = new ScreenshotDocument(ScreenshotFrame.Blank(200, 100)); doc.Add(Rect("a")); doc.Add(Rect("b", 80)); var original = doc.State;
        var bounds = ScreenshotGeometry.Bounds(doc.State.Annotations); Assert.IsTrue(bounds.Contains(doc.State.Annotations[1].Bounds));
        var resized = ScreenshotGeometry.ResizeGroup(doc.State.Annotations, bounds, new(20, 20, bounds.Width * 2, bounds.Height * 2));
        doc.ReplaceMany(resized); Assert.AreEqual(100f, doc.State.Annotations[0].Bounds.Width); Assert.AreEqual(160f, doc.State.Annotations[1].Bounds.X);
        doc.Undo(); Assert.AreSame(original, doc.State);
        doc.Delete(new HashSet<string> { "a", "b" }); Assert.HasCount(0, doc.State.Annotations); doc.Undo(); Assert.AreSame(original, doc.State);
    }
}
