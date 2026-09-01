using ChunPaint.Core;

namespace ChunPaint.Core.Tests;

[TestClass]
public sealed class ImageMeasurementTests
{
    [TestMethod]
    public void Convert_ReturnsPixelsWithoutChangingDimensions()
    {
        var result = ImageMeasurements.Convert(1152, 648, 144, ImageMeasurementUnit.Pixel);

        Assert.AreEqual(1152d, result.Width);
        Assert.AreEqual(648d, result.Height);
        Assert.AreEqual("px", result.UnitSuffix);
        Assert.AreEqual(0, result.FractionDigits);
    }

    [TestMethod]
    public void Convert_UsesDpiForInchesAndCentimeters()
    {
        var inches = ImageMeasurements.Convert(300, 150, 150, ImageMeasurementUnit.Inch);
        var centimeters = ImageMeasurements.Convert(300, 150, 150, ImageMeasurementUnit.Centimeter);

        Assert.AreEqual(2d, inches.Width, 0.0001d);
        Assert.AreEqual(1d, inches.Height, 0.0001d);
        Assert.AreEqual(5.08d, centimeters.Width, 0.0001d);
        Assert.AreEqual(2.54d, centimeters.Height, 0.0001d);
    }

    [TestMethod]
    [DataRow(0d)]
    [DataRow(-1d)]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    public void Convert_NormalizesInvalidDpi(double dpi)
    {
        var result = ImageMeasurements.Convert(192, 96, dpi, ImageMeasurementUnit.Inch);

        Assert.AreEqual(2d, result.Width, 0.0001d);
        Assert.AreEqual(1d, result.Height, 0.0001d);
    }

    [TestMethod]
    public void Convert_RejectsInvalidDimensionsAndUnits()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            ImageMeasurements.Convert(0, 10, 96, ImageMeasurementUnit.Pixel));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            ImageMeasurements.Convert(10, 0, 96, ImageMeasurementUnit.Pixel));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            ImageMeasurements.Convert(10, 10, 96, (ImageMeasurementUnit)99));
    }
}
