namespace ChunPaint.Core;

public static class DocumentFileFormats
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".chunpaint", ".png", ".jpg", ".jpeg", ".bmp", ".gif"
    };

    public static bool IsSupported(string fileName) =>
        !string.IsNullOrWhiteSpace(fileName) && SupportedExtensions.Contains(Path.GetExtension(fileName));
}
