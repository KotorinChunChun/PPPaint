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

        Assert.IsTrue(
            Regex.IsMatch(
                xaml,
                @"SelectionRibbonGroup[\s\S]*?Text=""(?:選択した部分|選択)""",
                RegexOptions.CultureInvariant),
            "選択リボングループの表示名が見つかりません。");

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

    [TestMethod]
    public void ZoomTool_IsToggleConnectedAndUsesClampedTenPercentSteps()
    {
        var root = FindProjectRoot();
        var xaml = ReadProjectFile(root, "src", "ChunPaint", "MainWindow.xaml");
        var codeBehind = ReadProjectFile(root, "src", "ChunPaint", "MainWindow.xaml.cs");

        Assert.IsTrue(
            Regex.IsMatch(
                xaml,
                @"<ToggleButton\b(?=[^>]*\bx:Name=""ZoomToolButton"")(?=[^>]*\bTag=""Zoom"")(?=[^>]*\bClick=""ToolButton_Click"")[^>]*>",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "ズームツールはZoomタグ付きのToggleButtonとして既存のツール選択経路へ接続してください。");

        var paintTool = ExtractEnum(codeBehind, "private enum PaintTool");
        StringAssert.Contains(paintTool, "Zoom", "PaintToolにZoomを追加してください。");

        var pointerPressed = ExtractMethod(codeBehind, "private async void PaintCanvas_PointerPressed");
        Assert.IsTrue(
            pointerPressed.Contains("PaintTool.Zoom", StringComparison.Ordinal) ||
            pointerPressed.Contains("HandleZoom", StringComparison.Ordinal),
            "ズームツールのクリック処理をキャンバスのPointerPressed経路へ接続してください。");
        Assert.IsTrue(
            pointerPressed.IndexOf("PaintTool.Zoom", StringComparison.Ordinal) <
            pointerPressed.IndexOf("PaintTool.Select", StringComparison.Ordinal),
            "ズーム分岐は選択や図形処理より前に判定してください。");

        Assert.IsTrue(pointerPressed.Contains("ZoomSlider.Value", StringComparison.Ordinal),
            "ズームツールは既存のZoomSliderへ倍率を反映してください。");
        Assert.IsTrue(pointerPressed.Contains("Math.Clamp", StringComparison.Ordinal),
            "ズーム倍率はスライダー範囲内へClampしてください。");
        Assert.IsTrue(
            Regex.IsMatch(pointerPressed, @"\b10d?\b", RegexOptions.CultureInvariant) &&
            Regex.IsMatch(pointerPressed, @"-\s*10d?\b", RegexOptions.CultureInvariant),
            "ズームツールは左右クリックを10%単位で増減してください。");

        var toolClick = ExtractMethod(codeBehind, "private void ToolButton_Click");
        Assert.IsFalse(toolClick.Contains("ZoomSlider.Value", StringComparison.Ordinal),
            "拡大鏡ツールを選択しただけで倍率を変更してはいけません。");
    }

    [TestMethod]
    public void CanvasCursor_UsesSafeInputSystemCursorAndUpdatesForTools()
    {
        var root = FindProjectRoot();
        var codeBehind = ReadProjectFile(root, "src", "ChunPaint", "MainWindow.xaml.cs");
        var allCode = ReadProjectCode(root);
        var selection = ReadProjectFile(root, "src", "ChunPaint", "MainWindow.Selection.cs");
        var objects = ReadProjectFile(root, "src", "ChunPaint", "MainWindow.Objects.cs");

        var updateCursor = ExtractMethod(allCode, "private void UpdateCanvasCursor");
        StringAssert.Contains(updateCursor, "SetCursorShape",
            "キャンバスカーソルは専用ホスト経由で安全に設定してください。");
        var resolveCursor = ExtractMethod(allCode, "private InputSystemCursorShape ResolveCanvasCursor");
        StringAssert.Contains(resolveCursor, "InputSystemCursorShape",
            "カーソル形状はInputSystemCursorShapeで選択してください。");
        StringAssert.Contains(resolveCursor, "PaintTool.Zoom",
            "UpdateCanvasCursorはズームツールの形状を明示してください。");
        foreach (var token in new[]
                 {
                     "PaintTool.Fill", "InputSystemCursorShape.Hand",
                     "PaintTool.Text", "InputSystemCursorShape.IBeam",
                     "PaintTool.Select", "InputSystemCursorShape.Arrow",
                     "PaintTool.Pencil", "PaintTool.Brush", "PaintTool.Eraser",
                     "PaintTool.Picker", "PaintTool.Shape", "PaintTool.Zoom",
                     "InputSystemCursorShape.Cross"
                 })
        {
            StringAssert.Contains(resolveCursor, token,
                $"ツール別カーソル割当に{token}が必要です。");
        }
        Assert.IsTrue(
            resolveCursor.Contains("Arrow", StringComparison.Ordinal) ||
            resolveCursor.Contains("Cross", StringComparison.Ordinal),
            "通常時のキャンバスカーソル形状を指定してください。");
        var cursorHost = ExtractMethod(allCode, "public void SetCursorShape");
        StringAssert.Contains(cursorHost, "InputSystemCursor.Create",
            "カーソルホストはWinUIのInputSystemCursorで形状を生成してください。");
        StringAssert.Contains(cursorHost, "ProtectedCursor",
            "生成したカーソルをWinUI要素へ設定してください。");
        Assert.IsTrue(cursorHost.Contains("Dispose", StringComparison.Ordinal),
            "カーソル差し替え時に古いカーソルを破棄してください。");

        var handleCursor = ExtractMethod(allCode, "private static InputSystemCursorShape CursorForHandle");
        foreach (var cursor in new[]
                 {
                     "SizeNorthwestSoutheast", "SizeNortheastSouthwest",
                     "SizeNorthSouth", "SizeWestEast", "SizeAll"
                 })
        {
            StringAssert.Contains(handleCursor, cursor,
                $"方向別ハンドルカーソル{cursor}を割り当ててください。");
        }

        var pointerMoved = ExtractMethod(codeBehind, "private void PaintCanvas_PointerMoved");
        StringAssert.Contains(pointerMoved, "UpdateCanvasCursor",
            "ポインター移動時にカーソル状態を更新してください。");
        Assert.IsTrue(
            selection.Contains("UpdateCanvasCursor", StringComparison.Ordinal) ||
            objects.Contains("UpdateCanvasCursor", StringComparison.Ordinal) ||
            pointerMoved.Contains("HitTest", StringComparison.Ordinal),
            "選択範囲または編集オブジェクトのハンドル状態をカーソル更新へ接続してください。");

        var toolClick = ExtractMethod(codeBehind, "private void ToolButton_Click");
        StringAssert.Contains(toolClick, "UpdateCanvasCursor",
            "ツール切り替え時にツール別カーソルを更新してください。");
    }

    [TestMethod]
    public void ZoomTool_SuppressesCanvasRightTappedFlyout()
    {
        var root = FindProjectRoot();
        var layers = ReadProjectFile(root, "src", "ChunPaint", "MainWindow.Layers.cs");
        var rightTapped = ExtractMethod(layers, "private void PaintCanvas_RightTapped");

        StringAssert.Contains(rightTapped, "PaintTool.Zoom",
            "キャンバス右クリック時にズームツールを判定してください。");
        StringAssert.Contains(rightTapped, "e.Handled",
            "ズームツールの右クリックでコンテキストメニューを抑止してください。");
        Assert.IsTrue(
            Regex.IsMatch(rightTapped, @"PaintTool\.Zoom[\s\S]*?e\.Handled\s*=\s*true", RegexOptions.CultureInvariant),
            "ズームツール時はFlyout生成前にRightTappedをHandledにしてください。");
    }

    [TestMethod]
    public void ShapesRibbon_ContainsTwentyThreeUniqueShapesInFourBySixGrid()
    {
        var root = FindProjectRoot();
        var xaml = ReadProjectFile(root, "src", "ChunPaint", "MainWindow.xaml");
        var start = xaml.IndexOf("x:Name=\"ShapesRibbonGroup\"", StringComparison.Ordinal);
        var end = xaml.IndexOf("x:Name=\"ColorsRibbonGroup\"", start, StringComparison.Ordinal);
        Assert.IsTrue(start >= 0 && end > start, "図形リボングループが見つかりません。");
        var group = xaml[start..end];

        var expectedTags = new[]
        {
            "Line", "Curve", "Ellipse", "Rectangle", "RoundedRectangle", "Parallelogram",
            "Triangle", "RightTriangle", "Diamond", "Pentagon", "Hexagon", "RightArrow",
            "LeftArrow", "UpArrow", "DownArrow", "FourPointStar", "Star", "FilledStar",
            "Callout", "Oval", "Cloud", "Heart", "Lightning"
        };
        var buttons = Regex.Matches(
            group,
            @"<Button\b(?=[^>]*\bStyle=""\{StaticResource ShapeButton\}"")(?=[^>]*\bGrid\.Row=""(?<row>\d+)"")(?=[^>]*\bGrid\.Column=""(?<column>\d+)"")(?=[^>]*\bTag=""(?<tag>[^""]+)"")(?=[^>]*\bClick=""ShapeButton_Click"")[^>]*>",
            RegexOptions.CultureInvariant);
        Assert.AreEqual(expectedTags.Length, buttons.Count,
            "図形ボタンは23種類を重複なく配置してください。");

        var actualTags = buttons.Select(match => match.Groups["tag"].Value).ToArray();
        CollectionAssert.AreEquivalent(expectedTags, actualTags,
            "図形ボタンのTag一覧がShapeKindと一致していません。");
        Assert.AreEqual(expectedTags.Length, actualTags.Distinct(StringComparer.Ordinal).Count(),
            "図形のTagは重複してはいけません。");

        var occupiedCells = buttons
            .Select(match => (Row: int.Parse(match.Groups["row"].Value), Column: int.Parse(match.Groups["column"].Value)))
            .ToHashSet();
        Assert.AreEqual(expectedTags.Length, occupiedCells.Count,
            "図形ボタンのGridセルが重複しています。");
        CollectionAssert.AreEquivalent(
            Enumerable.Range(0, 6).SelectMany(row => Enumerable.Range(0, 4).Select(column => (row, column)))
                .Where(cell => cell != (5, 3)).ToArray(),
            occupiedCells.ToArray(),
            "図形ボタンは4列×6段の23セルへ配置し、24枠目だけを空けてください。");
    }

    [TestMethod]
    public void ShapesRibbon_StyleButtonsAreToggleButtonsWithDirectHandlers()
    {
        var root = FindProjectRoot();
        var xaml = ReadProjectFile(root, "src", "ChunPaint", "MainWindow.xaml");
        var source = ReadProjectFile(root, "src", "ChunPaint", "MainWindow.Shapes.cs");

        var styleActions = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["SolidOutlineButton"] = ["SolidOutline_Click"],
            ["NoOutlineButton"] = ["NoOutline_Click"],
            ["SolidFillButton"] = ["SolidFill_Click"],
            ["NoFillButton"] = ["NoFill_Click"]
        };
        foreach (var (name, handlers) in styleActions)
        {
            var control = Regex.Match(
                xaml,
                $@"<ToggleButton\b(?=[^>]*\bx:Name=""{Regex.Escape(name)}"")(?=[^>]*\bClick=""(?<handler>[^""]+)"")[^>]*>",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            Assert.IsTrue(control.Success, $"{name}は直接操作できるToggleButtonとして必要です。");
            CollectionAssert.Contains(handlers, control.Groups["handler"].Value,
                $"{name}は専用のスタイルハンドラーへ接続してください。");
            StringAssert.Contains(source, $" {control.Groups["handler"].Value}(",
                $"{name}のハンドラー実装が見つかりません。");
        }

        Assert.IsFalse(xaml.Contains("OutlineStyleButton", StringComparison.Ordinal),
            "旧OutlineStyleButtonのFlyout入口を残してはいけません。");
        Assert.IsFalse(xaml.Contains("FillStyleButton", StringComparison.Ordinal),
            "旧FillStyleButtonのFlyout入口を残してはいけません。");
        Assert.IsFalse(source.Contains("OutlineStyle_Click", StringComparison.Ordinal),
            "旧OutlineStyle Flyoutハンドラーを残してはいけません。");
        Assert.IsFalse(source.Contains("FillStyle_Click", StringComparison.Ordinal),
            "旧FillStyle Flyoutハンドラーを残してはいけません。");
    }

    [TestMethod]
    public void ShapesRibbon_StyleVisualsSynchronizeOutlineAndFillIndependently()
    {
        var root = FindProjectRoot();
        var source = ReadProjectFile(root, "src", "ChunPaint", "MainWindow.Shapes.cs");
        var updateVisuals = ExtractMethod(source, "private void UpdateShapeStyleVisuals");

        foreach (var button in new[]
                 {
                     "SolidOutlineButton", "NoOutlineButton", "SolidFillButton", "NoFillButton"
                 })
        {
            StringAssert.Contains(updateVisuals, button,
                $"UpdateShapeStyleVisualsで{button}を同期してください。");
        }
        StringAssert.Contains(updateVisuals, "_drawShapeOutline",
            "輪郭スタイルの状態を同期してください。");
        StringAssert.Contains(updateVisuals, "_drawShapeFill",
            "塗りつぶしスタイルの状態を同期してください。");
        Assert.IsTrue(
            Regex.IsMatch(
                updateVisuals,
                @"SolidOutlineButton[\s\S]*?NoOutlineButton[\s\S]*?SolidFillButton[\s\S]*?NoFillButton",
                RegexOptions.CultureInvariant),
            "輪郭2ボタンと塗りつぶし2ボタンを独立した順序で同期してください。");

        var editableDrawing = ExtractMethod(source, "private void DrawEditableShape");
        Assert.IsTrue(
            Regex.IsMatch(
                editableDrawing,
                @"EditableShapeKind\.Curve[\s\S]*?DrawCurve\([^;]*style\.DrawOutline\)",
                RegexOptions.CultureInvariant),
            "保存済み曲線は現在のツール設定ではなく、オブジェクト固有のDrawOutlineで描画してください。");
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.IsTrue(start >= 0, $"メソッド「{signature}」が見つかりません。");
        var next = source.IndexOf("\n    private ", start + signature.Length, StringComparison.Ordinal);
        return source[start..(next >= 0 ? next : source.Length)];
    }

    private static string ExtractEnum(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.IsTrue(start >= 0, $"列挙型「{signature}」が見つかりません。");
        var end = source.IndexOf("\n    }", start + signature.Length, StringComparison.Ordinal);
        Assert.IsTrue(end >= 0, $"列挙型「{signature}」の終端が見つかりません。");
        return source[start..end];
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

    private static string ReadProjectCode(string root)
    {
        var sourceDirectory = Path.Combine(root, "src", "ChunPaint");
        return string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(sourceDirectory, "*.cs", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => File.ReadAllText(path, Encoding.UTF8)));
    }

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
