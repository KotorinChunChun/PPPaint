using PPPaint.Core;
using PPPaint.Imaging;
using Microsoft.Graphics.Canvas;
using Windows.Storage.Streams;
using Windows.UI;

namespace PPPaint.Imaging.Tests;

[TestClass]
public sealed class WebpOptionsTests
{
    [TestMethod]
    public async Task Webp_PreservesAlphaAndCanFlattenOnWhite()
    {
        using var device = new CanvasDevice();
        using var source = new CanvasRenderTarget(device, 16, 16, 96);
        using (var drawing = source.CreateDrawingSession()) drawing.Clear(Color.FromArgb(96, 255, 0, 0));
        using var transparent = new InMemoryRandomAccessStream();
        await Win2DImageCodec.SaveWithOptionsAsync(source, transparent, DocumentFileFormat.Webp, 1, true);
        using var decoded = await Win2DImageCodec.LoadAsync(device, transparent);
        Assert.IsTrue(Math.Abs(decoded.GetPixelColors()[0].A - 96) <= 1);
        using var opaque = new InMemoryRandomAccessStream();
        await Win2DImageCodec.SaveWithOptionsAsync(source, opaque, DocumentFileFormat.Webp, 1, false);
        using var flattened = await Win2DImageCodec.LoadAsync(device, opaque);
        var pixel = flattened.GetPixelColors()[0];
        Assert.AreEqual((byte)255, pixel.A);
        Assert.IsTrue(pixel.R > 240 && pixel.G > 140 && pixel.B > 140, "半透明赤は白背景へ合成される必要があります。");
    }

    [TestMethod]
    public async Task Webp_QualityChangesEncodedContentWithoutChangingDimensions()
    {
        using var device = new CanvasDevice();
        using var source = new CanvasRenderTarget(device, 64, 64, 96);
        using (var drawing = source.CreateDrawingSession())
        {
            drawing.Clear(Color.FromArgb(255, 0, 0, 0));
            for (var y = 0; y < 64; y++)
            for (var x = 0; x < 64; x++)
                drawing.FillRectangle(x, y, 1, 1, Color.FromArgb(255, (byte)(x * 13 + y * 7), (byte)(x * 3 + y * 19), (byte)(x * 23 + y * 11)));
        }
        using var low = new InMemoryRandomAccessStream();
        using var high = new InMemoryRandomAccessStream();
        await Win2DImageCodec.SaveWithOptionsAsync(source, low, DocumentFileFormat.Webp, .1f);
        await Win2DImageCodec.SaveWithOptionsAsync(source, high, DocumentFileFormat.Webp, 1f);
        Assert.IsTrue(high.Size > 0 && low.Size > 0);
        using var highDecoded = await Win2DImageCodec.LoadAsync(device, high);
        using var decoded = await Win2DImageCodec.LoadAsync(device, low);
        var original = source.GetPixelColors();
        static long Error(Color[] expected, Color[] actual) => expected.Zip(actual).Sum(pair =>
            (long)Math.Pow(pair.First.R - pair.Second.R, 2) + (long)Math.Pow(pair.First.G - pair.Second.G, 2) + (long)Math.Pow(pair.First.B - pair.Second.B, 2));
        Assert.IsTrue(Error(original, highDecoded.GetPixelColors()) < Error(original, decoded.GetPixelColors()),
            "高品質設定で色の復元誤差が減ること。容量の大小は圧縮方式と画像内容に依存する。");
        Assert.AreEqual(64u, decoded.SizeInPixels.Width);
        Assert.AreEqual(64u, decoded.SizeInPixels.Height);
    }

    [TestMethod]
    public async Task Webp_RejectsCorruptPayloadAndInvalidQuality()
    {
        using var device = new CanvasDevice();
        using var corrupt = new InMemoryRandomAccessStream();
        await corrupt.AsStreamForWrite().WriteAsync(new byte[] { 82, 73, 70, 70, 4, 0, 0, 0, 87, 69, 66, 80 });
        await corrupt.AsStreamForWrite().FlushAsync();
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => Win2DImageCodec.LoadAsync(device, corrupt));
        using var source = new CanvasRenderTarget(device, 1, 1, 96);
        using var output = new InMemoryRandomAccessStream(); output.Size = 32;
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => Win2DImageCodec.SaveWithOptionsAsync(source, output, DocumentFileFormat.Webp, float.NaN));
        Assert.AreEqual(32UL, output.Size);
    }
}
