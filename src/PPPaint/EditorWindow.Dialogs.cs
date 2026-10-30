using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using PPPaint.Core;
using Windows.System;

namespace PPPaint;

public sealed partial class EditorWindow
{
    private sealed class EditorDialog : ContentDialog
    {
        public EditorDialog() => DefaultStyleKey = typeof(ContentDialog);
        protected override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            // ContentDialog later moves the template into its popup; capture buttons here.
            ConfigureDialogButton(GetTemplateChild("PrimaryButton") as Button, PrimaryButtonText, "S", "PrimaryButton", DefaultButton == ContentDialogButton.Primary);
            ConfigureDialogButton(GetTemplateChild("SecondaryButton") as Button, SecondaryButtonText, "D", "SecondaryButton", DefaultButton == ContentDialogButton.Secondary);
            ConfigureDialogButton(GetTemplateChild("CloseButton") as Button, CloseButtonText, "C", "CloseButton", DefaultButton == ContentDialogButton.Close);
        }
    }
    private async Task<ContentDialogResult> ShowDialogAsync(EditorDialog dialog, Control? initialFocus = null)
    {
        if (_dialogOpen) throw new InvalidOperationException("別の画面を操作中です。");
        _dialogOpen = true;
        var previousFocus = FocusManager.GetFocusedElement(Root.XamlRoot) as Control;
        dialog.IsAccessKeyScope = true; dialog.TabFocusNavigation = KeyboardNavigationMode.Cycle;
        AutomationProperties.SetHelpText(dialog, "Tab/Shift+Tabで移動、Enterで既定操作、Escで取消。Altと括弧内のキーで操作。");
        if (dialog.DefaultButton == ContentDialogButton.None && string.IsNullOrEmpty(dialog.PrimaryButtonText)) dialog.DefaultButton = ContentDialogButton.Close;
        dialog.Opened += (_, _) =>
        {
            initialFocus?.Focus(FocusState.Programmatic);
        };
        try { dialog.XamlRoot = Root.XamlRoot; dialog.RequestedTheme = Root.ActualTheme; return await dialog.ShowManagedAsync(); }
        finally { _dialogOpen = false; if (previousFocus?.IsLoaded == true) previousFocus.Focus(FocusState.Programmatic); }
    }
    private static void ConfigureDialogButton(Button? button, string label, string key, string templateName, bool isDefault)
    {
        if (button is null || string.IsNullOrEmpty(label)) return;
        var keys = "Alt+" + key + (isDefault ? ", Enter" : "") + (templateName == "CloseButton" ? ", Esc" : "");
        button.AccessKey = key; button.IsTabStop = true;
        AutomationProperties.SetAutomationId(button, "Dialog" + templateName); AutomationProperties.SetName(button, label);
        AutomationProperties.SetAcceleratorKey(button, keys); AutomationProperties.SetHelpText(button, label + " (" + keys + ")"); ToolTipService.SetToolTip(button, label + " (" + keys + ")");
        // Bind only the label. The suffix is a fixed key, never user-supplied XAML.
        var template = (DataTemplate)XamlReader.Load("<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><TextBlock FontSize=\"16\"><Run Text=\"{Binding}\"/><Run Text=\" (\"/><Underline><Run Text=\"" + key + "\"/></Underline><Run Text=\")\"/></TextBlock></DataTemplate>");
        button.ContentTemplate = template;
    }
    private static void DialogField(Control control, string name, string key)
    {
        var label = KeyLabel(name, key);
        if (control is TextBox text) text.Header = label;
        if (control is NumberBox number) number.Header = label;
        AutomationProperties.SetName(control, name); AutomationProperties.SetAcceleratorKey(control, "Alt+" + key);
        AutomationProperties.SetHelpText(control, $"{name} (Alt+{key})"); ToolTipService.SetToolTip(control, $"{name} (Alt+{key})"); control.AccessKey = key;
        control.AccessKeyInvoked += (_, args) => { control.Focus(FocusState.Programmatic); args.Handled = true; };
    }
    private Task<ContentDialogResult> AskAsync(string title, string text, string primary, string secondary = "") => ShowDialogAsync(new()
    {
        Title = title, Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 16 },
        PrimaryButtonText = primary, SecondaryButtonText = secondary, CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Close
    });
    private async Task MessageAsync(string title, string text) => await ShowDialogAsync(new()
    {
        Title = title, Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 16, MaxWidth = 500 }, CloseButtonText = "閉じる"
    });
    private sealed record FileEntry(string Path, string Label, bool IsFolder);
    private string? _lastFolder;
    private async Task<string?> PickPathAsync(bool save, string? title = null)
    {
        var directory = _lastFolder ?? (_savePath is not null ? Path.GetDirectoryName(_savePath) : null) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) directory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var folder = new TextBox { Text = directory, FontSize = 16 };
        AutomationProperties.SetName(folder, "フォルダー");
        AutomationProperties.SetAutomationId(folder, "DialogFolder"); DialogField(folder, "フォルダー", "L");
        var name = new TextBox { Text = save ? Path.GetFileNameWithoutExtension(_sourceName ?? "スクリーンショット") + ".png" : "", PlaceholderText = save ? "保存するPNGファイル名" : "画像ファイル名", FontSize = 16 };
        AutomationProperties.SetName(name, "ファイル名");
        AutomationProperties.SetAutomationId(name, "DialogFileName"); DialogField(name, "ファイル名", "N");
        var error = new TextBlock { FontSize = 16, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(error, "DialogValidationError");
        var list = new ListView { Height = 280, IsItemClickEnabled = true, SelectionMode = ListViewSelectionMode.Single };
        AutomationProperties.SetName(list, "ファイル一覧");
        AutomationProperties.SetAutomationId(list, "DialogFileList");
        var up = new Button { Content = "上へ", FontSize = 16 };
        CommandLabel(up, "上へ", "Alt+U"); up.AccessKey = "U"; up.VerticalAlignment = VerticalAlignment.Bottom;
        var row = new Grid { ColumnSpacing = 6 }; row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        Grid.SetColumn(folder, 1); row.Children.Add(up); row.Children.Add(folder);
        var panel = new StackPanel { Spacing = 8, MinWidth = 420 }; panel.Children.Add(row); panel.Children.Add(list); panel.Children.Add(name); panel.Children.Add(error);
        string? chosen = null;
        void LoadFolder(string path)
        {
            try
            {
                path = Path.GetFullPath(path);
                var directories = Directory.EnumerateDirectories(path).OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase).Take(1000).Select(p => new FileEntry(p, "▸ " + Path.GetFileName(p), true));
                var files = Directory.EnumerateFiles(path).Where(p => save ? Path.GetExtension(p).Equals(".png", StringComparison.OrdinalIgnoreCase) : IsImage(p)).OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase).Take(2000).Select(p => new FileEntry(p, Path.GetFileName(p), false));
                var entries = directories.Concat(files).ToArray();
                list.Items.Clear();
                foreach (var entry in entries) list.Items.Add(new ListViewItem { Content = entry.Label, Tag = entry, FontSize = 16 });
                directory = path; folder.Text = path; error.Text = "";
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { error.Text = e.Message; }
        }
        void ParentFolder() { var parent = Directory.GetParent(directory); if (parent is not null) LoadFolder(parent.FullName); }
        up.Click += (_, _) => ParentFolder();
        folder.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) { LoadFolder(folder.Text); list.Focus(FocusState.Programmatic); e.Handled = true; } };
        list.ItemClick += (_, e) =>
        {
            if ((e.ClickedItem as ListViewItem)?.Tag is not FileEntry entry) return;
            if (entry.IsFolder) LoadFolder(entry.Path); else name.Text = Path.GetFileName(entry.Path);
        };
        var dialog = new EditorDialog { Title = title ?? (save ? "PNG保存" : "画像を開く"), Content = panel, PrimaryButtonText = save ? "保存" : "開く", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Primary };
        bool ChoosePath()
        {
            try
            {
                var path = Path.GetFullPath(Path.Combine(folder.Text, name.Text.Trim()));
                if (Directory.Exists(path)) { LoadFolder(path); list.Focus(FocusState.Programmatic); return false; }
                if (save)
                {
                    if (string.IsNullOrWhiteSpace(name.Text)) throw new ArgumentException("ファイル名を入力してください。");
                    if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) path += ".png";
                    if (!Directory.Exists(Path.GetDirectoryName(path))) throw new DirectoryNotFoundException("保存先フォルダーがありません。");
                }
                else if (!File.Exists(path) || !IsImage(path)) throw new FileNotFoundException("画像ファイルを選択してください。");
                _lastFolder = Path.GetDirectoryName(path); chosen = path; return true;
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { error.Text = ex.Message; name.Focus(FocusState.Programmatic); return false; }
        }
        dialog.PrimaryButtonClick += (_, e) => e.Cancel = !ChoosePath();
        list.SelectionChanged += (_, _) => { if ((list.SelectedItem as ListViewItem)?.Tag is FileEntry { IsFolder: false } entry) name.Text = Path.GetFileName(entry.Path); };
        list.KeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Enter) return;
            e.Handled = true;
            if ((list.SelectedItem as ListViewItem)?.Tag is not FileEntry entry) return;
            if (entry.IsFolder) LoadFolder(entry.Path);
            else { name.Text = Path.GetFileName(entry.Path); if (ChoosePath()) dialog.Hide(); }
        };
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Up && KeyDown(VirtualKey.Menu)) { ParentFolder(); e.Handled = true; }
        };
        LoadFolder(directory);
        await ShowDialogAsync(dialog, name); return chosen;
    }
    private async void CustomColor_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var picker = new ColorPicker { Color = ToColor(_color), IsAlphaEnabled = false, IsColorChannelTextInputVisible = true, IsHexInputVisible = true, IsAlphaTextInputVisible = false };
        var dialog = new EditorDialog { Title = "色", Content = picker, PrimaryButtonText = "適用", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Primary };
        if (await ShowDialogAsync(dialog) == ContentDialogResult.Primary) { var c = picker.Color; SetColor(new(c.R, c.G, c.B)); }
    });
    private async void Settings_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var reset = new Button { Content = "描画設定を初期値に戻す", FontSize = 16 };
        CommandLabel(reset, "描画設定を初期値に戻す", "Alt+R"); reset.AccessKey = "R";
        reset.Click += (_, _) => { _updatingControls = true; _color = new(220, 38, 38); WidthBox.Value = 3; FontBox.Value = 24; BlurRadiusBox.Value = 8; _updatingControls = false; SavePreferences(); Refresh(); };
        await ShowDialogAsync(new() { Title = "設定", Content = reset, CloseButtonText = "閉じる" });
    });
}
