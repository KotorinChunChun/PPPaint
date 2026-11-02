using System.Numerics;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class CalloutControlsTests
{
    private static ScreenshotAnnotation Callout() => new("c", ScreenshotTool.Callout, new(20, 20, 80, 50), Rgba32.Black, Text: "注釈", CalloutTip: new(150, 50));
    [TestMethod]
    public void RootsAndTipCanBeEditedIndependentlyAndRemainOnTheBodyEdge()
    {
        var original = Callout(); var moved = ScreenshotCalloutGeometry.MoveControl(original, 1, new(100, 25));
        var controls = ScreenshotCalloutGeometry.Controls(moved);
        Assert.AreEqual(original.Bounds, moved.Bounds); Assert.AreEqual(original.Tail, moved.Tail);
        Assert.AreEqual(1, controls.Edge); Assert.AreEqual(100f, controls.First.X); Assert.AreEqual(100f, controls.Second.X);
        Assert.IsTrue(controls.End > controls.Start);
        var tip = ScreenshotCalloutGeometry.MoveControl(moved, 0, new(180, 60));
        Assert.AreEqual(controls.First, ScreenshotCalloutGeometry.Controls(tip).First);
        Assert.IsTrue(tip.Hit((controls.First + controls.Second + tip.Tail) / 3, 0));
        var resized = tip.Resize(new(30, 40, 160, 100));
        var result = ScreenshotCalloutGeometry.Controls(resized);
        Assert.AreEqual(new Vector2(30 + (controls.First.X - 20) * 2, 40 + (controls.First.Y - 20) * 2), result.First);
        Assert.HasCount(3, ScreenshotCalloutGeometry.Points(tip));
    }
    [TestMethod]
    [DataRow(ScreenshotOrientationChange.RotateClockwise)]
    [DataRow(ScreenshotOrientationChange.FlipHorizontal)]
    [DataRow(ScreenshotOrientationChange.FlipVertical)]
    public void RotationAndFlipKeepBothRootsAttachedAtTheTransformedPoints(ScreenshotOrientationChange change)
    {
        var item = Callout() with { CalloutEdge = 0, CalloutStart = .2f, CalloutEnd = .7f };
        var source = new ScreenshotState(ScreenshotFrame.Transparent(200, 160), [item]);
        var before = ScreenshotCalloutGeometry.Controls(item);
        var transformed = ScreenshotOrientation.Apply(source, change);
        var after = ScreenshotCalloutGeometry.Controls(transformed.Annotations[0]);
        Vector2 Point(Vector2 point) => change switch { ScreenshotOrientationChange.RotateClockwise => new(160 - point.Y, point.X), ScreenshotOrientationChange.FlipHorizontal => new(200 - point.X, point.Y), _ => new(point.X, 160 - point.Y) };
        Assert.IsTrue(Vector2.Distance(Point(before.Tip), after.Tip) < .001f);
        Assert.IsTrue(Vector2.Distance(Point(change == ScreenshotOrientationChange.RotateClockwise ? before.First : before.Second), after.First) < .001f);
        Assert.IsTrue(Vector2.Distance(Point(change == ScreenshotOrientationChange.RotateClockwise ? before.Second : before.First), after.Second) < .001f);
    }
}
