using Microsoft.Graphics.Canvas;
using PPPaint.Core;
using PPPaint.Imaging;
using Windows.UI;

namespace PPPaint.Imaging.Tests;

[TestClass]
public sealed class CanvasResizeDetailRenderingTests
{
    [TestMethod]
    public void EveryDetailPixelIsAnUnsmoothedFourDipSquareIncludingTheImageEdges()
    {
        using var device = new CanvasDevice();
        var bytes = new byte[61 * 43 * 4];
        for (var y = 0; y < 43; y++) for (var x = 0; x < 61; x++)
        {
            var index = (y * 61 + x) * 4;
            bytes[index] = (byte)(20 + x * 3); bytes[index + 1] = (byte)(10 + y * 5); bytes[index + 2] = (byte)(200 - x); bytes[index + 3] = 255;
        }
        var document = ScreenshotDocument.FromImage(new(61, 43, bytes)); var before = document.State;
        foreach (var size in new[] { (61, 43), (60, 42), (63, 45), (1, 1) }) foreach (var centered in new[] { true, false })
        {
            var layout = ScreenshotCanvasResize.Create(before, size.Item1, size.Item2, centered);
            using var image = ScreenshotRenderer.RenderCanvasPreview(device, before, layout);
            var original = image.GetPixelColors();
            foreach (var anchor in Enum.GetValues<ScreenshotCanvasViewAnchor>()) foreach (var dpi in new[] { 96f, 144f, 192f })
            {
                using var detail = new CanvasRenderTarget(device, 100, 100, dpi);
                using (var ds = detail.CreateDrawingSession()) ScreenshotResizePreviewDrawing.DrawDetail(ds, image, anchor);
                var pixels = detail.GetPixelColors(); var targetWidth = (int)detail.SizeInPixels.Width;
                var block = (int)(4 * dpi / 96); var source = ScreenshotCanvasResizeViews.DetailSource(layout.Width, layout.Height, anchor);
                for (var cy = 0; cy < 25; cy++) for (var cx = 0; cx < 25; cx++)
                {
                    var sx = (int)source.X + cx; var sy = (int)source.Y + cy;
                    if (sx < 0 || sy < 0 || sx >= layout.Width || sy >= layout.Height) continue;
                    var expected = original[sy * layout.Width + sx];
                    for (var y = 0; y < block; y++) for (var x = 0; x < block; x++)
                        Assert.AreEqual(expected, pixels[(cy * block + y) * targetWidth + cx * block + x], $"{size}/{centered}/{anchor}/{dpi}/{sx},{sy} at {x},{y}");
                }
            }
        }
        Assert.AreSame(before, document.State); Assert.IsFalse(document.CanUndo);
    }

    [TestMethod]
    public void TransparentPaddingShowsCheckerButTheOutsideHasADistinctColor()
    {
        using var device = new CanvasDevice();
        var document = new ScreenshotDocument(ScreenshotFrame.Transparent(40, 30)); document.SetBackground(Rgba32.Transparent);
        document.Add(new("dot", ScreenshotTool.Pen, new(0, 0, 0, 0), new(255, 0, 0), Width: 1, Points: [new(.2f, .2f)]));
        using var image = ScreenshotRenderer.Render(device, document.State);
        using var detail = new CanvasRenderTarget(device, 100, 100, 96);
        using (var ds = detail.CreateDrawingSession()) ScreenshotResizePreviewDrawing.DrawDetail(ds, image, ScreenshotCanvasViewAnchor.TopLeft);
        var pixels = detail.GetPixelColors();
        Assert.AreEqual(16, pixels.Count(pixel => pixel == Color.FromArgb(255, 255, 0, 0)), "1pxが4 × 4の16画素で表示されること");
        Assert.AreNotEqual(pixels[10 * 100 + 10], pixels[70 * 100 + 70], "キャンバス外と透明な内側を区別すること");
        Assert.IsTrue(pixels.All(pixel => pixel.A == 255));
    }
}
