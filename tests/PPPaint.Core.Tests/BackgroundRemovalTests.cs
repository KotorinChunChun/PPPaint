using PPPaint.Core;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class BackgroundRemovalTests
{
    [TestMethod]
    public void RemoveBackgroundFromEdges_MakesConnectedBackgroundTransparent()
    {
        var background = new Rgba32(250, 250, 250, 255);
        var foreground = new Rgba32(10, 20, 30, 255);
        var source = new PixelSurface(3, 3, background);
        source[1, 1] = foreground;

        var result = ImageTransforms.RemoveBackgroundFromEdges(source, 8);

        Assert.AreEqual(new Rgba32(250, 250, 250, 0), result[0, 0]);
        Assert.AreEqual(foreground, result[1, 1]);
    }

    [TestMethod]
    public void RemoveBackgroundFromEdges_PreservesEnclosedMatchingColor()
    {
        var background = new Rgba32(255, 255, 255, 255);
        var border = new Rgba32(0, 0, 0, 255);
        var source = new PixelSurface(5, 5, background);
        for (var index = 1; index < 4; index++)
        {
            source[index, 1] = border;
            source[index, 3] = border;
            source[1, index] = border;
            source[3, index] = border;
        }

        var result = ImageTransforms.RemoveBackgroundFromEdges(source, 0);

        Assert.AreEqual((byte)0, result[0, 0].A);
        Assert.AreEqual(background, result[2, 2]);
    }

    [TestMethod]
    public void RemoveFromEdges_RestrictsChangesToSelectionRegion()
    {
        var source = new PixelSurface(5, 5, Rgba32.White);
        source[2, 2] = new Rgba32(20, 30, 40);
        var region = SelectionRegion.CreateRectangle(new PixelRect(1, 1, 3, 3));

        var result = BackgroundRemovalOperations.RemoveFromEdges(source, region, 0);

        Assert.AreEqual((byte)255, result[0, 0].A);
        Assert.AreEqual((byte)0, result[1, 1].A);
        Assert.AreEqual((byte)255, result[2, 2].A);
    }

    [TestMethod]
    public void RemoveFromVisibleLayers_ProcessesVisibleLayersOnly()
    {
        var visible = new PixelSurface(2, 2, Rgba32.White);
        var hidden = new PixelSurface(2, 2, Rgba32.White);

        var removed = BackgroundRemovalOperations.RemoveFromVisibleLayers(
            [(visible, true), (hidden, false)], tolerance: 0);

        Assert.AreEqual(4, removed);
        Assert.AreEqual((byte)0, visible[0, 0].A);
        Assert.AreEqual((byte)255, hidden[0, 0].A);
    }

    [TestMethod]
    public void RemoveFromEdges_HonorsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() =>
            BackgroundRemovalOperations.RemoveFromEdges(new PixelSurface(2, 2, Rgba32.White), cancellationToken: cancellation.Token));
    }
}
