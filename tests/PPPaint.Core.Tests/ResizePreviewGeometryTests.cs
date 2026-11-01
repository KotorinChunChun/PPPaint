using System.Numerics;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class ResizePreviewGeometryTests
{
    [TestMethod]
    public void OverviewIncludesOriginalAndCandidateForCropExpansionAndMixedChanges()
    {
        var doc = new ScreenshotDocument(ScreenshotFrame.Transparent(300, 200));
        foreach (var size in new[] { (280, 160), (320, 250), (250, 240), (1, 1) }) foreach (var centered in new[] { true, false })
        {
            var layout = ScreenshotCanvasResize.Create(doc.State, size.Item1, size.Item2, centered);
            var view = ScreenshotResizePreviewGeometry.Overview(layout, 220, 100);
            Assert.IsTrue(view.Contains(ScreenshotResizePreviewGeometry.Original(layout)));
            Assert.IsTrue(view.Contains(new ScreenshotBox(0, 0, layout.Width, layout.Height)));
        }
    }
    [TestMethod]
    public void DetailBoundaryHitsBothAxesAndDragsExactlyOnePixelWithoutChangingDocument()
    {
        var doc = new ScreenshotDocument(ScreenshotFrame.Transparent(300, 200)); var before = doc.State;
        var start = ScreenshotCanvasResize.Create(before, 300, 200, false); var view = new ScreenshotBox(-12, -12, 25, 25);
        Assert.AreEqual(ScreenshotCanvasHandle.TopLeft, ScreenshotResizePreviewGeometry.Hit(start, view, new(48, 48), 100, 100));
        Assert.IsNull(ScreenshotResizePreviewGeometry.Hit(start, view, new(90, 90), 100, 100));
        var dragged = ScreenshotResizePreviewGeometry.Drag(before, start, ScreenshotCanvasHandle.TopLeft, new(1, 2), false, false);
        Assert.AreEqual(299, dragged.Width); Assert.AreEqual(198, dragged.Height); Assert.AreEqual(new Vector2(-1, -2), dragged.Offset);
        Assert.AreSame(before, doc.State); Assert.IsFalse(doc.CanUndo);
        doc.Apply(dragged.Apply(before)); doc.Undo(); Assert.AreSame(before, doc.State);
    }
    [TestMethod]
    public void DragSupportsOppositeSideRatioAndSourceBounds()
    {
        var doc = new ScreenshotDocument(ScreenshotFrame.Transparent(300, 200)); var start = ScreenshotCanvasResize.Create(doc.State, 300, 200, false);
        var centered = ScreenshotResizePreviewGeometry.Drag(doc.State, start, ScreenshotCanvasHandle.TopLeft, new(2, 3), true, false);
        Assert.AreEqual(296, centered.Width); Assert.AreEqual(194, centered.Height); Assert.AreEqual(new Vector2(-2, -3), centered.Offset);
        var ratio = ScreenshotResizePreviewGeometry.Drag(doc.State, start, ScreenshotCanvasHandle.Right, new(30, 0), false, true);
        Assert.AreEqual(330, ratio.Width); Assert.AreEqual(220, ratio.Height);
        var grown = ScreenshotResizePreviewGeometry.Drag(doc.State, start, ScreenshotCanvasHandle.TopLeft, new(-4, -5), false, false);
        Assert.AreEqual(new Vector2(4, 5), grown.Offset); Assert.AreEqual(304, grown.Width); Assert.AreEqual(205, grown.Height);
    }
    [TestMethod]
    public void ActiveViewKeepsOriginalContentStillWhileDraggedEdgeFollowsPointer()
    {
        var doc = new ScreenshotDocument(ScreenshotFrame.Transparent(100, 80));
        var start = ScreenshotCanvasResize.Create(doc.State, 100, 80, false); var viewport = new ScreenshotBox(-12, -12, 25, 25);
        var current = ScreenshotResizePreviewGeometry.Drag(doc.State, start, ScreenshotCanvasHandle.Left, new(2, 0), false, false);
        var frozen = ScreenshotResizePreviewGeometry.FrozenViewport(viewport, start, current);
        Assert.AreEqual(48f, (start.OffsetX - viewport.X) * 4);
        Assert.AreEqual(48f, (current.OffsetX - frozen.X) * 4, "元の境界は動かない");
        Assert.AreEqual(56f, -frozen.X * 4, "候補の左辺はポインタと同じ8DIP動く");
        Assert.AreEqual((start.Width - viewport.X) * 4, (current.Width - frozen.X) * 4, "対角側は固定");
    }
}
