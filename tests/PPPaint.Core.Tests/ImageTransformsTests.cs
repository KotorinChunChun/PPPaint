using PPPaint.Core;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class ImageTransformsTests
{
    private static readonly Rgba32 Red = new(255, 0, 0);
    private static readonly Rgba32 Green = new(0, 255, 0);
    private static readonly Rgba32 Blue = new(0, 0, 255);
    private static readonly Rgba32 Yellow = new(255, 255, 0);

    [TestMethod]
    public void RotateClockwise_SwapsDimensionsAndMovesPixels()
    {
        var source = Create2By2();
        var result = ImageTransforms.RotateClockwise(source);

        Assert.AreEqual(2, result.Width);
        Assert.AreEqual(Blue, result[0, 0]);
        Assert.AreEqual(Red, result[1, 0]);
        Assert.AreEqual(Yellow, result[0, 1]);
        Assert.AreEqual(Green, result[1, 1]);
    }

    [TestMethod]
    public void RotateCounterClockwise_IsInverseOfClockwise()
    {
        var source = new PixelSurface(3, 2, Rgba32.White);
        source[2, 1] = Red;

        var result = ImageTransforms.RotateCounterClockwise(ImageTransforms.RotateClockwise(source));

        CollectionAssert.AreEqual(source.Snapshot(), result.Snapshot());
    }

    [TestMethod]
    public void Rotate180_MovesCornerPixels()
    {
        var result = ImageTransforms.Rotate180(Create2By2());
        Assert.AreEqual(Yellow, result[0, 0]);
        Assert.AreEqual(Blue, result[1, 0]);
        Assert.AreEqual(Green, result[0, 1]);
        Assert.AreEqual(Red, result[1, 1]);
    }

    [TestMethod]
    public void FlipHorizontal_MirrorsEachRow()
    {
        var result = ImageTransforms.FlipHorizontal(Create2By2());
        Assert.AreEqual(Green, result[0, 0]);
        Assert.AreEqual(Red, result[1, 0]);
    }

    [TestMethod]
    public void FlipVertical_MirrorsEachColumn()
    {
        var result = ImageTransforms.FlipVertical(Create2By2());
        Assert.AreEqual(Blue, result[0, 0]);
        Assert.AreEqual(Red, result[0, 1]);
    }

    [TestMethod]
    public void Crop_CopiesSelectedRectangle()
    {
        var source = new PixelSurface(4, 3, Rgba32.White);
        source[2, 1] = Red;
        source[3, 2] = Blue;

        var result = ImageTransforms.Crop(source, new PixelRect(2, 1, 2, 2));

        Assert.AreEqual(Red, result[0, 0]);
        Assert.AreEqual(Blue, result[1, 1]);
    }

    [TestMethod]
    public void ResizeNearestNeighbor_ReplicatesSourcePixels()
    {
        var source = new PixelSurface(2, 1, Rgba32.White);
        source[0, 0] = Red;
        source[1, 0] = Blue;

        var result = ImageTransforms.ResizeNearestNeighbor(source, 4, 2);

        Assert.AreEqual(Red, result[0, 1]);
        Assert.AreEqual(Red, result[1, 1]);
        Assert.AreEqual(Blue, result[2, 1]);
        Assert.AreEqual(Blue, result[3, 1]);
    }

    [TestMethod]
    public void InvertColors_PreservesAlpha()
    {
        var source = new PixelSurface(1, 1, new Rgba32(10, 20, 30, 40));
        var result = ImageTransforms.InvertColors(source);
        Assert.AreEqual(new Rgba32(245, 235, 225, 40), result[0, 0]);
    }

    [TestMethod]
    public void RotateNearestNeighbor_RotatesFloatingPixelsAndPreservesDimensions()
    {
        var source = new PixelSurface(2, 1, Rgba32.Transparent);
        source[0, 0] = Red;
        source[1, 0] = Blue;

        var result = ImageTransforms.RotateNearestNeighbor(source, 90);

        Assert.AreEqual(1, result.Width);
        Assert.AreEqual(2, result.Height);
        Assert.AreEqual(Red, result[0, 0]);
        Assert.AreEqual(Blue, result[0, 1]);
    }

    [TestMethod]
    public void SkewHorizontal_ExpandsOnlyTheHorizontalDimension()
    {
        var result = ImageTransforms.SkewHorizontal(new PixelSurface(4, 3, Rgba32.White), 20);

        Assert.IsTrue(result.Width > 4);
        Assert.AreEqual(3, result.Height);
    }

    [TestMethod]
    public void SkewVertical_ExpandsOnlyTheVerticalDimension()
    {
        var result = ImageTransforms.SkewVertical(new PixelSurface(4, 3, Rgba32.White), -20);

        Assert.AreEqual(4, result.Width);
        Assert.IsTrue(result.Height > 3);
    }

    [TestMethod]
    public void SkewZeroDegrees_PreservesEveryPixel()
    {
        var source = Create2By2();

        var horizontal = ImageTransforms.SkewHorizontal(source, 0);
        var vertical = ImageTransforms.SkewVertical(source, 0);

        CollectionAssert.AreEqual(source.Snapshot(), horizontal.Snapshot());
        CollectionAssert.AreEqual(source.Snapshot(), vertical.Snapshot());
    }

    [TestMethod]
    public void Skew_PreservesTheCenterPixelAroundTheImageCenter()
    {
        var source = new PixelSurface(3, 3, Rgba32.Transparent);
        source[1, 1] = Red;

        var horizontal = ImageTransforms.SkewHorizontal(source, 20);
        var vertical = ImageTransforms.SkewVertical(source, 20);

        Assert.AreEqual(Red, horizontal[1, 1]);
        Assert.AreEqual(Red, vertical[1, 1]);
    }

    [TestMethod]
    public void Skew_RejectsAnglesThatCouldCreateUnsafeDimensions()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            ImageTransforms.SkewHorizontal(new PixelSurface(2, 2, Rgba32.White), 90));
    }

    [TestMethod]
    public void Resize_RejectsPixelBuffersBeyondSafetyLimit()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            ImageTransforms.ResizeNearestNeighbor(new PixelSurface(1, 1, Rgba32.White), 8193, 8192));
    }

    private static PixelSurface Create2By2()
    {
        var surface = new PixelSurface(2, 2, Rgba32.White);
        surface[0, 0] = Red;
        surface[1, 0] = Green;
        surface[0, 1] = Blue;
        surface[1, 1] = Yellow;
        return surface;
    }
}
