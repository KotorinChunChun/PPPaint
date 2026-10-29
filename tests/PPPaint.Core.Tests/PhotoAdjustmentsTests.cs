using PPPaint.Core;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class PhotoAdjustmentsTests
{
    [TestMethod]
    public void NeutralAdjustmentPreservesEveryByteAndSource()
    {
        var source = Ramp();
        source[17, 0] = new Rgba32(98, 15, 201, 0);
        var before = source.Snapshot();
        var result = PhotoAdjustments.Apply(source, new());
        Assert.AreNotSame(source, result);
        CollectionAssert.AreEqual(before, result.Snapshot());
        CollectionAssert.AreEqual(before, source.Snapshot());
    }

    [TestMethod]
    public void LevelsClipEndpointsGammaBrightensAndAlphaIsPreserved()
    {
        var source = Ramp();
        source[128, 0] = new Rgba32(128, 128, 128, 77);
        var levels = PhotoAdjustments.Apply(source, new(BlackPoint: .25, WhitePoint: .75));
        Assert.AreEqual((byte)0, levels[0, 0].R);
        Assert.AreEqual((byte)255, levels[255, 0].R);
        var gamma = PhotoAdjustments.Apply(source, new(Gamma: 2));
        Assert.AreEqual((byte)181, gamma[128, 0].R);
        Assert.AreEqual((byte)77, gamma[128, 0].A);
    }

    [TestMethod]
    public void CrossingCurveControlsRemainMonotonicWithoutOvershoot()
    {
        foreach (var settings in new[] { new PhotoAdjustmentSettings(Shadows: .5, Midtones: -.5, Highlights: -.5), new PhotoAdjustmentSettings(Shadows: -.5, Midtones: .5, Highlights: .5) })
        {
            var result = PhotoAdjustments.Apply(Ramp(), settings);
            Assert.AreEqual((byte)0, result[0, 0].R);
            Assert.AreEqual((byte)255, result[255, 0].R);
            for (var x = 1; x < 256; x++) Assert.IsTrue(result[x, 0].R >= result[x - 1, 0].R);
        }
    }

    [TestMethod]
    public void WhiteBalanceAndSelectionAffectOnlyRequestedPixels()
    {
        var source = new PixelSurface(2, 1, new Rgba32(128, 128, 128, 99));
        var result = PhotoAdjustments.Apply(source, new(Temperature: 1, Tint: 1), SelectionRegion.CreateRectangle(new PixelRect(0, 0, 1, 1)));
        Assert.IsTrue(result[0, 0].R > result[0, 0].B);
        Assert.IsTrue(result[0, 0].G < source[0, 0].G);
        Assert.AreEqual(source[1, 0], result[1, 0]);
        Assert.AreEqual((byte)99, result[0, 0].A);
    }

    [TestMethod]
    public void FeatheredSelectionPartiallyAppliesAdjustment()
    {
        var source = new PixelSurface(1, 1, new Rgba32(100, 100, 100, 99));
        var settings = new PhotoAdjustmentSettings(Temperature: 1);
        var full = PhotoAdjustments.Apply(source, settings);
        var partial = PhotoAdjustments.Apply(source, settings, SelectionRegion.CreateCoverageMask(1, 1, [128]));
        Assert.IsTrue(partial[0, 0].R > source[0, 0].R && partial[0, 0].R < full[0, 0].R);
        Assert.AreEqual((byte)99, partial[0, 0].A);
    }

    [TestMethod]
    public void HistogramIgnoresFullyTransparentHiddenColor()
    {
        var source = new PixelSurface(3, 1, Rgba32.White);
        source[0, 0] = Rgba32.Black; source[2, 0] = new Rgba32(255, 255, 255, 0);
        var bins = PhotoAdjustments.Histogram(source);
        Assert.AreEqual(256, bins.Length);
        Assert.AreEqual(1L, bins[0]); Assert.AreEqual(1L, bins[255]); Assert.AreEqual(2L, bins.Sum());
    }

    [TestMethod]
    public void InvalidSettingsAndCancellationAreRejected()
    {
        foreach (var settings in new[] { new PhotoAdjustmentSettings(BlackPoint: 1, WhitePoint: 0), new PhotoAdjustmentSettings(Gamma: 0), new PhotoAdjustmentSettings(Tint: double.NaN), new PhotoAdjustmentSettings(Shadows: .6), new PhotoAdjustmentSettings(Temperature: double.PositiveInfinity) })
            Assert.Throws<ArgumentException>(() => PhotoAdjustments.Validate(settings));
        Assert.ThrowsExactly<OperationCanceledException>(() => PhotoAdjustments.Apply(Ramp(), new(), token: new CancellationToken(true)));
    }

    private static PixelSurface Ramp()
    {
        var source = new PixelSurface(256, 1, Rgba32.Transparent);
        for (var x = 0; x < 256; x++) source[x, 0] = new Rgba32((byte)x, (byte)x, (byte)x);
        return source;
    }
}
