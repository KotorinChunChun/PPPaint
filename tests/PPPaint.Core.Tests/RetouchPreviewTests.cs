using System.Numerics;
using PPPaint.Core;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class RetouchPreviewTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void StrokePreservesSourceAndTarget(bool heal)
    {
        var source = new PixelSurface(8, 4, new Rgba32(220, 80, 30));
        var target = new PixelSurface(8, 4, new Rgba32(10, 30, 90));
        source[2, 1] = Rgba32.White;
        var sourceBefore = source.Snapshot();
        var targetBefore = target.Snapshot();

        var result = RetouchOperations.Stroke(source, target, new(2, 1), new(4, 1), [new(4.5f, 1.5f)], 2, 1, .5f, heal);

        CollectionAssert.AreEqual(sourceBefore, source.Snapshot());
        CollectionAssert.AreEqual(targetBefore, target.Snapshot());
        Assert.AreNotSame(target, result);
        Assert.AreNotEqual(target[4, 1], result[4, 1]);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PreviewCountDoesNotAccumulateOpacity(bool heal)
    {
        var original = new PixelSurface(12, 4, new Rgba32(20, 50, 80));
        for (var y = 0; y < original.Height; y++)
        for (var x = 0; x < 4; x++) original[x, y] = new Rgba32(220, 160, 100);
        Vector2[] points = [new(6.5f, 1.5f), new(7.5f, 1.5f), new(8.5f, 1.5f)];
        var before = original.Snapshot();
        var direct = RetouchOperations.Stroke(original, original, new(1.5f, 1.5f), points[0], points, 1.5f, 1, .25f, heal);
        PixelSurface? preview = null;
        for (var count = 1; count <= points.Length; count++)
        {
            preview = RetouchOperations.Stroke(original, original, new(1.5f, 1.5f), points[0], points[..count], 1.5f, 1, .25f, heal);
        }

        CollectionAssert.AreEqual(direct.Snapshot(), preview!.Snapshot());
        CollectionAssert.AreEqual(before, original.Snapshot());
    }

    [TestMethod]
    public void RepeatedDabsRespectOpacityAndSelection()
    {
        var source = new PixelSurface(3, 1, Rgba32.White);
        var target = new PixelSurface(3, 1, Rgba32.Black);
        var point = new Vector2(1.5f, .5f);
        var result = RetouchOperations.Stroke(source, target, point, point, [point, point, point], 2, 1, .5f, false,
            SelectionRegion.CreateCoverageMask(3, 1, [0, 128, 255]));

        Assert.AreEqual(Rgba32.Black, result[0, 0]);
        Assert.AreEqual(new Rgba32(64, 64, 64), result[1, 0]);
        Assert.AreEqual(new Rgba32(128, 128, 128), result[2, 0]);
    }

    [TestMethod]
    public void StrokeRejectsNonFiniteAndExcessiveInput()
    {
        var source = new PixelSurface(1, 1, Rgba32.Black);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RetouchOperations.Stroke(source, source, Vector2.Zero, Vector2.Zero, [new(float.NaN, 0)], 1, 1, 1, false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RetouchOperations.Stroke(source, source, Vector2.Zero, Vector2.Zero, [Vector2.Zero], 513, 1, 1, false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RetouchOperations.Stroke(source, source, Vector2.Zero, Vector2.Zero, [Vector2.Zero, new(float.MaxValue, 0)], 1, 1, 1, false));
    }
}
