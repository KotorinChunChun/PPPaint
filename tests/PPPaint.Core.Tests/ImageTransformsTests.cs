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
