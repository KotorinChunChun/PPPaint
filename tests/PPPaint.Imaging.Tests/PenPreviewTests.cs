using System.Numerics;
using Microsoft.Graphics.Canvas;
using PPPaint.Core;
using Windows.Storage.Streams;
using Windows.UI;

namespace PPPaint.Imaging.Tests;

[TestClass]
public sealed class PenPreviewTests
{
    [TestMethod]
    [DataRow(1f)]
    [DataRow(3f)]
    [DataRow(12f)]
    public void PreviewMatchesRealPenPixelsAtPositionsEdgesZoomsAndDpis(float width)
    {
        using var device = new CanvasDevice();
        var color = new Rgba32(220, 38, 38);
        foreach (var point in new[] { new Vector2(16.2f, 15.7f), new Vector2(.2f, .3f), new Vector2(31.8f, 31.7f) })
        {
            var dot = new ScreenshotAnnotation("dot", ScreenshotTool.Pen, new(point.X, point.Y, 0, 0), color, Width: width, Points: [point]);
            using var scene = ScreenshotRenderer.Render(device, new(ScreenshotFrame.Transparent(32, 32), [dot]));
            foreach (var (zoom, dpi) in new[] { (1f, 96f), (.67f, 96f), (2f, 144f), (1f, 192f) })
            {
                using var expected = new CanvasRenderTarget(device, 88, 88, dpi);
                using var actual = new CanvasRenderTarget(device, 88, 88, dpi);
                var transform = Matrix3x2.CreateScale(zoom) * Matrix3x2.CreateTranslation(10, 10);
                using (var ds = expected.CreateDrawingSession())
                {
                    ds.Clear(Color.FromArgb(0, 0, 0, 0)); ds.Transform = transform;
                    ScreenshotRenderer.DrawCanvasImage(ds, scene);
                }
                using (var ds = actual.CreateDrawingSession())
                {
                    ds.Clear(Color.FromArgb(0, 0, 0, 0)); ds.Transform = transform;
                    ScreenshotPenPreview.Draw(ds, point, width, color, 32, 32);
                }
                CollectionAssert.AreEqual(expected.GetPixelBytes(), actual.GetPixelBytes(), $"Preview mismatch: {width}/{point}/{zoom}/{dpi}");
            }
        }
    }

    [TestMethod]
    public async Task PreviewUsesItsColorAndDoesNotChangeDocumentHistoryOrSavedPng()
    {
        using var device = new CanvasDevice();
        var document = new ScreenshotDocument(ScreenshotFrame.Transparent(32, 32));
        document.SetBackground(Rgba32.Transparent);
        var state = document.State; var canUndo = document.CanUndo;
        using var overlay = new CanvasRenderTarget(device, 32, 32, 96);
        using (var ds = overlay.CreateDrawingSession())
        {
            ds.Clear(Color.FromArgb(0, 0, 0, 0));
            ScreenshotPenPreview.Draw(ds, new(12.2f, 13.8f), 3, new(255, 0, 0), 32, 32);
        }
        Assert.IsTrue(overlay.GetPixelColors().Any(c => c.R > 0 && c.G == 0 && c.B == 0));
        Assert.AreSame(state, document.State); Assert.AreEqual(canUndo, document.CanUndo);
        using var image = ScreenshotRenderer.Render(device, document.State);
        using var png = new InMemoryRandomAccessStream();
        await Win2DImageCodec.SaveAsync(image, png, DocumentFileFormat.Png);
        using var reopened = await Win2DImageCodec.LoadAsync(device, png);
        Assert.IsTrue(reopened.GetPixelColors().All(c => c.A == 0), "Preview leaked into saved image.");
    }
}
