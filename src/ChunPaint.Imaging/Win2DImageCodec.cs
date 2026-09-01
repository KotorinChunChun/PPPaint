using ChunPaint.Core;
using Microsoft.Graphics.Canvas;
using Windows.Storage.Streams;

namespace ChunPaint.Imaging;

public static class Win2DImageCodec
{
    public static CanvasBitmapFileFormat GetBitmapFormat(DocumentFileFormat format) => format switch
    {
        DocumentFileFormat.Png => CanvasBitmapFileFormat.Png,
        DocumentFileFormat.Jpeg => CanvasBitmapFileFormat.Jpeg,
        DocumentFileFormat.Bmp => CanvasBitmapFileFormat.Bmp,
        DocumentFileFormat.Gif => CanvasBitmapFileFormat.Gif,
        _ => throw new ArgumentOutOfRangeException(nameof(format), "画像形式を指定してください。")
    };

    public static float GetQuality(DocumentFileFormat format) =>
        format == DocumentFileFormat.Jpeg ? 0.92f : 1f;

    public static async Task SaveAsync(
        CanvasBitmap bitmap,
        IRandomAccessStream stream,
        DocumentFileFormat format,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();
        stream.Size = 0;
        await bitmap.SaveAsync(stream, GetBitmapFormat(format), GetQuality(format)).AsTask(cancellationToken);
        stream.Seek(0);
    }

    public static async Task<CanvasBitmap> LoadAsync(
        ICanvasResourceCreator resourceCreator,
        IRandomAccessStream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resourceCreator);
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();
        stream.Seek(0);
        return await CanvasBitmap.LoadAsync(resourceCreator, stream).AsTask(cancellationToken);
    }
}
