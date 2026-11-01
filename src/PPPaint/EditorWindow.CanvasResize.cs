using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PPPaint.Core;

namespace PPPaint;

public sealed partial class EditorWindow
{
    private async void Resize_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var state = _document.State; var f = state.Frame;
        var width = new NumberBox { Minimum = 1, Maximum = 16384, Value = f.Width, SmallChange = 1, ValidationMode = NumberBoxValidationMode.Disabled, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        var height = new NumberBox { Minimum = 1, Maximum = 16384, Value = f.Height, SmallChange = 1, ValidationMode = NumberBoxValidationMode.Disabled, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        AutomationProperties.SetAutomationId(width, "DialogImageWidth"); AutomationProperties.SetAutomationId(height, "DialogImageHeight");
        DialogField(width, "幅 (px)", "W"); DialogField(height, "高さ (px)", "H");
        var ratio = new CheckBox { IsChecked = true, FontSize = 16 };
        CommandLabel(ratio, "縦横比を固定", "Alt+R"); ratio.AccessKey = "R"; AutomationProperties.SetAutomationId(ratio, "DialogImageRatio");
        var centered = new CheckBox { IsChecked = true, FontSize = 16 };
        CommandLabel(centered, "両端を連動", "Alt+B"); centered.AccessKey = "B"; AutomationProperties.SetAutomationId(centered, "DialogCanvasCentered");
        ToolTipService.SetToolTip(centered, "中心を固定して左右・上下を増減 (Alt+B)");
        using var preview = new CanvasResizePreviewPane(PaintCanvas, state, _renderCache);
        var error = new TextBlock { FontSize = 16, TextWrapping = TextWrapping.Wrap, MaxWidth = 442 };
        AutomationProperties.SetAutomationId(error, "CanvasResizeError");
        var inputs = new Grid { ColumnSpacing = 12 };
        inputs.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); inputs.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var widthDelta = new TextBlock { FontSize = 16 }; var heightDelta = new TextBlock { FontSize = 16 };
        var widthChange = new Border { Child = widthDelta, Padding = new(4, 2, 4, 2) }; var heightChange = new Border { Child = heightDelta, Padding = new(4, 2, 4, 2) };
        AutomationProperties.SetAutomationId(widthDelta, "CanvasResizeWidthDelta"); AutomationProperties.SetAutomationId(heightDelta, "CanvasResizeHeightDelta");
        var widthPanel = new StackPanel { Spacing = 2 }; widthPanel.Children.Add(width); widthPanel.Children.Add(widthChange);
        var heightPanel = new StackPanel { Spacing = 2 }; heightPanel.Children.Add(height); heightPanel.Children.Add(heightChange);
        Grid.SetColumn(heightPanel, 1); inputs.Children.Add(widthPanel); inputs.Children.Add(heightPanel);
        var options = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 }; options.Children.Add(ratio); options.Children.Add(centered);
        var magnification = new NumberBox { Width = 144, Minimum = .25, Maximum = 16, Value = 4, SmallChange = .25, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, AccessKey = "V" };
        AutomationProperties.SetName(magnification, "拡大図の倍率"); AutomationProperties.SetAcceleratorKey(magnification, "Alt+V");
        ToolTipService.SetToolTip(magnification, "拡大図の倍率 0.25～16倍 (Alt+V)"); AutomationProperties.SetAutomationId(magnification, "CanvasResizeMagnification");
        var visiblePixels = new TextBlock { Text = "25 × 25 px", FontSize = 16, VerticalAlignment = VerticalAlignment.Bottom, Margin = new(0, 0, 0, 6) };
        AutomationProperties.SetAutomationId(visiblePixels, "CanvasResizeVisiblePixels");
        var previewOptions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 }; previewOptions.Children.Add(KeyLabel("倍率", "V")); previewOptions.Children.Add(magnification); previewOptions.Children.Add(visiblePixels);
        magnification.ValueChanged += (_, args) =>
        {
            if (!double.IsFinite(args.NewValue) || args.NewValue <= 0) return;
            preview.SetMagnification((float)args.NewValue); visiblePixels.Text = $"{100 / args.NewValue:0.##} × {100 / args.NewValue:0.##} px";
        };
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        void Legend(string text, Windows.UI.Color color, bool dashed, string id)
        {
            var item = new Grid();
            var outline = new Microsoft.UI.Xaml.Shapes.Rectangle { Stroke = new SolidColorBrush(color), StrokeThickness = 1, IsHitTestVisible = false };
            if (dashed) outline.StrokeDashArray = new DoubleCollection { 4, 2 };
            item.Children.Add(outline);
            var label = new TextBlock { Text = text, FontSize = 16, Margin = new(5, 2, 5, 2) };
            AutomationProperties.SetAutomationId(label, id); item.Children.Add(label); legend.Children.Add(item);
        }
        Legend("元: 橙の破線", Windows.UI.Color.FromArgb(255, 196, 100, 0), true, "CanvasResizeLegendOriginal");
        Legend("変更後: 青", Windows.UI.Color.FromArgb(255, 0, 112, 192), false, "CanvasResizeLegendCandidate");
        Legend("切り取り: 暗色", Windows.UI.Color.FromArgb(255, 92, 92, 92), false, "CanvasResizeLegendRemoved");
        var panel = new StackPanel { Spacing = 6, MinWidth = 442 }; panel.Children.Add(inputs); panel.Children.Add(options); panel.Children.Add(previewOptions); panel.Children.Add(preview.View); panel.Children.Add(legend); panel.Children.Add(error);
        var dialog = new EditorDialog { Title = "キャンバスサイズ", Content = panel, PrimaryButtonText = "適用", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Primary };
        var updating = false; double pendingWidth = f.Width, pendingHeight = f.Height; ScreenshotCanvasResize? candidate = null, customReference = null;
        void UpdateDeltas()
        {
            void Label(TextBlock text, Border box, int original, double value)
            {
                var delta = value - original;
                text.Text = double.IsFinite(value) ? $"元 {original} px  ({delta:+0.###;-0.###;0} px)" : $"元 {original} px  (入力中)";
                var color = !double.IsFinite(delta) || delta == 0 ? Microsoft.UI.Colors.Gray : delta > 0 ? Windows.UI.Color.FromArgb(255, 0, 100, 190) : Windows.UI.Color.FromArgb(255, 190, 30, 30);
                text.Foreground = new SolidColorBrush(color);
                box.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(delta == 0 || !double.IsFinite(delta) ? (byte)0 : (byte)24, color.R, color.G, color.B));
            }
            Label(widthDelta, widthChange, f.Width, pendingWidth); Label(heightDelta, heightChange, f.Height, pendingHeight);
        }
        void UpdatePreview()
        {
            if (updating) return;
            UpdateDeltas();
            try
            {
                if (!double.IsFinite(pendingWidth) || !double.IsFinite(pendingHeight) || pendingWidth != Math.Truncate(pendingWidth) || pendingHeight != Math.Truncate(pendingHeight)) throw new ArgumentException("幅と高さを整数で入力してください。");
                var nextWidth = checked((int)pendingWidth); var nextHeight = checked((int)pendingHeight);
                var next = customReference is { } custom
                    ? ScreenshotCanvasResize.FromBounds(state, new(-custom.OffsetX + (centered.IsChecked == true ? (custom.Width - nextWidth) / 2 : 0), -custom.OffsetY + (centered.IsChecked == true ? (custom.Height - nextHeight) / 2 : 0), nextWidth, nextHeight))
                    : ScreenshotCanvasResize.Create(state, nextWidth, nextHeight, centered.IsChecked == true);
                error.Text = ""; error.Visibility = Visibility.Collapsed; dialog.IsPrimaryButtonEnabled = true;
                candidate = next; preview.FromCenter = centered.IsChecked == true; preview.KeepRatio = ratio.IsChecked == true; preview.SetLayout(next);
            }
            catch (Exception ex) when (ex is ArgumentException or OverflowException)
            {
                error.Text = ex.Message + " プレビューは直前の有効な値です。"; error.Visibility = Visibility.Visible; dialog.IsPrimaryButtonEnabled = false;
            }
        }
        void ChangeDimension(bool horizontal, double value)
        {
            if (updating) return;
            updating = true;
            try
            {
                if (horizontal) pendingWidth = value; else pendingHeight = value;
                if (ratio.IsChecked == true && double.IsFinite(value) && value == Math.Truncate(value) && value is >= 1 and <= 16384)
                {
                    if (horizontal) height.Value = pendingHeight = Math.Clamp(Math.Round(value * f.Height / f.Width), 1, 16384);
                    else width.Value = pendingWidth = Math.Clamp(Math.Round(value * f.Width / f.Height), 1, 16384);
                }
            }
            finally { updating = false; }
            UpdatePreview();
        }
        width.ValueChanged += (_, e) => ChangeDimension(true, e.NewValue);
        height.ValueChanged += (_, e) => ChangeDimension(false, e.NewValue);
        var editors = new HashSet<TextBox>();
        void BindEditor(NumberBox number, bool horizontal)
        {
            // NumberBox.Value commits on blur/Enter. Observe its editor without
            // rewriting the text/caret, so each valid typed integer previews immediately.
            number.ApplyTemplate();
            if (EditorDialog.Descendants(number).OfType<TextBox>().FirstOrDefault() is not { } input || !editors.Add(input)) return;
            AutomationProperties.SetAutomationId(input, horizontal ? "CanvasResizeWidthInput" : "CanvasResizeHeightInput");
            input.TextChanged += (_, _) => ChangeDimension(horizontal, double.TryParse(input.Text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out var value) ? value : double.NaN);
        }
        width.Loaded += (_, _) => BindEditor(width, true); height.Loaded += (_, _) => BindEditor(height, false);
        dialog.Opened += (_, _) =>
        {
            BindEditor(width, true); BindEditor(height, false);
        };
        preview.LayoutRequested += layout =>
        {
            updating = true;
            try { width.Value = pendingWidth = layout.Width; height.Value = pendingHeight = layout.Height; }
            finally { updating = false; }
            customReference = candidate = layout; error.Text = ""; error.Visibility = Visibility.Collapsed; dialog.IsPrimaryButtonEnabled = true;
            preview.SetLayout(layout); UpdateDeltas();
        };
        centered.Checked += (_, _) => { customReference = null; UpdatePreview(); }; centered.Unchecked += (_, _) => { customReference = null; UpdatePreview(); };
        ratio.Checked += (_, _) => preview.KeepRatio = true; ratio.Unchecked += (_, _) => preview.KeepRatio = false;
        dialog.PrimaryButtonClick += (_, args) => { UpdatePreview(); if (!dialog.IsPrimaryButtonEnabled) args.Cancel = true; };
        UpdatePreview();
        if (await ShowDialogAsync(dialog, width) != ContentDialogResult.Primary || candidate is not { } layout) return;
        if (layout.Width == f.Width && layout.Height == f.Height && layout.OffsetX == 0 && layout.OffsetY == 0) return;
        if (!ReferenceEquals(state, _document.State)) throw new InvalidOperationException("編集中の画像が変わったため適用できません。");
        _document.Apply(layout.Apply(state));
        _region = null; _selectedIds.Clear(); _hasImage = true;
        ShowSelectedOptions(); Refresh(); FitImage();
    });
}
