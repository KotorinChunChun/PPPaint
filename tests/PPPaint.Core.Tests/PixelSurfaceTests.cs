using PPPaint.Core;

namespace PPPaint.Core.Tests;

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
    public void FloodFillRegion_DoesNotCrossDiagonalOrWrapRows()
    {
        var surface = new PixelSurface(3, 3, Rgba32.Black);
        surface[2, 0] = Rgba32.White;
        surface[0, 1] = Rgba32.White;
        surface[1, 1] = Rgba32.White;
        var before = surface.Snapshot();

        CollectionAssert.AreEqual(new[] { 2 }, surface.FloodFillRegion(2, 0));
        CollectionAssert.AreEquivalent(new[] { 3, 4 }, surface.FloodFillRegion(0, 1));
        CollectionAssert.AreEqual(before, surface.Snapshot());
    }

    [TestMethod]
    public void FloodFillRegion_LargeSolidAreaHasBoundedAllocation()
    {
        const int count = 256 * 256;
        var surface = new PixelSurface(256, 256, Rgba32.White);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var region = surface.FloodFillRegion(0, 0);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual(count, region.Length);
        Assert.AreEqual(count, region.Distinct().Count());
        Assert.IsTrue(allocated < count * 16L, $"探索の割当が大きすぎます: {allocated} bytes");
    }

    [TestMethod]
    public void FloodFill_SameColorLeavesPixelsUnchanged()
    {
        var surface = new PixelSurface(3, 2, Rgba32.White);
        Assert.AreEqual(0, surface.FloodFill(1, 1, Rgba32.White));
        CollectionAssert.AreEqual(new PixelSurface(3, 2, Rgba32.White).Snapshot(), surface.Snapshot());
    }

    [TestMethod]
    public void InvalidCoordinates_Throw()
    {
        var surface = new PixelSurface(1, 1, Rgba32.White);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = surface[-1, 0]);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => surface[0, 1] = Rgba32.Black);
    }
}
