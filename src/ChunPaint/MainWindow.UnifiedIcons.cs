using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using ChunPaint.Core;

namespace ChunPaint;

public sealed partial class MainWindow
{
    private static readonly Dictionary<string, string> UnifiedIconLabels = new(StringComparer.Ordinal)
    {
        ["保存"] = "A-save", ["共有"] = "A-share", ["元に戻す"] = "A-undo", ["やり直す"] = "A-redo", ["やり直し"] = "A-redo",
        ["ボタン表示モード"] = "A-display-mode", ["ズームアウト"] = "A-zoom-out", ["ズームイン"] = "A-zoom-in",
        ["フィードバック"] = "A-feedback", ["アカウント"] = "A-account", ["設定"] = "A-settings",
        ["矩形選択"] = "A-select-rectangle", ["自由選択"] = "A-select-free", ["すべて選択"] = "A-select-all",
        ["選択をキャンセル"] = "A-cancel", ["オブジェクトをキャンセル"] = "A-cancel", ["レイヤーパネルを閉じる"] = "A-cancel",
        ["透明な選択"] = "A-select-transparent", ["選択範囲を削除"] = "A-delete", ["削除"] = "A-delete",
        ["トリミング"] = "A-crop", ["サイズ変更"] = "A-resize", ["キャンバスにインポート"] = "A-import", ["背景の削除"] = "A-background-remove",
        ["回転"] = "A-rotate", ["回転メニュー"] = "A-rotate", ["鉛筆・ブラシの種類"] = "A-pencil", ["ブラシ"] = "A-brush",
        ["塗りつぶしツール"] = "A-bucket-fill", ["テキストツール"] = "A-text", ["消しゴムツール"] = "A-eraser",
        ["スポイトツール"] = "A-eyedropper", ["拡大鏡ツール"] = "A-magnifier", ["色の編集"] = "A-edit-colors",
        ["レイヤー"] = "A-layers", ["レイヤーを追加"] = "A-layer-add", ["レイヤーを上へ移動"] = "A-layer-up", ["上へ移動"] = "A-layer-up",
        ["レイヤーを下へ移動"] = "A-layer-down", ["下へ移動"] = "A-layer-down", ["選択レイヤーを結合"] = "A-layer-merge",
        ["レイヤー名を変更"] = "A-layer-rename", ["名前の変更"] = "A-layer-rename", ["非表示"] = "A-layer-visible", ["表示"] = "A-layer-hidden",
        ["ペンの太さ"] = "A-stroke-width", ["線幅"] = "A-stroke-width", ["不透明度"] = "A-opacity",
        ["ポインター座標"] = "A-pointer-position", ["キャンバスサイズ"] = "A-canvas-size", ["ズームを100パーセントに戻す"] = "A-zoom-reset",
        ["太字"] = "A-text-bold", ["斜体"] = "A-text-italic", ["下線"] = "A-text-underline", ["取り消し線"] = "A-text-strike",
        ["輪郭の種類（ペン）"] = "E-outline-solid", ["塗りの種類（ペンキ）"] = "E-fill-solid",
        ["描画・保存設定"] = "A-settings", ["更新を確認・設定"] = "A-settings", ["画像を一括変換"] = "A-resize",
        ["モザイク"] = "B-brush-pixel", ["ぼかし"] = "A-opacity", ["黒塗り"] = "A-bucket-fill",
        ["明るさ・コントラスト・彩度"] = "A-edit-colors", ["背景の境界調整"] = "A-background-remove",
        ["選択範囲の背景を戻す"] = "A-undo", ["選択範囲の背景を消す"] = "A-eraser", ["AI画像編集・生成消去"] = "A-edit-colors",
        ["開く"] = "A-import", ["ファイルから"] = "A-import", ["プロジェクトとして保存"] = "A-save", ["終了"] = "A-cancel",
        ["油彩ブラシ"] = "B-brush-oil", ["水彩ブラシ"] = "B-brush-watercolor", ["マーカー"] = "B-brush-marker",
        ["鉛筆風ブラシ"] = "B-brush-natural-pencil", ["エアブラシ"] = "B-brush-airbrush", ["カリグラフィー"] = "B-brush-calligraphy",
        ["クレヨン"] = "B-brush-crayon", ["平筆"] = "B-brush-flat", ["ピクセルペン"] = "B-brush-pixel"
    };

