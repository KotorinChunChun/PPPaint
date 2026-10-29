using PPPaint.Core;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class PixelBlockSnapshotTests
{
    [TestMethod]
    public void CapturesOnlyChangedBlocksAndProtectsState()
    {
        var pixels = new byte[PixelBlockSnapshot.BlockSize * 2 + 4];
        var first = PixelBlockSnapshot.Capture(pixels);
        pixels[PixelBlockSnapshot.BlockSize + 1] = 100;
        var second = PixelBlockSnapshot.Capture(pixels, first);
        Assert.AreSame(first.Blocks[0], second.Blocks[0]);
        Assert.AreNotSame(first.Blocks[1], second.Blocks[1]);
        Assert.AreSame(first.Blocks[2], second.Blocks[2]);
        Assert.AreEqual(0, first.ToArray()[PixelBlockSnapshot.BlockSize + 1]);
        pixels[0] = 99;
        var returned = second.ToArray();
        returned[0] = 88;
        Assert.AreEqual(0, second.ToArray()[0]);
        Assert.AreEqual(100, second.ToArray()[PixelBlockSnapshot.BlockSize + 1]);
        Assert.AreEqual(pixels.Length, second.ByteLength);
    }

    [TestMethod]
    public void FortyGenerationsPreserveOrderAndShareUntouchedData()
    {
        var snapshots = new List<PixelBlockSnapshot>();
        var pixels = new byte[PixelBlockSnapshot.BlockSize * 2];
        for (var index = 0; index < 40; index++)
        {
            pixels[0] = (byte)index;
            snapshots.Add(PixelBlockSnapshot.Capture(pixels, snapshots.LastOrDefault()));
        }
        for (var index = 39; index >= 0; index--)
        {
            Assert.AreEqual(index, snapshots[index].ToArray()[0]);
            Assert.AreSame(snapshots[0].Blocks[1], snapshots[index].Blocks[1]);
        }
        for (var index = 0; index < 40; index++) Assert.AreEqual(index, snapshots[index].ToArray()[0]);
        Assert.AreEqual(41, snapshots.SelectMany(item => item.Blocks).Distinct().Count());
    }

    [TestMethod]
    public void DimensionChangesAndEmptyDataRoundTrip()
    {
        var first = PixelBlockSnapshot.Capture(new byte[PixelBlockSnapshot.BlockSize]);
        var resized = PixelBlockSnapshot.Capture(new byte[8], first);
        Assert.AreNotSame(first.Blocks[0], resized.Blocks[0]);
        Assert.AreEqual(8, resized.ToArray().Length);
        Assert.AreEqual(0, PixelBlockSnapshot.Capture([]).Blocks.Count);
        Assert.Throws<ArgumentException>(() => PixelBlockSnapshot.Capture(new byte[3]));
    }
}
