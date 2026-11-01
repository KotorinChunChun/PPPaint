using System.Numerics;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class CanvasCornerResizeTests
{
    [TestMethod]
    public void EntireCornerBandSelectsBothAxesAndImageBoundaryRemainsEditable()
    {
        foreach (var zoom in new[] { .25f, 1f, 1.75f, 4f })
        {
            var image = new ScreenshotBox(0, 0, 100 * zoom, 60 * zoom);
            var outer = new ScreenshotBox(-10, -10, image.Width + 20, image.Height + 20);
            for (var x = 1; x <= 10; x++) for (var y = 1; y <= 10; y++)
            {
                Assert.AreEqual(ScreenshotCanvasHandle.TopLeft, ScreenshotCanvasGeometry.HitResizeBorder(outer, image, new(-x, -y)));
                Assert.AreEqual(ScreenshotCanvasHandle.TopRight, ScreenshotCanvasGeometry.HitResizeBorder(outer, image, new(image.Right + x, -y)));
                Assert.AreEqual(ScreenshotCanvasHandle.BottomLeft, ScreenshotCanvasGeometry.HitResizeBorder(outer, image, new(-x, image.Bottom + y)));
                Assert.AreEqual(ScreenshotCanvasHandle.BottomRight, ScreenshotCanvasGeometry.HitResizeBorder(outer, image, new(image.Right + x, image.Bottom + y)));
            }
            Assert.AreEqual(ScreenshotCanvasHandle.Left, ScreenshotCanvasGeometry.HitResizeBorder(outer, image, new(-10, image.Height / 2)));
            Assert.AreEqual(ScreenshotCanvasHandle.Top, ScreenshotCanvasGeometry.HitResizeBorder(outer, image, new(image.Width / 2, -10)));
            Assert.AreEqual(ScreenshotCanvasHandle.Right, ScreenshotCanvasGeometry.HitResizeBorder(outer, image, new(image.Right + 10, image.Height / 2)));
            Assert.AreEqual(ScreenshotCanvasHandle.Bottom, ScreenshotCanvasGeometry.HitResizeBorder(outer, image, new(image.Width / 2, image.Bottom + 10)));
            foreach (var point in new Vector2[] { Vector2.Zero, new(image.Right, image.Bottom), new(-10.01f, -5), new(5, image.Bottom + 10.01f) })
                Assert.IsNull(ScreenshotCanvasGeometry.HitResizeBorder(outer, image, point));
        }
        Assert.AreEqual(ScreenshotCanvasHandle.TopLeft, ScreenshotCanvasGeometry.HitResizeBorder(new(-7, -8, 44, 35), new(3, 2, 24, 15), new(-7, -8)));
    }

    [TestMethod]
    public void EightHandlesExpandAndTrimOneAndTwoPixelsFromTheActualClickPoint()
    {
        foreach (var handle in Enum.GetValues<ScreenshotCanvasHandle>())
        foreach (var zoom in new[] { .25, 1, 1.75, 4 })
        foreach (var dpi in new[] { 1, 1.25, 1.5, 2 })
        foreach (var clickOffset in new[] { .01, 5, 10 })
        foreach (var pixels in new[] { -2, -1, 0, 1, 2 })
        {
            var nearX = handle is ScreenshotCanvasHandle.Left or ScreenshotCanvasHandle.TopLeft or ScreenshotCanvasHandle.BottomLeft;
            var farX = handle is ScreenshotCanvasHandle.Right or ScreenshotCanvasHandle.TopRight or ScreenshotCanvasHandle.BottomRight;
            var nearY = handle is ScreenshotCanvasHandle.Top or ScreenshotCanvasHandle.TopLeft or ScreenshotCanvasHandle.TopRight;
            var farY = handle is ScreenshotCanvasHandle.Bottom or ScreenshotCanvasHandle.BottomLeft or ScreenshotCanvasHandle.BottomRight;
            var startX = (nearX ? -clickOffset : farX ? 100 * zoom + clickOffset : 35) * dpi;
            var startY = (nearY ? -clickOffset : farY ? 60 * zoom + clickOffset : 21) * dpi;
            var endX = startX + (nearX ? pixels : farX ? -pixels : 0) * zoom * dpi;
            var endY = startY + (nearY ? pixels : farY ? -pixels : 0) * zoom * dpi;
            var result = ScreenshotCanvasGeometry.ResizeFromDrag(100, 60, handle, startX / dpi, startY / dpi, endX / dpi, endY / dpi, zoom);
            Assert.AreEqual(new PixelRect(nearX ? pixels : 0, nearY ? pixels : 0, 100 - (nearX || farX ? pixels : 0), 60 - (nearY || farY ? pixels : 0)), result,
                $"{handle}, zoom={zoom}, dpi={dpi}, offset={clickOffset}, pixels={pixels}");
        }
    }

    [TestMethod]
    public void CtrlUsesTheOriginalDragToMoveOppositeSidesEquallyOnBothAxes()
    {
        foreach (var handle in Enum.GetValues<ScreenshotCanvasHandle>()) foreach (var pixels in new[] { -2, -1, 1, 2 })
        {
            var nearX = handle is ScreenshotCanvasHandle.Left or ScreenshotCanvasHandle.TopLeft or ScreenshotCanvasHandle.BottomLeft;
            var farX = handle is ScreenshotCanvasHandle.Right or ScreenshotCanvasHandle.TopRight or ScreenshotCanvasHandle.BottomRight;
            var nearY = handle is ScreenshotCanvasHandle.Top or ScreenshotCanvasHandle.TopLeft or ScreenshotCanvasHandle.TopRight;
            var farY = handle is ScreenshotCanvasHandle.Bottom or ScreenshotCanvasHandle.BottomLeft or ScreenshotCanvasHandle.BottomRight;
            var dx = nearX ? pixels : farX ? -pixels : 0; var dy = nearY ? pixels : farY ? -pixels : 0;
            var normal = ScreenshotCanvasGeometry.ResizeFromDrag(101, 61, handle, -7, 23, -7 + dx, 23 + dy, 1);
            var centered = ScreenshotCanvasGeometry.ResizeFromDrag(101, 61, handle, -7, 23, -7 + dx, 23 + dy, 1, true);
            Assert.AreEqual(new PixelRect(nearX || farX ? pixels : 0, nearY || farY ? pixels : 0, 101 - (nearX || farX ? 2 * pixels : 0), 61 - (nearY || farY ? 2 * pixels : 0)), centered);
            Assert.AreEqual(101 / 2d, centered.X + centered.Width / 2d); Assert.AreEqual(61 / 2d, centered.Y + centered.Height / 2d);
            Assert.AreEqual(normal, ScreenshotCanvasGeometry.ResizeFromDrag(101, 61, handle, -7, 23, -7 + dx, 23 + dy, 1), "Releasing Ctrl must recompute from the same click.");
        }
    }

    [TestMethod]
    public void SignedHalfPixelRoundingIsSymmetricForInnerAndOuterDrags()
    {
        foreach (var movement in new[] { -.5, -1.5, .5, 1.5 })
        {
            var rounded = (int)Math.Round(movement, MidpointRounding.AwayFromZero);
            var left = ScreenshotCanvasGeometry.ResizeFromDrag(100, 60, ScreenshotCanvasHandle.TopLeft, -5, -5, -5 + movement, -5 + movement, 1);
            var right = ScreenshotCanvasGeometry.ResizeFromDrag(100, 60, ScreenshotCanvasHandle.BottomRight, 105, 65, 105 - movement, 65 - movement, 1);
            Assert.AreEqual(new PixelRect(rounded, rounded, 100 - rounded, 60 - rounded), left);
            Assert.AreEqual(new PixelRect(0, 0, 100 - rounded, 60 - rounded), right);
        }
    }

    [TestMethod]
    public void DimensionsAndMemoryLimitStopTheDragWithoutReversingAnExpandingAxis()
    {
        var tiny = ScreenshotCanvasGeometry.ResizeFromDrag(100, 60, ScreenshotCanvasHandle.TopLeft, 0, 0, 100000, 100000, 1);
        Assert.AreEqual(new PixelRect(99, 59, 1, 1), tiny);
        var centered = ScreenshotCanvasGeometry.ResizeFromDrag(100, 60, ScreenshotCanvasHandle.TopLeft, 0, 0, 100000, 100000, 1, true);
        Assert.AreEqual(new PixelRect(49, 29, 2, 2), centered);
        var wide = ScreenshotCanvasGeometry.ResizeFromDrag(10, 1, ScreenshotCanvasHandle.Right, 0, 0, 1000000, 0, 1);
        Assert.AreEqual(new PixelRect(0, 0, 16384, 1), wide);
        var square = ScreenshotCanvasGeometry.ResizeFromDrag(1000, 1000, ScreenshotCanvasHandle.BottomRight, 0, 0, 20000, 20000, 1);
        Assert.AreEqual(8192, square.Width); Assert.AreEqual(square.Width, square.Height);
        var cap = ScreenshotCanvasGeometry.ResizeFromDrag(8192, 8192, ScreenshotCanvasHandle.BottomRight, 0, 0, 100, 100, 1);
        Assert.AreEqual(new PixelRect(0, 0, 8192, 8192), cap);
        var asymmetric = ScreenshotCanvasGeometry.ResizeFromDrag(8000, 8000, ScreenshotCanvasHandle.TopLeft, 0, 0, -1000, -100, 1);
        Assert.IsTrue(asymmetric.Width >= 8000 && asymmetric.Height >= 8000);
        Assert.IsTrue((long)asymmetric.Width * asymmetric.Height * 4 <= PixelSurface.MaximumPixelBytes);
        Assert.IsTrue(asymmetric.X <= 0 && asymmetric.Y <= 0);
        var symmetric = ScreenshotCanvasGeometry.ResizeFromDrag(8001, 7999, ScreenshotCanvasHandle.TopLeft, 0, 0, -1000, -100, 1, true);
        Assert.AreEqual(8001 / 2d, symmetric.X + symmetric.Width / 2d); Assert.AreEqual(7999 / 2d, symmetric.Y + symmetric.Height / 2d);
        Assert.IsTrue(symmetric.Width >= 8001 && symmetric.Height >= 7999);
        Assert.IsTrue((long)symmetric.Width * symmetric.Height * 4 <= PixelSurface.MaximumPixelBytes);
    }

    [TestMethod]
    public void InvalidInputsAreRejectedBeforeAnyAllocation()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ScreenshotCanvasGeometry.ResizeFromDrag(0, 60, ScreenshotCanvasHandle.Right, 0, 0, 1, 1, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ScreenshotCanvasGeometry.ResizeFromDrag(100, 60, (ScreenshotCanvasHandle)99, 0, 0, 1, 1, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ScreenshotCanvasGeometry.ResizeFromDrag(100, 60, ScreenshotCanvasHandle.Right, double.NaN, 0, 1, 1, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ScreenshotCanvasGeometry.ResizeFromDrag(100, 60, ScreenshotCanvasHandle.Right, 0, 0, double.PositiveInfinity, 1, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ScreenshotCanvasGeometry.ResizeFromDrag(100, 60, ScreenshotCanvasHandle.Right, 0, 0, 1, 1, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ScreenshotCanvasGeometry.ResizeFromDrag(100, 60, ScreenshotCanvasHandle.Right, 0, 0, 1, 1, double.NaN));
    }

    [TestMethod]
    public void SignedBoundsPreserveAllRetainedPixelsAndAnnotationMetadataAndUndo()
    {
        var bytes = new byte[10 * 8 * 4];
        for (var i = 0; i < bytes.Length; i += 4) { bytes[i] = (byte)(i / 4); bytes[i + 1] = 20; bytes[i + 2] = 60; bytes[i + 3] = 255; }
        var doc = new ScreenshotDocument(new(10, 8, bytes));
        doc.Add(new("shape", ScreenshotTool.Rectangle, new(2, 2, 6, 4), Rgba32.Black, Width: 2, ClipBounds: new(2, 2, 5, 3), Cutouts: [new(3, 3, 1, 1)], SamplingBounds: new(0, 0, 10, 8), EraserBackground: Rgba32.Transparent));
        doc.SetBackground(new(30, 60, 90, 128)); doc.SetNextNumber(8); var before = doc.State;
        foreach (var bounds in new[] { new PixelRect(-2, -3, 12, 11), new PixelRect(2, 1, 8, 7), new PixelRect(-2, 1, 14, 5), new PixelRect(1, -1, 10, 8) })
        {
            var layout = ScreenshotCanvasResize.FromBounds(before, bounds); var result = layout.Apply(before);
            Assert.AreSame(before, doc.State, "Preview/Cancel must leave the current document alone.");
            Assert.AreEqual(new Vector2(-bounds.X, -bounds.Y), layout.Offset);
            Assert.AreEqual(before.Background, result.Background); Assert.AreEqual(before.NextNumber, result.NextNumber);
            var moved = before.Annotations[0].Move(layout.Offset); var item = result.Annotations[0];
            Assert.AreEqual(moved.Bounds, item.Bounds); Assert.AreEqual(moved.ClipBounds, item.ClipBounds); Assert.AreEqual(moved.SamplingBounds, item.SamplingBounds);
            Assert.AreEqual(moved.EraserBackground, item.EraserBackground); Assert.AreEqual(moved.Id, item.Id); Assert.AreEqual(moved.Width, item.Width);
            CollectionAssert.AreEqual(moved.Cutouts.ToArray(), item.Cutouts.ToArray());
            for (var y = 0; y < bounds.Height; y++) for (var x = 0; x < bounds.Width; x++)
            {
                var sx = x + bounds.X; var sy = y + bounds.Y;
                var expected = sx >= 0 && sy >= 0 && sx < 10 && sy < 8 ? before.Frame.Pixels.Slice((sy * 10 + sx) * 4, 4).ToArray() : new byte[4];
                CollectionAssert.AreEqual(expected, result.Frame.Pixels.Slice((y * bounds.Width + x) * 4, 4).ToArray());
            }
            doc.Apply(result); Assert.AreSame(result, doc.State); doc.Undo(); Assert.AreSame(before, doc.State); doc.Redo(); Assert.AreSame(result, doc.State); doc.Undo();
        }
        Assert.AreSame(before, ScreenshotCanvasResize.FromBounds(before, new(0, 0, 10, 8)).Apply(before));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ScreenshotCanvasResize.FromBounds(before, new(11, 0, 1, 1)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ScreenshotCanvasResize.FromBounds(before, new(-2, -2, 1, 1)));
    }
}
