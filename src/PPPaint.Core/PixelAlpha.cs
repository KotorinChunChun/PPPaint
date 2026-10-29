namespace PPPaint.Core;

/// <summary>Win2Dの乗算済みBGRAと、Core画像処理の非乗算BGRAを変換する。</summary>
public static class PixelAlpha
{
    public static byte[] PremultipliedToStraight(byte[] pixels) => Convert(pixels, true);
    public static byte[] StraightToPremultiplied(byte[] pixels) => Convert(pixels, false);

    private static byte[] Convert(byte[] pixels, bool unpremultiply)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Length % 4 != 0) throw new ArgumentException("BGRA画素は4バイト単位で指定してください。", nameof(pixels));
        var result = new byte[pixels.Length];
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            var alpha = pixels[offset + 3];
            result[offset + 3] = alpha;
            for (var channel = 0; channel < 3; channel++)
            {
                var value = pixels[offset + channel];
                result[offset + channel] = alpha == 0 ? (byte)0 : unpremultiply
                    ? (byte)Math.Min(255, (value * 255 + alpha / 2) / alpha)
                    : (byte)((value * alpha + 127) / 255);
            }
        }
        return result;
    }
}
