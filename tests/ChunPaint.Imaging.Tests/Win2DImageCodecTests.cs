using ChunPaint.Core;
using ChunPaint.Imaging;
using Microsoft.Graphics.Canvas;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.UI;

namespace ChunPaint.Imaging.Tests;

[TestClass]
public sealed class Win2DImageCodecTests
{
    private static readonly Color Red = Color.FromArgb(255, 255, 0, 0);
    private static readonly Color Green = Color.FromArgb(255, 0, 255, 0);
    private static readonly Color Blue = Color.FromArgb(255, 0, 0, 255);
    private static readonly Color Transparent = Color.FromArgb(0, 0, 0, 0);

    [TestMethod]
    [DataRow(DocumentFileFormat.Png)]
    [DataRow(DocumentFileFormat.Jpeg)]
    [DataRow(DocumentFileFormat.Bmp)]
    [DataRow(DocumentFileFormat.Gif)]
    public async Task SaveAndLoad_RoundTripsDimensionsAndRepresentativeColors(DocumentFileFormat format)
    {
        using var device = new CanvasDevice();
        using var source = CreateFixture(device);
        using var stream = new InMemoryRandomAccessStream();

        await Win2DImageCodec.SaveAsync(source, stream, format);
        Assert.IsTrue(stream.Size > 0);
        using var decoded = await Win2DImageCodec.LoadAsync(device, stream);

        Assert.AreEqual(source.SizeInPixels.Width, decoded.SizeInPixels.Width);
        Assert.AreEqual(source.SizeInPixels.Height, decoded.SizeInPixels.Height);
        var pixels = decoded.GetPixelColors();
        AssertColorNear(Red, pixels[SampleIndex(8, 8)], format == DocumentFileFormat.Jpeg ? 45 : 8);
        AssertColorNear(Green, pixels[SampleIndex(24, 8)], format == DocumentFileFormat.Jpeg ? 45 : 8);
        AssertColorNear(Blue, pixels[SampleIndex(8, 24)], format == DocumentFileFormat.Jpeg ? 45 : 8);

        var alpha = pixels[SampleIndex(24, 24)].A;
        if (format == DocumentFileFormat.Png)
        {
            Assert.IsLessThan((byte)255, alpha, "PNGは半透明画素を保持する必要があります。");
        }
        else if (format is DocumentFileFormat.Jpeg or DocumentFileFormat.Bmp)
        {
            Assert.AreEqual((byte)255, alpha, $"{format}の再読込画素は不透明である必要があります。");
        }
    }

    [TestMethod]
    public async Task SaveAsync_RejectsProjectFormatAndHonorsCancellation()
    {
        using var device = new CanvasDevice();
        using var source = CreateFixture(device);
        using var stream = new InMemoryRandomAccessStream();

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
            Win2DImageCodec.SaveAsync(source, stream, DocumentFileFormat.ChunPaint));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            Win2DImageCodec.SaveAsync(source, stream, DocumentFileFormat.Png, new CancellationToken(canceled: true)));
    }

    private static CanvasRenderTarget CreateFixture(CanvasDevice device)
    {
        var target = new CanvasRenderTarget(device, 32, 32, 96);
        using var drawing = target.CreateDrawingSession();
        drawing.Clear(Transparent);
        drawing.FillRectangle(new Rect(0, 0, 16, 16), Red);
        drawing.FillRectangle(new Rect(16, 0, 16, 16), Green);
        drawing.FillRectangle(new Rect(0, 16, 16, 16), Blue);
        drawing.FillRectangle(new Rect(16, 16, 16, 16), Color.FromArgb(96, 255, 0, 255));
        return target;
    }

    private static int SampleIndex(int x, int y) => (y * 32) + x;

    private static void AssertColorNear(Color expected, Color actual, int tolerance)
    {
        Assert.IsLessThanOrEqualTo(tolerance, Math.Abs(expected.R - actual.R), "赤成分が許容差を超えました。");
        Assert.IsLessThanOrEqualTo(tolerance, Math.Abs(expected.G - actual.G), "緑成分が許容差を超えました。");
        Assert.IsLessThanOrEqualTo(tolerance, Math.Abs(expected.B - actual.B), "青成分が許容差を超えました。");
    }
}
