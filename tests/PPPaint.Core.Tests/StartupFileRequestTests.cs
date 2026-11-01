
namespace PPPaint.Core.Tests;

[TestClass]
public sealed class StartupFileRequestTests
{
    [TestMethod]
    public void FindSupportedPath_ReturnsAbsoluteImagePath()
    {
        var relative = Path.Combine("samples", "drawing.webp");

        var result = StartupFileRequest.FindSupportedPath(["PPPaint.exe", relative]);

        Assert.AreEqual(Path.GetFullPath(relative), result);
    }

    [TestMethod]
    public void FindSupportedPath_SkipsOptionsAndUnsupportedArguments()
    {
        var image = Path.Combine("samples", "image.PNG");

        var result = StartupFileRequest.FindSupportedPath(["PPPaint.exe", "-SmokeLaunch", "notes.txt", image]);

        Assert.AreEqual(Path.GetFullPath(image), result);
    }

    [TestMethod]
    public void FindSupportedPath_ReturnsNullWithoutSupportedFile()
    {
        Assert.IsNull(StartupFileRequest.FindSupportedPath(null));
        Assert.IsNull(StartupFileRequest.FindSupportedPath(["PPPaint.exe"]));
        Assert.IsNull(StartupFileRequest.FindSupportedPath(["PPPaint.exe", "notes.txt"]));
    }

    [TestMethod]
    [DataRow("drawing.pppaint")]
    [DataRow("drawing.chunpaint")]
    public void FindSupportedPath_RejectsRetiredProjectFormats(string fileName)
    {
        // The editor opens images only; accepting a project file would end in a load error.
        Assert.IsNull(StartupFileRequest.FindSupportedPath(["PPPaint.exe", Path.Combine("samples", fileName)]));
        var request = $"pppaint://open?path={Uri.EscapeDataString(Path.GetFullPath(fileName))}";
        Assert.IsFalse(StartupFileRequest.TryGetProtocolPath(request, out _));
    }

    [TestMethod]
    public void FindSupportedPath_ResolvesEncodedCustomProtocolPath()
    {
        var imagePath = Path.GetFullPath(Path.Combine("samples", "protocol drawing.png"));
        var request = $"pppaint://open?path={Uri.EscapeDataString(imagePath)}";

        var result = StartupFileRequest.FindSupportedPath(["PPPaint.exe", request]);

        Assert.AreEqual(imagePath, result);
    }

    [TestMethod]
    [DataRow("pppaint", ".png")]
    [DataRow("pppaint", ".JPG")]
    [DataRow("chunpaint", ".gif")]
    [DataRow("CHUNPAINT", ".webp")]
    public void TryGetProtocolPath_AcceptsNewAndLegacyImageLinks(string scheme, string extension)
    {
        var imagePath = Path.GetFullPath(Path.Combine("samples", "旧画像" + extension));
        Assert.IsTrue(StartupFileRequest.TryGetProtocolPath($"{scheme}://open?path={Uri.EscapeDataString(imagePath)}", out var result));
        Assert.AreEqual(imagePath, result);
    }

    [TestMethod]
    [DataRow("pppaint://edit?path=C%3A%5Csample.png")]
    [DataRow("pppaint://open/extra?path=C%3A%5Csample.png")]
    [DataRow("pppaint://open?path=relative.png")]
    [DataRow("pppaint://open?path=C%3A%5Csample.txt")]
    [DataRow("pppaint://open?path=C%3A%5Csample.png&mode=edit")]
    [DataRow("https://open?path=C%3A%5Csample.png")]
    public void TryGetProtocolPath_RejectsUnsafeOrUnsupportedRequests(string request)
    {
        Assert.IsFalse(StartupFileRequest.TryGetProtocolPath(request, out var path));
        Assert.AreEqual(string.Empty, path);
    }
}
