using System.Numerics;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class CanvasResizeTests
{
    private static ScreenshotFrame Pattern(int width, int height)
    {
        var bytes = new byte[width * height * 4];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) { var i = (y * width + x) * 4; bytes[i] = (byte)(x * 10); bytes[i + 1] = (byte)(y * 20); bytes[i + 2] = 180; bytes[i + 3] = 255; }
        return new(width, height, bytes);
    }
    [TestMethod]
    public void CenterExpansionKeepsPixelsAndEditableShapeSizesAndCommitsOneUndo()
    {
        var doc = new ScreenshotDocument(Pattern(6, 4)); doc.SetBackground(new(10, 20, 30, 128));
        doc.Add(new("arrow", ScreenshotTool.Arrow, new(1, 1, 3, 2), Rgba32.Black, Width: 2, FontSize: 18, Points: [new(1, 1), new(4, 3)]));
        var before = doc.State; var layout = ScreenshotCanvasResize.Create(before, 10, 8, true);
        Assert.AreEqual(new Vector2(2, 2), layout.Offset); Assert.AreSame(before, doc.State);
        var expected = layout.Apply(before); Assert.AreSame(before, doc.State);
        var pixel = expected.Frame.Pixels.Span;
        Assert.AreEqual((byte)0, pixel[3]);
        for (var y = 0; y < 4; y++) for (var x = 0; x < 6; x++)
            CollectionAssert.AreEqual(before.Frame.Pixels.Slice((y * 6 + x) * 4, 4).ToArray(), expected.Frame.Pixels.Slice(((y + 2) * 10 + x + 2) * 4, 4).ToArray());
        Assert.AreEqual(before.Background, expected.Background); Assert.AreEqual(2f, expected.Annotations[0].Width); Assert.AreEqual(18f, expected.Annotations[0].FontSize);
        Assert.AreEqual(new ScreenshotBox(3, 3, 3, 2), expected.Annotations[0].Bounds); Assert.AreEqual("arrow", expected.Annotations[0].Id);
        doc.ResizeCanvas(10, 8, true); var committed = doc.State; doc.Undo(); Assert.AreSame(before, doc.State); doc.Redo(); Assert.AreSame(committed, doc.State);
    }
    [TestMethod]
    public void CenterTrimCopiesTheMiddleWithoutResamplingAndMovesClipsAndCutouts()
    {
        var doc = new ScreenshotDocument(Pattern(10, 8));
        doc.Add(new("shape", ScreenshotTool.Rectangle, new(2, 2, 6, 4), Rgba32.Black, Width: 1, ClipBounds: new(2, 2, 6, 4), Cutouts: [new(4, 4, 1, 1)]));
        var before = doc.State; var plan = ScreenshotCanvasResize.Create(before, 6, 4, true); var after = plan.Apply(before);
        Assert.AreEqual(new Vector2(-2, -2), plan.Offset);
        for (var y = 0; y < 4; y++)
            CollectionAssert.AreEqual(before.Frame.Pixels.Slice(((y + 2) * 10 + 2) * 4, 6 * 4).ToArray(), after.Frame.Pixels.Slice(y * 6 * 4, 6 * 4).ToArray());
        Assert.AreEqual(new ScreenshotBox(0, 0, 6, 4), after.Annotations[0].ClipBounds); Assert.AreEqual(new ScreenshotBox(2, 2, 1, 1), after.Annotations[0].Cutouts[0]);
        Assert.AreSame(before, doc.State);
    }
    [TestMethod]
    public void LeftTopAnchorAndOddDifferencesHaveDeterministicPixelPlacement()
    {
        var doc = new ScreenshotDocument(Pattern(6, 4));
        Assert.AreEqual(Vector2.Zero, ScreenshotCanvasResize.Create(doc.State, 10, 8, false).Offset);
        Assert.AreEqual(new Vector2(1, 1), ScreenshotCanvasResize.Create(doc.State, 9, 7, true).Offset);
        Assert.AreEqual(new Vector2(-1, -1), ScreenshotCanvasResize.Create(doc.State, 3, 1, true).Offset);
        var anchored = ScreenshotCanvasResize.Create(doc.State, 3, 2, false).Apply(doc.State);
        CollectionAssert.AreEqual(doc.State.Frame.Pixels.Slice(0, 12).ToArray(), anchored.Frame.Pixels.Slice(0, 12).ToArray());
    }
    [TestMethod]
    public void NoOpAndInvalidSizesDoNotChangeStateOrHistory()
    {
        var doc = new ScreenshotDocument(Pattern(6, 4)); var before = doc.State;
        doc.ResizeCanvas(6, 4, true); Assert.AreSame(before, doc.State); Assert.IsFalse(doc.CanUndo);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => doc.ResizeCanvas(0, 4, true));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => doc.ResizeCanvas(16384, 16384, true));
        var layout = ScreenshotCanvasResize.Create(before, 8, 6, true);
        Assert.ThrowsExactly<ArgumentException>(() => layout.Apply(new(ScreenshotFrame.Transparent(2, 2), [])));
        Assert.AreSame(before, doc.State); Assert.IsFalse(doc.CanUndo);
    }
}
