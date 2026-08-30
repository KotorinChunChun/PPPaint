namespace ChunPaint.Core;

public enum DocumentFileFormat
{
    ChunPaint,
    Png,
    Jpeg,
    Bmp,
    Gif
}

public static class DocumentFileFormats
{
    public const long MaximumInputBytes = 512L * 1024 * 1024;
    private static readonly IReadOnlyDictionary<string, DocumentFileFormat> Formats =
        new Dictionary<string, DocumentFileFormat>(StringComparer.OrdinalIgnoreCase)
        {
            [".chunpaint"] = DocumentFileFormat.ChunPaint,
            [".png"] = DocumentFileFormat.Png,
            [".jpg"] = DocumentFileFormat.Jpeg,
            [".jpeg"] = DocumentFileFormat.Jpeg,
            [".bmp"] = DocumentFileFormat.Bmp,
            [".gif"] = DocumentFileFormat.Gif
        };

    public static bool IsSupported(string fileName) => TryGetFormat(fileName, out _);

    public static bool TryGetFormat(string fileName, out DocumentFileFormat format)
    {
        format = default;
        return !string.IsNullOrWhiteSpace(fileName) &&
            Formats.TryGetValue(Path.GetExtension(fileName), out format);
    }

    public static bool IsProject(string fileName) =>
        TryGetFormat(fileName, out var format) && format == DocumentFileFormat.ChunPaint;

    public static bool IsImage(string fileName) =>
        TryGetFormat(fileName, out var format) && format != DocumentFileFormat.ChunPaint;

    public static void ValidateInputSize(long length)
    {
        if (length < 0 || length > MaximumInputBytes)
        {
            throw new InvalidDataException($"The input file exceeds the {MaximumInputBytes / (1024 * 1024)} MiB safety limit.");
        }
    }

    public static bool HasExpectedImageSignature(DocumentFileFormat format, ReadOnlySpan<byte> header) => format switch
    {
        DocumentFileFormat.Png => header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
        DocumentFileFormat.Jpeg => header.Length >= 2 && header[0] == 0xFF && header[1] == 0xD8,
        DocumentFileFormat.Bmp => header.Length >= 2 && header[0] == (byte)'B' && header[1] == (byte)'M',
        DocumentFileFormat.Gif => header.Length >= 6 && (header[..6].SequenceEqual("GIF87a"u8) || header[..6].SequenceEqual("GIF89a"u8)),
        _ => false
    };
}
