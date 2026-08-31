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
                    $@"Assets[/\\]RibbonIcons[/\\]final[/\\]{Regex.Escape(icon)}\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                $"リボンにfinal/{icon}のPNG参照が必要です。");
        }
        Assert.IsFalse(xaml.Contains("RibbonIcons/temporary", StringComparison.OrdinalIgnoreCase),
            "C14完了時に暫定リボンアイコン参照を残してはいけません。");
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
    public void C14LayerPanel_UsesHorizontalPreviewCenteredEllipsisAndFixedActions()
    {
        var root = FindProjectRoot();
        var xaml = ReadProjectFile(root, "src", "ChunPaint", "MainWindow.xaml");
        var layers = ReadProjectFile(root, "src", "ChunPaint", "MainWindow.Layers.cs");

        StringAssert.Contains(xaml, "Width=\"156\" MinWidth=\"148\" MaxWidth=\"168\"");
        StringAssert.Contains(xaml, "ScrollViewer.HorizontalScrollBarVisibility=\"Disabled\"");
        StringAssert.Contains(xaml, "ScrollViewer.VerticalScrollBarVisibility=\"Auto\"");
        StringAssert.Contains(xaml, "HorizontalContentAlignment=\"Stretch\"");
        Assert.AreEqual(4, Regex.Matches(xaml, "MinHeight=\"44\"").Count,
            "レイヤー下部の4操作は同じ44DIP以上の高さにしてください。");

        StringAssert.Contains(layers, "Width = 104");
        StringAssert.Contains(layers, "Height = 54");
        StringAssert.Contains(layers, "TextAlignment = TextAlignment.Center");
        StringAssert.Contains(layers, "TextTrimming = TextTrimming.CharacterEllipsis");
        StringAssert.Contains(layers, "ToolTipService.SetToolTip(name, layer.Name)");
        StringAssert.Contains(layers, "new WriteableBitmap(width, height)");
    }

    [TestMethod]
    public void C14RibbonIcons_AreFinalIndependentAndReproducible()
    {
        var root = FindProjectRoot();
        var generator = ReadProjectFile(root, "dev", "assets", "New-RibbonIcons.ps1");
        var provenance = ReadProjectFile(root, "docs", "icon-provenance.md");
        var finalRoot = Path.Combine(root, "src", "ChunPaint", "Assets", "RibbonIcons", "final");
        var temporaryRoot = Path.Combine(root, "src", "ChunPaint", "Assets", "RibbonIcons", "temporary");

        foreach (var icon in new[] { "resize.png", "eraser.png", "fill.png", "brush.png" })
        {
            Assert.IsTrue(File.Exists(Path.Combine(finalRoot, icon)), $"最終アイコン{icon}が必要です。");
            StringAssert.Contains(provenance, $"Assets/RibbonIcons/final/{icon}");
            StringAssert.Contains(generator, $"Save-Icon '{icon}'");
        }
        Assert.IsFalse(Directory.Exists(temporaryRoot), "暫定アイコンディレクトリを残してはいけません。");
        Assert.IsFalse(provenance.Contains("| temporary |", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(generator, "PngBitmapEncoder");
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
    public void NewlyCreatedContent_ActivatesSelectionToolForInnerClickMovement()
    {
        var root = FindProjectRoot();
        var selection = ReadProjectFile(root, "src", "ChunPaint", "MainWindow.Selection.cs");
        var imageOperations = ReadProjectFile(root, "src", "ChunPaint", "MainWindow.ImageOperations.cs");
        var shapes = ReadProjectFile(root, "src", "ChunPaint", "MainWindow.Shapes.cs");
        var text = ReadProjectFile(root, "src", "ChunPaint", "MainWindow.TextAndBrushes.cs");
        var objects = ReadProjectFile(root, "src", "ChunPaint", "MainWindow.Objects.cs");

        var activateTool = ExtractMethod(selection, "private void ActivateSelectionTool");
        foreach (var token in new[] { "_tool = PaintTool.Select", "UpdateSelectionModeButtonState" })
        {
            StringAssert.Contains(activateTool, token,
                $"作成直後の選択ツール状態に{token}が必要です。");
        }

        var paste = ExtractMethod(imageOperations, "private async Task PasteAsync");
        Assert.IsTrue(paste.IndexOf("_selection = SelectionRegion.CreateRectangle", StringComparison.Ordinal) >= 0,
            "貼り付け画像を作成直後から選択状態にしてください。");
        StringAssert.Contains(paste, "ActivateSelectionTool",
            "貼り付け後は選択ツールへ切り替えて内側クリックを受け付けてください。");

        var endShape = ExtractMethod(shapes, "private void EndShape");
        StringAssert.Contains(endShape, "ActivateEditableObject",
            "図形作成直後は編集オブジェクトをフォーカスしてください。");
        StringAssert.Contains(endShape, "ActivateSelectionTool",
            "図形作成後は選択ツールへ切り替えて内側クリックを受け付けてください。");

        var textEditor = ExtractMethod(text, "private async Task ShowTextEditorAsync");
        StringAssert.Contains(textEditor, "ActivateSelectionTool",
            "テキスト作成後は選択ツールへ切り替えて内側クリックを受け付けてください。");

        var pointerPressed = ExtractMethod(objects, "private bool HandleEditableObjectPointerPressed");
        StringAssert.Contains(pointerPressed, "FindEditableObject",
            "選択ツールの内側クリックで編集オブジェクトをヒットテストしてください。");
        StringAssert.Contains(pointerPressed, "BeginEditableObjectInteraction",
            "編集オブジェクトの内側クリックで移動操作を開始してください。");
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
        StringAssert.Contains(updateCursor, "UpdateCanvasPenCursorIndicator",
            "ペン種別変更時にキャンバス上のペンカーソル表示を更新してください。");
        var penCursor = ExtractMethod(allCode, "private void UpdateCanvasPenCursorIndicator");
        StringAssert.Contains(penCursor, "CanvasPenCursorIndicator",
            "ペンカーソルの表示状態を更新してください。");
        StringAssert.Contains(penCursor, "Canvas.SetLeft",
            "ペンカーソルをポインター位置へ配置してください。");
        var penGlyph = ExtractMethod(allCode, "private string? ResolveCanvasPenGlyph");
        foreach (var token in new[] { "PaintTool.Pencil", "PaintTool.Brush", "BrushKind.Watercolor", "BrushKind.Marker", "BrushKind.NaturalPencil" })
        {
            StringAssert.Contains(penGlyph, token,
                $"選択中ペンのカーソルアイコンへ{token}を反映してください。");
        }
        StringAssert.Contains(allCode, "CanvasPenCursorGlyph.Text",
            "選択中ペンのアイコンをキャンバスカーソルへ表示してください。");
        StringAssert.Contains(allCode, "CanvasPenCursorLayer.Width = PaintCanvas.Width",
            "キャンバスカーソルレイヤーの幅を描画キャンバスへ同期してください。");
        StringAssert.Contains(allCode, "CanvasPenCursorLayer.Height = PaintCanvas.Height",
            "キャンバスカーソルレイヤーの高さを描画キャンバスへ同期してください。");
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
    public void StrokeWidthRibbon_HasPresetButtonAndKeepsSliderSynchronized()
    {
        var root = FindProjectRoot();
        var xaml = ReadProjectFile(root, "src", "ChunPaint", "MainWindow.xaml");
        var source = ReadProjectCode(root);

        StringAssert.Contains(xaml, "x:Name=\"StrokeWidthRibbonGroup\"",
            "線幅専用のリボングループが必要です。");
        var button = FindNamedElement(xaml, "Button", "StrokeWidthButton");
        Assert.IsTrue(button.Success, "現在の線幅を表示する選択ボタンが必要です。");
        StringAssert.Contains(button.Value, "Click=\"StrokeWidthButton_Click\"",
            "線幅ボタンは太さ候補メニューへ接続してください。");
        StringAssert.Contains(xaml, "StrokeWidthName", "線幅ボタンに現在値を表示してください。");
        StringAssert.Contains(xaml, "StrokeWidthPreview", "線幅ボタンに太さのプレビューを表示してください。");
        StringAssert.Contains(source, "StrokeWidthPresets = [1, 3, 5, 8, 12, 20, 30]",
            "線幅の候補を複数用意してください。");
        StringAssert.Contains(source, "StrokeWidthSlider.Value = width",
            "線幅候補の選択値を既存スライダーへ反映してください。");
        StringAssert.Contains(source, "UpdateStrokeWidthVisuals()",
            "スライダー変更時にリボン表示を同期してください。");
    }

    [TestMethod]
    public void ShapesRibbon_ContainsTwentyThreeUniqueShapesInSixByFourGrid()
    {
        var root = FindProjectRoot();
        var xaml = ReadProjectFile(root, "src", "ChunPaint", "MainWindow.xaml");
        var start = xaml.IndexOf("x:Name=\"ShapesRibbonGroup\"", StringComparison.Ordinal);
        var end = xaml.IndexOf("x:Name=\"ColorsRibbonGroup\"", start, StringComparison.Ordinal);
        Assert.IsTrue(start >= 0 && end > start, "図形リボングループが見つかりません。");
        var group = xaml[start..end];
        StringAssert.Contains(xaml, "<Setter Property=\"Width\" Value=\"28\" /><Setter Property=\"Height\" Value=\"22\" />",
            "図形ボタンの高さが不足しており、アイコンが切れる可能性があります。");

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
            Enumerable.Range(0, 4).SelectMany(row => Enumerable.Range(0, 6).Select(column => (row, column)))
                .Where(cell => cell != (3, 5)).ToArray(),
            occupiedCells.ToArray(),
            "図形ボタンは6列×4段の23セルへ配置し、24枠目だけを空けてください。");
    }

    [TestMethod]
    public void ShapesRibbon_StyleButtonsAreGroupedButtonsWithStyleMenus()
    {
        var root = FindProjectRoot();
        var xaml = ReadProjectFile(root, "src", "ChunPaint", "MainWindow.xaml");
        var source = ReadProjectFile(root, "src", "ChunPaint", "MainWindow.Shapes.cs");

        var styleActions = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["OutlineStyleButton"] = ["OutlineStyleButton_Click"],
            ["FillStyleButton"] = ["FillStyleButton_Click"]
        };
        foreach (var (name, handlers) in styleActions)
        {
            var control = Regex.Match(
                xaml,
                $@"<(?:ToggleButton|Button)\b(?=[^>]*\bx:Name=""{Regex.Escape(name)}"")(?=[^>]*\bClick=""(?<handler>[^""]+)"")[^>]*>",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            Assert.IsTrue(control.Success, $"{name}は輪郭／塗りを選ぶ直接操作ボタンとして必要です。");
            CollectionAssert.Contains(handlers, control.Groups["handler"].Value,
                $"{name}は専用のスタイルハンドラーへ接続してください。");
            StringAssert.Contains(source, $" {control.Groups["handler"].Value}(",
                $"{name}のハンドラー実装が見つかりません。");
        }

        StringAssert.Contains(xaml, "輪郭の種類（ペン）",
            "輪郭ボタンはペンの種類を判別できる説明を表示してください。");
        StringAssert.Contains(xaml, "塗りの種類（ペンキ）",
            "塗りボタンはペンキの種類を判別できる説明を表示してください。");
        foreach (var label in new[] { "なし", "実線（ペン）", "単色（ペンキ）", "クレヨン", "マーカー", "油彩ブラシ", "鉛筆（通常）", "水彩ブラシ" })
        {
            StringAssert.Contains(source, label, $"図形スタイルメニューに「{label}」を用意してください。");
        }
    }

    [TestMethod]
    public void ShapesRibbon_StyleVisualsSynchronizeOutlineAndFillIndependently()
    {
        var root = FindProjectRoot();
        var source = ReadProjectFile(root, "src", "ChunPaint", "MainWindow.Shapes.cs");
        var updateVisuals = ExtractMethod(source, "private void UpdateShapeStyleVisuals");

        foreach (var button in new[] { "OutlineStyleButton", "FillStyleButton", "OutlineStyleName", "OutlineStyleGlyph", "FillStyleName", "FillStyleGlyph" })
        {
            StringAssert.Contains(updateVisuals, button,
                $"UpdateShapeStyleVisualsで{button}を同期してください。");
        }
        StringAssert.Contains(updateVisuals, "_drawShapeOutline",
            "輪郭スタイルの状態を同期してください。");
        StringAssert.Contains(updateVisuals, "_drawShapeFill",
            "塗りつぶしスタイルの状態を同期してください。");
        StringAssert.Contains(updateVisuals, "GetShapeStrokeLabel",
            "輪郭の選択モード名をボタンへ表示してください。");
        StringAssert.Contains(updateVisuals, "GetShapeFillLabel",
            "塗りの選択モード名をボタンへ表示してください。");
        StringAssert.Contains(source, "ShapeStrokeKind",
            "輪郭の描画モードを図形スタイルへ保存してください。");
        StringAssert.Contains(source, "ShapeFillKind",
            "塗りの描画モードを図形スタイルへ保存してください。");

        var editableDrawing = ExtractMethod(source, "private void DrawEditableShape");
        Assert.IsTrue(
            Regex.IsMatch(
                editableDrawing,
                @"EditableShapeKind\.Curve[\s\S]*?DrawCurve\([^;]*style\.DrawOutline[\s\S]*?style\.OutlineKind\)",
                RegexOptions.CultureInvariant),
            "保存済み曲線は現在のツール設定ではなく、オブジェクト固有のDrawOutlineで描画してください。");
    }

    [TestMethod]
    public void ColorsRibbon_ShowsTwoEqualTargetButtonsAndSharedPaletteFlyout()
    {
        var root = FindProjectRoot();
        var xaml = ReadProjectFile(root, "src", "ChunPaint", "MainWindow.xaml");
        var start = xaml.IndexOf("x:Name=\"ColorsRibbonGroup\"", StringComparison.Ordinal);
        var end = xaml.IndexOf("x:Name=\"LayersRibbonGroup\"", start, StringComparison.Ordinal);
        Assert.IsTrue(start >= 0 && end > start, "色リボングループが見つかりません。");
        var group = xaml[start..end];

        var primary = FindNamedElement(group, "Button", "PrimaryColorButton");
        var secondary = FindNamedElement(group, "Button", "SecondaryColorButton");
        Assert.IsTrue(primary.Success && secondary.Success,
            "常時表示するPrimary/Secondaryの色ボタンが必要です。");
        Assert.AreEqual(GetAttribute(primary.Value, "Width"), GetAttribute(secondary.Value, "Width"),
            "Primary/Secondary色ボタンの幅を統一してください。");
        Assert.AreEqual(GetAttribute(primary.Value, "Height"), GetAttribute(secondary.Value, "Height"),
            "Primary/Secondary色ボタンの高さを統一してください。");
        Assert.AreEqual("Transparent", GetAttribute(primary.Value, "Background"),
            "Primary色ボタンは透明背景にし、内部プレビューを表示してください。");
        Assert.AreEqual("Transparent", GetAttribute(secondary.Value, "Background"),
            "Secondary色ボタンは透明背景にし、内部プレビューを表示してください。");
        var primaryStart = group.IndexOf(primary.Value, StringComparison.Ordinal);
        var secondaryStart = group.IndexOf(secondary.Value, StringComparison.Ordinal);
        Assert.IsTrue(primaryStart >= 0 && secondaryStart > primaryStart, "色ターゲットボタンの配置を確認できません。");
        var primaryRegion = group[primaryStart..secondaryStart];
        var secondaryRegion = group[secondaryStart..];
        StringAssert.Contains(primaryRegion, "PrimaryColorPreview",
            "Primary色ボタン内に固定色プレビューを配置してください。");
        StringAssert.Contains(secondaryRegion, "SecondaryColorPreview",
            "Secondary色ボタン内に固定色プレビューを配置してください。");

        var flyoutStart = xaml.IndexOf("x:Name=\"ColorPaletteFlyout\"", StringComparison.Ordinal);
        Assert.IsTrue(flyoutStart >= 0, "Primary/Secondaryで共有するColorPaletteFlyoutが必要です。");
        var flyout = xaml[flyoutStart..];
        StringAssert.Contains(flyout, "CustomColorsPanel",
            "ColorPaletteFlyoutに3段目のCustomColorsPanelを残してください。");
        Assert.IsTrue(
            Regex.IsMatch(flyout, @"Text=""色の編集""[\s\S]*?Click=""EditColors_Click""|Click=""EditColors_Click""[\s\S]*?Text=""色の編集""",
                RegexOptions.CultureInvariant),
            "「色の編集」はColorPaletteFlyout内に残してください。");

        Assert.AreEqual(2, Regex.Matches(group, @"<Button\b[^>]*\bx:Name=""(?:PrimaryColorButton|SecondaryColorButton)""", RegexOptions.CultureInvariant).Count,
            "ColorsRibbonGroupの常時表示ターゲットはPrimary/Secondaryの2ボタンに限定してください。");
        var flyoutOffset = flyoutStart - start;
        var alwaysVisible = flyoutOffset > 0 && flyoutOffset < group.Length ? group[..flyoutOffset] : group;
        Assert.AreEqual(0, Regex.Matches(alwaysVisible, @"<Button\b[^>]*\bClick=""Palette_Click""", RegexOptions.CultureInvariant).Count,
            "既定色ボタンは常時表示せず共通Flyout内へ配置してください。");

        var targetClickCount = Regex.Matches(group, @"\bClick=""ColorTarget_Click""", RegexOptions.CultureInvariant).Count;
        Assert.AreEqual(2, targetClickCount,
            "Primary/Secondary色ボタンは共通のColorTarget_Clickへ接続してください。");
    }

    [TestMethod]
    public void ColorsRibbon_HasTwentyDefaultSwatchesInTenByTwoGrid()
    {
        var root = FindProjectRoot();
        var xaml = ReadProjectFile(root, "src", "ChunPaint", "MainWindow.xaml");
        var flyoutStart = xaml.IndexOf("x:Name=\"ColorPaletteFlyout\"", StringComparison.Ordinal);
        Assert.IsTrue(flyoutStart >= 0, "ColorPaletteFlyoutが見つかりません。");
        var customStart = xaml.IndexOf("x:Name=\"CustomColorsPanel\"", flyoutStart, StringComparison.Ordinal);
        Assert.IsTrue(customStart > flyoutStart, "CustomColorsPanelがColorPaletteFlyout内に必要です。");
        var defaults = xaml[flyoutStart..customStart];

        var swatches = Regex.Matches(
            defaults,
            @"<Button\b(?=[^>]*\bGrid\.Row=""(?<row>[01])"")(?=[^>]*\bGrid\.Column=""(?<column>[0-9])"")(?=[^>]*\bTag=""#(?<color>[0-9A-Fa-f]{8})"")(?=[^>]*\bClick=""Palette_Click"")[^>]*>",
            RegexOptions.CultureInvariant);
        Assert.AreEqual(20, swatches.Count,
            "既定色は20色（10列×2段）をColorPaletteFlyoutへ配置してください。");
        var cells = swatches
            .Select(match => (Row: int.Parse(match.Groups["row"].Value), Column: int.Parse(match.Groups["column"].Value)))
            .ToHashSet();
        Assert.AreEqual(20, cells.Count, "既定色のGridセルを重複させてはいけません。");
        CollectionAssert.AreEquivalent(
            Enumerable.Range(0, 2).SelectMany(row => Enumerable.Range(0, 10).Select(column => (row, column))).ToArray(),
            cells.ToArray(),
            "既定色は10列×2段のGridへ配置してください。");
    }

    [TestMethod]
    public void ColorsLogic_SeparatesActiveTargetAndManagesCustomColors()
    {
        var root = FindProjectRoot();
        var source = ReadProjectCode(root);
        var targetClick = ExtractMethod(source, "private void ColorTarget_Click");
        var paletteClick = ExtractMethod(source, "private void Palette_Click");
        var targetSetter = ExtractMethod(source, "private void SetColorTarget");

        Assert.IsTrue(
            targetClick.Contains("_activeColorTarget", StringComparison.Ordinal) &&
            Regex.IsMatch(source, @"ColorTarget\.(?:Primary|Secondary)", RegexOptions.CultureInvariant),
            "ColorTarget_ClickでPrimary/Secondaryの対象を区別してください。");
        Assert.IsTrue(
            paletteClick.Contains("SetActiveColor", StringComparison.Ordinal) ||
            paletteClick.Contains("SetColorTarget", StringComparison.Ordinal),
            "Palette_Clickはアクティブ対象へ色を設定する共通ロジックを呼び出してください。");
        Assert.IsTrue(
            targetSetter.Contains("_primaryColor", StringComparison.Ordinal) &&
            targetSetter.Contains("_secondaryColor", StringComparison.Ordinal),
            "色設定ロジックはアクティブ対象に応じてPrimary/Secondaryを更新してください。");
        Assert.IsTrue(
            Regex.IsMatch(paletteClick + targetSetter + source, @"_active(?:Color|Target)|ColorTarget|_colorTarget", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "Palette_Clickでアクティブな色ターゲットを判定してください。");
        Assert.IsTrue(
            paletteClick.Contains("Hide", StringComparison.Ordinal) ||
            source.Contains("ColorPaletteFlyout.Hide", StringComparison.Ordinal),
            "色を選択した後にColorPaletteFlyoutを閉じてください。");

        Assert.IsTrue(
            Regex.IsMatch(source, @"(?:const|static\s+readonly|readonly)\s+(?:int|short|byte)\s+\w*Custom\w*\s*=\s*10\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            Regex.IsMatch(source, @"CustomColors[\s\S]{0,500}\b10\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "カスタム色の最大数10を実装上で明示してください。");
        Assert.IsTrue(
            Regex.IsMatch(source, @"CustomColors[\s\S]{0,1000}(?:Contains|Any|Distinct)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "カスタム色の重複を防止してください。");
        Assert.IsTrue(
            Regex.IsMatch(source, @"CustomColors[\s\S]{0,1200}(?:RemoveAt\s*\(\s*0\s*\)|RemoveFirst|Dequeue|TakeLast)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "カスタム色が上限を超えた場合はFIFOで古い色から削除してください。");
        StringAssert.Contains(source, "AutomationProperties.SetName(swatch",
            "ユーザー作成色にも再選択可能なAutomation名を設定してください。");
    }

    private static Match FindNamedElement(string source, string elementName, string name)
    {
        return Regex.Match(
            source,
            $@"<{Regex.Escape(elementName)}\b(?=[^>]*\bx:Name=""{Regex.Escape(name)}"")[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string? GetAttribute(string element, string attributeName)
    {
        return Regex.Match(
                element,
                $@"\b{Regex.Escape(attributeName)}=""(?<value>[^""]*)""",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Groups["value"].Value;
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
