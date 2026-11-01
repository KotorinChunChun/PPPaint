namespace PPPaint.Core.Tests;

[TestClass]
public sealed class ScreenshotCropTests
{
    [TestMethod]
    public void ImageAndMultipleShapesUseOnlySelectedDrawingBounds()
    {
        var doc = new ScreenshotDocument(ScreenshotFrame.Transparent(200, 150));
        doc.Add(new("r", ScreenshotTool.Rectangle, new(20.5f, 30.5f, 40, 20), Rgba32.Black, Width: 3));
        doc.Add(new("o", ScreenshotTool.Ellipse, new(100, 70, 20, 20), Rgba32.Black, Width: 4));
        doc.Add(new("unused", ScreenshotTool.Rectangle, new(0, 0, 200, 150), Rgba32.Black));
        Assert.AreEqual(new PixelRect(19, 29, 103, 63), ScreenshotCrop.Resolve(doc.State, null, new HashSet<string> { "r", "o" }));
        var image = ScreenshotDocument.ImageObject(ScreenshotFrame.Blank(40, 30), new(12, 18)); doc.Add(image);
        Assert.AreEqual(new PixelRect(12, 18, 40, 30), ScreenshotCrop.Resolve(doc.State, null, new HashSet<string> { image.Id }));
    }
    [TestMethod]
    public void ExplicitRegionTakesPriorityAndEmptyOrOutsideSelectionIsDisabled()
    {
        var doc = new ScreenshotDocument(ScreenshotFrame.Transparent(100, 80));
        doc.Add(new("outside", ScreenshotTool.Rectangle, new(200, 100, 20, 20), Rgba32.Black));
        Assert.IsNull(ScreenshotCrop.Resolve(doc.State, null, new HashSet<string>()));
        Assert.IsNull(ScreenshotCrop.Resolve(doc.State, null, new HashSet<string> { "outside" }));
        Assert.AreEqual(new PixelRect(5, 6, 7, 8), ScreenshotCrop.Resolve(doc.State, new(5, 6, 7, 8), new HashSet<string> { "outside" }));
    }
    [TestMethod]
    public void MasksAndCanvasEdgesClipBoundsAndArrowHeadIsIncluded()
    {
        var doc = new ScreenshotDocument(ScreenshotFrame.Transparent(100, 80));
        doc.Add(new("r", ScreenshotTool.Rectangle, new(-10, -10, 40, 40), Rgba32.Black, Width: 4, ClipBounds: new(0, 0, 20, 20)));
        Assert.AreEqual(new PixelRect(0, 0, 20, 20), ScreenshotCrop.Resolve(doc.State, null, new HashSet<string> { "r" }));
        doc.Add(new("a", ScreenshotTool.Arrow, new(30, 30, 40, 10), Rgba32.Black, Width: 4, Points: [new(30, 30), new(70, 40)]));
        Assert.AreEqual(new PixelRect(14, 14, 72, 42), ScreenshotCrop.Resolve(doc.State, null, new HashSet<string> { "a" }));
    }
    [TestMethod]
    public void OnePixelPenAtIntegerCoordinateSurvivesCropAndUndoRedo()
    {
        var doc = new ScreenshotDocument(ScreenshotFrame.Transparent(100, 80));
        doc.Add(new("dot", ScreenshotTool.Pen, new(10, 20, 0, 0), Rgba32.Black, Width: 1, Points: [new(10, 20)]));
        var before = doc.State; var crop = ScreenshotCrop.Resolve(before, null, new HashSet<string> { "dot" })!.Value;
        Assert.AreEqual(new PixelRect(10, 20, 1, 1), crop);
        doc.Crop(crop.X, crop.Y, crop.Width, crop.Height);
        Assert.HasCount(1, doc.State.Annotations); Assert.AreEqual(new ScreenshotBox(0, 0, 0, 0), doc.State.Annotations[0].Bounds);
        var after = doc.State; doc.Undo(); Assert.AreSame(before, doc.State); doc.Redo(); Assert.AreSame(after, doc.State);
    }
}
