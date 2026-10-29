using PPPaint.Core;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class DocumentFileFormatsTests
{
    [TestMethod]
    [DataRow("drawing.pppaint")]
    [DataRow("drawing.chunpaint")]
    [DataRow("drawing.CHUNPAINT")]
    [DataRow("photo.PNG")]
    [DataRow("photo.jpg")]
    [DataRow("photo.jpeg")]
    [DataRow("bitmap.bmp")]
    [DataRow("animation.gif")]
    public void IsSupported_AcceptsDocumentAndImageExtensions(string fileName)
    {
        Assert.IsTrue(DocumentFileFormats.IsSupported(fileName));
    }

    [TestMethod]
    [DataRow("notes.txt")]
    [DataRow("archive.zip")]
    [DataRow("")]
    [DataRow("no-extension")]
    public void IsSupported_RejectsUnsupportedOrMissingExtensions(string fileName)
    {
        Assert.IsFalse(DocumentFileFormats.IsSupported(fileName));
    }

    [TestMethod]
    public void TryGetFormat_ClassifiesAliasesAndProjects()
    {
        Assert.IsTrue(DocumentFileFormats.TryGetFormat("photo.jpeg", out var jpeg));
        Assert.AreEqual(DocumentFileFormat.Jpeg, jpeg);
        Assert.IsTrue(DocumentFileFormats.IsProject("drawing.pppaint"));
        Assert.IsTrue(DocumentFileFormats.IsProject("drawing.chunpaint"));
        Assert.IsFalse(DocumentFileFormats.IsImage("drawing.chunpaint"));
        Assert.IsTrue(DocumentFileFormats.IsImage("drawing.PNG"));
        Assert.IsFalse(DocumentFileFormats.TryGetFormat("drawing.tiff", out _));
    }

    [TestMethod]
    public void HasExpectedImageSignature_RecognizesSupportedHeaders()
    {
        Assert.IsTrue(DocumentFileFormats.HasExpectedImageSignature(DocumentFileFormat.Png, [137, 80, 78, 71, 13, 10, 26, 10]));
        Assert.IsTrue(DocumentFileFormats.HasExpectedImageSignature(DocumentFileFormat.Jpeg, [0xFF, 0xD8, 0xFF]));
        Assert.IsTrue(DocumentFileFormats.HasExpectedImageSignature(DocumentFileFormat.Bmp, [(byte)'B', (byte)'M']));
        Assert.IsTrue(DocumentFileFormats.HasExpectedImageSignature(DocumentFileFormat.Gif, "GIF89a"u8));
        Assert.IsFalse(DocumentFileFormats.HasExpectedImageSignature(DocumentFileFormat.Png, "not an image"u8));
    }

    [TestMethod]
    public void ValidateInputSize_RejectsOversizedFiles()
    {
        Assert.ThrowsExactly<InvalidDataException>(() =>
            DocumentFileFormats.ValidateInputSize(DocumentFileFormats.MaximumInputBytes + 1));
    }
}
