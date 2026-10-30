namespace PPPaint.Core.Tests;

[TestClass]
public sealed class ColorSamplingTests
{
    [TestMethod]
    public void SampleConvertsPremultipliedBgraWithoutDarkeningTranslucentColors()
    {
        // Quantization of premultiplied 8-bit pixels cannot recover the original channel exactly.
        Assert.AreEqual(new Rgba32(199, 100, 50, 128), Rgba32.FromPremultipliedBgra([25, 50, 100, 128]));
        Assert.AreEqual(new Rgba32(220, 38, 12), Rgba32.FromPremultipliedBgra([12, 38, 220, 255]));
        Assert.AreEqual(Rgba32.Transparent, Rgba32.FromPremultipliedBgra([0, 0, 0, 0]));
        Assert.AreEqual(new Rgba32(255, 255, 255, 1), Rgba32.FromPremultipliedBgra([1, 1, 1, 1]));
        Assert.ThrowsExactly<ArgumentException>(() => Rgba32.FromPremultipliedBgra([]));
    }
}
