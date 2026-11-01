using System.Numerics;
using Microsoft.Graphics.Canvas;
using PPPaint.Core;
using Windows.Storage.Streams;

namespace PPPaint.Imaging.Tests;

[TestClass]
public sealed class SquareEraserTests
{
    private static ScreenshotDocument CoveredScene(Rgba32 background, string kind)
    {
        var doc = new ScreenshotDocument(ScreenshotFrame.Transparent(128, 128)); doc.SetBackground(background);
        if (kind == "raster")
        {
            var bytes = new byte[128 * 128 * 4];
            for (var index = 0; index < bytes.Length; index += 4) { bytes[index] = 30; bytes[index + 1] = 60; bytes[index + 2] = 220; bytes[index + 3] = 255; }
            doc.InsertImage(new(128, 128, bytes), Vector2.Zero);
        }
        else if (kind == "pen") doc.Add(new("ink", ScreenshotTool.Pen, new(20, 20, 88, 88), new(220, 60, 30), Width: 30, Points: [new(20.2f, 20.7f), new(108.6f, 108.1f)]));
        else doc.Add(new("ink", ScreenshotTool.Redact, new(0, 0, 128, 128), new(220, 60, 30)));
        return doc;
    }

    [TestMethod]
    [DataRow(8f, 9f)]
    [DataRow(8.8f, 9.7f)]
    [DataRow(0f, 0f)]
    [DataRow(31.99f, 31.99f)]
    public async Task OnePixelEraserCompletelyRemovesOnePixelPenAndPreservesEveryOtherPixel(float x, float y)
    {
        using var device = new CanvasDevice();
        foreach (var background in new[] { Rgba32.White, Rgba32.Transparent, new Rgba32(40, 100, 180, 128) })
        {
            var doc = new ScreenshotDocument(ScreenshotFrame.Transparent(32, 32)); doc.SetBackground(background);
            using var blank = ScreenshotRenderer.Render(device, doc.State); var expected = blank.GetPixelBytes();
            doc.Add(new("pen", ScreenshotTool.Pen, new(x, y, 0, 0), new(255, 0, 0), Width: 1, Points: [new(x, y)]));
            doc.Add(new("eraser", ScreenshotTool.Eraser, new(x, y, 0, 0), Rgba32.Black, Width: 1, Points: [new(x, y)]));
            using var erased = ScreenshotRenderer.Render(device, doc.State);
            CollectionAssert.AreEqual(expected, erased.GetPixelBytes(), "1画素の消しゴムが色を完全に置換していません。");
            using var png = new InMemoryRandomAccessStream(); await Win2DImageCodec.SaveAsync(erased, png, DocumentFileFormat.Png);
            using var loaded = await Win2DImageCodec.LoadAsync(device, png);
            CollectionAssert.AreEqual(expected, loaded.GetPixelBytes());
        }
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(12)]
    [DataRow(40)]
    public void SquareFootprintCompletelyReplacesItsCornersAndKeepsEveryOutsidePixel(int size)
    {
        using var device = new CanvasDevice();
        foreach (var background in new[] { Rgba32.White, Rgba32.Transparent, new Rgba32(40, 100, 180, 128) })
        foreach (var kind in new[] { "raster", "shape", "pen" })
        {
            var doc = CoveredScene(background, kind); using var before = ScreenshotRenderer.Render(device, doc.State);
            var point = new Vector2(64.8f, 64.2f); var box = ScreenshotEraserGeometry.Footprint(point, size);
            doc.Add(new("erase", ScreenshotTool.Eraser, new(point.X, point.Y, 0, 0), Rgba32.Black, Width: size, Points: [point]));
            using var after = ScreenshotRenderer.Render(device, doc.State);
            var blank = new ScreenshotDocument(ScreenshotFrame.Transparent(128, 128)); blank.SetBackground(background);
            using var blankPixels = ScreenshotRenderer.Render(device, blank.State);
            var expectedBackground = blankPixels.GetPixelColors()[0]; var original = before.GetPixelColors(); var erased = after.GetPixelColors(); var count = 0;
            for (var y = 0; y < 128; y++) for (var x = 0; x < 128; x++)
            {
                var inside = box.Contains(new Vector2(x + .5f, y + .5f));
                Assert.AreEqual(inside ? expectedBackground : original[y * 128 + x], erased[y * 128 + x], $"消去範囲/色が一致しません: {size}/{kind}/{background}/{x},{y}");
                if (inside) count++;
            }
            Assert.AreEqual(size * size, count);
        }
    }

