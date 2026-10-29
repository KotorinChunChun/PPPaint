using System.Text.Json;
using PPPaint.Core;
using PPPaint.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.System;

namespace PPPaint;

public sealed partial class MainWindow
{
    private sealed record UpdatePreferences(string ManifestUrl = "", bool Automatic = false, DateTime LastCheckedUtc = default, string LastNotifiedVersion = "");
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _updateTimer;
    private readonly CancellationTokenSource _updateLifetime = new();
    private bool _updateCheckRunning;
    private static string UpdatePreferencesPath => Path.Combine(UserDataDirectory, "update-settings.json");
    private static Version InstalledUpdateVersion => typeof(MainWindow).Assembly.GetName().Version ?? new Version(0, 0, 0);

    private void InitializeUpdateChecks()
    {
        _updateTimer = DispatcherQueue.CreateTimer();
        _updateTimer.Interval = TimeSpan.FromHours(1);
        _updateTimer.Tick += async (_, _) => await CheckUpdatesAutomaticallyAsync();
        _updateTimer.Start();
        PaintCanvas.Loaded += async (_, _) => await CheckUpdatesAutomaticallyAsync();
        Closed += (_, _) => { _updateTimer.Stop(); _updateLifetime.Cancel(); };
    }

    private static UpdatePreferences ReadUpdatePreferences()
    {
        if (!File.Exists(UpdatePreferencesPath)) return new();
        if (new FileInfo(UpdatePreferencesPath).Length > 16384) throw new InvalidDataException("更新設定ファイルが大きすぎます。");
        return JsonSerializer.Deserialize<UpdatePreferences>(File.ReadAllText(UpdatePreferencesPath)) ?? new();
    }

    private static void SaveUpdatePreferences(UpdatePreferences preferences)
    {
        Directory.CreateDirectory(UserDataDirectory);
        var temporary = UpdatePreferencesPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(preferences));
            File.Move(temporary, UpdatePreferencesPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private async Task CheckUpdatesAutomaticallyAsync()
    {
        if (_updateCheckRunning || _documentBusy || _updateLifetime.IsCancellationRequested) return;
        _updateCheckRunning = true;
        try
        {
            var settings = ReadUpdatePreferences();
            if (!settings.Automatic || string.IsNullOrWhiteSpace(settings.ManifestUrl) || DateTime.UtcNow - settings.LastCheckedUtc < TimeSpan.FromDays(1)) return;
            SaveUpdatePreferences(settings with { LastCheckedUtc = DateTime.UtcNow });
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_updateLifetime.Token);
            cancellation.CancelAfter(TimeSpan.FromSeconds(30));
            using var client = new UpdateClient();
            var manifest = await client.FetchManifestAsync(settings.ManifestUrl, cancellation.Token);
            if (manifest.IsNewerThan(InstalledUpdateVersion) && settings.LastNotifiedVersion != manifest.Version && !_updateLifetime.IsCancellationRequested)
            {
                PointerStatusText.Text = $"PPPaint {manifest.Version} が公開されています。「更新の確認」から取得できます。";
                SaveUpdatePreferences(settings with { LastCheckedUtc = DateTime.UtcNow, LastNotifiedVersion = manifest.Version });
            }
        }
        catch { /* 自動確認の失敗や未変更では作業を妨げない。手動確認で詳細を表示する。 */ }
        finally { _updateCheckRunning = false; }
    }

    private async void Updates_Click(object sender, RoutedEventArgs e)
    {
        if (_documentBusy || _updateCheckRunning) return;
        _documentBusy = true;
        _updateCheckRunning = true;
        RefreshDocumentTabs();
        try { await ShowUpdatesAsync(); }
        catch (OperationCanceledException) { await ShowInfoAsync("更新の確認", "更新処理を中止しました。アプリのインストール状態は変更していません。"); }
        catch (Exception exception) { await ShowInfoAsync("更新の確認", exception.Message); }
        finally { _documentBusy = false; _updateCheckRunning = false; RefreshDocumentTabs(); }
    }

