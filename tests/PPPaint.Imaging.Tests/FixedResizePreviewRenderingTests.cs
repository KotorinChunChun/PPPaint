using Microsoft.Graphics.Canvas;
using PPPaint.Core;
using Windows.UI;
using Windows.Storage.Streams;

namespace PPPaint.Imaging.Tests;

[TestClass]
public sealed class FixedResizePreviewRenderingTests
{
    [TestMethod]
    public async Task OriginalPixelsStayInPlaceInAllEightViewsAcrossResizeAndDpi()
    {
        using var device = new CanvasDevice(); var bytes = new byte[160 * 100 * 4];
        for (var y = 0; y < 100; y++) for (var x = 0; x < 160; x++)
        {
            var index = (y * 160 + x) * 4;
            bytes[index] = (byte)(20 + x); bytes[index + 1] = (byte)(10 + y * 2); bytes[index + 2] = (byte)(240 - x); bytes[index + 3] = 255;
        }
        var state = ScreenshotDocument.FromImage(new(160, 100, bytes)).State;
        using var original = ScreenshotRenderer.Render(device, state);
        var initial = ScreenshotCanvasResize.Create(state, 160, 100, false);
        var layouts = new[] { ScreenshotCanvasResize.Create(state, 150, 90, true), ScreenshotCanvasResize.Create(state, 164, 104, true), ScreenshotCanvasResize.FromBounds(state, new(3, 2, 155, 95)) };
        foreach (var anchor in Enum.GetValues<ScreenshotCanvasViewAnchor>()) foreach (var dpi in new[] { 96f, 144f, 192f })
        {
            using var before = new CanvasRenderTarget(device, 100, 100, dpi); using var after = new CanvasRenderTarget(device, 100, 100, dpi);
            var viewport = ScreenshotResizePreviewGeometry.FixedDetail(initial, anchor, 25, .2f);
            using (var ds = before.CreateDrawingSession()) ScreenshotResizePreviewDrawing.DrawFixedComparison(ds, original, initial, viewport, 100, 100);
            var reference = before.GetPixelColors(); var width = (int)before.SizeInPixels.Width; var scale = width / 25f;
            foreach (var layout in layouts)
            {
                var current = ScreenshotResizePreviewGeometry.FixedDetail(layout, anchor, 25, .2f);
                using (var ds = after.CreateDrawingSession()) ScreenshotResizePreviewDrawing.DrawFixedComparison(ds, original, layout, current, 100, 100);
                var actual = after.GetPixelColors(); var checkedPixels = 0;
                for (var y = 0; y < width; y++) for (var x = 0; x < width; x++)
                {
                    var sx = viewport.X + (x + .5f) / scale; var sy = viewport.Y + (y + .5f) / scale;
                    var cx = sx + layout.OffsetX; var cy = sy + layout.OffsetY;
                    if (sx <= 1 || sx >= 159 || sy <= 1 || sy >= 99 || cx <= 1 || cx >= layout.Width - 1 || cy <= 1 || cy >= layout.Height - 1) continue;
                    Assert.AreEqual(reference[y * width + x], actual[y * width + x], $"{anchor}/{dpi}/{layout.Width},{layout.Height} at {x},{y}"); checkedPixels++;
                }
                Assert.IsGreaterThan(1000, checkedPixels);
            }
        }
        await SaveGrid(device, original, initial, "initial.png");
        await SaveGrid(device, original, layouts[0], "cropped.png");
        await SaveGrid(device, original, layouts[1], "expanded.png");
    }

    [TestMethod]
    public void RemovedContentIsOnlyDarkenedAndOutsideMarginDoesNotChange()
    {
        using var device = new CanvasDevice(); var doc = new ScreenshotDocument(ScreenshotFrame.Transparent(40, 30)); doc.SetBackground(Rgba32.Transparent);
        doc.Add(new("red", ScreenshotTool.Highlight, new(0, 0, 40, 30), new(255, 0, 0)));
        using var original = ScreenshotRenderer.Render(device, doc.State);
        var initial = ScreenshotCanvasResize.Create(doc.State, 40, 30, false); var cropped = ScreenshotCanvasResize.Create(doc.State, 30, 30, false); var expanded = ScreenshotCanvasResize.Create(doc.State, 44, 30, false);
        Color[] Draw(ScreenshotCanvasResize layout)
        {
            using var target = new CanvasRenderTarget(device, 100, 100, 96); var viewport = ScreenshotResizePreviewGeometry.FixedDetail(layout, ScreenshotCanvasViewAnchor.Right, 25, .2f);
            using (var ds = target.CreateDrawingSession()) ScreenshotResizePreviewDrawing.DrawFixedComparison(ds, original, layout, viewport, 100, 100);
            return target.GetPixelColors();
        }
        var before = Draw(initial); var after = Draw(cropped); var grown = Draw(expanded);
        Assert.AreEqual(before[20 * 100 + 20], after[20 * 100 + 20]);
        Assert.AreEqual((byte)255, after[20 * 100 + 20].R); Assert.AreEqual((byte)170, after[20 * 100 + 20].G);
        var source = before[20 * 100 + 56]; var darkened = after[20 * 100 + 56];
        Assert.AreEqual(source.R * 163 / 255f, darkened.R, 1f); Assert.AreEqual(source.G * 163 / 255f, darkened.G, 1f);
        Assert.AreEqual(before[20 * 100 + 90], after[20 * 100 + 90]); Assert.AreEqual(before[20 * 100 + 90], grown[20 * 100 + 90]);
    }

    private static async Task SaveGrid(CanvasDevice device, CanvasBitmap original, ScreenshotCanvasResize layout, string name)
    {
        using var grid = new CanvasRenderTarget(device, 316, 312, 96);
        using (var ds = grid.CreateDrawingSession())
        {
            ds.Clear(Color.FromArgb(255, 255, 255, 255));
            var positions = new[] { (0, 0), (108, 0), (216, 0), (0, 106), (216, 106), (0, 212), (108, 212), (216, 212) };
            var anchors = Enum.GetValues<ScreenshotCanvasViewAnchor>();
            for (var i = 0; i < anchors.Length; i++)
            {
                using var tile = new CanvasRenderTarget(device, 100, 100, 96);
                var viewport = ScreenshotResizePreviewGeometry.FixedDetail(layout, anchors[i], 25, .2f);
                using (var draw = tile.CreateDrawingSession()) ScreenshotResizePreviewDrawing.DrawFixedComparison(draw, original, layout, viewport, 100, 100);
                ds.DrawImage(tile, positions[i].Item1, positions[i].Item2);
            }
        }
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "PPPaint.slnx"))) root = root.Parent ?? throw new InvalidOperationException();
        var folder = Path.Combine(root.FullName, "artifacts", "qa-v0.2.28", "rendered"); Directory.CreateDirectory(folder);
        using var stream = new InMemoryRandomAccessStream(); await Win2DImageCodec.SaveAsync(grid, stream, DocumentFileFormat.Png); stream.Seek(0);
        using var input = stream.AsStreamForRead(); using var output = File.Create(Path.Combine(folder, name)); await input.CopyToAsync(output);
    }
}
