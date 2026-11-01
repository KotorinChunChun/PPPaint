using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;

namespace PPPaint;

public sealed partial class EditorWindow
{
    private static TextBlock KeyLabel(string label, string key)
    {
        var text = new TextBlock { FontSize = 16, VerticalAlignment = VerticalAlignment.Center };
        text.Inlines.Add(new Run { Text = label + " (" });
        var separator = key.LastIndexOf('+');
        if (separator >= 0) text.Inlines.Add(new Run { Text = key[..(separator + 1)] });
        var underline = new Underline(); underline.Inlines.Add(new Run { Text = key[(separator + 1)..] }); text.Inlines.Add(underline);
        text.Inlines.Add(new Run { Text = ")" }); return text;
    }
    private static void CommandLabel(ContentControl control, string name, string key, bool showKey = true)
    {
        control.Content = showKey ? KeyLabel(name, key) : name; AutomationProperties.SetName(control, name); AutomationProperties.SetAcceleratorKey(control, key);
        AutomationProperties.SetHelpText(control, $"{name} ({key})"); ToolTipService.SetToolTip(control, $"{name} ({key})");
    }
    private static void CommandLabel(MenuFlyoutItem item, string name, string key)
    {
        item.Text = name; AutomationProperties.SetName(item, name); AutomationProperties.SetAcceleratorKey(item, key);
        item.KeyboardAcceleratorTextOverride = key;
        AutomationProperties.SetHelpText(item, $"{name} ({key})"); ToolTipService.SetToolTip(item, $"{name} ({key})");
    }
    private void InitializeCommandKeys()
    {
        CommandLabel(FileButton, "ファイル▼", "Alt+F", false); FileButton.AccessKey = "F";
        CommandLabel(NewMenuItem, "新規", "Ctrl+N"); CommandLabel(OpenMenuItem, "開く", "Ctrl+O");
        CommandLabel(SaveAsMenuItem, "名前を付けて保存", "Ctrl+Shift+S");
        CommandLabel(PasteButton, "貼り付け", "Ctrl+V", false); CommandLabel(CopyButton, "コピー", "Ctrl+C", false); CommandLabel(SaveButton, "保存", "Ctrl+S", false);
        const string pasteHelp = "現在のキャンバスへ重ねて貼り付け (Ctrl+V)。キャンバスは縮小しません";
        ToolTipService.SetToolTip(PasteButton, pasteHelp); AutomationProperties.SetHelpText(PasteButton, pasteHelp);
        CommandLabel(PasteOptionsButton, "▼", "Ctrl+Shift+V", false); AutomationProperties.SetName(PasteOptionsButton, "貼り付け方法▼");
        const string pasteOptionsHelp = "貼り付け方法（重ねる・上・下・左・右）を選択 (Ctrl+Shift+V)";
        ToolTipService.SetToolTip(PasteOptionsButton, pasteOptionsHelp); AutomationProperties.SetHelpText(PasteOptionsButton, pasteOptionsHelp);
        CommandLabel(CutButton, "切り取り", "Ctrl+X", false);
        ToolTipService.SetToolTip(CopyButton, "キャンバス全体を同じ大きさの画像としてコピー (Ctrl+C)");
        const string cutHelp = "選択範囲または選択中の図形・画像をコピーして削除 (Ctrl+X)";
        ToolTipService.SetToolTip(CutButton, cutHelp); AutomationProperties.SetHelpText(CutButton, cutHelp);
        CommandLabel(UndoButton, "↶", "Ctrl+Z", false); AutomationProperties.SetName(UndoButton, "元に戻す"); ToolTipService.SetToolTip(UndoButton, "元に戻す (Ctrl+Z)");
        CommandLabel(RedoButton, "↷", "Ctrl+Y", false); AutomationProperties.SetName(RedoButton, "やり直す"); ToolTipService.SetToolTip(RedoButton, "やり直す (Ctrl+Y)");
        CommandLabel(MoreButton, "その他▼", "Alt+G", false); MoreButton.AccessKey = "G";
        CommandLabel(SettingsButton, "設定", "Alt+P", false); SettingsButton.AccessKey = "P";
        CommandLabel(CustomColorButton, "色", "Alt+I"); CustomColorButton.AccessKey = "I";
        CommandLabel(AdoptStyleButton, "設定を取得", "Alt+Y"); AdoptStyleButton.AccessKey = "Y";
        const string adoptHelp = "選択中の図形1つから、対応する色・太さ・文字サイズ・効果の強さを新規作図設定へ取得して保存 (Alt+Y)。図形自体は変更しません";
        AutomationProperties.SetHelpText(AdoptStyleButton, adoptHelp); ToolTipService.SetToolTip(AdoptStyleButton, adoptHelp);
        CommandLabel(EyedropperButton, "スポイト", "I");
        EyedropperButton.Checked += (_, _) => SetColorPicking(true); EyedropperButton.Unchecked += (_, _) => SetColorPicking(false);
        CommandLabel(CropButton, "切り抜き", "Ctrl+Shift+X", false); CropButton.AccessKey = "C";
        AutomationProperties.SetAcceleratorKey(CropButton, "Ctrl+Shift+X, C, Enter"); ToolTipService.SetToolTip(CropButton, "選択範囲をキャンバスとして切り抜く (Ctrl+Shift+X / C / Enter)");
        CommandLabel(DeleteButton, "削除", "Del", false);
        CommandLabel(OrderButton, "重なり▼", "Alt+J", false); OrderButton.AccessKey = "J";
        CommandLabel(MergeSelectionMenuItem, "選択を画像に統合", "Ctrl+E");
        const string mergeHelp = "選択図形だけを背景透過の１枚の画像に統合 (Ctrl+E)。元に戻す操作で図形を復元できます";
        ToolTipService.SetToolTip(MergeSelectionMenuItem, mergeHelp); AutomationProperties.SetHelpText(MergeSelectionMenuItem, mergeHelp);
        CommandLabel(FitButton, "全体", "Alt+W"); FitButton.AccessKey = "W";
        ZoomLabel.AccessKey = "D"; ToolTipService.SetToolTip(ZoomLabel, "100%表示 (Alt+D)");
        DimensionsButton.AccessKey = "Z"; ToolTipService.SetToolTip(DimensionsButton, "キャンバスサイズを変更 (Alt+Z)");
        CommandLabel(CanvasBackgroundButton, "背景", "Alt+U"); CanvasBackgroundButton.AccessKey = "U";
    }
}