    [TestMethod]
    public void OnePixelEraserFollowsContinuousPenPathWithoutLeavingInk()
    {
        using var device = new CanvasDevice();
        foreach (var points in new Vector2[][] {
            [new(4.2f, 5.8f), new(28.7f, 17.1f), new(17.5f, 27.6f)],
            [new(28.9f, 5.1f), new(4.4f, 5.1f), new(4.4f, 28.2f)],
            [new(2.4f, 29.2f), new(28.9f, 2.4f)] })
        {
            var doc = new ScreenshotDocument(ScreenshotFrame.Transparent(32, 32)); doc.SetBackground(Rgba32.Transparent);
            using var blank = ScreenshotRenderer.Render(device, doc.State);
            doc.Add(new("pen", ScreenshotTool.Pen, new(0, 0, 32, 32), new(255, 0, 0), Width: 1, Points: [.. points]));
            doc.Add(new("eraser", ScreenshotTool.Eraser, new(0, 0, 32, 32), Rgba32.Black, Width: 1, Points: [.. points]));
            using var erased = ScreenshotRenderer.Render(device, doc.State);
            CollectionAssert.AreEqual(blank.GetPixelBytes(), erased.GetPixelBytes(), "連続線に消し残りがあります。");
        }
    }

    [TestMethod]
    public async Task MasksRangeCopyUndoRedoMovementAndPngKeepExactSquarePixels()
    {
        using var device = new CanvasDevice();
        foreach (var background in new[] { Rgba32.White, Rgba32.Transparent, new Rgba32(40, 100, 180, 128) })
        {
            var doc = CoveredScene(background, "raster"); using var initial = ScreenshotRenderer.Render(device, doc.State); var before = initial.GetPixelBytes();
            var erase = new ScreenshotAnnotation("erase", ScreenshotTool.Eraser, new(60, 60, 8, 8), Rgba32.Black, Width: 12, Points: [new(64.8f, 64.2f)], ClipBounds: new(60, 60, 8, 8), Cutouts: [new(63, 63, 2, 2)]);
            doc.Add(erase); using var image = ScreenshotRenderer.Render(device, doc.State); var after = image.GetPixelBytes();
            var blank = new ScreenshotDocument(ScreenshotFrame.Transparent(128, 128)); blank.SetBackground(background);
            using var clean = ScreenshotRenderer.Render(device, blank.State); var bg = clean.GetPixelColors()[0]; var original = initial.GetPixelColors(); var pixels = image.GetPixelColors();
            for (var y = 0; y < 128; y++) for (var x = 0; x < 128; x++)
            {
                var center = new Vector2(x + .5f, y + .5f); var affected = erase.ClipBounds!.Value.Contains(center) && !erase.Cutouts[0].Contains(center);
                Assert.AreEqual(affected ? bg : original[y * 128 + x], pixels[y * 128 + x]);
            }
            var transfer = ScreenshotEditing.Capture(doc.State, new(60, 60, 8, 8)); using var copy = ScreenshotRenderer.Render(device, transfer.Scene);
            var copied = copy.GetPixelColors();
            for (var y = 0; y < 8; y++) for (var x = 0; x < 8; x++) Assert.AreEqual(pixels[(y + 60) * 128 + x + 60], copied[y * 8 + x]);
            using var png = new InMemoryRandomAccessStream(); await Win2DImageCodec.SaveAsync(image, png, DocumentFileFormat.Png);
            using var decoded = await Win2DImageCodec.LoadAsync(device, png); CollectionAssert.AreEqual(after, decoded.GetPixelBytes());
            doc.Undo(); using (var undo = ScreenshotRenderer.Render(device, doc.State)) CollectionAssert.AreEqual(before, undo.GetPixelBytes());
            doc.Redo(); using (var redo = ScreenshotRenderer.Render(device, doc.State)) CollectionAssert.AreEqual(after, redo.GetPixelBytes());
            doc.Replace(erase.Move(new(20, 0))); using (var moved = ScreenshotRenderer.Render(device, doc.State))
            { Assert.AreEqual(original[60 * 128 + 60], moved.GetPixelColors()[60 * 128 + 60]); Assert.AreEqual(bg, moved.GetPixelColors()[60 * 128 + 80]); }
            doc.Delete("erase"); using var deleted = ScreenshotRenderer.Render(device, doc.State); CollectionAssert.AreEqual(before, deleted.GetPixelBytes());
        }
    }

