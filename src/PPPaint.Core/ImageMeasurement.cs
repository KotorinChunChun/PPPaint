namespace PPPaint.Core;

public enum ImageMeasurementUnit
{
    Pixel,
    Inch,
    Centimeter
}

public readonly record struct ImageMeasurement(double Width, double Height, string UnitSuffix, int FractionDigits);

public static class ImageMeasurements
{
    public const double DefaultDpi = 96d;

    public static double NormalizeDpi(double dpi) =>
        double.IsFinite(dpi) && dpi > 0 ? dpi : DefaultDpi;

    public static ImageMeasurement Convert(int pixelWidth, int pixelHeight, double dpi, ImageMeasurementUnit unit)
    {
        if (pixelWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelWidth));
        }
        if (pixelHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelHeight));
        }

        var normalizedDpi = NormalizeDpi(dpi);
        return unit switch
        {
            ImageMeasurementUnit.Pixel => new ImageMeasurement(pixelWidth, pixelHeight, "px", 0),
            ImageMeasurementUnit.Inch => new ImageMeasurement(pixelWidth / normalizedDpi, pixelHeight / normalizedDpi, "in", 2),
            ImageMeasurementUnit.Centimeter => new ImageMeasurement(
                pixelWidth / normalizedDpi * 2.54d,
                pixelHeight / normalizedDpi * 2.54d,
                "cm",
                2),
            _ => throw new ArgumentOutOfRangeException(nameof(unit))
        };
    }
}
