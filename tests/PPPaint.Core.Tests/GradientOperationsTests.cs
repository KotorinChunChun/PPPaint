using System.Numerics;
using PPPaint.Core;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class GradientOperationsTests
{
    [TestMethod]
    public void LinearGradientClampsAndPreservesInput()
    {
        var source = new PixelSurface(5, 1, Rgba32.Transparent);
        var before = source.Snapshot();
        var result = GradientOperations.Apply(source, new(1, 0), new(3, 0), Rgba32.Black, Rgba32.White, false);
        Assert.AreEqual(Rgba32.Black, result[0, 0]);
        Assert.AreEqual(new Rgba32(128, 128, 128), result[2, 0]);
        Assert.AreEqual(Rgba32.White, result[4, 0]);
        CollectionAssert.AreEqual(before, source.Snapshot());
    }

    [TestMethod]
    public void TransparentEndpointDoesNotLeakHiddenBlueIntoRed()
    {
        var result = GradientOperations.Apply(new PixelSurface(3, 1, Rgba32.Transparent), Vector2.Zero, new(2, 0),
            new Rgba32(255, 0, 0), new Rgba32(0, 0, 255, 0), false);
        Assert.AreEqual(new Rgba32(255, 0, 0, 128), result[1, 0]);
        Assert.AreEqual(Rgba32.Transparent, result[2, 0]);
    }

    [TestMethod]
    public void SourceOverKeepsOpaqueBackgroundAndTransparentGradientIsNoOp()
    {
        var source = new PixelSurface(3, 1, new Rgba32(0, 0, 255));
        var result = GradientOperations.Apply(source, Vector2.Zero, new(2, 0), new(255, 0, 0), Rgba32.Transparent, false);
        Assert.AreEqual(new Rgba32(128, 0, 128), result[1, 0]);
        var unchanged = GradientOperations.Apply(source, Vector2.Zero, Vector2.One, Rgba32.Transparent, new Rgba32(99, 77, 44, 0), true);
        CollectionAssert.AreEqual(source.Snapshot(), unchanged.Snapshot());
    }

    [TestMethod]
    public void RadialIsSymmetricAndSelectionLimitsWrites()
    {
        var source = new PixelSurface(3, 3, new Rgba32(10, 20, 30));
        var result = GradientOperations.Apply(source, new(1, 1), new(2, 1), Rgba32.Black, Rgba32.White, true);
        Assert.AreEqual(Rgba32.Black, result[1, 1]);
        Assert.AreEqual(result[0, 1], result[1, 0]);
        Assert.AreEqual(Rgba32.White, result[2, 2]);
        var selected = GradientOperations.Apply(source, Vector2.Zero, Vector2.One, Rgba32.White, Rgba32.White, false,
            SelectionRegion.CreateRectangle(new PixelRect(1, 1, 1, 1)));
        Assert.AreEqual(source[0, 0], selected[0, 0]); Assert.AreEqual(Rgba32.White, selected[1, 1]);
    }

    [TestMethod]
    public void FeatheredSelectionMultipliesGradientAlpha()
    {
        var source = new PixelSurface(1, 1, Rgba32.Transparent);
        var result = GradientOperations.Apply(source, Vector2.Zero, Vector2.One, new Rgba32(255, 0, 0), Rgba32.White, false,
            SelectionRegion.CreateCoverageMask(1, 1, [128]));
        Assert.AreEqual(new Rgba32(255, 0, 0, 128), result[0, 0]);
    }

    [TestMethod]
    public void DegenerateEndpointsInvalidCoordinatesAndCancellationAreRejected()
    {
        var source = new PixelSurface(1, 1, Rgba32.White);
        Assert.ThrowsExactly<ArgumentException>(() => GradientOperations.Apply(source, Vector2.Zero, Vector2.Zero, Rgba32.Black, Rgba32.White, false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => GradientOperations.Apply(source, new(float.NaN, 0), Vector2.One, Rgba32.Black, Rgba32.White, false));
        Assert.ThrowsExactly<OperationCanceledException>(() => GradientOperations.Apply(source, Vector2.Zero, Vector2.One, Rgba32.Black, Rgba32.White, false, token: new CancellationToken(true)));
    }
}
