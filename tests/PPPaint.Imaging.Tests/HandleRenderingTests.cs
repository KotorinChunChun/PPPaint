using System.Numerics;
using Microsoft.Graphics.Canvas;
using PPPaint.Core;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.UI;

namespace PPPaint.Imaging.Tests;

[TestClass]
public sealed class HandleRenderingTests
{
    [TestMethod]
    public async Task PaintedHandlePixelsAgreeWithHitGeometryIncludingOutsideCanvasHalves()
    {
        using var device = new CanvasDevice(); var centers = ScreenshotGeometry.Handles(new(0, 0, 400, 300));
        foreach (var zoom in new[] { .25f, .5f, 1f, 2f, 4f })
        {
            var width = (int)(400 * zoom + 20); var height = (int)(300 * zoom + 20);
            using var target = new CanvasRenderTarget(device, width, height, 96);
            using (var ds = target.CreateDrawingSession())
            {
                ds.Clear(Color.FromArgb(0, 0, 0, 0)); ds.Transform = Matrix3x2.CreateScale(zoom) * Matrix3x2.CreateTranslation(10, 10);
                foreach (var center in centers) ScreenshotHandleRenderer.Draw(ds, center, zoom, false);
                ScreenshotHandleRenderer.Draw(ds, new(200, 150), zoom, true);
            }
            var colors = target.GetPixelColors();
            foreach (var (center, round) in centers.Select(c => (c, false)).Append((new Vector2(200, 150), true)))
            {
                var sx = center.X * zoom + 10; var sy = center.Y * zoom + 10;
                for (var y = (int)Math.Floor(sy - 7); y <= Math.Ceiling(sy + 7); y++) for (var x = (int)Math.Floor(sx - 7); x <= Math.Ceiling(sx + 7); x++)
                {
                    var point = new Vector2((x + .5f - 10) / zoom, (y + .5f - 10) / zoom);
                    var distance = round ? Vector2.Distance(point, center) * zoom - ScreenshotHandleGeometry.RoundRadius
                        : Math.Max(Math.Abs(x + .5f - sx), Math.Abs(y + .5f - sy)) - ScreenshotHandleGeometry.SquareSize / 2;
                    if (Math.Abs(distance) < .3f) continue; // Ignore partial coverage exactly at antialiased boundaries.
                    Assert.AreEqual(ScreenshotHandleGeometry.Contains(center, point, zoom, round), colors[y * width + x].A > 127, $"{zoom}/{center}/{round}/{x},{y}");
                }
            }
        }
        if (Environment.GetEnvironmentVariable("PPPAINT_HANDLE_PREVIEW_PATH") is { Length: > 0 } path)
        {
            using var target = new CanvasRenderTarget(device, 420, 320, 96);
            using (var ds = target.CreateDrawingSession())
            {
                ds.Clear(Color.FromArgb(255, 255, 255, 255)); ds.FillRectangle(new Rect(10, 10, 400, 300), Color.FromArgb(255, 229, 242, 250));
                ds.Transform = Matrix3x2.CreateTranslation(10, 10); ds.DrawRectangle(new Rect(0, 0, 400, 300), Color.FromArgb(255, 30, 144, 255));
                foreach (var center in centers) ScreenshotHandleRenderer.Draw(ds, center, 1, false);
            }
            using var png = new InMemoryRandomAccessStream(); await Win2DImageCodec.SaveAsync(target, png, DocumentFileFormat.Png);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); using var file = File.Create(path); png.Seek(0); await png.AsStreamForRead().CopyToAsync(file);
        }
    }
}
