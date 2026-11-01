using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace PPPaint;

public sealed partial class EditorWindow
{
    private enum PasteMode { Overlay, Top, Bottom, Left, Right }

    private async Task<PasteMode?> ChoosePasteModeAsync()
    {
        var completion = new TaskCompletionSource<PasteMode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var menu = new MenuFlyout();
        foreach (var (mode, label) in new[] { (PasteMode.Overlay, "重ねる"), (PasteMode.Top, "上に連結"),
            (PasteMode.Bottom, "下に連結"), (PasteMode.Left, "左に連結"), (PasteMode.Right, "右に連結") })
        {
            var item = new MenuFlyoutItem { Text = label, FontSize = 16 };
            AutomationProperties.SetAutomationId(item, "PasteMode" + mode);
            AutomationProperties.SetHelpText(item, "上下キーで選択、Enterで貼り付け、Escで取消");
            item.Click += (_, _) => completion.TrySetResult(mode);
            menu.Items.Add(item);
        }
        menu.Closed += (_, _) => completion.TrySetResult(null);
        menu.Opened += (_, _) => menu.Items[0].Focus(FocusState.Programmatic);
        menu.ShowAt(PasteOptionsButton, new FlyoutShowOptions { Placement = FlyoutPlacementMode.Bottom });
        return await completion.Task;
    }
}