    [TestMethod]
    public void EraserRestoresDrawingSessionBlendAndAntialiasing()
    {
        using var device = new CanvasDevice(); using var target = new CanvasRenderTarget(device, 32, 32, 96); using var ds = target.CreateDrawingSession();
        ds.Blend = CanvasBlend.Add; ds.Antialiasing = CanvasAntialiasing.Antialiased;
        ScreenshotRenderer.Draw(ds, new("erase", ScreenshotTool.Eraser, new(10, 10, 0, 0), Rgba32.Black, Width: 1, Points: [new(10, 10)], ClipBounds: new(0, 0, 20, 20)), background: Rgba32.Transparent);
        Assert.AreEqual(CanvasBlend.Add, ds.Blend); Assert.AreEqual(CanvasAntialiasing.Antialiased, ds.Antialiasing);
    }

    [TestMethod]
    public void DiagonalSweepKeepsAxisAlignedCornersOpaqueAndDoesNotEraseItsWholeBoundingBox()
    {
        using var device = new CanvasDevice();
        foreach (var size in new[] { 1, 2, 3, 12 })
        {
            var doc = CoveredScene(Rgba32.Transparent, "shape"); using var before = ScreenshotRenderer.Render(device, doc.State); var original = before.GetPixelColors();
            var points = new[] { new Vector2(30.2f, 30.8f), new Vector2(90.7f, 90.1f) };
            var erase = new ScreenshotAnnotation("erase", ScreenshotTool.Eraser, new(30, 30, 60, 60), Rgba32.Black, Width: size, Points: [.. points]); doc.Add(erase);
            using var result = ScreenshotRenderer.Render(device, doc.State); var colors = result.GetPixelColors();
            var bounds = ScreenshotEraserGeometry.Bounds(points, size);
            for (var y = 0; y < 128; y++) for (var x = 0; x < 128; x++)
            {
                var color = colors[y * 128 + x];
                Assert.IsTrue(color == original[y * 128 + x] || color.A == 0, "対角消去に中間色が残っています。");
                if (!bounds.Contains(new Vector2(x + .5f, y + .5f)) || Math.Abs(x - y) > size)
                    Assert.AreEqual(original[y * 128 + x], color, "軸平行の掃引外まで消去しました。");
            }
            foreach (var center in new[] { points[0], new Vector2(60.2f, 60.8f), points[1] })
            {
                var box = ScreenshotEraserGeometry.Footprint(center, size);
                foreach (var x in new[] { (int)box.X, (int)box.Right - 1 }) foreach (var y in new[] { (int)box.Y, (int)box.Bottom - 1 })
                    Assert.AreEqual((byte)0, colors[y * 128 + x].A, "掃引内の四角の角が消えていません。");
            }
        }
    }

    [TestMethod]
    public void OnePixelEraserRespectsExactOnePixelClipAndCutout()
    {
        using var device = new CanvasDevice();
        var doc = new ScreenshotDocument(ScreenshotFrame.Transparent(32, 32)); doc.SetBackground(Rgba32.Transparent);
        doc.Add(new("dot", ScreenshotTool.Pen, new(8.99f, 9.99f, 0, 0), new(255, 0, 0), Width: 1, Points: [new(8.99f, 9.99f)]));
        using var original = ScreenshotRenderer.Render(device, doc.State); var before = original.GetPixelBytes();
        var eraser = new ScreenshotAnnotation("erase", ScreenshotTool.Eraser, new(8.99f, 9.99f, 0, 0), Rgba32.Black, Width: 1, Points: [new(8.99f, 9.99f)], ClipBounds: new(8, 9, 1, 1));
        doc.Add(eraser); using (var cleared = ScreenshotRenderer.Render(device, doc.State)) Assert.IsTrue(cleared.GetPixelColors().All(c => c.A == 0));
        doc.Replace(eraser with { Cutouts = [new(8, 9, 1, 1)] }); using (var cutout = ScreenshotRenderer.Render(device, doc.State)) CollectionAssert.AreEqual(before, cutout.GetPixelBytes());
        doc.Replace(eraser with { ClipBounds = new(8, 10, 1, 1) }); using var outside = ScreenshotRenderer.Render(device, doc.State); CollectionAssert.AreEqual(before, outside.GetPixelBytes());
    }
}
