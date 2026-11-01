
namespace PPPaint.Core.Tests;

[TestClass]
public sealed class ScreenshotRasterMergeTests
{
    private static ScreenshotAnnotation Shape(string id, ScreenshotBox bounds) => new(id, ScreenshotTool.Rectangle, bounds, Rgba32.Black, Width: 4);

    [TestMethod]
    public void PlanUsesSelectedObjectsOnlyAndKeepsOriginalSamplingCoordinates()
    {
        var selected = Shape("selected", new(10.5f, 12.5f, 10, 8));
        var other = Shape("other", new(70, 60, 10, 10));
        var source = new ScreenshotState(ScreenshotFrame.Blank(100, 80), [selected, other], 9, new(20, 30, 40));
        var plan = ScreenshotRasterMerge.Create(source, new HashSet<string> { selected.Id });
        Assert.AreEqual(new PixelRect(8, 10, 15, 13), plan.Bounds);
        Assert.AreEqual(100, plan.Scene.Frame.Width); Assert.AreEqual(80, plan.Scene.Frame.Height);
        Assert.AreEqual(Rgba32.Transparent, plan.Scene.Background); Assert.IsFalse(plan.Scene.Frame.HasVisiblePixels());
        Assert.AreEqual(1, plan.Scene.Annotations.Length); Assert.AreSame(selected, plan.Scene.Annotations[0]); Assert.AreEqual(9, plan.Scene.NextNumber);
    }

    [TestMethod]
    public void VisibleBoundsIncludeArrowHeadAndCalloutTailAndRespectCanvasAndMasks()
    {
        var arrow = new ScreenshotAnnotation("arrow", ScreenshotTool.Arrow, new(20, 20, 25, 10), Rgba32.Black, Width: 2, Points: [new(20, 20), new(45, 30)]);
        var callout = new ScreenshotAnnotation("callout", ScreenshotTool.Callout, new(50, 10, 20, 20), Rgba32.Black, Width: 4, CalloutTip: new(90, 60));
        var masked = Shape("masked", new(-10, -10, 200, 200)) with { ClipBounds = new(5.5f, 6.5f, 5, 7) };
        var source = new ScreenshotState(ScreenshotFrame.Transparent(100, 80), [arrow, callout, masked]);
        var plan = ScreenshotRasterMerge.Create(source, new HashSet<string> { arrow.Id, callout.Id });
        Assert.AreEqual(new PixelRect(8, 8, 84, 54), plan.Bounds);
        var maskPlan = ScreenshotRasterMerge.Create(source, new HashSet<string> { masked.Id });
        Assert.AreEqual(new PixelRect(5, 6, 6, 8), maskPlan.Bounds);
        var unmasked = source with { Annotations = [masked with { ClipBounds = null }] };
        Assert.AreEqual(new PixelRect(0, 0, 100, 80), ScreenshotRasterMerge.Create(unmasked, new HashSet<string> { masked.Id }).Bounds);
    }

    [TestMethod]
    public void OnePixelPenAndOutsideCanvasBoundsStayOnWholePixels()
    {
        var dot = new ScreenshotAnnotation("dot", ScreenshotTool.Pen, new(9.99f, 8.01f, 0, 0), Rgba32.Black, Width: 1, Points: [new(9.99f, 8.01f)]);
        var edge = Shape("edge", new(-3.25f, -2.5f, 8, 7));
        var source = new ScreenshotState(ScreenshotFrame.Transparent(20, 20), [dot, edge]);
        Assert.AreEqual(new PixelRect(9, 8, 1, 1), ScreenshotRasterMerge.Create(source, new HashSet<string> { dot.Id }).Bounds);
        Assert.AreEqual(new PixelRect(0, 0, 7, 7), ScreenshotRasterMerge.Create(source, new HashSet<string> { edge.Id }).Bounds);
    }

