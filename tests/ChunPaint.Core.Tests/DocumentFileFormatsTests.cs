using ChunPaint.Core;

namespace ChunPaint.Core.Tests;

[TestClass]
public sealed class DocumentFileFormatsTests
{
    [TestMethod]
    [DataRow("drawing.chunpaint")]
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
}
