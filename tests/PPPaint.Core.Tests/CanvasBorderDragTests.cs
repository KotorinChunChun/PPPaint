using System.Numerics;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class CanvasBorderDragTests
{
    [TestMethod]
    public void EveryDipOfTheRenderedTenDipBorderIsInteractiveAndImagePixelsAreNot()
    {
        foreach (var zoom in new[] { .25f, .5f, 1f, 2f, 4f })
        {
            var image = new ScreenshotBox(0, 0, 100 * zoom, 60 * zoom);
            var outer = new ScreenshotBox(-10, -10, image.Width + 20, image.Height + 20);
            for (var inset = 1; inset <= 10; inset++)
            {
                for (var y = 0; y <= image.Height; y++)
                {
                    Assert.AreEqual(ScreenshotCanvasEdge.Left, ScreenshotCanvasGeometry.HitBorder(outer, image, new(-inset, y)));
                    Assert.AreEqual(ScreenshotCanvasEdge.Right, ScreenshotCanvasGeometry.HitBorder(outer, image, new(image.Right + inset, y)));
                }
                for (var x = 0; x <= image.Width; x++)
                {
                    Assert.AreEqual(ScreenshotCanvasEdge.Top, ScreenshotCanvasGeometry.HitBorder(outer, image, new(x, -inset)));
                    Assert.AreEqual(ScreenshotCanvasEdge.Bottom, ScreenshotCanvasGeometry.HitBorder(outer, image, new(x, image.Bottom + inset)));
                }
            }
            foreach (var point in new Vector2[] { new(0, 0), new(image.Right, image.Bottom), new(0, image.Height / 2), new(image.Width / 2, 0), new(image.Width / 2, image.Height / 2), new(.01f, .01f) })
                Assert.IsNull(ScreenshotCanvasGeometry.HitBorder(outer, image, point), "Image contents and image-side selection handles must remain editable.");
            foreach (var point in new Vector2[] { new(-10.01f, 0), new(0, -10.01f), new(image.Right + 10.01f, 0), new(0, image.Bottom + 10.01f) })
                Assert.IsNull(ScreenshotCanvasGeometry.HitBorder(outer, image, point));
            Assert.AreEqual(ScreenshotCanvasEdge.Left, ScreenshotCanvasGeometry.HitBorder(outer, image, new(-.01f, image.Height / 2)), "The white strip directly next to the image is included.");
        }
    }

    [TestMethod]
    public void AllCornerPixelsChooseTheClosestEdgeWithStableTies()
    {
        var image = new ScreenshotBox(0, 0, 100, 60); var outer = new ScreenshotBox(-10, -10, 120, 80);
        for (var x = 1; x <= 10; x++) for (var y = 1; y <= 10; y++)
        {
            Assert.AreEqual(x <= y ? ScreenshotCanvasEdge.Left : ScreenshotCanvasEdge.Top, ScreenshotCanvasGeometry.HitBorder(outer, image, new(-x, -y)));
            Assert.AreEqual(y <= x ? ScreenshotCanvasEdge.Top : ScreenshotCanvasEdge.Right, ScreenshotCanvasGeometry.HitBorder(outer, image, new(100 + x, -y)));
            Assert.AreEqual(x <= y ? ScreenshotCanvasEdge.Left : ScreenshotCanvasEdge.Bottom, ScreenshotCanvasGeometry.HitBorder(outer, image, new(-x, 60 + y)));
            Assert.AreEqual(x <= y ? ScreenshotCanvasEdge.Right : ScreenshotCanvasEdge.Bottom, ScreenshotCanvasGeometry.HitBorder(outer, image, new(100 + x, 60 + y)));
        }
    }

    [TestMethod]
    public void HitTestingFollowsActualBoundsRatherThanAssumingFixedPaddingOrOrigin()
    {
        var image = new ScreenshotBox(4, 8, 30, 20); var outer = new ScreenshotBox(-5, -7, 51, 45);
        Assert.AreEqual(ScreenshotCanvasEdge.Left, ScreenshotCanvasGeometry.HitBorder(outer, image, new(-5, 15)));
        Assert.AreEqual(ScreenshotCanvasEdge.Top, ScreenshotCanvasGeometry.HitBorder(outer, image, new(15, -7)));
        Assert.AreEqual(ScreenshotCanvasEdge.Right, ScreenshotCanvasGeometry.HitBorder(outer, image, new(46, 15)));
        Assert.AreEqual(ScreenshotCanvasEdge.Bottom, ScreenshotCanvasGeometry.HitBorder(outer, image, new(15, 38)));
        Assert.IsNull(ScreenshotCanvasGeometry.HitBorder(outer, image, new(-5.01f, 15)));
        Assert.IsNull(ScreenshotCanvasGeometry.HitBorder(outer, image, new(15, 38.01f)));
        Assert.IsNull(ScreenshotCanvasGeometry.HitBorder(outer, image, new(4, 15)));
    }

    [TestMethod]
    public void ClickOffsetZoomAndDpiDoNotChangeOneAndTwoPixelDrags()
    {
        foreach (var edge in Enum.GetValues<ScreenshotCanvasEdge>())
        foreach (var zoom in new[] { .25, .5, 1, 2, 4 })
        foreach (var dpiScale in new[] { 1, 1.25, 1.5, 2 })
        foreach (var borderOffset in new[] { .01, 5, 10 })
        foreach (var pixels in new[] { 0, 1, 2 })
        {
            var farEdge = edge is ScreenshotCanvasEdge.Right or ScreenshotCanvasEdge.Bottom;
            var size = edge is ScreenshotCanvasEdge.Left or ScreenshotCanvasEdge.Right ? 100 : 60;
            var startDip = farEdge ? size * zoom + borderOffset : -borderOffset;
            // OS physical coordinates -> XAML DIPs -> source-image pixel movement.
            var startPhysical = startDip * dpiScale;
            var endPhysical = startPhysical + (farEdge ? -pixels : pixels) * zoom * dpiScale;
            var crop = ScreenshotCanvasGeometry.CropFromDrag(100, 60, edge, startPhysical / dpiScale, endPhysical / dpiScale, zoom);
            var expected = edge switch
            {
                ScreenshotCanvasEdge.Left => new PixelRect(pixels, 0, 100 - pixels, 60),
                ScreenshotCanvasEdge.Top => new PixelRect(0, pixels, 100, 60 - pixels),
                ScreenshotCanvasEdge.Right => new PixelRect(0, 0, 100 - pixels, 60),
                _ => new PixelRect(0, 0, 100, 60 - pixels)
            };
            Assert.AreEqual(expected, crop, $"{edge}, zoom {zoom}, DPI {dpiScale}, click {borderOffset}, movement {pixels}");
        }
    }

    [TestMethod]
    public void SignedHalfPixelsAreRoundedSymmetricallyOnEverySide()
    {
        foreach (var zoom in new[] { .25, .5, 1, 2, 4 })
        foreach (var movement in new[] { .49, .5, 1.49, 1.5, 2.5 })
        {
            var rounded = (int)Math.Round(movement, MidpointRounding.AwayFromZero);
            var left = ScreenshotCanvasGeometry.CropFromDrag(100, 60, ScreenshotCanvasEdge.Left, -5, -5 + movement * zoom, zoom);
            var right = ScreenshotCanvasGeometry.CropFromDrag(100, 60, ScreenshotCanvasEdge.Right, 100 * zoom + 5, 100 * zoom + 5 - movement * zoom, zoom);
            var top = ScreenshotCanvasGeometry.CropFromDrag(100, 60, ScreenshotCanvasEdge.Top, -5, -5 + movement * zoom, zoom);
            var bottom = ScreenshotCanvasGeometry.CropFromDrag(100, 60, ScreenshotCanvasEdge.Bottom, 60 * zoom + 5, 60 * zoom + 5 - movement * zoom, zoom);
            Assert.AreEqual(100 - rounded, left.Width); Assert.AreEqual(left.Width, right.Width);
            Assert.AreEqual(60 - rounded, top.Height); Assert.AreEqual(top.Height, bottom.Height);
        }
    }

    [TestMethod]
    public void CtrlRecomputesFromTheOriginalPointerAndClampsWithoutExpanding()
    {
        foreach (var edge in Enum.GetValues<ScreenshotCanvasEdge>())
        {
            var farEdge = edge is ScreenshotCanvasEdge.Right or ScreenshotCanvasEdge.Bottom;
            var horizontal = edge is ScreenshotCanvasEdge.Left or ScreenshotCanvasEdge.Right;
            var delta = farEdge ? -2 : 2;
            var normal = ScreenshotCanvasGeometry.CropFromDrag(100, 60, edge, 35, 35 + delta, 1);
            var centered = ScreenshotCanvasGeometry.CropFromDrag(100, 60, edge, 35, 35 + delta, 1, true);
            Assert.AreEqual(horizontal ? new PixelRect(2, 0, 96, 60) : new PixelRect(0, 2, 100, 56), centered);
            Assert.AreEqual(normal, ScreenshotCanvasGeometry.CropFromDrag(100, 60, edge, 35, 35 + delta, 1), "Releasing Ctrl returns to the same original movement.");
            Assert.AreEqual(new PixelRect(0, 0, 100, 60), ScreenshotCanvasGeometry.CropFromDrag(100, 60, edge, 35, 35 - delta, 1));
            var limited = ScreenshotCanvasGeometry.CropFromDrag(100, 60, edge, 0, delta * 10000, 1);
            Assert.AreEqual(1, horizontal ? limited.Width : limited.Height);
            var symmetricLimit = ScreenshotCanvasGeometry.CropFromDrag(100, 60, edge, 0, delta * 10000, 1, true);
            Assert.IsTrue(horizontal ? symmetricLimit.Width is 1 or 2 : symmetricLimit.Height is 1 or 2);
        }
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ScreenshotCanvasGeometry.CropFromDrag(100, 60, ScreenshotCanvasEdge.Left, 0, 1, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ScreenshotCanvasGeometry.CropFromDrag(100, 60, ScreenshotCanvasEdge.Left, double.NaN, 1, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ScreenshotCanvasGeometry.CropFromDrag(100, 60, ScreenshotCanvasEdge.Left, 0, double.PositiveInfinity, 1));
    }

    [TestMethod]
    public void PreviewCanBeDiscardedAndCommittedCropPreservesPixelsObjectsAndUndo()
    {
        foreach (var edge in Enum.GetValues<ScreenshotCanvasEdge>())
        foreach (var centered in new[] { false, true })
        {
            var bytes = Enumerable.Range(0, 10 * 8 * 4).Select(i => (byte)i).ToArray();
            var doc = new ScreenshotDocument(new(10, 8, bytes));
            doc.Add(new("rectangle", ScreenshotTool.Rectangle, new(2, 2, 6, 4), Rgba32.Black));
            var before = doc.State; var source = before.Frame;
            var delta = edge is ScreenshotCanvasEdge.Right or ScreenshotCanvasEdge.Bottom ? -2 : 2;
            var preview = ScreenshotCanvasGeometry.CropFromDrag(10, 8, edge, -5, -5 + delta, 1, centered);
            Assert.AreSame(before, doc.State, "Discarding the preview (Escape/capture cancellation) must need no document repair.");
            doc.Crop(preview.X, preview.Y, preview.Width, preview.Height);
            var after = doc.State;
            for (var y = 0; y < preview.Height; y++)
                CollectionAssert.AreEqual(source.Pixels.Slice(((y + preview.Y) * 10 + preview.X) * 4, preview.Width * 4).ToArray(),
                    after.Frame.Pixels.Slice(y * preview.Width * 4, preview.Width * 4).ToArray());
            Assert.AreEqual(before.Annotations[0].Bounds.Width, after.Annotations[0].Bounds.Width);
            Assert.AreEqual(before.Annotations[0].Bounds.Height, after.Annotations[0].Bounds.Height);
            Assert.AreEqual(before.Annotations[0].Bounds.X - preview.X, after.Annotations[0].Bounds.X);
            Assert.AreEqual(before.Annotations[0].Bounds.Y - preview.Y, after.Annotations[0].Bounds.Y);
            doc.Undo(); Assert.AreSame(before, doc.State); doc.Redo(); Assert.AreSame(after, doc.State);
        }
    }
}
