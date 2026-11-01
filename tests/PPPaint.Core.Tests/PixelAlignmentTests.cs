using System.Numerics;

namespace PPPaint.Core.Tests;

// v0.2.30: rasters and rectangular selections stay on whole pixels.
[TestClass]
public sealed class PixelAlignmentTests
{
    [TestMethod]
    public void SnapToPixelsMovesEachEdgeToTheNearestBoundary()
    {
        Assert.AreEqual(new ScreenshotBox(11, 5, 5, 4), new ScreenshotBox(10.6f, 5.2f, 5.8f, 3.6f).SnapToPixels());
        Assert.AreEqual(new ScreenshotBox(3, 4, 2, 1), new ScreenshotBox(2.5f, 4.4f, 2f, .2f).SnapToPixels());
        Assert.AreEqual(new ScreenshotBox(7, 8, 9, 10), new ScreenshotBox(7, 8, 9, 10).SnapToPixels());
    }

    [TestMethod]
    public void RegionAreaCoversThePixelsWhoseCentersAreInsideTheFrame()
    {
        // Truncation used to shift this frame one pixel left and drop the last covered column.
        Assert.AreEqual(new PixelRect(11, 5, 5, 4), ScreenshotCrop.RegionArea(new(10.6f, 5.2f, 5.8f, 3.6f), 100, 80));
        Assert.AreEqual(new PixelRect(90, 70, 10, 10), ScreenshotCrop.RegionArea(new(90, 70, 40, 40), 100, 80));
        Assert.AreEqual(new PixelRect(0, 0, 3, 3), ScreenshotCrop.RegionArea(new(-5, -5, 8, 8), 100, 80));
        Assert.IsNull(ScreenshotCrop.RegionArea(new(4, 4, .8f, 6), 100, 80));
        Assert.IsNull(ScreenshotCrop.RegionArea(new(float.NaN, 4, 6, 6), 100, 80));
    }

    [TestMethod]
    public void CropUsesTheSameRegionRuleAsDeleteAndCut()
    {
        var state = new ScreenshotState(ScreenshotFrame.Transparent(100, 80), []);
        var region = new ScreenshotBox(10.6f, 5.2f, 5.8f, 3.6f);
        Assert.AreEqual(ScreenshotCrop.RegionArea(region, 100, 80), ScreenshotCrop.Resolve(state, region, new HashSet<string>()));
    }

    [TestMethod]
    public void WholePixelsRoundsMoveDeltasSymmetrically()
    {
        Assert.AreEqual(new Vector2(3, -3), ScreenshotManipulation.WholePixels(new(2.5f, -2.5f)));
        Assert.AreEqual(new Vector2(0, 1), ScreenshotManipulation.WholePixels(new(.49f, .51f)));
        var image = ScreenshotDocument.ImageObject(ScreenshotFrame.Transparent(4, 4), new(12, 18));
        var moved = image.Move(ScreenshotManipulation.WholePixels(new(1.4f / .67f, 2.2f / .67f)));
        Assert.AreEqual(moved.Bounds.X, MathF.Round(moved.Bounds.X));
        Assert.AreEqual(moved.Bounds.Y, MathF.Round(moved.Bounds.Y));
    }

    [TestMethod]
    public void CenteredOriginNeverLandsOnAHalfPixel()
    {
        var canvas = ScreenshotFrame.Transparent(1000, 650);
        Assert.AreEqual(new Vector2(99, 24), ScreenshotPasting.CenteredOrigin(canvas, 801, 601));
        Assert.AreEqual(new Vector2(100, 25), ScreenshotPasting.CenteredOrigin(canvas, 800, 600));
        Assert.AreEqual(Vector2.Zero, ScreenshotPasting.CenteredOrigin(canvas, 1200, 700));
    }

    [TestMethod]
    public void InsertedImagesStartOnWholePixels()
    {
        var document = new ScreenshotDocument(ScreenshotFrame.Transparent(50, 40));
        var id = document.InsertImage(ScreenshotFrame.Transparent(10, 10), new(12.7f, 3.2f));
        var image = document.State.Annotations.Single(a => a.Id == id);
        Assert.AreEqual(new ScreenshotBox(12, 3, 10, 10), image.Bounds);
        document.InsertImage(ScreenshotFrame.Transparent(10, 10), new(-3.5f, 45.5f));
        Assert.AreEqual(new ScreenshotBox(0, 45, 10, 10), document.State.Annotations[^1].Bounds);
        Assert.AreEqual(55, document.State.Frame.Height);
    }
}
