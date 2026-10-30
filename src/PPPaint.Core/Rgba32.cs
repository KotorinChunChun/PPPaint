namespace PPPaint.Core;

public readonly record struct Rgba32(byte R, byte G, byte B, byte A = 255)
{
    public static Rgba32 Transparent => new(0, 0, 0, 0);
    public static Rgba32 White => new(255, 255, 255);
    public static Rgba32 Black => new(0, 0, 0);

    public static Rgba32 FromPremultipliedBgra(ReadOnlySpan<byte> pixel)
    {
        if (pixel.Length != 4) throw new ArgumentException("1画素のBGRAを指定してください。", nameof(pixel));
        var alpha = pixel[3];
        if (alpha == 0) return Transparent;
        byte Channel(byte value) => (byte)Math.Min(255, (value * 255 + alpha / 2) / alpha);
        return new(Channel(pixel[2]), Channel(pixel[1]), Channel(pixel[0]), alpha);
    }
}
