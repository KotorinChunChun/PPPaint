using System.Numerics;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class ScreenshotModifierTests
{
    [TestMethod]
    public void EveryResizeHandleKeepsCenterWithControlAndRatioWithControlShift()
    {
        var original = new ScreenshotBox(20, 30, 120, 60);
        for (var handle = 0; handle < 8; handle++)
        {
            var point = ScreenshotGeometry.Handles(original)[handle] + new Vector2(13, 21);
            foreach (var ratio in new[] { false, true })
            {
                var result = ScreenshotGeometry.Resize(original, handle, point, ratio, true);
                Assert.AreEqual(80f, result.X + result.Width / 2, .001f);
                Assert.AreEqual(60f, result.Y + result.Height / 2, .001f);
                if (ratio) Assert.AreEqual(2f, result.Width / result.Height, .001f);
                Assert.IsTrue(result.Width >= 2 && result.Height >= 2);
            }
        }
    }

    [TestMethod]
    public void RatioResizeSupportsSideHandlesCrossingAndMinimumSize()
    {
        var original = new ScreenshotBox(20, 30, 120, 60);
        for (var handle = 0; handle < 8; handle++)
        {
            foreach (var point in new[] { new Vector2(-80, -90), new(80, 60), new(20, 30) })
            {
                var box = ScreenshotGeometry.Resize(original, handle, point, true);
                Assert.AreEqual(2f, box.Width / box.Height, .001f); Assert.IsTrue(box.Width >= 2 && box.Height >= 2);
            }
        }
        Assert.AreEqual(new ScreenshotBox(20, 15, 180, 90), ScreenshotGeometry.Resize(original, 3, new(200, 1), true));
    }

    [TestMethod]
    public void CenterAndSquareCreationWorkInEveryDragDirection()
    {
        foreach (var point in new[] { new Vector2(90, 80), new(10, 80), new(10, 20), new(90, 20) })
        {
            var box = ScreenshotManipulation.CreateBox(new(50, 50), point, true, true);
            Assert.AreEqual(new ScreenshotBox(10, 10, 80, 80), box);
        }
        Assert.AreEqual(new ScreenshotBox(10, 20, 40, 30), ScreenshotManipulation.CreateBox(new(50, 50), new(10, 20), false, false));
    }

    [TestMethod]
    public void RegionAtCanvasBoundaryKeepsItsAnchorCenterAndRatio()
    {
        var start = new Vector2(50, 50);
        var square = ScreenshotManipulation.FitToCanvas(ScreenshotManipulation.CreateBox(start, new(500, 300), true, false), start, 500, 300, true);
        Assert.AreEqual(50f, square.X, .001f); Assert.AreEqual(50f, square.Y, .001f); Assert.AreEqual(250f, square.Width, .001f); Assert.AreEqual(square.Width, square.Height, .001f);
        var centered = ScreenshotManipulation.FitToCanvas(ScreenshotManipulation.CreateBox(start, new(500, 300), true, true), start, 500, 300, true);
        Assert.AreEqual(0f, centered.X, .001f); Assert.AreEqual(0f, centered.Y, .001f); Assert.AreEqual(100f, centered.Width, .001f); Assert.AreEqual(centered.Width, centered.Height, .001f);
        var original = new ScreenshotBox(50, 40, 100, 50); var center = new Vector2(100, 65);
        var resized = ScreenshotManipulation.FitToCanvas(ScreenshotGeometry.Resize(original, 4, new(500, 300), true, true), center, 500, 300, true);
        Assert.AreEqual(2f, resized.Width / resized.Height, .001f); Assert.AreEqual(100f, resized.X + resized.Width / 2, .001f); Assert.AreEqual(65f, resized.Y + resized.Height / 2, .001f);
        Assert.IsTrue(resized.X >= 0 && resized.Y >= 0 && resized.Right <= 500 && resized.Bottom <= 300);
    }

    [TestMethod]
    public void AltSnapsNearestEdgeOrCenterWithinScreenToleranceAndRespectsShiftAxis()
    {
        ScreenshotBox[] targets = [new(0, 0, 400, 300), new(150, 100, 60, 50)];
        var snapped = ScreenshotManipulation.SnapMove(new(20, 20, 30, 20), new(98, 58), targets, 6, false);
        Assert.AreEqual(new Vector2(100, 60), snapped.Point); Assert.AreEqual(150f, snapped.X); Assert.AreEqual(100f, snapped.Y);
        var constrained = ScreenshotManipulation.SnapMove(new(20, 20, 30, 20), new(98, 2), targets, 6, true);
        Assert.AreEqual(new Vector2(100, 0), constrained.Point); Assert.IsNull(constrained.Y);
        var far = ScreenshotManipulation.SnapPoint(new(80, 80), targets, 6);
        Assert.AreEqual(new Vector2(80, 80), far.Point); Assert.IsNull(far.X); Assert.IsNull(far.Y);
        var centered = ScreenshotManipulation.SnapPoint(new(178, 124), targets, 3);
        Assert.AreEqual(new Vector2(180, 125), centered.Point);
    }

    [TestMethod]
    public void ControlOrShiftClickTogglesWithoutLosingOtherSelectedObjects()
    {
        var selected = ScreenshotManipulation.ClickSelection(["a", "b"], "c", true);
        Assert.IsTrue(selected.SetEquals(["a", "b", "c"]));
        selected = ScreenshotManipulation.ClickSelection(selected, "b", true); Assert.IsTrue(selected.SetEquals(["a", "c"]));
        selected = ScreenshotManipulation.ClickSelection(selected, "a", false); Assert.IsTrue(selected.SetEquals(["a", "c"]));
        selected = ScreenshotManipulation.ClickSelection(selected, "b", false); Assert.IsTrue(selected.SetEquals(["b"]));
    }

    [TestMethod]
    public void DuplicateGroupPreservesOriginalsOrderPixelsAndUndoAsOneEdit()
    {
        var doc = ScreenshotDocument.FromImage(ScreenshotFrame.Blank(100, 100));
        doc.Add(new("shape", ScreenshotTool.Blur, new(10, 10, 20, 20), Rgba32.Black)); var before = doc.State;
        var copies = ScreenshotManipulation.Duplicate(before.Annotations).Select(a => a.Move(new(30, 10))).ToArray();
        Assert.AreEqual(before.Annotations.Length, copies.Select(a => a.Id).Distinct().Count());
        Assert.AreSame(before.Annotations[0].Raster, copies[0].Raster);
        doc.AddMany(copies); Assert.HasCount(4, doc.State.Annotations);
        Assert.AreEqual(before.Annotations[0], doc.State.Annotations[0]); Assert.AreEqual(before.Annotations[1], doc.State.Annotations[1]);
        Assert.AreEqual(ScreenshotTool.Blur, doc.State.Annotations[^1].Tool);
        doc.Undo(); Assert.AreSame(before, doc.State); doc.Redo(); Assert.HasCount(4, doc.State.Annotations);
        Assert.ThrowsExactly<ArgumentException>(() => doc.AddMany(copies)); Assert.HasCount(4, doc.State.Annotations);
    }
}
