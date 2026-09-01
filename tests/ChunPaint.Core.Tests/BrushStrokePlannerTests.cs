using ChunPaint.Core;

namespace ChunPaint.Core.Tests;

[TestClass]
public sealed class BrushStrokePlannerTests
{
    [TestMethod]
    public void BrushKinds_ContainNineDistinctProfiles()
    {
        var kinds = Enum.GetValues<BrushKind>();
        var profiles = kinds.Select(BrushStrokePlanner.GetProfile).ToArray();

        Assert.HasCount(9, kinds);
        Assert.HasCount(9, profiles.Distinct().ToArray());
        Assert.IsTrue(profiles.All(profile => profile.WidthScale > 0));
        Assert.IsTrue(profiles.All(profile => profile.OpacityScale is > 0 and <= 1));
        Assert.IsTrue(profiles.All(profile => profile.DabsPerSample > 0));
    }

    [TestMethod]
    public void PlanSegment_ReturnsSameDabsForSameInput()
    {
        foreach (var kind in Enum.GetValues<BrushKind>())
        {
            var first = BrushStrokePlanner.PlanSegment(kind, 1.25f, 2.5f, 28.75f, 14.5f, 3f);
            var second = BrushStrokePlanner.PlanSegment(kind, 1.25f, 2.5f, 28.75f, 14.5f, 3f);

            CollectionAssert.AreEqual(first.ToArray(), second.ToArray(), kind.ToString());
        }
    }

    [TestMethod]
    public void PlanSegment_ProducesDistinctPatternsForAllBrushes()
    {
        var signatures = Enum.GetValues<BrushKind>()
            .Select(kind =>
            {
                var profile = BrushStrokePlanner.GetProfile(kind);
                var dabs = BrushStrokePlanner.PlanSegment(kind, 0f, 0f, 24f, 12f, 4f);
                return $"{profile}|{dabs.Count}|{string.Join(';', dabs.Take(4))}";
            })
            .ToArray();

        Assert.HasCount(9, signatures.Distinct().ToArray());
    }

    [TestMethod]
    public void PixelBrush_AlignsEveryDabToWholePixels()
    {
        var dabs = BrushStrokePlanner.PlanSegment(BrushKind.Pixel, 0.25f, 1.75f, 8.8f, 5.2f, 2.5f);

        Assert.IsTrue(dabs.All(dab => dab.X == MathF.Round(dab.X)));
        Assert.IsTrue(dabs.All(dab => dab.Y == MathF.Round(dab.Y)));
        Assert.IsTrue(dabs.All(dab => dab.Shape == BrushDabShape.Square));
    }

    [TestMethod]
    public void PlanSegment_RejectsInvalidInputs()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            BrushStrokePlanner.GetProfile((BrushKind)99));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            BrushStrokePlanner.PlanSegment(BrushKind.Oil, float.NaN, 0, 1, 1, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            BrushStrokePlanner.PlanSegment(BrushKind.Oil, 0, 0, 1, 1, 0));
    }
}
