using System.Numerics;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class FixedResizePreviewTests
{
    [TestMethod]
    public void InitialBoundaryLeavesTwentyPercentOutsideOnEachFacingAxis()
    {
        var state = new ScreenshotDocument(ScreenshotFrame.Transparent(300, 200)).State;
        var layout = ScreenshotCanvasResize.Create(state, 300, 200, false);
        var expected = new[] { (-5f, -5f), (137f, -5f), (280f, -5f), (-5f, 87f), (280f, 87f), (-5f, 180f), (137f, 180f), (280f, 180f) };
        var anchors = Enum.GetValues<ScreenshotCanvasViewAnchor>();
        for (var i = 0; i < anchors.Length; i++)
        {
            var view = ScreenshotResizePreviewGeometry.FixedDetail(layout, anchors[i], 25, .2f);
            Assert.AreEqual(expected[i].Item1, view.X, .0001f); Assert.AreEqual(expected[i].Item2, view.Y, .0001f);
            Assert.AreEqual(25f, view.Width); Assert.AreEqual(25f, view.Height);
        }
    }

    [TestMethod]
    public void EveryViewKeepsSourcePositionAcrossSizeOriginAndRepeatedDrags()
    {
        var state = new ScreenshotDocument(ScreenshotFrame.Transparent(300, 200)).State;
        var start = ScreenshotCanvasResize.Create(state, 300, 200, false);
        var left = ScreenshotResizePreviewGeometry.Drag(state, start, ScreenshotCanvasHandle.Left, new(1, 0), false, false);
        var twice = ScreenshotResizePreviewGeometry.Drag(state, left, ScreenshotCanvasHandle.Left, new(2, 0), false, false);
        var layouts = new[] { start, left, twice, ScreenshotCanvasResize.Create(state, 270, 160, true), ScreenshotCanvasResize.Create(state, 340, 230, true) };
        foreach (var anchor in Enum.GetValues<ScreenshotCanvasViewAnchor>()) foreach (var span in new[] { 6.25f, 25f, 50f, 400f })
        {
            var baseline = ScreenshotResizePreviewGeometry.FixedDetail(start, anchor, span, .2f);
            foreach (var layout in layouts)
            {
                var view = ScreenshotResizePreviewGeometry.FixedDetail(layout, anchor, span, .2f);
                Assert.AreEqual(baseline.X, view.X - layout.OffsetX, .001f);
                Assert.AreEqual(baseline.Y, view.Y - layout.OffsetY, .001f);
                Assert.AreEqual(span, view.Width); Assert.AreEqual(span, view.Height);
            }
        }
        Assert.AreEqual(-3, twice.OffsetX); Assert.AreEqual(297, twice.Width);
    }

    [TestMethod]
    public void DraggedEdgeStaysAtItsNewPositionAfterReleaseAndNextDrag()
    {
        var state = new ScreenshotDocument(ScreenshotFrame.Transparent(300, 200)).State;
        var start = ScreenshotCanvasResize.Create(state, 300, 200, false);
        var first = ScreenshotResizePreviewGeometry.Drag(state, start, ScreenshotCanvasHandle.Right, new(-2, 0), false, false);
        var view = ScreenshotResizePreviewGeometry.FixedDetail(first, ScreenshotCanvasViewAnchor.Right, 25, .2f);
        var position = new Vector2((first.Width - view.X) * 4, 50);
        Assert.AreEqual(72f, position.X);
        Assert.AreEqual(ScreenshotCanvasHandle.Right, ScreenshotResizePreviewGeometry.Hit(first, view, position, 100, 100));
        var second = ScreenshotResizePreviewGeometry.Drag(state, first, ScreenshotCanvasHandle.Right, new(-1, 0), false, false);
        var secondView = ScreenshotResizePreviewGeometry.FixedDetail(second, ScreenshotCanvasViewAnchor.Right, 25, .2f);
        Assert.AreEqual(68f, (second.Width - secondView.X) * 4);
        Assert.AreEqual(view, secondView);
    }
}
