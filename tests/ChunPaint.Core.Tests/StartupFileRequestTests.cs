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
}
