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

    [TestMethod]
    public void SelectionRibbon_UsesSixNamedActionsAndKeepsClickConnections()
    {
        var root = FindProjectRoot();
        var xaml = ReadProjectFile(root, "src", "ChunPaint", "MainWindow.xaml");
        var selection = ReadProjectFile(root, "src", "ChunPaint", "MainWindow.Selection.cs");
        var imageOperations = ReadProjectFile(root, "src", "ChunPaint", "MainWindow.ImageOperations.cs");

        Assert.IsFalse(xaml.Contains("x:Name=\"SelectToolButton\"", StringComparison.Ordinal),
            "旧SelectToolButtonを6ボタン構成に残してはいけません。");

        var actions = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["RectangleSelectionButton"] = ["RectangleSelection_Click"],
            ["FreeSelectionButton"] = ["FreeSelection_Click"],
            ["SelectAllButton"] = ["SelectAll_Click"],
            ["CancelSelectionButton"] = ["CancelSelection_Click"],
            ["TransparentSelectionButton"] = ["ToggleTransparentSelection_Click"],
            ["DeleteSelectionButton"] = ["DeleteSelection_Click", "Delete_Click"]
        };

        foreach (var (name, handlers) in actions)
        {
            var control = Regex.Match(
                xaml,
                $@"<(?:ToggleButton|Button)\b(?=[^>]*\bx:Name=""{Regex.Escape(name)}"")(?=[^>]*\bClick=""(?<handler>[^""]+)"")[^>]*>",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            Assert.IsTrue(control.Success, $"選択操作{name}にClick接続されたボタンが必要です。");
            var handler = control.Groups["handler"].Value;
            CollectionAssert.Contains(handlers, handler,
                $"選択操作{name}のClick接続は既存操作ハンドラへ接続してください。");
            Assert.IsTrue(
                Regex.IsMatch(
                    selection + Environment.NewLine + imageOperations,
                    $@"\b{Regex.Escape(handler)}\s*\(",
                    RegexOptions.CultureInvariant),
                $"選択操作{name}のClickハンドラ実装が見つかりません。");
        }

        foreach (var name in new[] { "RectangleSelectionButton", "FreeSelectionButton" })
        {
            Assert.IsTrue(
                Regex.IsMatch(
                    xaml,
                    $@"<ToggleButton\b(?=[^>]*\bx:Name=""{Regex.Escape(name)}"")",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                $"{name}は選択状態を表示できるToggleButtonである必要があります。");
        }

        foreach (var (name, key) in new[]
                 {
                     ("SelectAllButton", "Ctrl+A"),
                     ("CancelSelectionButton", "Escape"),
                     ("DeleteSelectionButton", "Delete")
                 })
        {
            Assert.IsTrue(
                Regex.IsMatch(
                    xaml,
                    $@"<(?:ToggleButton|Button)\b(?=[^>]*\bx:Name=""{Regex.Escape(name)}"")(?=[^>]*\bAutomationProperties.HelpText=""[^""]*{Regex.Escape(key)}[^""]*"")[^>]*>",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                $"{name}のHelpTextにショートカット{key}を含めてください。");
        }
    }

    [TestMethod]
    public void SelectionMode_DoesNotUseLegacyFixedButtonAndSeparatesNormalTools()
    {
        var root = FindProjectRoot();
        var xamlCodeBehind = ReadProjectFile(root, "src", "ChunPaint", "MainWindow.xaml.cs");
        var selection = ReadProjectFile(root, "src", "ChunPaint", "MainWindow.Selection.cs");

        var activate = ExtractMethod(selection, "private void ActivateSelectionMode");
        Assert.IsFalse(activate.Contains("SelectToolButton", StringComparison.Ordinal),
            "ActivateSelectionModeは旧SelectToolButtonを固定参照してはいけません。");
        StringAssert.Contains(activate, "_selectionMode = mode",
            "ActivateSelectionModeは選択モードを更新してください。");
        StringAssert.Contains(activate, "_tool = PaintTool.Select",
            "ActivateSelectionModeは選択ツールへ切り替えてください。");
        Assert.IsTrue(
            activate.Contains("RectangleSelectionButton", StringComparison.Ordinal) ||
            activate.Contains("FreeSelectionButton", StringComparison.Ordinal) ||
            activate.Contains("UpdateSelectionModeButtons", StringComparison.Ordinal) ||
            activate.Contains("UpdateSelectionModeButtonState", StringComparison.Ordinal),
            "ActivateSelectionModeは矩形／自由選択ボタンの表示を同期してください。");

        var toolButtons = ExtractInitializer(xamlCodeBehind, "_toolButtons");
        Assert.IsFalse(toolButtons.Contains("SelectToolButton", StringComparison.Ordinal),
            "通常ツールの排他リストに旧選択ボタンを含めてはいけません。");
        foreach (var selectionButton in new[]
                 {
                     "RectangleSelectionButton", "FreeSelectionButton", "SelectAllButton",
                     "CancelSelectionButton", "TransparentSelectionButton", "DeleteSelectionButton"
                 })
        {
            Assert.IsFalse(toolButtons.Contains(selectionButton, StringComparison.Ordinal),
                $"通常ツールの排他リストに{selectionButton}を含めてはいけません。");
        }

        var toolClick = ExtractMethod(xamlCodeBehind, "private void ToolButton_Click");
        StringAssert.Contains(toolClick, "UpdateSelectionModeButtonState",
            "通常ツール選択時にも矩形／自由選択の表示を解除して排他状態を保ってください。");
    }

    [TestMethod]
    public void SelectionState_SynchronizesButtonsAndTransparentSelection()
    {
        var root = FindProjectRoot();
        var imageOperations = ReadProjectFile(root, "src", "ChunPaint", "MainWindow.ImageOperations.cs");
        var selection = ReadProjectFile(root, "src", "ChunPaint", "MainWindow.Selection.cs");

        var updateState = ExtractMethod(imageOperations, "private void UpdateSelectionCommandState");
        foreach (var button in new[]
                 {
                     "SelectAllButton", "CancelSelectionButton", "TransparentSelectionButton", "DeleteSelectionButton"
                 })
        {
            Assert.IsTrue(updateState.Contains(button, StringComparison.Ordinal),
                $"UpdateSelectionCommandStateで{button}の状態を同期してください。");
        }
        StringAssert.Contains(updateState, "TransparentSelectionMenuItem",
            "透明な選択メニューの状態も同期してください。");
        Assert.IsTrue(
            updateState.Contains("IsChecked", StringComparison.Ordinal) ||
            updateState.Contains("SetTransparentSelection", StringComparison.Ordinal),
            "透明な選択のチェック状態を同期してください。");
        Assert.IsFalse(
            Regex.IsMatch(updateState, @"DeleteSelectionButton\.IsEnabled\s*=\s*[^;]*hasObject", RegexOptions.CultureInvariant),
            "選択範囲の削除ボタンは、選択がない編集オブジェクトだけを理由に有効化してはいけません。");

        var modeState = ExtractMethod(selection, "private void UpdateSelectionModeButtonState");
        Assert.IsTrue(
            modeState.Contains("RectangleSelectionButton", StringComparison.Ordinal) &&
            modeState.Contains("FreeSelectionButton", StringComparison.Ordinal) ||
            modeState.Contains("_selectionModeButtons", StringComparison.Ordinal) &&
            modeState.Contains("IsChecked", StringComparison.Ordinal),
            "矩形／自由選択ボタンの選択表示を同期してください。");

        var selectAll = ExtractMethod(selection, "private void SelectAll");
        Assert.IsTrue(
            selectAll.Contains("SelectionMode.Rectangle", StringComparison.Ordinal) ||
            selectAll.Contains("ActivateSelectionMode", StringComparison.Ordinal),
            "SelectAllは矩形選択モードと選択ボタン表示を同期してください。");
        Assert.IsTrue(
            selectAll.Contains("UpdateSelectionCommandState", StringComparison.Ordinal) ||
            selectAll.Contains("UpdateSelectionModeButtons", StringComparison.Ordinal),
            "SelectAll後に選択状態表示を更新してください。");
    }

    [TestMethod]
    public void SelectionKeyboardRoutes_RemainConnected()
    {
        var root = FindProjectRoot();
        var codeBehind = ReadProjectFile(root, "src", "ChunPaint", "MainWindow.xaml.cs");

        var acceleratorSetup = ExtractMethod(
            ReadProjectFile(root, "src", "ChunPaint", "MainWindow.ImageOperations.cs"),
            "private void ConfigureWidgetAccelerators");
        Assert.IsTrue(
            Regex.IsMatch(acceleratorSetup, @"AddAccelerator\(VirtualKey\.A,\s*VirtualKeyModifiers\.Control[\s\S]*?SelectAll\(\)", RegexOptions.CultureInvariant),
            "Ctrl+Aは既存のSelectAll経路を維持してください。");
        Assert.IsTrue(
            Regex.IsMatch(acceleratorSetup, @"AddAccelerator\(VirtualKey\.Delete,\s*VirtualKeyModifiers\.None[\s\S]*?DeleteSelection\(\)", RegexOptions.CultureInvariant),
            "Deleteは既存のDeleteSelection経路を維持してください。");
        Assert.IsTrue(
            Regex.IsMatch(acceleratorSetup, @"AddAccelerator\(VirtualKey\.Escape,\s*VirtualKeyModifiers\.None[\s\S]*?CancelSelection\(\)", RegexOptions.CultureInvariant),
            "Escapeは既存のCancelSelection経路を維持してください。");
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.IsTrue(start >= 0, $"メソッド「{signature}」が見つかりません。");
        var next = source.IndexOf("\n    private ", start + signature.Length, StringComparison.Ordinal);
        return source[start..(next >= 0 ? next : source.Length)];
    }

    private static string ExtractInitializer(string source, string fieldName)
    {
        var start = source.IndexOf(fieldName, StringComparison.Ordinal);
        Assert.IsTrue(start >= 0, $"初期化子「{fieldName}」が見つかりません。");
        var end = source.IndexOf("];", start, StringComparison.Ordinal);
        Assert.IsTrue(end >= 0, $"初期化子「{fieldName}」の終端が見つかりません。");
        return source[start..(end + 2)];
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
