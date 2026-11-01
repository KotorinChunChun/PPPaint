using System.Collections.Immutable;
using Microsoft.Graphics.Canvas;
using PPPaint.Core;
using Windows.Storage.Streams;

namespace PPPaint.Imaging.Tests;

[TestClass]
public sealed class ScreenshotRasterMergeRenderingTests
{
    private static ScreenshotFrame Pattern(int width, int height)
    {
        var bytes = new byte[width * height * 4];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var i = (y * width + x) * 4; var alpha = (x + y) % 3 == 0 ? 100 : 230;
            bytes[i] = (byte)(30 * alpha / 255); bytes[i + 1] = (byte)((y * 5 + 20) * alpha / 255); bytes[i + 2] = (byte)((x * 7 + 40) * alpha / 255); bytes[i + 3] = (byte)alpha;
        }
        return new(width, height, bytes);
    }

    [TestMethod]
    public void RasterizationKeepsAllSelectedPixelsIncludingThinStrokesArrowHeadsMasksAndText()
    {
        using var device = new CanvasDevice();
        var raster = ScreenshotDocument.ImageObject(Pattern(20, 15), new(7.5f, 6.5f)) with { Rotation = 90, MirrorX = true, ClipBounds = new(9, 5, 19, 17), Cutouts = [new(12, 8, 3, 4)] };
        var source = new ScreenshotState(ScreenshotFrame.Blank(140, 100), [
            new("unselected", ScreenshotTool.Redact, new(0, 0, 140, 100), new(0, 255, 0)),
            raster,
            new("dot", ScreenshotTool.Pen, new(1.9f, 2.4f, 0, 0), new(255, 0, 0), Width: 1, Points: [new(1.9f, 2.4f)]),
            new("line", ScreenshotTool.Line, new(4.5f, 10.5f, 85, 21), new(20, 80, 190, 180), Width: 3, Points: [new(4.5f, 10.5f), new(89.5f, 31.5f)]),
            new("arrow", ScreenshotTool.Arrow, new(40, 30, 45, 25), new(220, 40, 30), Width: 4, Points: [new(40, 55), new(85, 30)]),
            new("ellipse", ScreenshotTool.Ellipse, new(5, 55, 30, 25), new(190, 20, 140), Width: 5),
            new("number", ScreenshotTool.Number, new(50, 60, 32, 32), new(200, 40, 20), FontSize: 19, Text: "12"),
            new("text", ScreenshotTool.Text, new(90, 50, 35, 10), new(15, 45, 110), FontSize: 20, Text: "説明です\n2行目"),
            new("callout", ScreenshotTool.Callout, new(90, 10, 30, 25), new(20, 90, 130), Width: 2, FontSize: 10, Text: "A", CalloutTip: new(137, 48))
        ], 17, new(40, 50, 60));
        var ids = source.Annotations.Skip(1).Select(item => item.Id).ToHashSet();
        var plan = ScreenshotRasterMerge.Create(source, ids); var image = ScreenshotRasterMergeRenderer.Render(device, plan);
        var result = plan.Apply(source, image);
        var onlyMerged = result.State with { Frame = ScreenshotFrame.Transparent(140, 100), Background = Rgba32.Transparent, Annotations = result.State.Annotations.Where(item => item.Id == result.MergedId).ToImmutableArray() };
        using var expected = ScreenshotRenderer.Render(device, plan.Scene); using var actual = ScreenshotRenderer.Render(device, onlyMerged);
        CollectionAssert.AreEqual(expected.GetPixelBytes(), actual.GetPixelBytes());
        Assert.AreEqual((byte)255, image.Frame.Pixels.Span[(2 - image.Bounds.Y) * image.Frame.Width * 4 + (1 - image.Bounds.X) * 4 + 3]);
        Assert.AreEqual(2, result.State.Annotations.Length); Assert.AreSame(source.Annotations[0], result.State.Annotations[0]);
        Assert.AreEqual(source.Background, result.State.Background); Assert.AreEqual(17, result.State.NextNumber);
    }

    [TestMethod]
    [DataRow(ScreenshotTool.Blur)]
    [DataRow(ScreenshotTool.Mosaic)]
    [DataRow(ScreenshotTool.Eraser)]
    public void EffectsUseOnlyTheSelectedBackdropAndKeepOriginalSamplingDomain(ScreenshotTool tool)
    {
        using var device = new CanvasDevice();
        var pattern = ScreenshotDocument.ImageObject(Pattern(26, 20), new(7, 8));
        var effect = new ScreenshotAnnotation("effect", tool, new(3.25f, 4.25f, 34.5f, 28.5f), Rgba32.Black,
            Width: 3, Points: [new(5.25f, 15.25f), new(36.75f, 16.25f)], BlurRadius: 3, MosaicSize: 7, ClipBounds: new(4, 5, 35, 25), Cutouts: [new(12, 12, 3, 3)],
            SamplingBounds: new(2, 3, 38, 30), EraserBackground: new(255, 0, 255));
        var source = new ScreenshotState(ScreenshotFrame.Blank(50, 40), [new("other", ScreenshotTool.Redact, new(0, 0, 50, 40), new(0, 255, 0)), pattern, effect], Background: new(100, 30, 70));
        var plan = ScreenshotRasterMerge.Create(source, new HashSet<string> { pattern.Id, effect.Id });
        var image = ScreenshotRasterMergeRenderer.Render(device, plan); var result = plan.Apply(source, image);
        var onlyMerged = new ScreenshotState(ScreenshotFrame.Transparent(50, 40), [result.State.Annotations.Single(item => item.Id == result.MergedId)], Background: Rgba32.Transparent);
        using var before = ScreenshotRenderer.Render(device, plan.Scene); using var after = ScreenshotRenderer.Render(device, onlyMerged);
        CollectionAssert.AreEqual(before.GetPixelBytes(), after.GetPixelBytes(), tool.ToString());
        Assert.IsTrue(image.Frame.Pixels.ToArray().Where((_, index) => index % 4 == 3).Any(alpha => alpha < 255));
        var withoutUnselected = source with { Annotations = [pattern, effect], Background = Rgba32.Transparent };
        var isolatedPlan = ScreenshotRasterMerge.Create(withoutUnselected, new HashSet<string> { pattern.Id, effect.Id });
        var isolated = ScreenshotRasterMergeRenderer.Render(device, isolatedPlan);
        Assert.AreEqual(image.Bounds, isolated.Bounds); CollectionAssert.AreEqual(image.Frame.Pixels.ToArray(), isolated.Frame.Pixels.ToArray());
    }

    [TestMethod]
    public void ActualTextInkOutsideDeclaredBoundsIsRetainedAndCanvasEdgesAreClipped()
    {
        using var device = new CanvasDevice();
        var text = new ScreenshotAnnotation("text", ScreenshotTool.Text, new(30, 25, 35, 5), new(40, 80, 140), FontSize: 20, Text: "AB\nCD\nEF");
        var outside = new ScreenshotAnnotation("outside", ScreenshotTool.Rectangle, new(-3, -4, 20, 20), new(220, 40, 90), Width: 5);
        var source = new ScreenshotState(ScreenshotFrame.Transparent(90, 80), [text, outside], Background: Rgba32.Transparent);
        var plan = ScreenshotRasterMerge.Create(source, new HashSet<string> { text.Id, outside.Id });
        var image = ScreenshotRasterMergeRenderer.Render(device, plan); var result = plan.Apply(source, image);
        using var before = ScreenshotRenderer.Render(device, plan.Scene); using var after = ScreenshotRenderer.Render(device, result.State);
        CollectionAssert.AreEqual(before.GetPixelBytes(), after.GetPixelBytes());
        Assert.IsTrue(image.Bounds.Bottom > text.Bounds.Bottom + text.Width / 2, "宣言Boundsより下の文字画素も統合すること");
        Assert.IsTrue(image.Bounds.X >= 0 && image.Bounds.Y >= 0 && image.Bounds.Right <= 90 && image.Bounds.Bottom <= 80);
    }

    [TestMethod]
    public void FullyTransparentSelectionRemainsTransparentAndOnePixelDotStaysOnePixel()
    {
        using var device = new CanvasDevice();
        var dot = new ScreenshotAnnotation("dot", ScreenshotTool.Pen, new(9.99f, 8.01f, 0, 0), new(255, 0, 0), Width: 1, Points: [new(9.99f, 8.01f)]);
        var erase = new ScreenshotAnnotation("erase", ScreenshotTool.Eraser, new(9.99f, 8.01f, 0, 0), Rgba32.Black, Width: 1, Points: [new(9.99f, 8.01f)]);
        var source = new ScreenshotState(ScreenshotFrame.Blank(25, 20), [dot, erase], Background: Rgba32.White);
        var dotPlan = ScreenshotRasterMerge.Create(source, new HashSet<string> { dot.Id }); var dotImage = ScreenshotRasterMergeRenderer.Render(device, dotPlan);
        Assert.AreEqual(new PixelRect(9, 8, 1, 1), dotImage.Bounds); Assert.AreEqual(1, dotImage.Frame.Width); Assert.AreEqual(1, dotImage.Frame.Height);
        CollectionAssert.AreEqual(new byte[] { 0, 0, 255, 255 }, dotImage.Frame.Pixels.ToArray());
        var emptyPlan = ScreenshotRasterMerge.Create(source, new HashSet<string> { dot.Id, erase.Id }); var emptyImage = ScreenshotRasterMergeRenderer.Render(device, emptyPlan);
        Assert.IsFalse(emptyImage.Frame.HasVisiblePixels());
        var merged = emptyPlan.Apply(source, emptyImage); Assert.AreEqual(1, merged.State.Annotations.Length);
    }

    [TestMethod]
    public async Task MergedRasterPngReloadKeepsAlphaAndUndoRestoresOriginalVectorObjects()
    {
        using var device = new CanvasDevice(); var document = new ScreenshotDocument(ScreenshotFrame.Transparent(40, 30));
        document.SetBackground(Rgba32.Transparent);
        document.AddMany([new("fill", ScreenshotTool.Highlight, new(2, 3, 15, 12), new(220, 50, 20)), new("line", ScreenshotTool.Line, new(3, 5, 20, 10), new(20, 50, 220, 150), Width: 2, Points: [new(3, 5), new(23, 15)])]);
        var original = document.State; var plan = ScreenshotRasterMerge.Create(original, original.Annotations.Select(item => item.Id).ToHashSet());
        var image = ScreenshotRasterMergeRenderer.Render(device, plan); var merged = plan.Apply(document.State, image); document.Apply(merged.State);
        using var rendered = ScreenshotRenderer.Render(device, document.State); using var png = new InMemoryRandomAccessStream();
        await Win2DImageCodec.SaveAsync(rendered, png, DocumentFileFormat.Png); using var loaded = await Win2DImageCodec.LoadAsync(device, png);
        using var before = ScreenshotRenderer.Render(device, plan.Scene); using var beforePng = new InMemoryRandomAccessStream();
        await Win2DImageCodec.SaveAsync(before, beforePng, DocumentFileFormat.Png); using var beforeLoaded = await Win2DImageCodec.LoadAsync(device, beforePng);
        // PNG stores straight RGB; compare the same codec round trip on both
        // sides, while requiring alpha itself to remain byte-for-byte exact.
        CollectionAssert.AreEqual(beforeLoaded.GetPixelBytes(), loaded.GetPixelBytes());
        var raw = rendered.GetPixelBytes(); var decoded = loaded.GetPixelBytes();
        for (var index = 3; index < raw.Length; index += 4) Assert.AreEqual(raw[index], decoded[index], "PNGでalphaが変わっていないこと");
        document.Undo(); Assert.AreSame(original, document.State); document.Redo(); Assert.AreSame(merged.State, document.State);
    }
}
