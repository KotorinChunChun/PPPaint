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
    private void InitializeCommandKeys()
    {
        CommandLabel(NewButton, "新規", "Ctrl+N", false); CommandLabel(OpenButton, "開く", "Ctrl+O", false);
        CommandLabel(PasteButton, "貼り付け", "Ctrl+V", false); CommandLabel(CopyButton, "コピー", "Ctrl+C", false); CommandLabel(SaveButton, "保存", "Ctrl+S", false);
        CommandLabel(CutButton, "切り取り", "Ctrl+X", false);
        ToolTipService.SetToolTip(CopyButton, "キャンバス全体を同じ大きさの画像としてコピー (Ctrl+C)");
        ToolTipService.SetToolTip(CutButton, "選択範囲をコピーして削除 (Ctrl+X)");
        CommandLabel(UndoButton, "↶", "Ctrl+Z", false); AutomationProperties.SetName(UndoButton, "元に戻す"); ToolTipService.SetToolTip(UndoButton, "元に戻す (Ctrl+Z)");
        CommandLabel(RedoButton, "↷", "Ctrl+Y", false); AutomationProperties.SetName(RedoButton, "やり直す"); ToolTipService.SetToolTip(RedoButton, "やり直す (Ctrl+Y)");
        CommandLabel(MoreButton, "その他", "Alt+G", false); MoreButton.AccessKey = "G";
        CommandLabel(SettingsButton, "設定", "Alt+P", false); SettingsButton.AccessKey = "P";
        CommandLabel(CustomColorButton, "色", "Alt+I"); CustomColorButton.AccessKey = "I";
        CommandLabel(EyedropperButton, "スポイト", "I");
        EyedropperButton.Checked += (_, _) => SetColorPicking(true); EyedropperButton.Unchecked += (_, _) => SetColorPicking(false);
        CommandLabel(CropButton, "切り抜き", "C"); CropButton.AccessKey = "C";
        AutomationProperties.SetAcceleratorKey(CropButton, "Ctrl+Shift+X, C, Enter"); ToolTipService.SetToolTip(CropButton, "選択範囲をキャンバスとして切り抜く (Ctrl+Shift+X / C / Enter)");
        CommandLabel(DeleteButton, "削除", "Del");
        CommandLabel(OrderButton, "重なり", "Alt+J"); OrderButton.AccessKey = "J";
        CommandLabel(FitButton, "全体", "Alt+W"); FitButton.AccessKey = "W";
        ZoomLabel.AccessKey = "D"; ToolTipService.SetToolTip(ZoomLabel, "100%表示 (Alt+D)");
        DimensionsButton.AccessKey = "Z"; ToolTipService.SetToolTip(DimensionsButton, "画像サイズを変更 (Alt+Z)");
        CommandLabel(CanvasBackgroundButton, "背景", "Alt+U"); CanvasBackgroundButton.AccessKey = "U";
    }
}
