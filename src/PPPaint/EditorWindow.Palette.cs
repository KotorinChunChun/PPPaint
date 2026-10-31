using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PPPaint.Core;
using PPPaint.Imaging;

namespace PPPaint;

public sealed partial class EditorWindow
{
    private Rgba32[] _palette = DefaultPalette();
    private readonly List<ToggleButton> _paletteButtons = [];
    private int _activePaletteIndex;
    private bool _pickingColor, _updatingPalette, _updatingPick;
    private static Rgba32[] DefaultPalette() => new[] { "#DC2626", "#EA580C", "#FACC15", "#16A34A", "#2563EB", "#9333EA", "#000000", "#FFFFFF" }.Select(ParseColor).ToArray();
    private static string ColorHex(Rgba32 color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private void BuildPalette()
    {
        for (var i = 0; i < _palette.Length; i++)
        {
            var index = i;
            var swatch = new Border { Width = 24, Height = 24, BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Colors.Gray) };
            var selectionFrame = new Border { Width = 32, Height = 32, BorderThickness = new Thickness(3), Padding = new Thickness(1), Child = swatch };
            var button = new ToggleButton { Width = 36, Height = 36, MinWidth = 36, MinHeight = 36, Padding = new Thickness(1), Content = selectionFrame, BorderThickness = new Thickness(0) };
            AutomationProperties.SetAutomationId(button, "PaletteColor" + index);
            button.Click += (_, _) => SelectPaletteColor(index);
            button.Checked += (_, _) => SelectPaletteColor(index);
            button.AddHandler(UIElement.DoubleTappedEvent, new DoubleTappedEventHandler(async (_, e) => { e.Handled = true; await RunAsync(() => EditPaletteColorAsync(index)); }), true);
            var menu = new MenuFlyout();
            var edit = new MenuFlyoutItem { Text = "この色を編集" };
            AutomationProperties.SetAutomationId(edit, "EditPaletteColor" + index);
            edit.Click += async (_, _) => await RunAsync(() => EditPaletteColorAsync(index));
            menu.Items.Add(edit); button.ContextFlyout = menu;
            _paletteButtons.Add(button); ColorsPanel.Children.Add(button);
        }
    }
    private void RefreshPalette()
    {
        _updatingPalette = true;
        try
        {
        if (_activePaletteIndex < 0 || _activePaletteIndex >= _palette.Length || _palette[_activePaletteIndex] != _color)
            _activePaletteIndex = Array.FindIndex(_palette, color => color == _color);
        for (var i = 0; i < _paletteButtons.Count; i++)
        {
            var button = _paletteButtons[i]; var selected = i == _activePaletteIndex;
            var frame = (Border)button.Content;
            ((Border)frame.Child).Background = new SolidColorBrush(ToColor(_palette[i]));
            button.IsChecked = selected;
            frame.BorderBrush = new SolidColorBrush(selected ? Colors.DodgerBlue : Colors.Transparent);
            var name = ColorHex(_palette[i]);
            AutomationProperties.SetName(button, name); AutomationProperties.SetItemStatus(button, selected ? "選択中" : "");
            AutomationProperties.SetHelpText(button, name + "。ダブルクリックでこの色を編集");
            ToolTipService.SetToolTip(button, name + " — ダブルクリックで編集");
        }
        }
        finally { _updatingPalette = false; }
    }
    private void SelectPaletteColor(int index)
    {
        if (_updatingPalette) return;
        if (_busy || _dialogOpen) { RefreshPalette(); return; }
        SetColorPicking(false); _activePaletteIndex = index;
        if (SelectedObjects.Length == 0 && _drawingColor == _palette[index]) RefreshPalette(); else SetColor(_palette[index]);
    }
    private async Task EditPaletteColorAsync(int index)
    {
        SetColorPicking(false);
        var initial = index >= 0 && index < _palette.Length ? _palette[index] : _color;
        var picker = new ColorPicker { Color = ToColor(initial), IsAlphaEnabled = false, IsColorChannelTextInputVisible = true, IsHexInputVisible = false, IsAlphaTextInputVisible = false };
        AutomationProperties.SetAutomationId(picker, "PaletteColorPicker");
        var hex = new TextBox { Text = ColorHex(initial), FontSize = 16 };
        DialogField(hex, "RGB (16進数)", "H"); AutomationProperties.SetAutomationId(hex, "PaletteColorHex");
        var updating = false;
        picker.ColorChanged += (_, e) => { updating = true; hex.Text = ColorHex(new(e.NewColor.R, e.NewColor.G, e.NewColor.B)); updating = false; };
        hex.TextChanged += (_, _) => { if (updating) return; try { picker.Color = ToColor(ParseColor(hex.Text)); } catch (ArgumentException) { } };
        var error = new TextBlock { FontSize = 16, TextWrapping = TextWrapping.Wrap };
        var panel = new StackPanel { Spacing = 8 }; panel.Children.Add(picker); panel.Children.Add(hex); panel.Children.Add(error);
        var dialog = new EditorDialog { Title = index >= 0 ? "パレットの色" : "色", Content = panel, PrimaryButtonText = "適用", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Primary };
        dialog.PrimaryButtonClick += (_, e) => { try { ParseColor(hex.Text); } catch (ArgumentException ex) { error.Text = ex.Message; e.Cancel = true; } };
        if (await ShowDialogAsync(dialog, hex) != ContentDialogResult.Primary) return;
        var color = ParseColor(hex.Text);
        if (index >= 0 && index < _palette.Length) _palette[index] = color;
        _activePaletteIndex = index; SetColor(color);
        if (index >= 0 && SelectedObjects.Length > 0) SavePreferences();
    }
    private void Eyedropper_Click(object sender, RoutedEventArgs e) => SetColorPicking(EyedropperButton.IsChecked == true);
    private void SetColorPicking(bool enabled)
    {
        if (_updatingPick) return;
        if (enabled && (_busy || _dialogOpen || _pointerId is not null)) enabled = _pickingColor;
        _pickingColor = enabled;
        _updatingPick = true; EyedropperButton.IsChecked = enabled; _updatingPick = false;
        AutomationProperties.SetItemStatus(EyedropperButton, enabled ? "色を採取中" : "");
        if (PaintCanvas.IsLoaded) { if (enabled) InteractionCanvas.Focus(FocusState.Programmatic); UpdateCursor(_hoverPoint); }
    }
    private void PickCanvasColor(Vector2 point)
    {
        var state = _document.State;
        var x = Math.Clamp((int)Math.Floor(point.X), 0, state.Frame.Width - 1); var y = Math.Clamp((int)Math.Floor(point.Y), 0, state.Frame.Height - 1);
        try
        {
            using var rendered = ScreenshotRenderer.Render(PaintCanvas, state);
            var color = Rgba32.FromPremultipliedBgra(rendered.GetPixelBytes(x, y, 1, 1));
            SetColorPicking(false); SetDrawingColor(color);
            StatusText.Text = ColorHex(_drawingColor);
        }
        catch (Exception) { SetColorPicking(false); _ = RunAsync(() => MessageAsync("スポイト", "色を取得できませんでした。")); }
    }
}
