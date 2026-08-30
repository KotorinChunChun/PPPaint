using System.Text.Json;
using ChunPaint.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;

namespace ChunPaint;

public sealed partial class MainWindow
{
    private const int MaximumRecentFiles = 10;
    private const string RecentFilesSetting = "recentFiles";
    private readonly List<string> _recentFilePaths = [];

    private void LoadRecentFiles()
    {
        try
        {
            if (ApplicationData.Current.LocalSettings.Values[RecentFilesSetting] is not string serialized)
            {
                RefreshRecentFilesMenu();
                return;
            }

            var paths = JsonSerializer.Deserialize<string[]>(serialized) ?? [];
            foreach (var path in paths)
            {
                if (IsSafeRecentFilePath(path) && !_recentFilePaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    _recentFilePaths.Add(path);
                }
                if (_recentFilePaths.Count >= MaximumRecentFiles)
                {
                    break;
                }
            }
        }
        catch
        {
            _recentFilePaths.Clear();
        }
        RefreshRecentFilesMenu();
    }

    private void AddRecentFile(StorageFile file)
    {
        if (!IsSafeRecentFilePath(file.Path))
        {
            return;
        }

        _recentFilePaths.RemoveAll(path => path.Equals(file.Path, StringComparison.OrdinalIgnoreCase));
        _recentFilePaths.Insert(0, file.Path);
        if (_recentFilePaths.Count > MaximumRecentFiles)
        {
            _recentFilePaths.RemoveRange(MaximumRecentFiles, _recentFilePaths.Count - MaximumRecentFiles);
        }

        try
        {
            ApplicationData.Current.LocalSettings.Values[RecentFilesSetting] = JsonSerializer.Serialize(_recentFilePaths);
        }
        catch
        {
            // Recent-file persistence is optional and must not block a successful save/open.
        }
        RefreshRecentFilesMenu();
    }

    private void RefreshRecentFilesMenu()
    {
        if (RecentFilesMenu is null)
        {
            return;
        }

        RecentFilesMenu.Items.Clear();
        var validPaths = _recentFilePaths.Where(IsSafeRecentFilePath).ToArray();
        _recentFilePaths.Clear();
        _recentFilePaths.AddRange(validPaths);
        if (_recentFilePaths.Count == 0)
        {
            RecentFilesMenu.Items.Add(new MenuFlyoutItem { Text = "履歴はありません", IsEnabled = false });
            return;
        }

        foreach (var path in _recentFilePaths)
        {
            var item = new MenuFlyoutItem { Text = Path.GetFileName(path), Tag = path };
            ToolTipService.SetToolTip(item, path);
            item.Click += RecentFile_Click;
            RecentFilesMenu.Items.Add(item);
        }
    }

    private static bool IsSafeRecentFilePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            return false;
        }
        try
        {
            return File.Exists(path) && !Directory.Exists(path) && DocumentFileFormats.IsSupported(path);
        }
        catch
        {
            return false;
        }
    }

    private async void RecentFile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string path)
        {
            return;
        }

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            if (!await ConfirmDiscardAsync())
            {
                return;
            }
            CommitFloatingSelection();
            await LoadStorageFileAsync(file, import: false);
        }
        catch (Exception exception)
        {
            _recentFilePaths.RemoveAll(item => item.Equals(path, StringComparison.OrdinalIgnoreCase));
            RefreshRecentFilesMenu();
            await ShowInfoAsync("最近使ったファイル", $"ファイルを開けませんでした。履歴から削除しました。\n{exception.Message}");
        }
    }
}
