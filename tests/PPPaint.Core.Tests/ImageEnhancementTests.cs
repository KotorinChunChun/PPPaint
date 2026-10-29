using System.Numerics;
using PPPaint.Core;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class ImageEnhancementTests
{
    [TestMethod]
    public void Resampling_IgnoresHiddenColorInTransparentPixels()
    {
        var s = new PixelSurface(2, 1, new Rgba32(0, 0, 255, 0)); s[0, 0] = new(255, 0, 0);
        foreach (var mode in new[] { ResamplingMode.Area, ResamplingMode.Bilinear })
            Assert.AreEqual(new Rgba32(255, 0, 0, 128), ImageResampling.Resize(s, 1, 1, mode)[0, 0]);
    }
    [TestMethod]
    public void Resampling_NearestPreservesExactPixelsAndRejectsOversize()
    {
        var s = new PixelSurface(1, 1, new Rgba32(12, 34, 56, 78));
        Assert.AreEqual(s[0, 0], ImageResampling.Resize(s, 3, 2, ResamplingMode.Nearest)[2, 1]);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ImageResampling.Resize(s, 100000, 100000, ResamplingMode.Area));
    }
    [TestMethod]
    public void Adjustments_PreservePixelsOutsideSelectionAndSource()
    {
        var s = new PixelSurface(3, 2, new Rgba32(40, 80, 120, 90));
        var region = SelectionRegion.CreateRectangle(new PixelRect(1, 0, 1, 1));
        foreach (var result in new[] { ImageAdjustments.Blackout(s, region), ImageAdjustments.Adjust(s, .3, .2, 1.4, region), ImageAdjustments.Blur(s, 2, region), ImageAdjustments.Mosaic(s, 2, region) })
            Assert.AreEqual(s[0, 0], result[0, 0]);
        Assert.AreEqual(new Rgba32(0, 0, 0, 255), ImageAdjustments.Blackout(s, region)[1, 0]);
        Assert.AreEqual(new Rgba32(40, 80, 120, 90), s[1, 0]);
        Assert.AreEqual(s[1, 0], ImageAdjustments.Adjust(s, 0, 0, 1)[1, 0]);
    }
    [TestMethod]
    public void BlurAndMosaic_AverageAlphaWithoutBlueFringes()
    {
        var s = new PixelSurface(2, 1, new Rgba32(0, 0, 255, 0)); s[0, 0] = new(255, 0, 0);
        Assert.AreEqual(new Rgba32(255, 0, 0, 128), ImageAdjustments.Blur(s, 1)[0, 0]);
        Assert.AreEqual(new Rgba32(255, 0, 0, 128), ImageAdjustments.Mosaic(s, 2)[1, 0]);
    }
    [TestMethod]
    public void InvalidValuesAndCancellation_AreRejected()
    {
        var s = new PixelSurface(1, 1, Rgba32.White); var token = new CancellationToken(true);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ImageAdjustments.Adjust(s, double.NaN, 0, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ImageAdjustments.Blur(s, -1));
        Assert.ThrowsExactly<OperationCanceledException>(() => ImageAdjustments.Mosaic(s, 1, cancellationToken: token));
        Assert.ThrowsExactly<OperationCanceledException>(() => ImageResampling.Resize(s, 1, 1, ResamplingMode.Area, token));
    }
    [TestMethod]
    public void StrokeFilter_UsesMouseDefaultAndFlushesExactEndpoint()
    {
        var filter = new StrokeInputFilter(.5f);
        Assert.AreEqual(1f, filter.Add(Vector2.Zero, .2f).Pressure);
        var next = filter.Add(new Vector2(10, 20), .3f, true);
        Assert.AreEqual(new Vector2(5, 10), next.Point); Assert.AreEqual(.3f, next.Pressure);
        Assert.AreEqual(new Vector2(10, 20), filter.Flush().Point);
        Assert.AreEqual(new Vector2(20, 30), filter.Add(new Vector2(20, 30)).Point);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => filter.Add(new Vector2(float.NaN, 0)));
    }
}
