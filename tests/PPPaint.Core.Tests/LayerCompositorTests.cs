using PPPaint.Core;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class LayerCompositorTests
{
    private static PixelSurface Pixel(Rgba32 color) => new(1, 1, color);

    [TestMethod]
    public void NormalRespectsStraightAlphaAndDoesNotMutateInputs()
    {
        var back = Pixel(new(255, 0, 0, 128)); var front = Pixel(new(0, 0, 255, 128));
        var result = LayerCompositor.Composite(back, front);
        Assert.AreEqual(new Rgba32(85, 0, 170, 192), result[0, 0]);
        Assert.AreEqual(new Rgba32(255, 0, 0, 128), back[0, 0]);
        Assert.AreEqual(new Rgba32(0, 0, 255, 128), front[0, 0]);
        Assert.AreNotSame(back, result);
    }

    [TestMethod]
    public void BlendModesApplyOnlyWithinOverlappingCoverage()
    {
        var back = Pixel(new(255, 0, 0, 128)); var front = Pixel(new(0, 0, 255, 128));
        Assert.AreEqual(new Rgba32(85, 0, 85, 192), LayerCompositor.Composite(back, front, blendMode: LayerBlendMode.Multiply)[0, 0]);
        Assert.AreEqual(new Rgba32(170, 0, 170, 192), LayerCompositor.Composite(back, front, blendMode: LayerBlendMode.Screen)[0, 0]);
        foreach (var mode in Enum.GetValues<LayerBlendMode>())
            Assert.AreEqual(front[0, 0], LayerCompositor.Composite(Pixel(Rgba32.Transparent), front, blendMode: mode)[0, 0]);
        var overlay = LayerCompositor.Composite(Pixel(new(64, 192, 0, 255)), Pixel(new(128, 128, 255, 255)), blendMode: LayerBlendMode.Overlay);
        Assert.AreEqual(new Rgba32(64, 192, 0, 255), overlay[0, 0]);
    }

    [TestMethod]
    public void MaskAndOpacityMultiplyCoverageWithoutDarkeningRgb()
    {
        var front = Pixel(new(255, 100, 20, 255));
        Assert.AreEqual(new Rgba32(255, 100, 20, 64), LayerCompositor.Composite(Pixel(Rgba32.Transparent), front, .5f, mask: [128])[0, 0]);
        var back = Pixel(new(2, 3, 4, 91));
        Assert.AreEqual(back[0, 0], LayerCompositor.Composite(back, front, mask: [0])[0, 0]);
        Assert.AreEqual(front[0, 0], LayerCompositor.Composite(back, front, mask: [255])[0, 0]);
        Assert.Throws<ArgumentException>(() => LayerCompositor.Composite(back, front, mask: []));
        Assert.Throws<ArgumentOutOfRangeException>(() => LayerCompositor.Composite(back, front, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => LayerCompositor.Composite(back, front, blendMode: (LayerBlendMode)99));
    }

    [TestMethod]
    public void GrayscaleSnapshotsShareArbitraryLengthMasks()
    {
        var first = PixelBlockSnapshot.CaptureBytes([20, 30, 40]);
        var second = PixelBlockSnapshot.CaptureBytes([20, 30, 40], first);
        Assert.AreSame(first.Blocks[0], second.Blocks[0]);
        CollectionAssert.AreEqual(new byte[] { 20, 30, 40 }, second.ToArray());
        Assert.Throws<ArgumentException>(() => PixelBlockSnapshot.Capture([20, 30, 40]));
    }
}
