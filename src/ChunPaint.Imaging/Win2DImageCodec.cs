using ChunPaint.Core;
using Microsoft.Graphics.Canvas;
using Windows.Storage.Streams;
using SkiaSharp;
using System.Runtime.InteropServices;
using Windows.Graphics.DirectX;

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

    public static float GetQuality(DocumentFileFormat format) => format == DocumentFileFormat.Jpeg ? 0.92f : 1f;

    public static Task SaveAsync(CanvasBitmap bitmap, IRandomAccessStream stream, DocumentFileFormat format,
        CancellationToken cancellationToken = default) => SaveWithOptionsAsync(bitmap, stream, format, GetQuality(format), true, cancellationToken);

    public static async Task SaveWithOptionsAsync(CanvasBitmap bitmap, IRandomAccessStream stream,
        DocumentFileFormat format, float quality = .92f, bool preserveTransparency = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();
        if (!float.IsFinite(quality) || quality is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(quality));
        var width = checked((int)bitmap.SizeInPixels.Width);
        var height = checked((int)bitmap.SizeInPixels.Height);
        ValidateDimensions(width, height);
        if (format == DocumentFileFormat.Webp)
        {
            // Win2Dへのアクセスは呼出側、SkiaのCPU処理は独立した画素コピーで実行する。
            var pixels = bitmap.GetPixelBytes();
            var encoded = await Task.Run(() => EncodeWebp(pixels, width, height, quality, preserveTransparency, cancellationToken), cancellationToken).WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            // エンコード失敗・取消では呼出側のストリームを変更しない。
            stream.Size = 0; stream.Seek(0);
            var output = stream.AsStreamForWrite();
            await output.WriteAsync(encoded, cancellationToken);
            await output.FlushAsync(cancellationToken);
            stream.Seek(0);
            return;
        }
        var bitmapFormat = GetBitmapFormat(format);
        using var staging = new InMemoryRandomAccessStream();
        if (!preserveTransparency || format is DocumentFileFormat.Jpeg or DocumentFileFormat.Bmp)
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

    private static byte[] EncodeWebp(byte[] bytes, int width, int height, float quality, bool preserveTransparency, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var pixels = new SKBitmap(info);
        if (pixels.GetPixels() == IntPtr.Zero) throw new OutOfMemoryException("WebP作業領域を確保できません。");
        Marshal.Copy(bytes, 0, pixels.GetPixels(), bytes.Length);
        using var opaque = preserveTransparency ? null : new SKBitmap(info);
        if (opaque is not null)
        {
            if (opaque.GetPixels() == IntPtr.Zero) throw new OutOfMemoryException("WebP作業領域を確保できません。");
            using var canvas = new SKCanvas(opaque);
            canvas.Clear(SKColors.White); canvas.DrawBitmap(pixels, 0, 0);
        }
        token.ThrowIfCancellationRequested();
        using var data = (opaque ?? pixels).Encode(SKEncodedImageFormat.Webp, (int)(quality * 100))
            ?? throw new InvalidDataException("WebP画像をエンコードできません。");
        token.ThrowIfCancellationRequested();
        return data.ToArray();
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
        if (width < 1 || height < 1 || (long)width * height * 4 > PixelSurface.MaximumPixelBytes)
            throw new InvalidDataException("画像寸法が上限を超えています。");
    }
}
