using System.Reflection;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI;
using WinRT.Interop;

namespace ChunPaint;

public sealed partial class MainWindow
{
    private DataTransferManager? _dataTransferManager;
    private InMemoryRandomAccessStream? _shareStream;

    private async void Share_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            if (_dataTransferManager is null)
            {
                _dataTransferManager = DataTransferManagerInterop.GetForWindow(hwnd);
                _dataTransferManager.DataRequested += Share_DataRequested;
            }

            DataTransferManagerInterop.ShowShareUIForWindow(hwnd);
        }
        catch (Exception exception)
        {
            await ShowInfoAsync("共有", $"Windows の共有画面を開けませんでした。\n{exception.Message}");
        }
    }

    private async void Share_DataRequested(DataTransferManager sender, DataRequestedEventArgs args)
    {
        var request = args.Request;
        var deferral = request.GetDeferral();
        try
        {
            EnsureSurface(PaintCanvas);
            _shareStream?.Dispose();
            _shareStream = new InMemoryRandomAccessStream();
            using var composite = CreateComposite();
            await composite.SaveAsync(_shareStream, CanvasBitmapFileFormat.Png);
            _shareStream.Seek(0);

            request.Data.Properties.Title = _currentFile?.DisplayName ?? "ChunPaint の画像";
            request.Data.Properties.Description = $"{_imageWidth} × {_imageHeight}px の画像";
            request.Data.SetBitmap(RandomAccessStreamReference.CreateFromStream(_shareStream));
        }
        catch (Exception exception)
        {
            request.FailWithDisplayText($"画像を共有用に準備できませんでした。{exception.Message}");
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async void Feedback_Click(object sender, RoutedEventArgs e)
    {
        var content = new StackPanel { Spacing = 8, MaxWidth = 430 };
        content.Children.Add(new TextBlock
        {
            Text = "ご意見や不具合報告は Windows のフィードバック Hub から送信できます。送信前に個人情報が含まれていないか確認してください。",
            TextWrapping = TextWrapping.Wrap
        });
        var dialog = new ContentDialog
        {
            XamlRoot = PaintCanvas.XamlRoot,
            Title = "フィードバックの送信",
            Content = content,
            PrimaryButtonText = "フィードバック Hub を開く",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary
        };

        ConfigureDialogAccessibility(dialog);
        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;

            bool launched;
            try
            {
                launched = await Launcher.LaunchUriAsync(new Uri("feedback-hub:"));
            }
            catch (Exception exception)
            {
                await ShowInfoAsync("フィードバック", $"フィードバック Hub を開けませんでした。\n{exception.Message}");
                return;
            }

            if (!launched)
                await ShowInfoAsync("フィードバック", "フィードバック Hub を開けませんでした。");
        }
        catch (Exception exception)
        {
            await ShowInfoAsync("フィードバック", $"フィードバック画面を表示できませんでした。\n{exception.Message}");
        }
    }

    private async void Account_Click(object sender, RoutedEventArgs e)
    {
        var content = new StackPanel { Spacing = 8, MaxWidth = 430 };
        content.Children.Add(new TextBlock { Text = "ローカル アカウント", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        content.Children.Add(new TextBlock
        {
            Text = "ChunPaint はサインインなしで使用でき、編集内容をローカルに保存します。Windows アカウントは設定アプリで管理できます。",
            TextWrapping = TextWrapping.Wrap
        });
        var dialog = new ContentDialog
        {
            XamlRoot = PaintCanvas.XamlRoot,
            Title = "アカウント",
            Content = content,
            PrimaryButtonText = "Windows アカウント設定",
            CloseButtonText = "閉じる"
        };

        ConfigureDialogAccessibility(dialog);
        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;

            bool launched;
            try
            {
                launched = await Launcher.LaunchUriAsync(new Uri("ms-settings:yourinfo"));
            }
            catch (Exception exception)
            {
                await ShowInfoAsync("アカウント", $"Windows アカウント設定を開けませんでした。\n{exception.Message}");
                return;
            }

            if (!launched)
                await ShowInfoAsync("アカウント", "Windows アカウント設定を開けませんでした。");
        }
        catch (Exception exception)
        {
            await ShowInfoAsync("アカウント", $"アカウント画面を表示できませんでした。\n{exception.Message}");
        }
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        var rulers = new CheckBox
        {
            Content = "ルーラーを表示",
            IsChecked = HorizontalRuler.Visibility == Visibility.Visible
        };
        ConfigureFieldAccessibility(rulers, "ルーラーを表示", "ルーラーの表示と非表示を切り替えます");
        var grid = new CheckBox
        {
            Content = "グリッド線を表示（400%以上）",
            IsChecked = _showGrid
        };
        ConfigureFieldAccessibility(grid, "グリッド線を表示", "高倍率時のグリッド線を切り替えます");
        var statusBar = new CheckBox
        {
            Content = "ステータス バーを表示",
            IsChecked = StatusBar.Visibility == Visibility.Visible
        };
        ConfigureFieldAccessibility(statusBar, "ステータスバーを表示", "ステータスバーの表示と非表示を切り替えます");
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
        var content = new StackPanel { Spacing = 12, MinWidth = 360 };
        content.Children.Add(new TextBlock { Text = "表示", FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        content.Children.Add(rulers);
        content.Children.Add(grid);
        content.Children.Add(statusBar);
        content.Children.Add(new TextBlock
        {
            Text = $"ChunPaint {version}",
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Colors.Gray)
        });

        var dialog = new ContentDialog
        {
            XamlRoot = PaintCanvas.XamlRoot,
            Title = "設定",
            Content = content,
            PrimaryButtonText = "適用",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary
        };
        ConfigureDialogAccessibility(dialog);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        RulerMenuItem.IsChecked = rulers.IsChecked == true;
        ToggleRulers(RulerMenuItem);

        _showGrid = grid.IsChecked == true;
        GridMenuItem.IsChecked = _showGrid;
        PaintCanvas.Invalidate();

        var showStatusBar = statusBar.IsChecked == true;
        StatusBarMenuItem.IsChecked = showStatusBar;
        StatusBar.Visibility = showStatusBar ? Visibility.Visible : Visibility.Collapsed;
    }
}