    private static BitmapIcon CreateUnifiedIcon(string id, double size = 16)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
            throw new ArgumentException("アイコンIDが不正です。", nameof(id));
        return new BitmapIcon
        {
            UriSource = new Uri($"ms-appx:///Assets/RibbonIcons/unified/{id}-64.png"),
            Width = size, Height = size, ShowAsMonochrome = true, IsHitTestVisible = false
        };
    }

    private void InitializeUnifiedIcons()
    {
        RootLayout.Loaded += (_, _) =>
        {
            ApplyUnifiedIcons(RootLayout);
            ReplaceStatusGlyph(PointerStatusText, "A-pointer-position");
            ReplaceStatusGlyph(CanvasSizeText, "A-canvas-size");
            UpdateShapeStyleVisuals();
        };
    }

    private static string GetBrushUnifiedIconId(BrushKind kind) => "B-brush-" + (kind == BrushKind.NaturalPencil ? "natural-pencil" : kind.ToString().ToLowerInvariant());

    private void SetUnifiedButtonIcon(ContentControl button, string id)
    {
        var icon = CreateUnifiedIcon(id, 22);
        _buttonIconContents[button] = icon;
        if (!_textButtonDisplayMode) button.Content = icon;
    }

    private static void ReplaceStatusGlyph(FrameworkElement label, string id)
    {
        if (label.Parent is Panel panel && panel.Children.Count > 1 && panel.Children[0] != label)
            panel.Children[0] = CreateUnifiedIcon(id, 18);
    }

    private void ApplyUnifiedIcons(DependencyObject root)
    {
        // テンプレートを置換する前に子を確保し、既存の名前付き要素とメニューも巡回する。
        var children = Enumerable.Range(0, VisualTreeHelper.GetChildrenCount(root))
            .Select(index => VisualTreeHelper.GetChild(root, index)).ToArray();
        if (root is ButtonBase button)
        {
            var label = AutomationProperties.GetName(button);
            string? id = null;
            if (button.Tag is string shape && Enum.TryParse<ChunPaint.Core.EditableShapeKind>(shape, out _)) id = "D-" + shape;
            else UnifiedIconLabels.TryGetValue(label, out id);
            if (id is not null)
            {
                var icon = CreateUnifiedIcon(id, id.StartsWith("D-", StringComparison.Ordinal) ? 16 : 22);
                if (!_textButtonDisplayMode) button.Content = icon;
                _buttonIconContents[button] = icon;
                if (ReferenceEquals(button, PencilToolButton))
                {
                    var pair = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
                    pair.Children.Add(CreateUnifiedIcon("A-pencil", 20));
                    pair.Children.Add(CreateUnifiedIcon("A-brush", 15));
                    _buttonIconContents[button] = pair;
                    if (!_textButtonDisplayMode) button.Content = pair;
                }
            }
            if (button is Button { Flyout: MenuFlyout menu }) ApplyUnifiedMenuIcons(menu.Items);
        }
        if (root is MenuBar menuBar)
            foreach (var item in menuBar.Items) ApplyUnifiedMenuIcons(item.Items);
        if (root is TextBlock glyph && glyph.Parent is Panel glyphPanel &&
            AutomationProperties.GetName(glyph) is "線幅" or "不透明度")
        {
            var index = glyphPanel.Children.IndexOf(glyph);
            if (index >= 0) glyphPanel.Children[index] = CreateUnifiedIcon(AutomationProperties.GetName(glyph) == "線幅" ? "A-stroke-width" : "A-opacity", 20);
        }
        foreach (var child in children) ApplyUnifiedIcons(child);
    }

    private static void ApplyUnifiedMenuIcons(IEnumerable<MenuFlyoutItemBase> items)
    {
        foreach (var item in items)
        {
            if (item is MenuFlyoutSubItem submenu)
            {
                if (UnifiedIconLabels.TryGetValue(submenu.Text, out var subId)) submenu.Icon = CreateUnifiedIcon(subId);
                ApplyUnifiedMenuIcons(submenu.Items);
            }
            else if (item is MenuFlyoutItem action && UnifiedIconLabels.TryGetValue(action.Text, out var id)) action.Icon = CreateUnifiedIcon(id);
            else if (item is ToggleMenuFlyoutItem toggle && UnifiedIconLabels.TryGetValue(toggle.Text, out var toggleId)) toggle.Icon = CreateUnifiedIcon(toggleId);
        }
    }
}
