using ChunPaint.Core;

namespace ChunPaint.Core.Tests;

[TestClass]
public sealed class PixelSurfaceTests
{
    [TestMethod]
    public void Constructor_ClearsEveryPixelWithBackground()
    {
        var color = new Rgba32(10, 20, 30, 40);
        var surface = new PixelSurface(3, 2, color);

        for (var y = 0; y < surface.Height; y++)
        for (var x = 0; x < surface.Width; x++)
            Assert.AreEqual(color, surface[x, y]);
    }

    [TestMethod]
    public void SnapshotAndRestore_RoundTripsBgraPixels()
    {
        var surface = new PixelSurface(2, 1, Rgba32.White);
        surface[0, 0] = new Rgba32(1, 2, 3, 4);
        var snapshot = surface.Snapshot();

        surface.Clear(Rgba32.Black);
        surface.Restore(snapshot);

        Assert.AreEqual(new Rgba32(1, 2, 3, 4), surface[0, 0]);
        Assert.AreEqual(Rgba32.White, surface[1, 0]);
    }

    [TestMethod]
    public void FloodFill_StopsAtDifferentColoredBoundary()
    {
        var surface = new PixelSurface(5, 3, Rgba32.White);
        for (var y = 0; y < 3; y++) surface[2, y] = Rgba32.Black;
        var red = new Rgba32(255, 0, 0);

        var filled = surface.FloodFill(0, 1, red);

        Assert.AreEqual(6, filled);
        Assert.AreEqual(red, surface[1, 2]);
        Assert.AreEqual(Rgba32.White, surface[3, 1]);
        Assert.AreEqual(Rgba32.Black, surface[2, 1]);
    }

    [TestMethod]
    public void FloodFillRegion_ReturnsOnlyTheConnectedAreaWithoutChangingPixels()
    {
        var surface = new PixelSurface(5, 3, Rgba32.White);
        for (var y = 0; y < 3; y++) surface[2, y] = Rgba32.Black;

        var region = surface.FloodFillRegion(0, 1);

        CollectionAssert.AreEquivalent(new[] { 0, 1, 5, 6, 10, 11 }, region);
        Assert.AreEqual(Rgba32.White, surface[0, 1]);
        Assert.AreEqual(Rgba32.White, surface[1, 2]);
        Assert.AreEqual(Rgba32.Black, surface[2, 1]);
    }

    [TestMethod]
    public void InvalidCoordinates_Throw()
    {
        var surface = new PixelSurface(1, 1, Rgba32.White);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = surface[-1, 0]);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => surface[0, 1] = Rgba32.Black);
    }
}
