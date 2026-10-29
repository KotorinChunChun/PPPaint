using System.Numerics;
using PPPaint.Core;
namespace PPPaint.Core.Tests;

[TestClass]
public sealed class SelectionEditingTests
{
    [TestMethod]
    public void Ellipse_HasSoftEdgeAndExcludesCorners()
    {
        var selection = SelectionEditing.Ellipse(12, 12, new PixelRect(2, 2, 8, 8));
        Assert.AreEqual((byte)255, selection.GetCoverage(6, 6));
        Assert.AreEqual((byte)0, selection.GetCoverage(2, 2));
        Assert.IsTrue(Enumerable.Range(0, 144).Any(i => selection.GetCoverage(i % 12, i / 12) is > 0 and < 255));
    }
    [TestMethod]
    public void MagicWand_SeparatesContiguousFromGlobalAndRespectsTolerance()
    {
        var source = new PixelSurface(3, 1, new Rgba32(20, 20, 20)); source[1, 0] = new(200, 200, 200); source[2, 0] = new(24, 24, 24);
        Assert.AreEqual((byte)0, SelectionEditing.MagicWand(source, 0, 0, 5, true).GetCoverage(2, 0));
        Assert.AreEqual((byte)255, SelectionEditing.MagicWand(source, 0, 0, 5, false).GetCoverage(2, 0));
        Assert.AreEqual((byte)0, SelectionEditing.MagicWand(source, 0, 0, 2, false).GetCoverage(2, 0));
    }
    [TestMethod]
    public void Coverage_CombineAndFeatherPreserveFractionalWeights()
    {
        var a = SelectionRegion.CreateCoverageMask(3, 1, new byte[] { 255, 128, 0 });
        var b = SelectionRegion.CreateCoverageMask(3, 1, new byte[] { 0, 64, 255 });
        Assert.AreEqual((byte)128, SelectionEditing.Combine(a, b, 3, 1, SelectionCombineMode.Add).GetCoverage(1, 0));
        Assert.AreEqual((byte)64, SelectionEditing.Combine(a, b, 3, 1, SelectionCombineMode.Subtract).GetCoverage(1, 0));
        Assert.AreEqual((byte)64, SelectionEditing.Combine(a, b, 3, 1, SelectionCombineMode.Replace).GetCoverage(1, 0));
        var feather = SelectionEditing.Feather(SelectionRegion.CreateRectangle(new PixelRect(1, 0, 1, 1)), 3, 1, 1);
        Assert.AreEqual((byte)85, feather.GetCoverage(1, 0)); Assert.IsTrue(feather.GetCoverage(0, 0) > 0);
    }
    [TestMethod]
    public void CoverageMask_CopiesInputAndTransformsWithoutThresholding()
    {
        byte[] mask = [0, 0, 0, 0, 0, 128, 255, 0, 0, 64, 128, 0, 0, 0, 0, 0];
        var region = SelectionRegion.CreateCoverageMask(4, 4, mask); mask[5] = 0;
        Assert.AreEqual((byte)128, region.GetCoverage(1, 1)); Assert.AreEqual((byte)0, region.GetCoverage(-1, 0));
        var moved = region.MoveBy(new Vector2(1, 0), new PixelRect(0, 0, 4, 4));
        Assert.AreEqual((byte)128, moved.GetCoverage(2, 1)); Assert.AreEqual((byte)0, moved.GetCoverage(1, 1));
        var rotated = region.Rotate(90, new PixelRect(0, 0, 4, 4));
        Assert.AreEqual((byte)128, rotated.GetCoverage(2, 1));
        Assert.IsTrue(region.Resize(SelectionHandle.BottomRight, new Vector2(1, 1), new PixelRect(0, 0, 4, 4)).GetCoverage(2, 2) is > 0 and < 255);
        Assert.AreEqual(region.GetCoverage(1, 1), region.Skew(0, true, new PixelRect(0, 0, 4, 4)).GetCoverage(1, 1));
    }
    [TestMethod]
    public void SelectionEditing_RejectsUnsafeDimensionsAndCoordinates()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SelectionEditing.Ellipse(100000, 100000, new PixelRect(0, 0, 1, 1)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SelectionEditing.MagicWand(new PixelSurface(1, 1, Rgba32.White), -1, 0, 1, true));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SelectionEditing.Feather(SelectionRegion.CreateRectangle(new PixelRect(0, 0, 1, 1)), 1, 1, -1));
    }
}

[TestClass]
public sealed class RetouchOperationsTests
{
    [TestMethod]
    public void Clone_InterpolatesPathAndLeavesOriginalInputsUntouched()
    {
        var source = new PixelSurface(12, 3, new Rgba32(240, 20, 10, 128)); var target = new PixelSurface(12, 3, Rgba32.Transparent);
        var result = RetouchOperations.Stroke(source, target, Vector2.Zero, Vector2.Zero, [new(.5f, 1.5f), new(10.5f, 1.5f)], 1, 1, 1, false);
        Assert.AreEqual(source[5, 1], result[5, 1]); Assert.AreEqual(Rgba32.Transparent, target[5, 1]);
        Assert.AreEqual(new Rgba32(240, 20, 10, 128), source[5, 1]);
    }
    [TestMethod]
    public void Clone_AppliesSoftSelectionAndOpacityWithoutRepeatedDabBuildup()
    {
        var source = new PixelSurface(3, 3, new Rgba32(255, 0, 0)); var target = new PixelSurface(3, 3, new Rgba32(0, 0, 0));
        var mask = SelectionRegion.CreateCoverageMask(3, 3, new byte[] { 0, 0, 0, 0, 128, 0, 0, 0, 0 });
        var result = RetouchOperations.Stroke(source, target, Vector2.Zero, Vector2.Zero, [new(1.5f, 1.5f), new(1.5f, 1.5f)], 2, 1, .5f, false, mask);
        Assert.AreEqual((byte)64, result[1, 1].R); Assert.AreEqual(target[0, 0], result[0, 0]);
    }
    [TestMethod]
    public void Heal_AdjustsNeighborhoodColorInsteadOfCloningIt()
    {
        var source = new PixelSurface(9, 9, new Rgba32(100, 100, 100)); var target = new PixelSurface(9, 9, new Rgba32(180, 180, 180));
        source[4, 4] = new(120, 120, 120);
        var clone = RetouchOperations.Stroke(source, target, new(4, 4), new(4, 4), [new(4.5f, 4.5f)], 1, 1, 1, false);
        var heal = RetouchOperations.Stroke(source, target, new(4, 4), new(4, 4), [new(4.5f, 4.5f)], 1, 1, 1, true);
        Assert.AreEqual((byte)120, clone[4, 4].R); Assert.AreEqual((byte)200, heal[4, 4].R);
    }
    [TestMethod]
    public void Retouch_RejectsInvalidInputAndKeepsOutOfSourcePixels()
    {
        var surface = new PixelSurface(3, 3, Rgba32.White);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RetouchOperations.Stroke(surface, surface, Vector2.Zero, Vector2.Zero, [], float.NaN, 1, 1, false));
        var result = RetouchOperations.Stroke(surface, surface, new(-100, 0), Vector2.Zero, [new(1, 1)], 2, 0, 1, false);
        Assert.AreEqual(surface[1, 1], result[1, 1]);
    }
}
