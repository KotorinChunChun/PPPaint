using ChunPaint.Core;

namespace ChunPaint.Core.Tests;

[TestClass]
public sealed class BatchImagePlanTests
{
    [TestMethod]
    public void GetSize_FitsAspectInsideBounds()
    {
        Assert.AreEqual(new BatchImageSize(800, 400), BatchImagePlan.GetSize(2000, 1000, 800, 600, true));
        Assert.AreEqual(new BatchImageSize(300, 600), BatchImagePlan.GetSize(1000, 2000, 800, 600, true));
        Assert.AreEqual(new BatchImageSize(800, 600), BatchImagePlan.GetSize(1000, 2000, 800, 600, false));
    }
    [TestMethod]
    public void GetSize_RejectsUnsafeDimensions()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => BatchImagePlan.GetSize(10, 10, 16384, 16384, false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => BatchImagePlan.GetSize(0, 10, 100, 100, true));
    }
    [TestMethod]
    public void OutputName_DoesNotOverwriteOrTraverseDirectories()
    {
        Assert.AreEqual("photo_converted_2.webp", BatchImagePlan.GetOutputName("photo.png", DocumentFileFormat.Webp, ["PHOTO_CONVERTED.WEBP", "photo_converted_1.webp"]));
        Assert.AreEqual("photo_converted.png", BatchImagePlan.GetOutputName("../../photo.png", DocumentFileFormat.Png, []));
        Assert.AreEqual("CON_converted.jpg", BatchImagePlan.GetOutputName("CON.png", DocumentFileFormat.Jpeg, []));
    }
}