    [TestMethod]
    public void ApplyCollapsesAtTheFrontSelectedPositionAndPreservesUnselectedReferencesAndDocumentValues()
    {
        var a = Shape("a", new(1, 1, 10, 10)); var b = Shape("b", new(2, 2, 10, 10)); var c = Shape("c", new(3, 3, 10, 10)); var d = Shape("d", new(4, 4, 10, 10)); var e = Shape("e", new(5, 5, 10, 10));
        var source = new ScreenshotState(ScreenshotFrame.Blank(40, 30), [a, b, c, d, e], 21, new(25, 50, 90, 120));
        var plan = ScreenshotRasterMerge.Create(source, new HashSet<string> { a.Id, d.Id });
        var raster = new ScreenshotRasterMergeImage(ScreenshotFrame.Transparent(15, 15), new(0, 0, 15, 15));
        var result = plan.Apply(source, raster);
        CollectionAssert.AreEqual(new[] { b.Id, c.Id, result.MergedId, e.Id }, result.State.Annotations.Select(item => item.Id).ToArray());
        Assert.AreSame(b, result.State.Annotations[0]); Assert.AreSame(c, result.State.Annotations[1]); Assert.AreSame(e, result.State.Annotations[3]);
        var merged = result.State.Annotations[2]; Assert.AreEqual(ScreenshotTool.Image, merged.Tool); Assert.AreSame(raster.Frame, merged.Raster); Assert.AreEqual(new ScreenshotBox(0, 0, 15, 15), merged.Bounds);
        Assert.AreSame(source.Frame, result.State.Frame); Assert.AreEqual(source.Background, result.State.Background); Assert.AreEqual(21, result.State.NextNumber);
        Assert.IsFalse(source.Annotations.Any(item => item.Id == result.MergedId));
    }

    [TestMethod]
    public void ApplyChangesHistoryOnlyWhenCallerCommitsAndOneUndoRestoresAllObjects()
    {
        var document = new ScreenshotDocument(ScreenshotFrame.Transparent(30, 20));
        document.AddMany([Shape("a", new(1, 1, 4, 4)), Shape("b", new(8, 3, 4, 4))]);
        var before = document.State; var plan = ScreenshotRasterMerge.Create(before, before.Annotations.Select(item => item.Id).ToHashSet());
        var result = plan.Apply(before, new(ScreenshotFrame.Transparent(16, 12), new(0, 0, 16, 12)));
        Assert.AreSame(before, document.State);
        document.Apply(result.State); var merged = document.State; Assert.AreEqual(1, merged.Annotations.Length);
        document.Undo(); Assert.AreSame(before, document.State); document.Redo(); Assert.AreSame(merged, document.State);
    }

    [TestMethod]
    public void ErasersBecomeTransparentOnlyInTheRenderScene()
    {
        var eraser = new ScreenshotAnnotation("eraser", ScreenshotTool.Eraser, new(1, 1, 5, 0), Rgba32.Black, Width: 2, Points: [new(1, 1), new(6, 1)], EraserBackground: new(255, 0, 0));
        var source = new ScreenshotState(ScreenshotFrame.Blank(10, 10), [eraser], Background: Rgba32.White);
        var plan = ScreenshotRasterMerge.Create(source, new HashSet<string> { eraser.Id });
        Assert.AreEqual(Rgba32.Transparent, plan.Scene.Annotations[0].EraserBackground); Assert.AreEqual(new Rgba32(255, 0, 0), eraser.EraserBackground);
        Assert.AreEqual(eraser.Points, plan.Scene.Annotations[0].Points);
    }

    [TestMethod]
    public void InvalidSelectionStaleStateAndMalformedRasterCannotChangeTheDocument()
    {
        var source = new ScreenshotState(ScreenshotFrame.Transparent(20, 20), [Shape("inside", new(2, 2, 4, 4)), Shape("outside", new(50, 50, 4, 4))]);
        Assert.ThrowsExactly<ArgumentException>(() => ScreenshotRasterMerge.Create(source, new HashSet<string>()));
        Assert.ThrowsExactly<ArgumentException>(() => ScreenshotRasterMerge.Create(source, new HashSet<string> { "missing" }));
        Assert.ThrowsExactly<InvalidOperationException>(() => ScreenshotRasterMerge.Create(source, new HashSet<string> { "outside" }));
        var invalid = source with { Annotations = [Shape("bad", new(float.NaN, 1, 4, 4))] };
        Assert.ThrowsExactly<ArgumentException>(() => ScreenshotRasterMerge.Create(invalid, new HashSet<string> { "bad" }));
        var plan = ScreenshotRasterMerge.Create(source, new HashSet<string> { "inside" });
        var raster = new ScreenshotRasterMergeImage(ScreenshotFrame.Transparent(4, 4), new(2, 2, 4, 4));
        Assert.ThrowsExactly<InvalidOperationException>(() => plan.Apply(source with { NextNumber = 2 }, raster));
        foreach (var bad in new[] { new PixelRect(-1, 0, 4, 4), new PixelRect(18, 18, 4, 4), new PixelRect(1, 1, 5, 4), new PixelRect(0, 0, int.MaxValue, int.MaxValue) })
            Assert.ThrowsExactly<ArgumentException>(() => plan.Apply(source, raster with { Bounds = bad }));
        Assert.AreEqual(2, source.Annotations.Length);
    }
}
