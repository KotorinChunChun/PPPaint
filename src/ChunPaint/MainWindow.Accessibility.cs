using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ChunPaint;

public sealed partial class MainWindow
{
    private static void ConfigureDialogAccessibility(ContentDialog dialog)
    {
        var title = dialog.Title?.ToString() ?? "ダイアログ";
        AutomationProperties.SetName(dialog, title);
        AutomationProperties.SetHelpText(dialog, "Tabで項目を移動し、Escでキャンセルできます");
    }

    private static void ConfigureFieldAccessibility(DependencyObject control, string name, string helpText)
    {
        AutomationProperties.SetName(control, name);
        AutomationProperties.SetHelpText(control, helpText);
        if (control is Control frameworkControl)
        {
            ToolTipService.SetToolTip(frameworkControl, name);
        }
    }

    private void ConfigureAccessibility()
    {
        ApplyTagAccessibility(RootLayout);
    }

    private static void ApplyTagAccessibility(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is Control control && control.Tag is string tag && TryGetTagLabel(tag, out var label))
            {
                AutomationProperties.SetName(control, label);
                AutomationProperties.SetHelpText(control, $"{label}を選択します");
                ToolTipService.SetToolTip(control, label);
            }

            ApplyTagAccessibility(child);
        }
    }

    private static bool TryGetTagLabel(string tag, out string label)
    {
        label = tag switch
        {
            "Select" => "選択ツール",
            "Pencil" => "鉛筆ツール",
            "Fill" => "塗りつぶしツール",
            "Text" => "テキストツール",
            "Eraser" => "消しゴムツール",
            "Picker" => "スポイトツール",
            "Brush" => "ブラシの種類",
            "Line" => "直線",
            "Curve" => "曲線",
            "Ellipse" => "楕円",
            "Rectangle" => "四角形",
            "RoundedRectangle" => "角丸四角形",
            "Parallelogram" => "平行四辺形",
            "Triangle" => "三角形",
            "RightTriangle" => "直角三角形",
            "Diamond" => "ひし形",
            "Pentagon" => "五角形",
            "Hexagon" => "六角形",
            "RightArrow" => "右矢印",
            "LeftArrow" => "左矢印",
            "UpArrow" => "上矢印",
            "DownArrow" => "下矢印",
            "FourWayArrow" => "四方向矢印",
            "Star" => "星",
            "Explosion" => "爆発形",
            "Callout" => "吹き出し",
            "Cloud" => "雲",
            "Heart" => "ハート",
            "Lightning" => "稲妻",
            _ => string.Empty
        };
        return label.Length != 0;
    }
}
