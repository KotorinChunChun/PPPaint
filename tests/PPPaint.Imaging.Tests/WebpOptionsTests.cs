using Microsoft.Graphics.Canvas;
using SkiaSharp;
using Windows.Storage.Streams;

namespace PPPaint.Imaging.Tests;

// WebPは読み込み専用。試験用の入力はSkiaSharpで直接作る。
[TestClass]
public sealed class WebpOptionsTests
{
    [TestMethod]
    public async Task Webp_LoadPreservesAlphaAndDimensions()
    {
        using var device = new CanvasDevice();
        using var stream = await EncodeAsync(16, 12, new SKColor(255, 0, 0, 96));
        using var decoded = await Win2DImageCodec.LoadAsync(device, stream);
        Assert.AreEqual(16u, decoded.SizeInPixels.Width);
        Assert.AreEqual(12u, decoded.SizeInPixels.Height);
        Assert.IsTrue(Math.Abs(decoded.GetPixelColors()[0].A - 96) <= 1);
    }

    [TestMethod]
    public async Task Webp_RejectsCorruptPayload()
    {
        using var device = new CanvasDevice();
        using var corrupt = new InMemoryRandomAccessStream();
        await corrupt.AsStreamForWrite().WriteAsync(new byte[] { 82, 73, 70, 70, 4, 0, 0, 0, 87, 69, 66, 80 });
        await corrupt.AsStreamForWrite().FlushAsync();
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => Win2DImageCodec.LoadAsync(device, corrupt));
    }

    private static async Task<InMemoryRandomAccessStream> EncodeAsync(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        bitmap.Erase(color);
        using var data = bitmap.Encode(SKEncodedImageFormat.Webp, 100);
        var stream = new InMemoryRandomAccessStream();
        await stream.AsStreamForWrite().WriteAsync(data.ToArray());
        await stream.AsStreamForWrite().FlushAsync();
        stream.Seek(0);
        return stream;
    }
}
