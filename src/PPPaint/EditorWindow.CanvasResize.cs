using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
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
        Grid.SetColumn(height, 1); inputs.Children.Add(width); inputs.Children.Add(height);
        var options = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 }; options.Children.Add(ratio); options.Children.Add(centered);
        var panel = new StackPanel { Spacing = 8, MinWidth = 442 }; panel.Children.Add(inputs); panel.Children.Add(options); panel.Children.Add(preview.View); panel.Children.Add(error);
        var dialog = new EditorDialog { Title = "キャンバスサイズ", Content = panel, PrimaryButtonText = "適用", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Primary };
        var updating = false; double pendingWidth = f.Width, pendingHeight = f.Height; ScreenshotCanvasResize? candidate = null;
        void UpdatePreview()
        {
            if (updating) return;
            try
            {
                if (!double.IsFinite(pendingWidth) || !double.IsFinite(pendingHeight) || pendingWidth != Math.Truncate(pendingWidth) || pendingHeight != Math.Truncate(pendingHeight)) throw new ArgumentException("幅と高さを整数で入力してください。");
                var next = ScreenshotCanvasResize.Create(state, checked((int)pendingWidth), checked((int)pendingHeight), centered.IsChecked == true);
                error.Text = ""; error.Visibility = Visibility.Collapsed; dialog.IsPrimaryButtonEnabled = true;
                candidate = next; preview.SetLayout(next);
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
        centered.Checked += (_, _) => UpdatePreview(); centered.Unchecked += (_, _) => UpdatePreview();
        dialog.PrimaryButtonClick += (_, args) => { UpdatePreview(); if (!dialog.IsPrimaryButtonEnabled) args.Cancel = true; };
        UpdatePreview();
        if (await ShowDialogAsync(dialog, width) != ContentDialogResult.Primary || candidate is not { } layout) return;
        if (layout.Width == f.Width && layout.Height == f.Height) return;
        if (!ReferenceEquals(state, _document.State)) throw new InvalidOperationException("編集中の画像が変わったため適用できません。");
        _document.ResizeCanvas(layout.Width, layout.Height, centered.IsChecked == true);
        _region = null; _selectedIds.Clear(); _hasImage = true;
        ShowSelectedOptions(); Refresh(); FitImage();
    });
}
