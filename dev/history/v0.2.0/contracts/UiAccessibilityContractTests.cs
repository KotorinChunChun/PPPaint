using System.Text;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class UiAccessibilityContractTests
{
    [TestMethod]
    public void MainWindow_UsesProjectBrandingAndAccessibleRegions()
    {
        var root = FindProjectRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "PPPaint", "MainWindow.xaml"), Encoding.UTF8);
        var codeBehind = File.ReadAllText(Path.Combine(root, "src", "PPPaint", "MainWindow.xaml.cs"), Encoding.UTF8);
        var forbiddenExternalIconToken = "ms" + "paint";

        StringAssert.Contains(xaml, "PPPaintIcon.svg");
        StringAssert.Contains(xaml, "AutomationProperties.Name");
        StringAssert.Contains(xaml, "AutomationProperties.HelpText");
        StringAssert.Contains(xaml, "UseLayoutRounding=\"True\"");
        StringAssert.Contains(xaml, "ThemeResource");
        StringAssert.Contains(xaml, "MinHeight=\"136\"");
        StringAssert.Contains(xaml, "<ColumnDefinition Width=\"44\" />");
        StringAssert.Contains(xaml, "<Image Grid.Column=\"0\" Source=\"Assets/PPPaintIcon.svg\"");
        StringAssert.Contains(xaml, "<MenuBar Grid.Column=\"1\" Padding=\"0,0,8,0\"");
        Assert.IsFalse(codeBehind.Contains(forbiddenExternalIconToken, StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void AppManifest_DeclaresPerMonitorDpiWithoutExternalBranding()
    {
        var root = FindProjectRoot();
        var manifest = File.ReadAllText(Path.Combine(root, "src", "PPPaint", "app.manifest"), Encoding.UTF8);
        var project = File.ReadAllText(Path.Combine(root, "src", "PPPaint", "PPPaint.csproj"), Encoding.UTF8);
        var forbiddenExternalIconToken = "ms" + "paint";

        StringAssert.Contains(manifest, "PerMonitorV2");
        Assert.IsFalse(manifest.Contains(forbiddenExternalIconToken, StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(project.Contains(forbiddenExternalIconToken, StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(project, "Assets\\PPPaintIcon.svg");
    }

    [TestMethod]
    public void ImageProperties_ProvidesAccessibleUnitSelectionAndUsesLoadedDpi()
    {
        var root = FindProjectRoot();
        var properties = File.ReadAllText(Path.Combine(root, "src", "PPPaint", "MainWindow.ImageProperties.cs"), Encoding.UTF8);
        var loading = File.ReadAllText(Path.Combine(root, "src", "PPPaint", "MainWindow.DragDrop.cs"), Encoding.UTF8);

        StringAssert.Contains(properties, "ピクセル");
        StringAssert.Contains(properties, "インチ");
        StringAssert.Contains(properties, "センチメートル");
        StringAssert.Contains(properties, "ConfigureFieldAccessibility(unitSelector");
        StringAssert.Contains(properties, "ImageMeasurements.Convert");
        StringAssert.Contains(loading, "ImageMeasurements.NormalizeDpi(bitmap.Dpi)");
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "PPPaint", "PPPaint.slnx")) ||
                Directory.Exists(Path.Combine(directory.FullName, "src", "PPPaint")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        Assert.Fail("PPPaint project root was not found from the test output directory.");
        return string.Empty;
    }
}
