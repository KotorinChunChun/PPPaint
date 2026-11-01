using System.Numerics;
using Microsoft.Graphics.Canvas;
using PPPaint.Core;
using Windows.Storage.Streams;

namespace PPPaint.Imaging.Tests;

[TestClass]
public sealed class EraserFootprintTests
{
    [TestMethod]
    public async Task SquareCursorFootprintMatchesErasedPixelsAtDifferentSizesAndZooms()
    {
        using var device = new CanvasDevice();
        foreach (var diameter in new[] { 1, 2, 3, 4, 12, 40 })
        {
            var doc = new ScreenshotDocument(ScreenshotFrame.Transparent(128, 128));
            doc.Add(new("red", ScreenshotTool.Redact, new(0, 0, 128, 128), new(255, 0, 0)));
            doc.Add(new("erase", ScreenshotTool.Eraser, new(64, 64, 0, 0), Rgba32.Black, Width: diameter, Points: [new(64, 64)]));
            using var output = ScreenshotRenderer.Render(device, doc.State);
            var outputColors = output.GetPixelColors();
            var erased = Enumerable.Range(0, 128).Where(x => outputColors[64 * 128 + x].G > 127).ToArray();
            Assert.AreEqual(diameter, erased.Length, "消去幅が設定値と一致しません。");
            var footprint = ScreenshotEraserGeometry.Footprint(new(64, 64), diameter);
            foreach (var x in new[] { (int)footprint.X, (int)footprint.Right - 1 }) foreach (var y in new[] { (int)footprint.Y, (int)footprint.Bottom - 1 })
                Assert.AreEqual((byte)255, outputColors[y * 128 + x].G, "四角の角に消し残りがあります。");
            Assert.IsFalse(outputColors.Any(c => c.A > 0 && c.R == 0 && c.G == 0 && c.B == 0), "出力に黒いポインタ輪郭が含まれています。");
            foreach (var zoom in new[] { .5f, 1f, 2f })
            {
                var side = (int)(128 * zoom);
                using var overlay = new CanvasRenderTarget(device, side, side, 96);
                using (var ds = overlay.CreateDrawingSession())
                {
                    ds.Clear(Windows.UI.Color.FromArgb(0, 0, 0, 0)); ds.Transform = Matrix3x2.CreateScale(zoom);
                    ScreenshotEraserPreview.Draw(ds, new(64, 64), diameter, zoom);
                }
                var colors = overlay.GetPixelColors(); var y = (int)(64 * zoom);
                var ink = Enumerable.Range(0, side).Where(x => colors[y * side + x].A > 1).ToArray();
                Assert.IsTrue(ink.Length > 0);
                var span = ink[^1] - ink[0] + 1;
                Assert.IsTrue(Math.Abs(span - (erased.Length * zoom + 3)) <= 3, $"消去範囲と四角の表示がずれています: {diameter}/{zoom}/{span}");
            }
        }
        if (Environment.GetEnvironmentVariable("PPPAINT_ERASER_PREVIEW_PATH") is { Length: > 0 } path)
        {
            using var sample = new CanvasRenderTarget(device, 440, 160, 96);
            using (var ds = sample.CreateDrawingSession())
            {
                ds.Clear(Windows.UI.Color.FromArgb(255, 238, 242, 247));
                foreach (var (center, diameter) in new[] { (new Vector2(70, 80), 4), (new Vector2(160, 80), 12), (new Vector2(260, 80), 40), (new Vector2(370, 80), 80) })
                    ScreenshotEraserPreview.Draw(ds, center, diameter, 1);
            }
            using var png = new InMemoryRandomAccessStream(); await Win2DImageCodec.SaveAsync(sample, png, DocumentFileFormat.Png);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            using var file = File.Create(path); png.Seek(0); await png.AsStreamForRead().CopyToAsync(file);
        }
    }
}
