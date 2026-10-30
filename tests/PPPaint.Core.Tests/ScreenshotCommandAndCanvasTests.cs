namespace PPPaint.Core.Tests;

[TestClass]
public sealed class ScreenshotCommandAndCanvasTests
{
    [TestMethod]
    public void ToolKeysAreUniqueAndTextAndCommandModifiersNeverSwitchTools()
    {
        Assert.AreEqual(ScreenshotCommands.Tools.Count, ScreenshotCommands.Tools.Select(t => t.Key).Distinct().Count());
        foreach (var command in ScreenshotCommands.Tools)
        {
            Assert.IsTrue(ScreenshotCommands.TryTool(char.ToLowerInvariant(command.Key), false, false, false, false, out var tool)); Assert.AreEqual(command.Tool, tool);
            Assert.IsFalse(ScreenshotCommands.TryTool(command.Key, true, false, false, false, out _));
            Assert.IsFalse(ScreenshotCommands.TryTool(command.Key, false, true, false, false, out _));
            Assert.IsFalse(ScreenshotCommands.TryTool(command.Key, false, false, false, true, out _));
        }
        Assert.IsFalse(ScreenshotCommands.TryTool('?', false, false, false, false, out _));
    }
    [TestMethod]
    public void EachCanvasEdgeCropsTheCorrectSideWithoutScaling()
    {
        Assert.AreEqual(new PixelRect(20, 0, 80, 60), ScreenshotCanvasGeometry.CropFromEdge(100, 60, ScreenshotCanvasEdge.Left, 20));
        Assert.AreEqual(new PixelRect(0, 15, 100, 45), ScreenshotCanvasGeometry.CropFromEdge(100, 60, ScreenshotCanvasEdge.Top, 15));
        Assert.AreEqual(new PixelRect(0, 0, 75, 60), ScreenshotCanvasGeometry.CropFromEdge(100, 60, ScreenshotCanvasEdge.Right, 75));
        Assert.AreEqual(new PixelRect(0, 0, 100, 45), ScreenshotCanvasGeometry.CropFromEdge(100, 60, ScreenshotCanvasEdge.Bottom, 45));
        Assert.AreEqual(new PixelRect(0, 0, 100, 60), ScreenshotCanvasGeometry.CropFromEdge(100, 60, ScreenshotCanvasEdge.Left, -20));
        Assert.AreEqual(new PixelRect(0, 0, 1, 60), ScreenshotCanvasGeometry.CropFromEdge(100, 60, ScreenshotCanvasEdge.Right, -20));
        Assert.AreEqual(1, ScreenshotCanvasGeometry.CropFromEdge(100, 60, ScreenshotCanvasEdge.Left, 1000).Width);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ScreenshotCanvasGeometry.CropFromEdge(100, 60, ScreenshotCanvasEdge.Top, float.NaN));
    }
    [TestMethod]
    public void CanvasCropKeepsObjectSizeAndSourcePixelsAndIsOneUndo()
    {
        var frame = ScreenshotFrame.Blank(100, 60); var doc = ScreenshotDocument.FromImage(frame); var before = doc.State;
        var crop = ScreenshotCanvasGeometry.CropFromEdge(100, 60, ScreenshotCanvasEdge.Left, 20);
        doc.Crop(crop.X, crop.Y, crop.Width, crop.Height);
        Assert.AreEqual(100f, doc.State.Annotations[0].Bounds.Width); Assert.AreEqual(-20f, doc.State.Annotations[0].Bounds.X);
        Assert.AreSame(frame, doc.State.Annotations[0].Raster); doc.Undo(); Assert.AreSame(before, doc.State);
    }
}
