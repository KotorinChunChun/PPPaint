namespace PPPaint.Core;

public readonly record struct BatchImageSize(int Width, int Height);

public static class BatchImagePlan
{
    public static BatchImageSize GetSize(int sourceWidth, int sourceHeight, int width, int height, bool maintainAspect)
    {
        ValidateSize(sourceWidth, sourceHeight);
        if (width < 1 || width > 16384 || height < 1 || height > 16384) throw new ArgumentOutOfRangeException(nameof(width));
        if (maintainAspect)
        {
            var scale = Math.Min((double)width / sourceWidth, (double)height / sourceHeight);
            width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
            height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        }
        ValidateSize(width, height);
        return new(width, height);
    }

    public static string GetOutputName(string sourceName, DocumentFileFormat format, IEnumerable<string> occupiedNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName); ArgumentNullException.ThrowIfNull(occupiedNames);
        var extension = format switch { DocumentFileFormat.Png => ".png", DocumentFileFormat.Jpeg => ".jpg", DocumentFileFormat.Webp => ".webp", _ => throw new ArgumentOutOfRangeException(nameof(format)) };
        // Windows以外でのテストでもパス区切りや予約名を出力名として許可しない。
        var stem = Path.GetFileNameWithoutExtension(sourceName.Replace('\\', '/'));
        stem = new string(stem.Select(c => c < 32 || "<>:\"/\\|?*".Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
        if (stem.Length == 0) stem = "image";
        if (stem.Length > 150) stem = stem[..150];
        stem += "_converted";
        var occupied = new HashSet<string>(occupiedNames, StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < 100000; index++)
        {
            var name = stem + (index == 0 ? "" : "_" + index) + extension;
            if (!occupied.Contains(name)) return name;
        }
        throw new IOException("出力名の空きを見つけられませんでした。");
    }

    private static void ValidateSize(int width, int height)
    {
        if (width < 1 || height < 1 || (long)width * height > PixelSurface.MaximumPixelBytes / 4)
            throw new ArgumentOutOfRangeException(nameof(width), "画像の画素数が上限を超えています。");
    }
}
