using System.Numerics;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class ScreenshotOrientationTests
{
    // 3x2 BGRA frame whose blue channel stores the source index.
    private static ScreenshotFrame Indexed()
    {
        var bytes = new byte[3 * 2 * 4];
        for (var i = 0; i < 6; i++) { bytes[i * 4] = (byte)i; bytes[i * 4 + 3] = 255; }
        return new(3, 2, bytes);
    }
    private static byte[] Blue(ScreenshotFrame frame) => Enumerable.Range(0, frame.Width * frame.Height).Select(i => frame.Pixels.Span[i * 4]).ToArray();

    [TestMethod]
    public void PixelsRotateClockwiseAndMirror()
    {
        var state = new ScreenshotState(Indexed(), []);
        var rotated = ScreenshotOrientation.Apply(state, ScreenshotOrientationChange.RotateClockwise).Frame;
        Assert.AreEqual(2, rotated.Width); Assert.AreEqual(3, rotated.Height);
        // 0 1 2      3 0
        // 3 4 5  ->  4 1
        //            5 2
        CollectionAssert.AreEqual(new byte[] { 3, 0, 4, 1, 5, 2 }, Blue(rotated));
        CollectionAssert.AreEqual(new byte[] { 2, 1, 0, 5, 4, 3 }, Blue(ScreenshotOrientation.Apply(state, ScreenshotOrientationChange.FlipHorizontal).Frame));
        CollectionAssert.AreEqual(new byte[] { 3, 4, 5, 0, 1, 2 }, Blue(ScreenshotOrientation.Apply(state, ScreenshotOrientationChange.FlipVertical).Frame));
        CollectionAssert.AreEqual(new byte[] { 0, 1, 2, 3, 4, 5 }, Blue(state.Frame));
    }

    [TestMethod]
    public void ObjectsMoveWithTheCanvas()
    {
        var arrow = new ScreenshotAnnotation("arrow", ScreenshotTool.Arrow, new(5, 2, 5, 6), Rgba32.Black, Points: [new(5, 2), new(10, 8)]);
        var image = ScreenshotDocument.ImageObject(ScreenshotFrame.Transparent(4, 2), new(1, 1)) with { Id = "image" };
        var callout = new ScreenshotAnnotation("callout", ScreenshotTool.Callout, new(20, 10, 6, 4), Rgba32.Black, CalloutTip: new(18, 16), ClipBounds: new(19, 9, 10, 10));
        var text = new ScreenshotAnnotation("text", ScreenshotTool.Text, new(2, 12, 8, 4), Rgba32.Black, Text: "a");
        var state = new ScreenshotState(ScreenshotFrame.Transparent(30, 20), [arrow, image, callout, text]);

        var rotated = ScreenshotOrientation.Apply(state, ScreenshotOrientationChange.RotateClockwise);
        Assert.AreEqual(20, rotated.Frame.Width); Assert.AreEqual(30, rotated.Frame.Height);
        var turnedArrow = rotated.Annotations[0];
        CollectionAssert.AreEqual(new Vector2[] { new(18, 5), new(12, 10) }, turnedArrow.Points.ToArray());
        Assert.AreEqual(new ScreenshotBox(12, 5, 6, 5), turnedArrow.Bounds);
        Assert.AreEqual(new ScreenshotBox(17, 1, 2, 4), rotated.Annotations[1].Bounds);
        Assert.AreEqual(90f, rotated.Annotations[1].Rotation);
        Assert.AreEqual(new Vector2(4, 18), rotated.Annotations[2].Tail);
        Assert.AreEqual(new ScreenshotBox(1, 19, 10, 10), rotated.Annotations[2].ClipBounds);

        var mirrored = ScreenshotOrientation.Apply(state, ScreenshotOrientationChange.FlipHorizontal);
        Assert.IsTrue(mirrored.Annotations[1].MirrorX); Assert.IsFalse(mirrored.Annotations[1].MirrorY);
        Assert.AreEqual(new ScreenshotBox(25, 1, 4, 2), mirrored.Annotations[1].Bounds);
        Assert.IsFalse(mirrored.Annotations[3].MirrorX, "文字は鏡像にしない。");
        Assert.AreEqual(new ScreenshotBox(20, 12, 8, 4), mirrored.Annotations[3].Bounds);
        Assert.IsTrue(ScreenshotOrientation.Apply(state, ScreenshotOrientationChange.FlipVertical).Annotations[1].MirrorY);
    }

    [TestMethod]
    public void FourQuarterTurnsRestoreTheCanvas()
    {
        var arrow = new ScreenshotAnnotation("arrow", ScreenshotTool.Arrow, new(5, 2, 5, 6), Rgba32.Black, Points: [new(5, 2), new(10, 8)]);
        var state = new ScreenshotState(Indexed(), [arrow]);
        var turned = state;
        for (var i = 0; i < 4; i++) turned = ScreenshotOrientation.Apply(turned, ScreenshotOrientationChange.RotateClockwise);
        CollectionAssert.AreEqual(Blue(state.Frame), Blue(turned.Frame));
        Assert.AreEqual(arrow.Bounds, turned.Annotations[0].Bounds);
        Assert.AreEqual(0f, turned.Annotations[0].Rotation);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ScreenshotOrientation.Apply(state, (ScreenshotOrientationChange)9));
    }
}
