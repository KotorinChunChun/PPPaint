using System.Globalization;

namespace ChunPaint;

internal static class CompatConvert
{
    public static uint ToUInt32(ReadOnlySpan<char> value, int fromBase)
    {
        if (fromBase != 16)
        {
            throw new ArgumentOutOfRangeException(nameof(fromBase));
        }

        return uint.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }
}
