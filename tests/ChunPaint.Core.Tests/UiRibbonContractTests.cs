using System.Text;
using System.Text.RegularExpressions;

namespace ChunPaint.Core.Tests;

[TestClass]
public sealed class UiRibbonContractTests
{
    [TestMethod]
    public void Ribbon_RemovesFixedWidthAndKeepsNarrowWidthScrolling()
    {
        var root = FindProjectRoot();
        var xaml = ReadProjectFile(root, "src", "ChunPaint", "MainWindow.xaml");

        Assert.IsFalse(xaml.Contains("MinWidth=\"1535\"", StringComparison.Ordinal),
            "リボンは1535px固定幅に依存してはいけません。");
        Assert.IsTrue(
            Regex.IsMatch(
                xaml,
                @"<ScrollViewer\b(?=[^>]*HorizontalScrollBarVisibility=""Auto"")(?=[^>]*HorizontalScrollMode=""Enabled"")[^>]*>",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "狭幅時にもリボンを操作できるScrollViewer（Auto/Enabled）が必要です。");
        Assert.IsFalse(xaml.Contains("Copilot", StringComparison.OrdinalIgnoreCase),
            "Copilotのボタン、ラベル、グループをリボンへ残してはいけません。");
    }

    [TestMethod]
    public void Ribbon_ContainsAllRequiredGroupsAndPngReferences()
    {
        var root = FindProjectRoot();
        var xaml = ReadProjectFile(root, "src", "ChunPaint", "MainWindow.xaml");

        var requiredGroups = new[]
        {
            "選択した部分",
            "イメージ",
            "ツール",
            "ブラシ",
            "図形",
            "色",
            "レイヤー"
        };

        foreach (var group in requiredGroups)
        {
            StringAssert.Contains(xaml, group, $"リボングループ「{group}」が見つかりません。");
        }

        foreach (var icon in new[] { "resize.png", "eraser.png", "fill.png", "brush.png" })
        {
            Assert.IsTrue(
                Regex.IsMatch(
                    xaml,
                    $@"Assets[/\\]RibbonIcons[/\\](?:temporary[/\\])?{Regex.Escape(icon)}\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                $"リボンに{icon}のPNG参照が必要です。");
        }
    }

    [TestMethod]
    public void RibbonIcons_AreContentAndImageAiBoundaryRemains()
    {
        var root = FindProjectRoot();
        var project = ReadProjectFile(root, "src", "ChunPaint", "ChunPaint.csproj");
        var provider = ReadProjectFile(root, "src", "ChunPaint.Core", "ImageAiProvider.cs");

        Assert.IsTrue(
            Regex.IsMatch(
                project,
                @"<Content\s+(?:Include|Update)=""Assets[/\\]RibbonIcons[/\\]\*\*[/\\]\*\.png""",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "RibbonIconsのPNG一式をcsprojのContentとして登録してください。");
        StringAssert.Contains(project, "CopyToOutputDirectory=\"PreserveNewest\"",
            "RibbonIconsのPNGはビルド出力へ差し替え可能な形でコピーしてください。");
        Assert.IsTrue(
            Regex.IsMatch(provider, @"\binterface\s+IImageAiProvider\b", RegexOptions.CultureInvariant),
            "C8ではCoreのIImageAiProvider拡張境界を削除してはいけません。");
    }

    [TestMethod]
    public void ImageRibbon_KeepsExistingCommandConnections()
    {
        var root = FindProjectRoot();
        var xaml = ReadProjectFile(root, "src", "ChunPaint", "MainWindow.xaml");
        var codeBehind = ReadProjectFile(root, "src", "ChunPaint", "MainWindow.xaml.cs");
        var imageOperations = ReadProjectFile(root, "src", "ChunPaint", "MainWindow.ImageOperations.cs");

        foreach (var handler in new[]
                 {
                     "Crop_Click",
                     "Resize_Click",
                     "Import_Click",
                     "BackgroundRemoval_Click",
                     "Rotate_Click"
                 })
        {
            StringAssert.Contains(xaml, $"Click=\"{handler}\"", $"イメージ操作の{handler}配線を維持してください。");
            Assert.IsTrue(
                codeBehind.Contains($" {handler}(", StringComparison.Ordinal) ||
                imageOperations.Contains($" {handler}(", StringComparison.Ordinal),
                $"イメージ操作の{handler}実装が見つかりません。");
        }
    }

    private static string ReadProjectFile(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()), Encoding.UTF8);

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "ChunPaint")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        Assert.Fail("ChunPaint project root was not found from the test output directory.");
        return string.Empty;
    }
}
