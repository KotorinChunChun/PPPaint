using ChunPaint.Core;

namespace ChunPaint.Core.Tests;

[TestClass]
public sealed class StartupFileRequestTests
{
    [TestMethod]
    public void FindSupportedPath_ReturnsAbsoluteProjectPath()
    {
        var relative = Path.Combine("samples", "drawing.chunpaint");

        var result = StartupFileRequest.FindSupportedPath(["ChunPaint.exe", relative]);

        Assert.AreEqual(Path.GetFullPath(relative), result);
    }

    [TestMethod]
    public void FindSupportedPath_SkipsOptionsAndUnsupportedArguments()
    {
        var image = Path.Combine("samples", "image.PNG");

        var result = StartupFileRequest.FindSupportedPath(["ChunPaint.exe", "-SmokeLaunch", "notes.txt", image]);

        Assert.AreEqual(Path.GetFullPath(image), result);
    }

    [TestMethod]
    public void FindSupportedPath_ReturnsNullWithoutSupportedFile()
    {
        Assert.IsNull(StartupFileRequest.FindSupportedPath(null));
        Assert.IsNull(StartupFileRequest.FindSupportedPath(["ChunPaint.exe"]));
        Assert.IsNull(StartupFileRequest.FindSupportedPath(["ChunPaint.exe", "notes.txt"]));
    }

    [TestMethod]
    public void FindSupportedPath_ResolvesEncodedCustomProtocolPath()
    {
        var projectPath = Path.GetFullPath(Path.Combine("samples", "protocol drawing.chunpaint"));
        var request = $"chunpaint://open?path={Uri.EscapeDataString(projectPath)}";

        var result = StartupFileRequest.FindSupportedPath(["ChunPaint.exe", request]);

        Assert.AreEqual(projectPath, result);
    }

    [TestMethod]
    [DataRow("chunpaint://edit?path=C%3A%5Csample.chunpaint")]
    [DataRow("chunpaint://open/extra?path=C%3A%5Csample.chunpaint")]
    [DataRow("chunpaint://open?path=relative.chunpaint")]
    [DataRow("chunpaint://open?path=C%3A%5Csample.txt")]
    [DataRow("chunpaint://open?path=C%3A%5Csample.chunpaint&mode=edit")]
    [DataRow("https://open?path=C%3A%5Csample.chunpaint")]
    public void TryGetProtocolPath_RejectsUnsafeOrUnsupportedRequests(string request)
    {
        Assert.IsFalse(StartupFileRequest.TryGetProtocolPath(request, out var path));
        Assert.AreEqual(string.Empty, path);
    }
}
