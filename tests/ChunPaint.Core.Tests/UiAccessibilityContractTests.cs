using System.Text;

namespace ChunPaint.Core.Tests;

[TestClass]
public sealed class UiAccessibilityContractTests
{
    [TestMethod]
    public void MainWindow_UsesProjectBrandingAndAccessibleRegions()
    {
        var root = FindProjectRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "ChunPaint", "MainWindow.xaml"), Encoding.UTF8);
        var codeBehind = File.ReadAllText(Path.Combine(root, "src", "ChunPaint", "MainWindow.xaml.cs"), Encoding.UTF8);
        var forbiddenExternalIconToken = "ms" + "paint";

        StringAssert.Contains(xaml, "ChunPaintIcon.svg");
        StringAssert.Contains(xaml, "AutomationProperties.Name");
        StringAssert.Contains(xaml, "AutomationProperties.HelpText");
        StringAssert.Contains(xaml, "UseLayoutRounding=\"True\"");
        StringAssert.Contains(xaml, "ThemeResource");
        StringAssert.Contains(xaml, "MinHeight=\"136\"");
        Assert.IsFalse(codeBehind.Contains(forbiddenExternalIconToken, StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void AppManifest_DeclaresPerMonitorDpiWithoutExternalBranding()
    {
        var root = FindProjectRoot();
        var manifest = File.ReadAllText(Path.Combine(root, "src", "ChunPaint", "app.manifest"), Encoding.UTF8);
        var project = File.ReadAllText(Path.Combine(root, "src", "ChunPaint", "ChunPaint.csproj"), Encoding.UTF8);
        var forbiddenExternalIconToken = "ms" + "paint";

        StringAssert.Contains(manifest, "PerMonitorV2");
        Assert.IsFalse(manifest.Contains(forbiddenExternalIconToken, StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(project.Contains(forbiddenExternalIconToken, StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(project, "Assets\\ChunPaintIcon.svg");
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "ChunPaint", "ChunPaint.slnx")) ||
                Directory.Exists(Path.Combine(directory.FullName, "src", "ChunPaint")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        Assert.Fail("ChunPaint project root was not found from the test output directory.");
        return string.Empty;
    }
}
