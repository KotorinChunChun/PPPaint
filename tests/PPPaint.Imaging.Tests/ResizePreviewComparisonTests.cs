using Microsoft.Graphics.Canvas;
using PPPaint.Core;
using Windows.Storage.Streams;

namespace PPPaint.Imaging.Tests;

[TestClass]
public sealed class ResizePreviewComparisonTests
{
    [TestMethod]
    public async Task TrimmedContentRemainsVisibleDarkenedAndKeptPixelsRemainUnchanged()
    {
        using var device = new CanvasDevice(); var doc = new ScreenshotDocument(ScreenshotFrame.Transparent(40, 30));
        doc.Add(new("red", ScreenshotTool.Redact, new(0, 0, 20, 30), new(255, 0, 0)));
        doc.Add(new("blue", ScreenshotTool.Redact, new(20, 0, 20, 30), new(0, 0, 255)));
        using var original = ScreenshotRenderer.Render(device, doc.State);
        var layout = ScreenshotCanvasResize.Create(doc.State, 20, 30, false);
        using var candidate = ScreenshotRenderer.RenderCanvasPreview(device, doc.State, layout);
        using var target = new CanvasRenderTarget(device, 100, 100, 96);
        using (var ds = target.CreateDrawingSession()) ScreenshotResizePreviewDrawing.DrawComparison(ds, original, candidate, layout, new(10, 0, 25, 25), 100, 100, true);
        var pixels = target.GetPixelColors(); var kept = pixels[40 * 100 + 20]; var removed = pixels[40 * 100 + 64];
        Assert.AreEqual((byte)255, kept.R); Assert.AreEqual((byte)0, kept.B);
        Assert.IsTrue(removed.B is >= 162 and <= 164); Assert.AreEqual((byte)0, removed.R); Assert.AreEqual((byte)255, removed.A);
        Assert.AreEqual(40, doc.State.Frame.Width);
        await SaveGrid(device, doc.State, 32, 22, "trimmed-grid.png");
        await SaveGrid(device, doc.State, 48, 38, "expanded-grid.png");
    }
    [TestMethod]
    public void TransparentKeptAreaIsCompositedOnceAndAddedSpaceUsesCandidateBackground()
    {
        using var device = new CanvasDevice(); var doc = new ScreenshotDocument(ScreenshotFrame.Transparent(40, 30)); doc.SetBackground(Rgba32.Transparent);
        doc.Add(new("red", ScreenshotTool.Highlight, new(0, 0, 40, 30), new(255, 0, 0, 128)));
        using var original = ScreenshotRenderer.Render(device, doc.State);
        var layout = ScreenshotCanvasResize.Create(doc.State, 50, 30, false);
        using var candidate = ScreenshotRenderer.RenderCanvasPreview(device, doc.State, layout);
        using var target = new CanvasRenderTarget(device, 100, 100, 96);
        using (var ds = target.CreateDrawingSession()) ScreenshotResizePreviewDrawing.DrawComparison(ds, original, candidate, layout, new(0, 0, 25, 25), 100, 100, true);
        var pixel = target.GetPixelColors()[20 * 100 + 20];
        Assert.AreEqual((byte)255, pixel.R); Assert.IsTrue(pixel.G is >= 169 and <= 171); Assert.AreEqual((byte)255, pixel.A);
        using (var ds = target.CreateDrawingSession()) ScreenshotResizePreviewDrawing.DrawComparison(ds, original, candidate, layout, new(30, 0, 25, 25), 100, 100, true);
        var added = target.GetPixelColors()[40 * 100 + 60];
        Assert.AreEqual((byte)255, added.R); Assert.AreEqual((byte)255, added.G); Assert.AreEqual((byte)255, added.B);
    }
    private static async Task SaveGrid(CanvasDevice device, ScreenshotState state, int width, int height, string name)
    {
        var layout = ScreenshotCanvasResize.Create(state, width, height, true);
        using var original = ScreenshotRenderer.Render(device, state); using var candidate = ScreenshotRenderer.RenderCanvasPreview(device, state, layout);
        using var grid = new CanvasRenderTarget(device, 442, 312, 96);
        using (var ds = grid.CreateDrawingSession())
        {
            ds.Clear(Windows.UI.Color.FromArgb(255, 255, 255, 255));
            foreach (var (anchor, x, y) in new (ScreenshotCanvasViewAnchor?, int, int)[] {
                (ScreenshotCanvasViewAnchor.TopLeft,0,0),(ScreenshotCanvasViewAnchor.Top,170,0),(ScreenshotCanvasViewAnchor.TopRight,340,0),
                (ScreenshotCanvasViewAnchor.Left,0,106),(null,110,106),(ScreenshotCanvasViewAnchor.Right,340,106),
                (ScreenshotCanvasViewAnchor.BottomLeft,0,212),(ScreenshotCanvasViewAnchor.Bottom,170,212),(ScreenshotCanvasViewAnchor.BottomRight,340,212) })
            {
                var w = anchor is null ? 220 : 100;
                using var tile = new CanvasRenderTarget(device, w, 100, 96);
                var viewport = anchor is null ? ScreenshotResizePreviewGeometry.Overview(layout, w, 100) : ScreenshotCanvasResizeViews.DetailSource(width, height, anchor.Value);
                using (var tileDs = tile.CreateDrawingSession()) ScreenshotResizePreviewDrawing.DrawComparison(tileDs, original, candidate, layout, viewport, w, 100, anchor is not null);
                ds.DrawImage(tile, x, y);
            }
        }
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "PPPaint.slnx"))) root = root.Parent ?? throw new InvalidOperationException();
        var folder = Path.Combine(root.FullName, "artifacts", "qa-v0.2.25", "rendered"); Directory.CreateDirectory(folder);
        using var stream = new InMemoryRandomAccessStream(); await Win2DImageCodec.SaveAsync(grid, stream, DocumentFileFormat.Png); stream.Seek(0);
        using var input = stream.AsStreamForRead(); using var output = File.Create(Path.Combine(folder, name)); await input.CopyToAsync(output);
    }
}
