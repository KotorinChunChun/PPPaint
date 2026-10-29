using System.Text;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class ReleaseContractTests
{
    [TestMethod]
    public void ReleaseMetadata_DeclaresIndependentBrandAndVersion()
    {
        var root = FindProjectRoot();
        var project = Read(root, "src", "PPPaint", "PPPaint.csproj");
        var manifest = Read(root, "src", "PPPaint", "app.manifest");
        var xaml = Read(root, "src", "PPPaint", "MainWindow.xaml");
        var icoPath = Path.Combine(root, "src", "PPPaint", "Assets", "PPPaintIcon.ico");
        var cursorModulePath = Path.Combine(root, "src", "PPPaint", "Assets", "PPPaintCursors.dll");

        StringAssert.Contains(project, "<AssemblyTitle>PPPaint</AssemblyTitle>");
        StringAssert.Contains(project, "<Product>PPPaint</Product>");
        StringAssert.Contains(project, "<Version>0.2.0</Version>");
        StringAssert.Contains(project, "<AssemblyVersion>0.2.0.0</AssemblyVersion>");
        StringAssert.Contains(project, "<FileVersion>0.2.0.0</FileVersion>");
        StringAssert.Contains(project, "<ApplicationDisplayName>PPPaint</ApplicationDisplayName>");
        StringAssert.Contains(project, "<ApplicationPublisher>PPPaint contributors</ApplicationPublisher>");
        StringAssert.Contains(project, "<ApplicationIconSource>Assets\\PPPaintIcon.svg</ApplicationIconSource>");
        StringAssert.Contains(project, "<ApplicationIcon>Assets\\PPPaintIcon.ico</ApplicationIcon>");
        Assert.IsTrue(File.Exists(icoPath), "EXEへ埋め込むICOアイコンが必要です。");
        Assert.IsTrue(new FileInfo(icoPath).Length > 0, "EXEへ埋め込むICOアイコンが空でないことが必要です。");
        Assert.IsTrue(File.Exists(cursorModulePath), "独自カーソルリソースDLLが必要です。");
        Assert.IsTrue(new FileInfo(cursorModulePath).Length > 0, "独自カーソルリソースDLLが空でないことが必要です。");
        StringAssert.Contains(project, "<WindowsPackageType>None</WindowsPackageType>");
        StringAssert.Contains(manifest, "version=\"0.2.0.0\"");
        StringAssert.Contains(manifest, "requestedExecutionLevel level=\"asInvoker\"");
        StringAssert.Contains(xaml, "Assets/PPPaintIcon.svg");
    }

    [TestMethod]
    public void ReleaseValidation_CoversArtifactArchitectureAndSmokeChecks()
    {
        var root = FindProjectRoot();
        var script = Read(root, "dev", "release", "Invoke-ReleaseValidation.ps1");

        StringAssert.Contains(script, "Copy-Item");
        StringAssert.Contains(script, "staged self-contained win-x64 build");
        StringAssert.Contains(script, "win-x64");
        StringAssert.Contains(script, "PPPaint.exe");
        StringAssert.Contains(script, "PPPaintIcon.svg");
        StringAssert.Contains(script, "PPPaintIcon.ico");
        StringAssert.Contains(script, "PPPaintCursors.dll");
        StringAssert.Contains(script, "App.xbf");
        StringAssert.Contains(script, "MainWindow.xbf");
        StringAssert.Contains(script, "PPPaint.pri");
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
    public void ReleaseValidation_IgnoresGeneratedTreesAndHandlesEmptyTextFiles()
    {
        var root = FindProjectRoot();
        var script = Read(root, "dev", "release", "Invoke-ReleaseValidation.ps1");

        StringAssert.Contains(script, "\\(bin|obj|artifacts)\\");
        StringAssert.Contains(script, "$text = [string](Get-Content");
    }

    [TestMethod]
    public void UserInstaller_RegistersProjectAssociationProtocolAndStartupFilePath()
    {
        var root = FindProjectRoot();
        var installer = Read(root, "dev", "installer", "Install-PPPaint.ps1");
        var uninstaller = Read(root, "dev", "installer", "Uninstall-PPPaint.ps1");
        var release = Read(root, "dev", "release", "Invoke-ReleaseValidation.ps1");
        var app = Read(root, "src", "PPPaint", "App.xaml.cs");
        var launchOptions = Read(root, "src", "PPPaint.Core", "LaunchOptions.cs");
        var loading = Read(root, "src", "PPPaint", "MainWindow.DragDrop.cs");

        StringAssert.Contains(installer, "SupportsShouldProcess");
        StringAssert.Contains(installer, "HKCU:\\Software\\Classes");
        StringAssert.Contains(installer, "'.pppaint'");
        StringAssert.Contains(installer, "'PPPaint.Project'");
        StringAssert.Contains(installer, "'URL Protocol'");
        StringAssert.Contains(installer, "'URL:PPPaint Protocol'");
        StringAssert.Contains(installer, "Programs\\PPPaint");
        StringAssert.Contains(installer, "'\"{0}\" \"%1\"'");
        StringAssert.Contains(uninstaller, "'HKCU:\\Software\\Classes\\pppaint'");
        StringAssert.Contains(uninstaller, "Resolve-SafeInstallDirectory");
        StringAssert.Contains(uninstaller, "Remove-Item -LiteralPath $installPath -Recurse -Force");
        StringAssert.Contains(release, "Install-PPPaint.ps1");
        StringAssert.Contains(release, "Uninstall-PPPaint.ps1");
        StringAssert.Contains(release, "Test-InstallerWhatIf");
        StringAssert.Contains(app, "LaunchOptions.Parse");
        StringAssert.Contains(app, "Options.FilePath");
        StringAssert.Contains(launchOptions, "StartupFileRequest.FindSupportedPath");
        StringAssert.Contains(loading, "OpenStartupFileAsync");
    }

    [TestMethod]
    public void ReleaseValidation_RequiresOnlyFinalRibbonAssets()
    {
        var root = FindProjectRoot();
        var script = Read(root, "dev", "release", "Invoke-ReleaseValidation.ps1");
        var xaml = Read(root, "src", "PPPaint", "MainWindow.xaml");
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
        var xaml = Read(root, "src", "PPPaint", "MainWindow.xaml");
        var projectFiles = Read(root, "src", "PPPaint", "MainWindow.ProjectFiles.cs");
        var mainWindow = Read(root, "src", "PPPaint", "MainWindow.xaml.cs");
        var dragDrop = Read(root, "src", "PPPaint", "MainWindow.DragDrop.cs");
        var codec = Read(root, "src", "PPPaint.Imaging", "Win2DImageCodec.cs");
        var export = Read(root, "src", "PPPaint", "MainWindow.Export.cs");

        foreach (var formatTag in new[] { "Tag=\"png\"", "Tag=\"jpg\"", "Tag=\"bmp\"", "Tag=\"gif\"", "Tag=\"pppaint\"" })
            StringAssert.Contains(xaml, formatTag);
        StringAssert.Contains(projectFiles, "PPPaintProjectArchive.Save");
        StringAssert.Contains(projectFiles, "PPPaintProjectArchive.Load");
        StringAssert.Contains(mainWindow, "var encoded = await PrepareExportAsync(composite, format)");
        StringAssert.Contains(mainWindow, "if (encoded is null) return false");
        StringAssert.Contains(mainWindow, "DocumentFileWriter.WriteAsync(file, async stream =>");
        StringAssert.Contains(mainWindow, "output.WriteAsync(encoded)");
        StringAssert.Contains(export, "Win2DImageCodec.SaveWithOptionsAsync(bitmap, stream, format");
        StringAssert.Contains(export, "stream.AsStreamForRead().CopyToAsync(bytes, cancellation.Token)");
        StringAssert.Contains(export, "encoded = bytes.ToArray()");
        StringAssert.Contains(export, "ContentDialogResult.Primary ? encoded : null");
        Assert.IsTrue(mainWindow.IndexOf("PrepareExportAsync(composite, format)", StringComparison.Ordinal) <
            mainWindow.IndexOf("output.WriteAsync(encoded)", StringComparison.Ordinal),
            "利用者が確認したエンコード結果をトランザクション経由で保存してください。");
        StringAssert.Contains(dragDrop, "Win2DImageCodec.LoadAsync");
        StringAssert.Contains(codec, "CanvasBitmapFileFormat.Png");
        StringAssert.Contains(codec, "CanvasBitmapFileFormat.Jpeg");
        StringAssert.Contains(codec, "CanvasBitmapFileFormat.Bmp");
        StringAssert.Contains(codec, "CanvasBitmapFileFormat.Gif");
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
            if (File.Exists(Path.Combine(directory.FullName, "PPPaint.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the project root.");
    }
}
