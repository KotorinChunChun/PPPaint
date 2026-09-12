using ChunPaint.Core;

namespace ChunPaint.Core.Tests;

[TestClass]
public sealed class BackgroundMaskOperationsTests
{
    [TestMethod]
    public void Refine_PreservesOriginalSemitransparency()
    {
        var s = new PixelSurface(3, 1, new Rgba32(20, 40, 60, 128));
        var result = BackgroundMaskOperations.Refine(s, s);
        Assert.AreEqual(s[1, 0], result[1, 0]);
    }
    [TestMethod]
    public void Refine_ExpandsContractsAndFeathersRetainedMask()
    {
        var original = new PixelSurface(5, 1, new Rgba32(100, 50, 25));
        var removed = new PixelSurface(5, 1, Rgba32.Transparent); removed[2, 0] = original[2, 0];
        var expanded = BackgroundMaskOperations.Refine(original, removed, expansion: 1);
        Assert.AreEqual((byte)255, expanded[1, 0].A); Assert.AreEqual((byte)0, expanded[0, 0].A);
        var contracted = BackgroundMaskOperations.Refine(original, expanded, expansion: -1);
        Assert.AreEqual((byte)0, contracted[1, 0].A); Assert.AreEqual((byte)255, contracted[2, 0].A);
        var feathered = BackgroundMaskOperations.Refine(original, removed, featherRadius: 1);
        Assert.AreEqual((byte)85, feathered[1, 0].A); Assert.AreEqual((byte)100, feathered[1, 0].R);
    }
    [TestMethod]
    public void Retouch_RestoresOriginalAndDoesNotTouchOutsideMask()
    {
        var original = new PixelSurface(3, 1, new Rgba32(12, 34, 56, 78));
        var removed = new PixelSurface(3, 1, Rgba32.Transparent);
        var region = SelectionRegion.CreateRectangle(new PixelRect(1, 0, 1, 1));
        var restored = BackgroundMaskOperations.Retouch(original, removed, region, true);
        Assert.AreEqual(original[1, 0], restored[1, 0]); Assert.AreEqual(removed[0, 0], restored[0, 0]);
        Assert.AreEqual((byte)0, BackgroundMaskOperations.Retouch(original, restored, region, false)[1, 0].A);
        Assert.AreEqual((byte)0, removed[1, 0].A);
    }
    [TestMethod]
    public void Refine_RejectsMismatchAndCancellation()
    {
        var original = new PixelSurface(1, 1, Rgba32.White);
        Assert.ThrowsExactly<ArgumentException>(() => BackgroundMaskOperations.Refine(original, new PixelSurface(2, 1, Rgba32.White)));
        Assert.ThrowsExactly<OperationCanceledException>(() => BackgroundMaskOperations.Refine(original, original, cancellationToken: new CancellationToken(true)));
    }
}
