using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PPPaint.Core;
using Windows.System;

namespace PPPaint;

public sealed partial class EditorWindow
{
    private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        if (_dialogOpen) throw new InvalidOperationException("別の画面を操作中です。");
        _dialogOpen = true;
        try { dialog.XamlRoot = Root.XamlRoot; dialog.RequestedTheme = Root.ActualTheme; return await dialog.ShowManagedAsync(); }
        finally { _dialogOpen = false; }
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
        var folder = new TextBox { Text = directory, FontSize = 16, MinWidth = 450 };
        AutomationProperties.SetName(folder, "フォルダー");
        var name = new TextBox { Text = save ? Path.GetFileNameWithoutExtension(_sourceName ?? "スクリーンショット") + ".png" : "", PlaceholderText = save ? "保存するPNGファイル名" : "画像ファイル名", FontSize = 16 };
        AutomationProperties.SetName(name, "ファイル名");
        var error = new TextBlock { FontSize = 16, TextWrapping = TextWrapping.Wrap };
        var list = new ListView { Height = 280, IsItemClickEnabled = true, SelectionMode = ListViewSelectionMode.Single };
        AutomationProperties.SetName(list, "ファイル一覧");
        var up = new Button { Content = "上へ", FontSize = 16 };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 }; row.Children.Add(up); row.Children.Add(folder);
        var panel = new StackPanel { Spacing = 8 }; panel.Children.Add(row); panel.Children.Add(list); panel.Children.Add(name); panel.Children.Add(error);
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
        up.Click += (_, _) => { var parent = Directory.GetParent(directory); if (parent is not null) LoadFolder(parent.FullName); };
        folder.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) { LoadFolder(folder.Text); e.Handled = true; } };
        list.ItemClick += (_, e) =>
        {
            if ((e.ClickedItem as ListViewItem)?.Tag is not FileEntry entry) return;
            if (entry.IsFolder) LoadFolder(entry.Path); else name.Text = Path.GetFileName(entry.Path);
        };
        var dialog = new ContentDialog { Title = title ?? (save ? "PNG保存" : "画像を開く"), Content = panel, PrimaryButtonText = save ? "保存" : "開く", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Primary };
        dialog.PrimaryButtonClick += (_, e) =>
        {
            try
            {
                var path = Path.GetFullPath(Path.Combine(folder.Text, name.Text.Trim()));
                if (Directory.Exists(path)) { LoadFolder(path); e.Cancel = true; return; }
                if (save)
                {
                    if (string.IsNullOrWhiteSpace(name.Text)) throw new ArgumentException("ファイル名を入力してください。");
                    if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) path += ".png";
                    if (!Directory.Exists(Path.GetDirectoryName(path))) throw new DirectoryNotFoundException("保存先フォルダーがありません。");
                }
                else if (!File.Exists(path) || !IsImage(path)) throw new FileNotFoundException("画像ファイルを選択してください。");
                _lastFolder = Path.GetDirectoryName(path); chosen = path;
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { error.Text = ex.Message; e.Cancel = true; }
        };
        LoadFolder(directory);
        return await ShowDialogAsync(dialog) == ContentDialogResult.Primary ? chosen : null;
    }
    private async void CustomColor_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var picker = new ColorPicker { Color = ToColor(_color), IsAlphaEnabled = false, IsColorChannelTextInputVisible = true, IsHexInputVisible = true, IsAlphaTextInputVisible = false };
        var dialog = new ContentDialog { Title = "色", Content = picker, PrimaryButtonText = "適用", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Primary };
        if (await ShowDialogAsync(dialog) == ContentDialogResult.Primary) { var c = picker.Color; SetColor(new(c.R, c.G, c.B)); }
    });
    private async void Settings_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var reset = new Button { Content = "描画設定を初期値に戻す", FontSize = 16 };
        reset.Click += (_, _) => { _updatingControls = true; _color = new(220, 38, 38); WidthBox.Value = 3; FontBox.Value = 24; BlurRadiusBox.Value = 8; _updatingControls = false; SavePreferences(); Refresh(); };
        await ShowDialogAsync(new() { Title = "設定", Content = reset, CloseButtonText = "閉じる" });
    });
}
