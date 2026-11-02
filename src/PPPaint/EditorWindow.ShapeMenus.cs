using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PPPaint.Core;

namespace PPPaint;

public sealed partial class EditorWindow
{
    private void ShowShapeMenu(ScreenshotTool tool, Button anchor)
    {
        if (_busy || _dialogOpen || _pointerId is not null) return;
        var selected = SelectedObjects.Where(a => a.Tool == tool || tool == ScreenshotTool.Rectangle && a.Tool == ScreenshotTool.Redact).ToArray();
        var current = selected.Length > 0 ? ScreenshotShapeStyle.Read(selected[0]) : _toolPreferences.GetShape(tool);
        var menu = new MenuFlyout();
        void Choice(string text, string id, ScreenshotShapeStyle value, bool enabled = true)
        {
            var item = new ToggleMenuFlyoutItem { Text = text, FontSize = 16, IsChecked = selected.Length > 0 ? selected.All(a => ScreenshotShapeStyle.Read(a) == value) : current == value, IsEnabled = enabled };
            AutomationProperties.SetAutomationId(item, "Shape" + tool + id);
            item.Click += (_, _) => ApplyShapeStyle(tool, value); menu.Items.Add(item);
        }
        if (tool == ScreenshotTool.Arrow)
        {
            foreach (var (kind, label) in new[] { (ScreenshotArrowStyle.Line, "線の矢印"), (ScreenshotArrowStyle.DoubleEnded, "両端矢印"), (ScreenshotArrowStyle.Block, "太いブロック矢印"), (ScreenshotArrowStyle.ThreeDimensional, "立体矢印") })
                Choice(label, kind.ToString(), current with { Arrow = kind });
            menu.Items.Add(new MenuFlyoutSeparator());
            Choice("塗りつぶし", "Fill", current with { Filled = !current.Filled });
            ((ToggleMenuFlyoutItem)menu.Items[^1]).IsChecked = current.Filled;
        }
        else if (tool == ScreenshotTool.Number)
        {
            foreach (var (kind, label) in new[] { (ScreenshotNumberStyle.Circle, "丸"), (ScreenshotNumberStyle.Square, "四角"), (ScreenshotNumberStyle.Plain, "囲みなし") })
                Choice(label, kind.ToString(), current with { Number = kind });
            menu.Items.Add(new MenuFlyoutSeparator());
            Choice("塗りつぶし", "Fill", current with { Filled = !current.Filled }, current.Number != ScreenshotNumberStyle.Plain);
            ((ToggleMenuFlyoutItem)menu.Items[^1]).IsChecked = current.Filled;
        }
        else
        {
            Choice("輪郭のみ", "Outline", current with { Filled = false });
            Choice(tool == ScreenshotTool.Rectangle ? "塗りつぶし（情報隠し）" : "塗りつぶし", "Fill", current with { Filled = true });
        }
        menu.ShowAt(anchor);
    }
    private void ApplyShapeStyle(ScreenshotTool tool, ScreenshotShapeStyle style)
    {
        var selected = SelectedObjects.Where(a => a.Tool == tool || tool == ScreenshotTool.Rectangle && a.Tool == ScreenshotTool.Redact).ToArray();
        if (selected.Length > 0) _document.ReplaceMany(selected.Select(style.Apply));
        else
        {
            SelectTool(tool); _toolPreferences.SetShape(tool, style); _shapePreferenceChanges[tool] = style; SavePreferences();
        }
        ShowSelectedOptions(); Refresh();
    }
}
