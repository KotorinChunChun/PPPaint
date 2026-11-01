using Microsoft.Graphics.Canvas;
using PPPaint.Core;
using Windows.Storage.Streams;
using Windows.UI;

namespace PPPaint.Imaging.Tests;

[TestClass]
public sealed class EffectPreservationTests
{
    private static ScreenshotDocument Scene(Rgba32 background, bool raster = false)
    {
        var doc = new ScreenshotDocument(ScreenshotFrame.Transparent(160, 120)); doc.SetBackground(background);
        if (raster)
        {
            var bytes = new byte[160 * 120 * 4];
            for (var y = 0; y < 120; y++) for (var x = 0; x < 160; x++)
            { var i = (y * 160 + x) * 4; bytes[i] = (byte)(x % 4 < 2 ? 40 : 210); bytes[i + 1] = (byte)(y % 4 < 2 ? 20 : 180); bytes[i + 2] = 80; bytes[i + 3] = 255; }
            doc.InsertImage(new(160, 120, bytes), System.Numerics.Vector2.Zero);
        }
        doc.Add(new("line", ScreenshotTool.Line, new(10, 10, 90, 50), new(220, 38, 38), Width: 3, Points: [new(10, 10), new(100, 60)]));
        doc.Add(new("circle", ScreenshotTool.Ellipse, new(105, 10, 30, 30), new(220, 38, 38), Width: 3));
        doc.Add(new("rectangle", ScreenshotTool.Rectangle, new(10, 80, 50, 25), new(220, 38, 38), Width: 3));
        doc.Add(new("arrow", ScreenshotTool.Arrow, new(85, 75, 40, 30), new(220, 38, 38), Width: 3, Points: [new(125, 75), new(85, 105)]));
        return doc;
    }
    private static void AssertOutsideUnchanged(Color[] before, Color[] after, ScreenshotAnnotation effect)
    {
        var changed = 0;
        for (var y = 0; y < 120; y++) for (var x = 0; x < 160; x++)
        {
            var point = new System.Numerics.Vector2(x + .5f, y + .5f);
            var affected = effect.Bounds.Contains(point) && effect.VisibleAt(point);
            if (!affected) Assert.AreEqual(before[y * 160 + x], after[y * 160 + x], $"効果範囲外のRGBAが変わりました: {effect.Tool}/{x},{y}");
            else if (before[y * 160 + x] != after[y * 160 + x]) changed++;
        }
        Assert.IsTrue(changed > 0, "範囲内には効果が適用される必要があります。");
    }
    private static async Task SaveEvidence(CanvasBitmap image, string name)
    {
        if (Environment.GetEnvironmentVariable("PPPAINT_EFFECT_EVIDENCE_ROOT") is not { Length: > 0 } root) return;
        Directory.CreateDirectory(root); using var png = new InMemoryRandomAccessStream(); await Win2DImageCodec.SaveAsync(image, png, DocumentFileFormat.Png);
        using var file = File.Create(Path.Combine(root, name)); png.Seek(0); await png.AsStreamForRead().CopyToAsync(file);
    }
    [TestMethod]
    public async Task AddingBlurPreservesEveryOutsidePixelAndVectorInPng()
    {
        using var device = new CanvasDevice(); using var cache = new ScreenshotRenderCache(device);
        var doc = Scene(Rgba32.White); var before = doc.State;
        using var original = ScreenshotRenderer.Render(device, before, cache);
        var blur = new ScreenshotAnnotation("blur", ScreenshotTool.Blur, new(30, 30, 40, 30), Rgba32.Black, BlurRadius: 4); doc.Add(blur);
        using var result = ScreenshotRenderer.Render(device, doc.State, cache);
        await SaveEvidence(original, "original.png"); await SaveEvidence(result, "blurred.png");
        AssertOutsideUnchanged(original.GetPixelColors(), result.GetPixelColors(), blur);
        foreach (var item in before.Annotations) Assert.AreEqual(item, doc.State.Annotations.First(a => a.Id == item.Id));
        using var png = new InMemoryRandomAccessStream(); await Win2DImageCodec.SaveAsync(result, png, DocumentFileFormat.Png);
        using var decoded = await Win2DImageCodec.LoadAsync(device, png);
        CollectionAssert.AreEqual(result.GetPixelColors(), decoded.GetPixelColors());
    }
    [TestMethod]
    public void EffectsPreserveOutsideRgbaWithRastersAlphaAndMasks()
    {
        using var device = new CanvasDevice();
        foreach (var tool in new[] { ScreenshotTool.Blur, ScreenshotTool.Mosaic }) foreach (var background in new[] { Rgba32.White, Rgba32.Transparent, new Rgba32(40, 100, 180, 128) })
        foreach (var raster in new[] { false, true }) foreach (var masked in new[] { false, true })
        {
            var doc = Scene(background, raster); using var before = ScreenshotRenderer.Render(device, doc.State);
            var effect = new ScreenshotAnnotation("effect", tool, new(30, 30, 40, 30), Rgba32.Black, BlurRadius: 4, MosaicSize: 8,
                ClipBounds: masked ? new(35, 32, 30, 24) : null, Cutouts: masked ? [new(45, 40, 8, 8)] : default);
            doc.Add(effect); using var after = ScreenshotRenderer.Render(device, doc.State);
            AssertOutsideUnchanged(before.GetPixelColors(), after.GetPixelColors(), effect);
        }
    }
    [TestMethod]
    public void SecondBlurPreservesFirstBlurAndLaterShapesKeepTheirStackOrder()
    {
        using var device = new CanvasDevice(); var doc = Scene(Rgba32.White, true);
        doc.Add(new("first", ScreenshotTool.Blur, new(30, 30, 40, 30), Rgba32.Black, BlurRadius: 4));
        using var first = ScreenshotRenderer.Render(device, doc.State);
        var second = new ScreenshotAnnotation("second", ScreenshotTool.Blur, new(90, 70, 30, 25), Rgba32.Black, BlurRadius: 4); doc.Add(second);
        using var both = ScreenshotRenderer.Render(device, doc.State); AssertOutsideUnchanged(first.GetPixelColors(), both.GetPixelColors(), second);
        doc.Add(new("top", ScreenshotTool.Redact, new(35, 35, 4, 4), new(0, 0, 255)));
        using var top = ScreenshotRenderer.Render(device, doc.State); Assert.AreEqual(new Rgba32(0, 0, 255), Rgba32.FromPremultipliedBgra(top.GetPixelBytes(36, 36, 1, 1)));
        doc.Undo(); using var undone = ScreenshotRenderer.Render(device, doc.State); CollectionAssert.AreEqual(both.GetPixelColors(), undone.GetPixelColors());
    }
    [TestMethod]
    public void MovingDeletingAndUndoingBlurKeepsEditableObjectsAndRestoresPixels()
    {
        using var device = new CanvasDevice(); var doc = Scene(Rgba32.White); var initial = doc.State;
        using var original = ScreenshotRenderer.Render(device, initial);
        var blur = new ScreenshotAnnotation("blur", ScreenshotTool.Blur, new(30, 30, 40, 30), Rgba32.Black, BlurRadius: 4); doc.Add(blur);
        using var first = ScreenshotRenderer.Render(device, doc.State);
        var moved = blur.Move(new(60, 40)); doc.Replace(moved);
        using var relocated = ScreenshotRenderer.Render(device, doc.State); AssertOutsideUnchanged(original.GetPixelColors(), relocated.GetPixelColors(), moved);
        doc.Delete("blur"); using var deleted = ScreenshotRenderer.Render(device, doc.State); CollectionAssert.AreEqual(original.GetPixelColors(), deleted.GetPixelColors());
        doc.Undo(); using var undoDelete = ScreenshotRenderer.Render(device, doc.State); CollectionAssert.AreEqual(relocated.GetPixelColors(), undoDelete.GetPixelColors());
        doc.Undo(); using var undoMove = ScreenshotRenderer.Render(device, doc.State); CollectionAssert.AreEqual(first.GetPixelColors(), undoMove.GetPixelColors());
        doc.Undo(); Assert.AreSame(initial, doc.State);
        doc.Redo(); using var redoAdd = ScreenshotRenderer.Render(device, doc.State); CollectionAssert.AreEqual(first.GetPixelColors(), redoAdd.GetPixelColors());
        doc.Redo(); using var redoMove = ScreenshotRenderer.Render(device, doc.State); CollectionAssert.AreEqual(relocated.GetPixelColors(), redoMove.GetPixelColors());
        doc.Redo(); using var redoDelete = ScreenshotRenderer.Render(device, doc.State); CollectionAssert.AreEqual(original.GetPixelColors(), redoDelete.GetPixelColors());
    }
}