    private async Task ShowUpdatesAsync()
    {
        var settings = ReadUpdatePreferences();
        var url = new TextBox { Header = "信頼する配布元の更新情報URL（HTTPSのupdate.json）", Text = settings.ManifestUrl, MaxLength = 4096 };
        var automatic = new CheckBox { Content = "起動時と1日ごとに更新を確認する", IsChecked = settings.Automatic };
        var dialog = new ContentDialog
        {
            XamlRoot = PaintCanvas.XamlRoot, Title = $"更新の確認（現在 {InstalledUpdateVersion}）", PrimaryButtonText = "保存して今すぐ確認", SecondaryButtonText = "設定だけ保存", CloseButtonText = "キャンセル",
            Content = new StackPanel { Spacing = 10, Children = { url, automatic,
                new TextBlock { Text = "公開URLは初期設定されていません。自動確認では更新情報のみを取得し、新版だけ通知します。パッケージの取得とインストーラー起動は毎回確認します。", TextWrapping = TextWrapping.Wrap } } }
        };
        ConfigureDialogAccessibility(dialog);
        var choice = await dialog.ShowManagedAsync();
        if (choice == ContentDialogResult.None) return;
        var address = url.Text.Trim();
        if (address.Length != 0) UpdateClient.ValidateManifestUri(address);
        if (automatic.IsChecked == true && address.Length == 0) throw new ArgumentException("自動確認には更新情報URLが必要です。");
        settings = settings with { ManifestUrl = address, Automatic = automatic.IsChecked == true, LastCheckedUtc = address == settings.ManifestUrl ? settings.LastCheckedUtc : default, LastNotifiedVersion = address == settings.ManifestUrl ? settings.LastNotifiedVersion : "" };
        SaveUpdatePreferences(settings);
        if (choice == ContentDialogResult.Secondary) return;
        if (address.Length == 0) throw new ArgumentException("配布元の更新情報URLを入力してください。");
        using var client = new UpdateClient();
        var manifest = await RunUpdateOperationAsync("更新情報を確認中", TimeSpan.FromSeconds(30), token => client.FetchManifestAsync(address, token));
        SaveUpdatePreferences(settings with { LastCheckedUtc = DateTime.UtcNow });
        if (!manifest.IsNewerThan(InstalledUpdateVersion))
        {
            await ShowInfoAsync("更新の確認", "現在より新しい版は公開されていません。同じ版や古い版への更新は行いません。"); return;
        }
        var confirm = new ContentDialog
        {
            XamlRoot = PaintCanvas.XamlRoot, Title = $"PPPaint {manifest.Version} を取得", PrimaryButtonText = "確認して取得", CloseButtonText = "キャンセル",
            Content = new TextBlock { Text = $"更新情報: {address}\n配布元が申告する発行者: {manifest.Publisher ?? "未指定"}\n取得URL: {manifest.DownloadUrl}\nSHA-256: {manifest.Sha256}\n\nこれは配布元が申告した情報です。ハッシュ一致だけでは発行者を証明できません。取得後にWindowsインストーラーが署名・信頼・パッケージ互換性を確認します。非MSIX版からの移行は別インストールとなる場合があります。", TextWrapping = TextWrapping.Wrap }
        };
        ConfigureDialogAccessibility(confirm);
        if (await confirm.ShowManagedAsync() != ContentDialogResult.Primary) return;
        var downloadDirectory = Path.Combine(UserDataDirectory, "Updates");
        Directory.CreateDirectory(downloadDirectory);
        var downloadPath = Path.Combine(downloadDirectory, $"PPPaint-{Guid.NewGuid():N}.msix");
        await RunUpdateOperationAsync("更新パッケージを取得・検証中", TimeSpan.FromMinutes(10), async token =>
        {
            await client.DownloadVerifiedAsync(manifest, InstalledUpdateVersion, downloadPath, true, token);
            return true;
        });
        var launch = new ContentDialog
        {
            XamlRoot = PaintCanvas.XamlRoot, Title = "更新パッケージの取得完了", PrimaryButtonText = "Windowsインストーラーを開く", CloseButtonText = "あとで",
            Content = new TextBlock { Text = $"SHA-256の一致を確認しました。\n{downloadPath}\n\n未保存の文書は保存してからインストールしてください。Windowsの画面で発行者と署名を確認し、最終的なインストール操作を行います。現在のアプリや文書はここでは閉じません。", TextWrapping = TextWrapping.Wrap }
        };
        ConfigureDialogAccessibility(launch);
        if (await launch.ShowManagedAsync() != ContentDialogResult.Primary) return;
        var file = await StorageFile.GetFileFromPathAsync(downloadPath);
        var launchOptions = new LauncherOptions { TreatAsUntrusted = true, TargetApplicationPackageFamilyName = "Microsoft.DesktopAppInstaller_8wekyb3d8bbwe" };
        WinRT.Interop.InitializeWithWindow.Initialize(launchOptions, WinRT.Interop.WindowNative.GetWindowHandle(this));
        if (!await Launcher.LaunchFileAsync(file, launchOptions))
            await ShowInfoAsync("更新の確認", $"インストーラーを起動できませんでした。Windows App Installerの利用可否を確認し、取得ファイルを開いてください。\n{downloadPath}");
    }

    private async Task<T> RunUpdateOperationAsync<T>(string title, TimeSpan timeout, Func<CancellationToken, Task<T>> operation)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_updateLifetime.Token);
        cancellation.CancelAfter(timeout);
        var finished = false;
        var progress = new ContentDialog { XamlRoot = PaintCanvas.XamlRoot, Title = title, Content = new ProgressRing { IsActive = true }, CloseButtonText = "中止" };
        progress.Closing += (_, _) => { if (!finished) cancellation.Cancel(); };
        ConfigureDialogAccessibility(progress);
        var showing = progress.ShowManagedAsync();
        try { return await operation(cancellation.Token); }
        finally { finished = true; progress.Hide(); await showing; }
    }
}
