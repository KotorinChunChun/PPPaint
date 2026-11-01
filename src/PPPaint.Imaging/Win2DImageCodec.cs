using PPPaint.Core;
using Microsoft.Graphics.Canvas;
using Windows.Storage.Streams;
using SkiaSharp;
using System.Runtime.InteropServices;
using Windows.Graphics.DirectX;

namespace PPPaint.Imaging;

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

    public static float GetQuality(DocumentFileFormat format) => format == DocumentFileFormat.Jpeg ? 0.92f : 1f;

    // WebPは読み込み専用。保存はWin2Dが扱う形式だけに限る。
    public static async Task SaveAsync(CanvasBitmap bitmap, IRandomAccessStream stream, DocumentFileFormat format,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateDimensions(checked((int)bitmap.SizeInPixels.Width), checked((int)bitmap.SizeInPixels.Height));
        var bitmapFormat = GetBitmapFormat(format);
        var quality = GetQuality(format);
        using var staging = new InMemoryRandomAccessStream();
        if (format is DocumentFileFormat.Jpeg or DocumentFileFormat.Bmp)
        {
            using var opaque = new CanvasRenderTarget(bitmap.Device, (float)bitmap.Size.Width, (float)bitmap.Size.Height, bitmap.Dpi);
            using (var drawing = opaque.CreateDrawingSession()) { drawing.Clear(Windows.UI.Color.FromArgb(255, 255, 255, 255)); drawing.DrawImage(bitmap); }
            await opaque.SaveAsync(staging, bitmapFormat, quality).AsTask(cancellationToken);
        }
        else await bitmap.SaveAsync(staging, bitmapFormat, quality).AsTask(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        staging.Seek(0);
        stream.Size = 0; stream.Seek(0);
        var destination = stream.AsStreamForWrite();
        await staging.AsStreamForRead().CopyToAsync(destination, cancellationToken);
        await destination.FlushAsync(cancellationToken);
        stream.Seek(0);
    }

    public static async Task<CanvasBitmap> LoadAsync(ICanvasResourceCreator resourceCreator, IRandomAccessStream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resourceCreator);
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();
        DocumentFileFormats.ValidateInputSize(checked((long)stream.Size));
        stream.Seek(0);
        var header = new byte[12];
        var input = stream.AsStreamForRead();
        var count = 0;
        while (count < header.Length)
        {
            var read = await input.ReadAsync(header.AsMemory(count), cancellationToken);
            if (read == 0) break;
            count += read;
        }
        input.Position = 0; stream.Seek(0);
        if (count == 12 && DocumentFileFormats.HasExpectedImageSignature(DocumentFileFormat.Webp, header))
        {
            using var encoded = new MemoryStream();
            var chunk = new byte[65536];
            int read;
            while ((read = await input.ReadAsync(chunk, cancellationToken)) != 0)
            {
                if (encoded.Length + read > DocumentFileFormats.MaximumInputBytes) throw new InvalidDataException("画像ファイルが大きすぎます。");
                encoded.Write(chunk, 0, read);
            }
            var encodedBytes = encoded.ToArray();
            var decoded = await Task.Run(() => DecodeWebp(encodedBytes, cancellationToken), cancellationToken).WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return CanvasBitmap.CreateFromBytes(resourceCreator, decoded.Bytes, decoded.Width, decoded.Height, DirectXPixelFormat.B8G8R8A8UIntNormalized, 96, CanvasAlphaMode.Premultiplied);
        }
        return await CanvasBitmap.LoadAsync(resourceCreator, stream).AsTask(cancellationToken);
    }

    private static (byte[] Bytes, int Width, int Height) DecodeWebp(byte[] encoded, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var data = SKData.CreateCopy(encoded);
        using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("WebP画像を読み込めません。");
        var info = codec.Info;
        ValidateDimensions(info.Width, info.Height);
        var outputInfo = new SKImageInfo(info.Width, info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var decoded = new SKBitmap(outputInfo);
        if (decoded.GetPixels() == IntPtr.Zero) throw new OutOfMemoryException("WebP作業領域を確保できません。");
        if (codec.GetPixels(outputInfo, decoded.GetPixels()) != SKCodecResult.Success) throw new InvalidDataException("WebP画像が破損しています。");
        token.ThrowIfCancellationRequested();
        var bytes = new byte[checked(info.Width * info.Height * 4)];
        Marshal.Copy(decoded.GetPixels(), bytes, 0, bytes.Length);
        return (bytes, info.Width, info.Height);
    }

    private static void ValidateDimensions(int width, int height)
    {
        if (width < 1 || height < 1 || (long)width * height * 4 > ScreenshotFrame.MaximumPixelBytes)
            throw new InvalidDataException("画像寸法が上限を超えています。");
    }
}
