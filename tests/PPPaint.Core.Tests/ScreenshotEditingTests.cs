using System.Numerics;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class ScreenshotEditingTests
{
    [TestMethod]
    public void BackgroundIsWhiteByDefaultAndTransparentChangeIsUndoable()
    {
        var doc = new ScreenshotDocument(ScreenshotFrame.Transparent(20, 10)); var before = doc.State;
        Assert.AreEqual(Rgba32.White, doc.State.Background); doc.SetBackground(Rgba32.Transparent);
        Assert.AreEqual((byte)0, doc.State.Background.A); doc.Undo(); Assert.AreSame(before, doc.State);
        var white = doc.State.Frame.Over(doc.State.Background); Assert.AreEqual((byte)255, white.Pixels.Span[0]); Assert.AreEqual((byte)255, white.Pixels.Span[3]);
        var translucent = doc.State.Frame.Over(new(200, 100, 50, 128)); Assert.AreEqual((byte)25, translucent.Pixels.Span[0]); Assert.AreEqual((byte)100, translucent.Pixels.Span[2]); Assert.AreEqual((byte)128, translucent.Pixels.Span[3]);
    }
    [TestMethod]
    public void PartialCutKeepsOutsidePixelsAndEditableShapesAndUndoRestoresExactly()
    {
        var doc = ScreenshotDocument.FromImage(ScreenshotFrame.Blank(100, 80)); doc.Add(new("rect", ScreenshotTool.Rectangle, new(10, 10, 50, 50), Rgba32.Black)); var before = doc.State;
        doc.DeleteRange(new(20, 20, 30, 30)); Assert.HasCount(2, doc.State.Annotations);
        foreach (var item in doc.State.Annotations) { Assert.HasCount(1, item.Cutouts); Assert.IsFalse(item.Hit(new(30, 30))); }
        Assert.IsTrue(doc.State.Annotations[0].Hit(new(5, 5))); doc.Undo(); Assert.AreSame(before, doc.State); doc.Redo(); Assert.HasCount(2, doc.State.Annotations);
        doc.DeleteRange(new(0, 0, 100, 80)); Assert.HasCount(0, doc.State.Annotations); Assert.AreEqual(Rgba32.White, doc.State.Background);
    }
    [TestMethod]
    public void CapturedRangeKeepsOriginalPixelsShapeIdsUntilPasteAndClipsHiddenContent()
    {
        var doc = ScreenshotDocument.FromImage(ScreenshotFrame.Blank(100, 80)); doc.Add(new("rect", ScreenshotTool.Rectangle, new(0, 0, 60, 60), Rgba32.Black));
        var payload = ScreenshotEditing.Capture(doc.State, new(20, 10, 40, 30));
        Assert.AreEqual(40, payload.Scene.Frame.Width); Assert.AreEqual(30, payload.Scene.Frame.Height); Assert.AreEqual(Rgba32.White, payload.Scene.Background);
        Assert.AreEqual(new ScreenshotBox(0, 0, 40, 30), payload.Scene.Annotations[1].ClipBounds);
        Assert.AreEqual(new Vector2(20, 10), payload.Origin);
        var pasted = payload.ObjectsAt(new(10, 20)); Assert.HasCount(3, pasted); Assert.AreEqual(ScreenshotTool.Rectangle, pasted[^1].Tool);
        Assert.AreNotEqual("rect", pasted[^1].Id); Assert.IsFalse(pasted[^1].Hit(new(0, 0)));
        Assert.AreEqual(new ScreenshotBox(10, 20, 40, 30), pasted[^1].ClipBounds);
        Assert.AreSame(doc.State.Annotations[0].Raster, pasted[1].Raster);
    }
    [TestMethod]
    public void ClipAndCutoutsFollowObjectMoveAndResize()
    {
        var item = new ScreenshotAnnotation("r", ScreenshotTool.Rectangle, new(10, 10, 40, 40), Rgba32.Black, ClipBounds: new(10, 10, 30, 30), Cutouts: [new(20, 20, 5, 5)]);
        var moved = item.Move(new(5, 10)); Assert.AreEqual(new ScreenshotBox(15, 20, 30, 30), moved.ClipBounds); Assert.AreEqual(new ScreenshotBox(25, 30, 5, 5), moved.Cutouts[0]);
        var resized = item.Resize(new(20, 20, 80, 80)); Assert.AreEqual(new ScreenshotBox(20, 20, 60, 60), resized.ClipBounds); Assert.AreEqual(new ScreenshotBox(40, 40, 10, 10), resized.Cutouts[0]);
    }
    [TestMethod]
    public void NumberPressAndDragOnlyPreviewAndReleaseCommitsOnceWithSequenceUndo()
    {
        var doc = new ScreenshotDocument(ScreenshotFrame.Transparent(200, 100)); doc.SetNextNumber(7); var before = doc.State;
        Assert.IsTrue(ScreenshotEditing.SameImage(before with { NextNumber = 1 }, before));
        var placement = new ScreenshotNumberPlacement(doc.State, new(10, 20), 20, Rgba32.Black); placement.Move(new(60, 40));
        Assert.AreSame(before, doc.State); Assert.AreEqual("7", placement.Preview.Text); Assert.AreEqual(60f, placement.Preview.Bounds.X);
        placement.Commit(doc); Assert.AreEqual(8, doc.State.NextNumber); Assert.HasCount(1, doc.State.Annotations);
        Assert.IsFalse(ScreenshotEditing.SameImage(before, doc.State));
        Assert.ThrowsExactly<InvalidOperationException>(() => placement.Commit(doc)); doc.Undo(); Assert.AreSame(before, doc.State); doc.Redo(); Assert.AreEqual(8, doc.State.NextNumber);
        var canceled = new ScreenshotNumberPlacement(doc.State, new(0, 0), 20, Rgba32.Black); var current = doc.State; canceled.Cancel(); Assert.AreSame(current, doc.State);
    }
    [TestMethod]
    public void ControlCanvasCropKeepsCenterAndChangesOppositeEdgeEqually()
    {
        Assert.AreEqual(new PixelRect(20, 0, 60, 80), ScreenshotCanvasGeometry.CropFromEdge(100, 80, ScreenshotCanvasEdge.Left, 20, true));
        Assert.AreEqual(new PixelRect(20, 0, 60, 80), ScreenshotCanvasGeometry.CropFromEdge(100, 80, ScreenshotCanvasEdge.Right, 80, true));
        Assert.AreEqual(new PixelRect(0, 10, 100, 60), ScreenshotCanvasGeometry.CropFromEdge(100, 80, ScreenshotCanvasEdge.Top, 10, true));
        Assert.AreEqual(new PixelRect(0, 10, 100, 60), ScreenshotCanvasGeometry.CropFromEdge(100, 80, ScreenshotCanvasEdge.Bottom, 70, true));
        Assert.AreEqual(2, ScreenshotCanvasGeometry.CropFromEdge(100, 80, ScreenshotCanvasEdge.Left, 1000, true).Width);
        Assert.AreEqual(1, ScreenshotCanvasGeometry.CropFromEdge(101, 80, ScreenshotCanvasEdge.Left, 1000, true).Width);
    }
    [TestMethod]
    public void WheelZoomInvertsDirectionAndClampsAtBothLimits()
    {
        Assert.AreEqual(110d, ScreenshotZoom.Wheel(100, 120)); Assert.IsTrue(ScreenshotZoom.Wheel(100, -120) < 100);
        Assert.AreEqual(400d, ScreenshotZoom.Wheel(400, 120)); Assert.AreEqual(10d, ScreenshotZoom.Wheel(10, -120));
    }
}
