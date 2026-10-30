using System.Numerics;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class HandleGeometryTests
{
    [TestMethod]
    public void SquareHitIncludesTheFullVisibleHandleOnBothSidesOfCanvasEdges()
    {
        var centers = ScreenshotGeometry.Handles(new(0, 0, 400, 300));
        foreach (var zoom in new[] { .1f, .25f, .5f, 1f, 2f, 4f }) for (var i = 0; i < centers.Length; i++)
        {
            foreach (var dx in new[] { -4.9f, 4.9f }) foreach (var dy in new[] { -4.9f, 4.9f })
                Assert.AreEqual(i, ScreenshotHandleGeometry.Hit(centers, centers[i] + new Vector2(dx, dy) / zoom, zoom));
            Assert.AreEqual(-1, ScreenshotHandleGeometry.Hit(centers, centers[i] + new Vector2(5.1f, 0) / zoom, zoom));
        }
    }
    [TestMethod]
    public void RoundHitMatchesItsVisibleCircleAndRejectsInvisibleSquareCorners()
    {
        foreach (var zoom in new[] { .1f, .5f, 1f, 4f })
        {
            var center = new Vector2(100, 100);
            Assert.IsTrue(ScreenshotHandleGeometry.Contains(center, center + new Vector2(5.99f, 0) / zoom, zoom, true));
            Assert.IsFalse(ScreenshotHandleGeometry.Contains(center, center + new Vector2(6.01f, 0) / zoom, zoom, true));
            Assert.IsFalse(ScreenshotHandleGeometry.Contains(center, center + new Vector2(4.9f, 4.9f) / zoom, zoom, true));
            Assert.IsTrue(ScreenshotHandleGeometry.Contains(center, center + new Vector2(4.9f, 4.9f) / zoom, zoom, false));
        }
    }
    [TestMethod]
    public void GrabbingAnOutsideHalfDoesNotJumpAndDragUsesTheActualPointerDelta()
    {
        var box = new ScreenshotBox(0, 0, 400, 300); var handles = ScreenshotGeometry.Handles(box);
        foreach (var zoom in new[] { .25f, 1f, 4f })
        {
            for (var i = 0; i < handles.Length; i++)
            {
                var pressed = handles[i] + new Vector2(-4.5f, -4.5f) / zoom;
                var still = ScreenshotHandleGeometry.DragPoint(handles[i], pressed, pressed);
                Assert.AreEqual(box, ScreenshotGeometry.Resize(box, i, still));
            }
            var outside = handles[7] - new Vector2(4.5f / zoom, 0);
            var moved = ScreenshotHandleGeometry.DragPoint(handles[7], outside, outside + new Vector2(10 / zoom, 0));
            var resized = ScreenshotGeometry.Resize(box, 7, moved);
            Assert.AreEqual(10 / zoom, resized.X); Assert.AreEqual(400 - 10 / zoom, resized.Width);
            var centered = ScreenshotGeometry.Resize(box, 7, moved, fromCenter: true);
            Assert.AreEqual(200f, centered.X + centered.Width / 2);
        }
    }
    [TestMethod]
    public void InvalidScaleCannotProduceInvisibleOrUnboundedHitRegions()
    {
        foreach (var zoom in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ScreenshotHandleGeometry.Square(Vector2.Zero, zoom));
    }
}
