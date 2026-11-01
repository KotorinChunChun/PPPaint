using PPPaint.Core;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class ReliabilityRegressionTests
{
    [TestMethod]
    public void PixelSurface_RejectsOversizedAllocationBeforeCreatingArray()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new PixelSurface(8193, 8192, Rgba32.Transparent));
    }

    [TestMethod]
    public void DocumentFileFormats_RejectsEmptyAndNegativeInputs()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => DocumentFileFormats.ValidateInputSize(0));
        Assert.ThrowsExactly<InvalidDataException>(() => DocumentFileFormats.ValidateInputSize(-1));
        Assert.IsFalse(DocumentFileFormats.TryGetFormat(null!, out _));
    }

    [TestMethod]
    public void SupportedImageHeaders_AreCheckedAsSmallRealFileInputs()
    {
        var samples = new (DocumentFileFormat Format, byte[] Header)[]
        {
            (DocumentFileFormat.Png, [137, 80, 78, 71, 13, 10, 26, 10]),
            (DocumentFileFormat.Jpeg, [0xFF, 0xD8, 0xFF, 0xE0]),
            (DocumentFileFormat.Bmp, [(byte)'B', (byte)'M', 0, 0]),
            (DocumentFileFormat.Gif, "GIF89a"u8.ToArray())
        };

        foreach (var sample in samples)
        {
            using var stream = new MemoryStream(sample.Header);
            DocumentFileFormats.ValidateInputSize(stream.Length);
            Assert.IsTrue(DocumentFileFormats.HasExpectedImageSignature(sample.Format, sample.Header));
        }
    }
}
