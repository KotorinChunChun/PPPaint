using PPPaint.Core;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class SoftSelectionIntegrationTests
{
    private static SelectionRegion Half() => SelectionRegion.CreateCoverageMask(1, 1, [128]);

    [TestMethod]
    public void ExtractScalesAlphaOnceAndApplyDoesNotSquareCoverage()
    {
        var source = new PixelSurface(1, 1, new Rgba32(255, 0, 0, 128));
        var extracted = SelectionOperations.Extract(source, Half());
        Assert.AreEqual(new Rgba32(255, 0, 0, 64), extracted[0, 0]);
        var destination = new PixelSurface(1, 1, Rgba32.Transparent);
        SelectionOperations.Apply(destination, Half(), extracted);
        Assert.AreEqual(extracted[0, 0], destination[0, 0]);
        Assert.AreEqual(new Rgba32(255, 0, 0, 128), source[0, 0]);
    }

    [TestMethod]
    public void ClearPartiallyRemovesAlphaWithoutDarkFringe()
    {
        var target = new PixelSurface(1, 1, new Rgba32(255, 0, 0));
        SelectionOperations.Clear(target, Half(), new Rgba32(0, 0, 255, 0));
        Assert.AreEqual(new Rgba32(255, 0, 0, 127), target[0, 0]);
    }

    [TestMethod]
    public void InvertPartiallyChangesColorAndPreservesAlpha()
    {
        var target = new PixelSurface(1, 1, new Rgba32(0, 0, 0, 77));
        SelectionOperations.InvertColors(target, Half());
        Assert.AreEqual(new Rgba32(128, 128, 128, 77), target[0, 0]);
    }

    [TestMethod]
    public void BlackoutDoesNotMixHiddenTransparentColor()
    {
        var source = new PixelSurface(1, 1, new Rgba32(255, 0, 0, 0));
        var output = ImageAdjustments.Blackout(source, Half());
        Assert.AreEqual(new Rgba32(0, 0, 0, 128), output[0, 0]);
        Assert.AreEqual(new Rgba32(255, 0, 0, 0), source[0, 0]);
    }

    [TestMethod]
    public void BrightnessUsesCoverageWithoutChangingSource()
    {
        var source = new PixelSurface(1, 1, new Rgba32(0, 0, 0, 128));
        var result = ImageAdjustments.Adjust(source, 1, 0, 1, Half());
        Assert.AreEqual(new Rgba32(128, 128, 128, 128), result[0, 0]);
        Assert.AreEqual(new Rgba32(0, 0, 0, 128), source[0, 0]);
    }

    [TestMethod]
    public void BlurInterpolatesPremultipliedColorsAndAlpha()
    {
        var source = new PixelSurface(2, 1, new Rgba32(0, 0, 255, 0));
        source[0, 0] = new Rgba32(255, 0, 0);
        var region = SelectionRegion.CreateCoverageMask(2, 1, [128, 0]);
        var result = ImageAdjustments.Blur(source, 1, region);
        Assert.AreEqual(new Rgba32(255, 0, 0, 191), result[0, 0]);
        Assert.AreEqual(source[1, 0], result[1, 0]);
    }

    [TestMethod]
    public void MosaicWeightsSelectedPixelsAndPartiallyAppliesResult()
    {
        var source = new PixelSurface(2, 1, Rgba32.White); source[0, 0] = Rgba32.Black;
        var result = ImageAdjustments.Mosaic(source, 2, SelectionRegion.CreateCoverageMask(2, 1, [128, 255]));
        Assert.AreEqual(new Rgba32(85, 85, 85), result[0, 0]);
        Assert.AreEqual(new Rgba32(170, 170, 170), result[1, 0]);
        Assert.AreEqual(Rgba32.Black, source[0, 0]);
    }

    [TestMethod]
    public void RetouchRestoreAndEraseRespectSoftCoverage()
    {
        var original = new PixelSurface(1, 1, new Rgba32(255, 0, 0));
        var current = new PixelSurface(1, 1, new Rgba32(0, 0, 255, 0));
        var restored = BackgroundMaskOperations.Retouch(original, current, Half(), true);
        Assert.AreEqual(new Rgba32(255, 0, 0, 128), restored[0, 0]);
        current[0, 0] = new Rgba32(0, 255, 0);
        var erased = BackgroundMaskOperations.Retouch(original, current, Half(), false);
        Assert.AreEqual(new Rgba32(0, 255, 0, 127), erased[0, 0]);
        Assert.AreEqual(new Rgba32(0, 255, 0), current[0, 0]);
    }

    [TestMethod]
    public void BinarySelectionKeepsExactLegacyReplacementIncludingHiddenChannels()
    {
        var original = new Rgba32(10, 20, 30, 0); var processed = new Rgba32(40, 50, 60, 0);
        Assert.AreEqual(original, SelectionOperations.InterpolateCoverage(original, processed, 0));
        Assert.AreEqual(processed, SelectionOperations.InterpolateCoverage(original, processed, 255));
        var source = new PixelSurface(1, 1, original);
        var full = SelectionRegion.CreateRectangle(new PixelRect(0, 0, 1, 1));
        Assert.AreEqual(original, SelectionOperations.Extract(source, full)[0, 0]);
        SelectionOperations.Clear(source, full, processed);
        Assert.AreEqual(processed, source[0, 0]);
    }
}
