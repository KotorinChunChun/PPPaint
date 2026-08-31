using System.Text;

namespace ChunPaint.Core.Tests;

[TestClass]
public sealed class ReleaseContractTests
{
    [TestMethod]
    public void ReleaseMetadata_DeclaresIndependentBrandAndVersion()
    {
        var root = FindProjectRoot();
        var project = Read(root, "src", "ChunPaint", "ChunPaint.csproj");
        var manifest = Read(root, "src", "ChunPaint", "app.manifest");
        var xaml = Read(root, "src", "ChunPaint", "MainWindow.xaml");

        StringAssert.Contains(project, "<AssemblyTitle>ChunPaint</AssemblyTitle>");
        StringAssert.Contains(project, "<Product>ChunPaint</Product>");
        StringAssert.Contains(project, "<Version>0.1.0</Version>");
        StringAssert.Contains(project, "<AssemblyVersion>0.1.0.0</AssemblyVersion>");
        StringAssert.Contains(project, "<FileVersion>0.1.0.0</FileVersion>");
        StringAssert.Contains(project, "<ApplicationDisplayName>ChunPaint</ApplicationDisplayName>");
        StringAssert.Contains(project, "<ApplicationPublisher>ChunPaint contributors</ApplicationPublisher>");
        StringAssert.Contains(project, "<ApplicationIconSource>Assets\\ChunPaintIcon.svg</ApplicationIconSource>");
        StringAssert.Contains(project, "<WindowsPackageType>None</WindowsPackageType>");
        StringAssert.Contains(manifest, "version=\"0.1.0.0\"");
        StringAssert.Contains(manifest, "requestedExecutionLevel level=\"asInvoker\"");
        StringAssert.Contains(xaml, "Assets/ChunPaintIcon.svg");
    }

    [TestMethod]
    public void ReleaseValidation_CoversArtifactArchitectureAndSmokeChecks()
    {
        var root = FindProjectRoot();
        var script = Read(root, "dev", "release", "Invoke-ReleaseValidation.ps1");

        StringAssert.Contains(script, "Copy-Item");
        StringAssert.Contains(script, "staged self-contained win-x64 build");
        StringAssert.Contains(script, "win-x64");
        StringAssert.Contains(script, "ChunPaint.exe");
        StringAssert.Contains(script, "ChunPaintIcon.svg");
        StringAssert.Contains(script, "App.xbf");
        StringAssert.Contains(script, "MainWindow.xbf");
        StringAssert.Contains(script, "ChunPaint.pri");
        StringAssert.Contains(script, "coreclr.dll");
        StringAssert.Contains(script, "hostfxr.dll");
        StringAssert.Contains(script, "Amd64");
        StringAssert.Contains(script, "FileVersionInfo");
        StringAssert.Contains(script, "forbiddenPattern");
        StringAssert.Contains(script, "SmokeLaunch");
        StringAssert.Contains(script, "Invoke-StartupSmoke");
        StringAssert.Contains(script, "requiredRibbonIcons");
        StringAssert.Contains(script, "Assets\\RibbonIcons\\final");
        StringAssert.Contains(script, "Temporary ribbon assets remain in the Release artifact");
        StringAssert.Contains(script, "icon-provenance.md");
    }

    [TestMethod]
    public void ReleaseValidation_RequiresOnlyFinalRibbonAssets()
    {
        var root = FindProjectRoot();
        var script = Read(root, "dev", "release", "Invoke-ReleaseValidation.ps1");
        var xaml = Read(root, "src", "ChunPaint", "MainWindow.xaml");
        var provenance = Read(root, "docs", "icon-provenance.md");

        foreach (var icon in new[] { "resize.png", "eraser.png", "fill.png", "brush.png" })
        {
            StringAssert.Contains(xaml, $"Assets/RibbonIcons/final/{icon}");
            StringAssert.Contains(provenance, $"Assets/RibbonIcons/final/{icon}");
        }
        Assert.IsFalse(xaml.Contains("RibbonIcons/temporary", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(provenance.Contains("| temporary |", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(script, "temporaryRibbonIconPath");
        StringAssert.Contains(script, "publishedTemporaryIconPath");
    }

    [TestMethod]
    public void ReleaseSurface_DeclaresImageAndProjectSmokePaths()
    {
        var root = FindProjectRoot();
        var xaml = Read(root, "src", "ChunPaint", "MainWindow.xaml");
        var projectFiles = Read(root, "src", "ChunPaint", "MainWindow.ProjectFiles.cs");
        var mainWindow = Read(root, "src", "ChunPaint", "MainWindow.xaml.cs");

        foreach (var formatTag in new[] { "Tag=\"png\"", "Tag=\"jpg\"", "Tag=\"bmp\"", "Tag=\"gif\"", "Tag=\"chunpaint\"" })
            StringAssert.Contains(xaml, formatTag);
        StringAssert.Contains(projectFiles, "ChunPaintProjectArchive.Save");
        StringAssert.Contains(projectFiles, "ChunPaintProjectArchive.Load");
        StringAssert.Contains(mainWindow, "CanvasBitmapFileFormat.Png");
        StringAssert.Contains(mainWindow, "CanvasBitmapFileFormat.Jpeg");
        StringAssert.Contains(mainWindow, "CanvasBitmapFileFormat.Bmp");
        StringAssert.Contains(mainWindow, "CanvasBitmapFileFormat.Gif");
    }

    [TestMethod]
    public void ReleaseDocumentation_ExplainsNonMsixPublisherAndPrivacyBoundaries()
    {
        var root = FindProjectRoot();
        var license = Read(root, "LICENSE");
        var privacy = Read(root, "PRIVACY.md");
        var changelog = Read(root, "CHANGELOG.md");
        var notes = Read(root, "docs", "v0.1.0-release-notes.md");

        StringAssert.Contains(license, "MIT License");
        StringAssert.Contains(privacy, "外部AI");
        StringAssert.Contains(privacy, "asInvoker");
        StringAssert.Contains(changelog, "[0.1.0]");
        StringAssert.Contains(notes, "WindowsPackageType=None");
        StringAssert.Contains(notes, "署名証明書");
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine([root, .. parts]), Encoding.UTF8);

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ChunPaint.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the project root.");
    }
}
