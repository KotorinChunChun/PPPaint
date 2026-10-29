using System.Numerics;
using PPPaint.Core;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class SelectionRegionTests
{
    [TestMethod]
    public void RectangleMask_ContainsOnlyPixelsInsideBounds()
    {
        var region = SelectionRegion.CreateRectangle(new PixelRect(1, 1, 2, 2));
        var mask = region.CreateMask(4, 4);

        Assert.IsFalse(mask[0]);
        Assert.IsTrue(mask[(1 * 4) + 1]);
        Assert.IsTrue(mask[(2 * 4) + 2]);
        Assert.IsFalse(mask[(3 * 4) + 3]);
    }

    [TestMethod]
    public void PolygonMask_UsesPointInPolygon()
    {
        var region = SelectionRegion.CreatePolygon([
            new Vector2(0, 0),
            new Vector2(4, 0),
            new Vector2(0, 4)
        ]);

        Assert.IsTrue(region.ContainsPixel(0, 0));
        Assert.IsTrue(region.ContainsPixel(1, 1));
        Assert.IsFalse(region.ContainsPixel(3, 3));
    }

    [TestMethod]
    public void Move_ClampsRegionToCanvas()
    {
        var region = SelectionRegion.CreateRectangle(new PixelRect(2, 3, 4, 2));

        var moved = SelectionTransform.Move(region, new Vector2(20, -20), new PixelRect(0, 0, 10, 10));

        Assert.AreEqual(new PixelRect(6, 0, 4, 2), moved.Bounds);
    }

    [TestMethod]
    public void Resize_UsesHandleAndKeepsMinimumSize()
    {
        var region = SelectionRegion.CreateRectangle(new PixelRect(2, 2, 3, 3));

        var resized = SelectionTransform.Resize(region, SelectionHandle.BottomRight, new Vector2(2, 1), new PixelRect(0, 0, 10, 10));

        Assert.AreEqual(new PixelRect(2, 2, 5, 4), resized.Bounds);
    }

    [TestMethod]
    public void Resize_CanPreserveAspectRatioAndResizeFromCenter()
    {
        var region = SelectionRegion.CreateRectangle(new PixelRect(20, 30, 40, 20));

        var resized = SelectionTransform.Resize(
            region,
            SelectionHandle.BottomRight,
            new Vector2(10, 5),
            new PixelRect(0, 0, 200, 200),
            preserveAspectRatio: true,
            fromCenter: true);

        Assert.AreEqual(new PixelRect(10, 25, 60, 30), resized.Bounds);
    }

    [TestMethod]
    public void Resize_PreservesAspectRatioAtCanvasEdge()
    {
        var region = SelectionRegion.CreateRectangle(new PixelRect(20, 30, 40, 20));

        var resized = SelectionTransform.Resize(
            region,
            SelectionHandle.BottomRight,
            new Vector2(100, 100),
            new PixelRect(0, 0, 100, 100),
            preserveAspectRatio: true);

        Assert.AreEqual(new PixelRect(20, 30, 80, 40), resized.Bounds);
    }

    [TestMethod]
    public void Rotate_CalculatesRotatedPolygonBounds()
    {
        var region = SelectionRegion.CreateRectangle(new PixelRect(2, 1, 2, 4));

        var rotated = SelectionTransform.Rotate(region, 90, new PixelRect(0, 0, 10, 10));

        Assert.AreEqual(new PixelRect(1, 2, 4, 2), rotated.Bounds);
        Assert.AreEqual(SelectionRegionKind.Polygon, rotated.Kind);
    }

    [TestMethod]
    public void Rotate_FitsFullCanvasSelectionInsideCanvas()
    {
        var canvas = new PixelRect(0, 0, 10, 10);
        var region = SelectionRegion.CreateRectangle(canvas);

        var rotated = SelectionTransform.Rotate(region, 45, canvas);

        Assert.IsGreaterThanOrEqualTo(canvas.X, rotated.Bounds.X);
        Assert.IsGreaterThanOrEqualTo(canvas.Y, rotated.Bounds.Y);
        Assert.IsLessThanOrEqualTo(canvas.Right, rotated.Bounds.Right);
        Assert.IsLessThanOrEqualTo(canvas.Bottom, rotated.Bounds.Bottom);
    }

    [TestMethod]
    public void Skew_TransformsPolygonAndKeepsItInsideCanvas()
    {
        var region = SelectionRegion.CreateRectangle(new PixelRect(2, 2, 4, 3));

        var skewed = SelectionTransform.Skew(region, 20, horizontal: true, new PixelRect(0, 0, 20, 20));

        Assert.AreEqual(SelectionRegionKind.Polygon, skewed.Kind);
        Assert.IsTrue(skewed.Bounds.Right <= 20);
        Assert.IsTrue(skewed.Bounds.Bottom <= 20);
    }

    [TestMethod]
    public void Skew_FitsFullCanvasSelectionInsideCanvas()
    {
        var canvas = new PixelRect(0, 0, 10, 10);
        var region = SelectionRegion.CreateRectangle(canvas);

        var skewed = SelectionTransform.Skew(region, 60, horizontal: true, canvas);

        Assert.IsGreaterThanOrEqualTo(canvas.X, skewed.Bounds.X);
        Assert.IsGreaterThanOrEqualTo(canvas.Y, skewed.Bounds.Y);
        Assert.IsLessThanOrEqualTo(canvas.Right, skewed.Bounds.Right);
        Assert.IsLessThanOrEqualTo(canvas.Bottom, skewed.Bounds.Bottom);
    }
}
