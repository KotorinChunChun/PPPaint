using System.Numerics;
using ChunPaint.Core;

namespace ChunPaint.Core.Tests;

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
    public void Rotate_CalculatesRotatedPolygonBounds()
    {
        var region = SelectionRegion.CreateRectangle(new PixelRect(2, 1, 2, 4));

        var rotated = SelectionTransform.Rotate(region, 90, new PixelRect(0, 0, 10, 10));

        Assert.AreEqual(new PixelRect(1, 2, 4, 2), rotated.Bounds);
        Assert.AreEqual(SelectionRegionKind.Polygon, rotated.Kind);
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
}
