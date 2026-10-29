using PPPaint.Core;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class StartupFileRequestTests
{
    [TestMethod]
    public void FindSupportedPath_ReturnsAbsoluteProjectPath()
    {
        var relative = Path.Combine("samples", "drawing.pppaint");

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
    public void FindSupportedPath_ResolvesEncodedCustomProtocolPath()
    {
        var projectPath = Path.GetFullPath(Path.Combine("samples", "protocol drawing.pppaint"));
        var request = $"pppaint://open?path={Uri.EscapeDataString(projectPath)}";

        var result = StartupFileRequest.FindSupportedPath(["PPPaint.exe", request]);

        Assert.AreEqual(projectPath, result);
    }

    [TestMethod]
    [DataRow("pppaint", ".pppaint")]
    [DataRow("pppaint", ".chunpaint")]
    [DataRow("chunpaint", ".pppaint")]
    [DataRow("CHUNPAINT", ".chunpaint")]
    public void TryGetProtocolPath_AcceptsNewAndLegacyProjectLinks(string scheme, string extension)
    {
        var projectPath = Path.GetFullPath(Path.Combine("samples", "旧プロジェクト" + extension));
        Assert.IsTrue(StartupFileRequest.TryGetProtocolPath($"{scheme}://open?path={Uri.EscapeDataString(projectPath)}", out var result));
        Assert.AreEqual(projectPath, result);
    }

    [TestMethod]
    [DataRow("pppaint://edit?path=C%3A%5Csample.pppaint")]
    [DataRow("pppaint://open/extra?path=C%3A%5Csample.pppaint")]
    [DataRow("pppaint://open?path=relative.pppaint")]
    [DataRow("pppaint://open?path=C%3A%5Csample.txt")]
    [DataRow("pppaint://open?path=C%3A%5Csample.pppaint&mode=edit")]
    [DataRow("https://open?path=C%3A%5Csample.pppaint")]
    public void TryGetProtocolPath_RejectsUnsafeOrUnsupportedRequests(string request)
    {
        Assert.IsFalse(StartupFileRequest.TryGetProtocolPath(request, out var path));
        Assert.AreEqual(string.Empty, path);
    }
}
