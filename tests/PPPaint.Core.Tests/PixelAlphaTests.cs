using PPPaint.Core;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class PixelAlphaTests
{
    [TestMethod]
    public void HalfTransparentRedRoundTripsWithoutDoubleMultiplication()
    {
        byte[] straight = [0, 0, 255, 128];
        var premultiplied = PixelAlpha.StraightToPremultiplied(straight);
        CollectionAssert.AreEqual(new byte[] { 0, 0, 128, 128 }, premultiplied);
        CollectionAssert.AreEqual(straight, PixelAlpha.PremultipliedToStraight(premultiplied));
        CollectionAssert.AreEqual(new byte[] { 0, 0, 255, 128 }, straight);
    }

    [TestMethod]
    public void OpaqueAndTransparentBoundariesRemainValid()
    {
        byte[] source = [12, 100, 255, 255, 20, 30, 40, 0];
        byte[] expected = [12, 100, 255, 255, 0, 0, 0, 0];
        CollectionAssert.AreEqual(expected, PixelAlpha.StraightToPremultiplied(source));
        CollectionAssert.AreEqual(expected, PixelAlpha.PremultipliedToStraight(source));
        CollectionAssert.AreEqual(new byte[] { 255, 255, 255, 1 }, PixelAlpha.PremultipliedToStraight([1, 1, 1, 1]));
        Assert.Throws<ArgumentException>(() => PixelAlpha.StraightToPremultiplied([1, 2, 3]));
    }

    [TestMethod]
    public void AllValidPremultipliedChannelsSurviveRoundTrip()
    {
        for (var alpha = 1; alpha <= 255; alpha++)
        for (var channel = 0; channel <= alpha; channel++)
        {
            byte[] pixel = [(byte)channel, 0, 0, (byte)alpha];
            CollectionAssert.AreEqual(pixel, PixelAlpha.StraightToPremultiplied(PixelAlpha.PremultipliedToStraight(pixel)));
        }
    }
}
