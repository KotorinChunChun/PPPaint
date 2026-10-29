using System.Text.Json;
using PPPaint.Core;
using PPPaint.Editing;
using PPPaint.Services;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Security.Credentials;
using Windows.Storage.Streams;

namespace PPPaint;

public sealed partial class MainWindow
{
    private sealed record AiPreferences(string Endpoint = "https://api.openai.com/v1/images/edits", string Model = "gpt-image-2.5-sunburst", int DailyLimit = 10);
    private sealed record AiUsage(string Day, int Attempts);
    private const string AiCredentialResource = "PPPaint.ImageEditing";

    private async void AiEditing_Click(object sender, RoutedEventArgs e)
    {
        if (_documentBusy) return;
        _documentBusy = true;
        RefreshDocumentTabs();
        try { await ShowAiEditingAsync(); }
        catch (OperationCanceledException) { await ShowInfoAsync("AI画像編集", "処理を中止しました。送信済みの要求はサービス側で処理・課金される場合があります。自動再試行はしません。"); }
        catch (Exception exception) { await ShowInfoAsync("AI画像編集", exception.Message); }
        finally { _documentBusy = false; RefreshDocumentTabs(); }
    }

    private async Task ShowAiEditingAsync()
    {
        EnsureSurface(PaintCanvas);
        CommitFloatingSelection();
        var documentId = _activeDocument?.Id;
        var selection = _selection;
        var preferencesPath = Path.Combine(UserDataDirectory, "ai-settings.json");
        var preferences = File.Exists(preferencesPath)
            ? JsonSerializer.Deserialize<AiPreferences>(File.ReadAllText(preferencesPath)) ?? new AiPreferences()
            : new AiPreferences();
        var endpoint = new TextBox { Header = "画像編集APIのHTTPS URL", Text = preferences.Endpoint };
        var model = new TextBox { Header = "モデル", Text = preferences.Model };
        var key = new PasswordBox { Header = "APIキー（空欄ならこの接続先の保存済みキーを使用）" };
        var saveKey = new CheckBox { Content = "APIキーをWindows資格情報ストアに保存する" };
        var forgetKey = new Button { Content = "この接続先の保存済みキーを削除" };
        var keyStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
        forgetKey.Click += (_, _) =>
        {
            try
            {
                var vault = new PasswordVault();
                vault.Remove(vault.Retrieve(AiCredentialResource, ImageEditingClient.ValidateEndpoint(endpoint.Text).AbsoluteUri));
                keyStatus.Text = "保存済みキーを削除しました。";
            }
            catch { keyStatus.Text = "この接続先の保存済みキーは見つからないか、削除できませんでした。"; }
        };
        var limit = new NumberBox { Header = "このPCでの1日あたり送信上限（失敗・取消も回数に含む）", Minimum = 1, Maximum = 100, Value = Math.Clamp(preferences.DailyLimit, 1, 100), SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var mode = new ComboBox { Header = "編集方法", SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        mode.Items.Add("指示による画像編集"); mode.Items.Add("生成消去（選択部分を自然な背景で埋める）");
        var onlySelected = new CheckBox { Content = "選択した画素だけ送信する（選択外は透明化）", IsChecked = selection is not null, IsEnabled = selection is not null };
        var prompt = new TextBox { Header = "編集指示", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 85, MaxLength = 32000 };
        var panel = new StackPanel { Spacing = 8 };
        foreach (var control in new UIElement[] { endpoint, model, key, saveKey, forgetKey, keyStatus, limit, mode, onlySelected, prompt,
            new TextBlock { Text = "画像・編集指示・APIキーを指定先へ送信します。サービス料金が発生します。送信前に対象を確認できます。最大3分で中止し、自動再試行はしません。再実行は新しい要求です。", TextWrapping = TextWrapping.Wrap } }) panel.Children.Add(control);
        var setup = new ContentDialog { XamlRoot = PaintCanvas.XamlRoot, Title = "AI画像編集", Content = new ScrollViewer { Content = panel, MaxHeight = 520 }, PrimaryButtonText = "送信内容を確認", CloseButtonText = "キャンセル" };
        ConfigureDialogAccessibility(setup);
        if (await setup.ShowManagedAsync() != ContentDialogResult.Primary) return;
        var uri = ImageEditingClient.ValidateEndpoint(endpoint.Text.Trim());
        if (string.IsNullOrWhiteSpace(model.Text) || model.Text.Length > 128 || !double.IsFinite(limit.Value)) throw new ArgumentException("モデルと利用上限を確認してください。");
        var erase = mode.SelectedIndex == 1;
        if (erase && selection is null) throw new InvalidOperationException("生成消去は消す範囲を選択してから実行してください。");
        var instruction = erase ? "Remove the subject inside the transparent mask and fill it with a natural continuation of the background. " + prompt.Text : prompt.Text;
        if (string.IsNullOrWhiteSpace(instruction) || instruction.Length > 32000) throw new ArgumentException("編集指示は1～32000文字で入力してください。");
        var selectedOnly = onlySelected.IsChecked == true && selection is not null;
        var bounds = selectedOnly ? selection!.Bounds : new PixelRect(0, 0, _imageWidth, _imageHeight);
        if ((long)bounds.Width * bounds.Height > 16 * 1024 * 1024 || bounds.Width > 8192 || bounds.Height > 8192) throw new InvalidOperationException("送信領域は各辺8192以下、合計16メガピクセル以下にしてください。");
        using var composite = CreateCompositeWithObjects();
        var original = ToPixelSurface(composite);
        var cropped = new PixelSurface(bounds.Width, bounds.Height, Rgba32.Transparent);
        var mask = new PixelSurface(bounds.Width, bounds.Height, Rgba32.White);
        for (var y = 0; y < bounds.Height; y++)
        for (var x = 0; x < bounds.Width; x++)
        {
            var globalX = bounds.X + x; var globalY = bounds.Y + y;
            var inside = selection?.ContainsPixel(globalX, globalY) ?? true;
            if (!selectedOnly || inside) cropped[x, y] = original[globalX, globalY];
            if (inside) mask[x, y] = Rgba32.Transparent;
        }
        var inputPng = await EncodeAiPngAsync(cropped);
        var maskPng = selection is not null ? await EncodeAiPngAsync(mask) : null;
        var preview = await CreateAiPreviewAsync(inputPng);
        var confirmation = new ContentDialog
        {
            XamlRoot = PaintCanvas.XamlRoot, Title = "画像の送信確認", PrimaryButtonText = "同意して送信", CloseButtonText = "キャンセル",
            Content = new StackPanel { Spacing = 8, Children =
            {
                new TextBlock { Text = $"送信先: {uri}\nモデル: {model.Text}\n範囲: {(selectedOnly ? "選択画素のみ（選択外は透明）" : "画像全体（表示レイヤーを合成）")}\n寸法: {bounds.Width} × {bounds.Height}\n指示: {instruction}", TextWrapping = TextWrapping.Wrap, MaxHeight = 150 },
                preview,
                new TextBlock { Text = "この画像と指示を送信します。元のレイヤー構造やファイル名は送りません。結果は確認後に新規レイヤーへ適用します。", TextWrapping = TextWrapping.Wrap }
            } }
        };
        ConfigureDialogAccessibility(confirmation);
        if (await confirmation.ShowManagedAsync() != ContentDialogResult.Primary) return;
        var vaultStore = new PasswordVault();
        var apiKey = key.Password;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            try { var credential = vaultStore.Retrieve(AiCredentialResource, uri.AbsoluteUri); credential.RetrievePassword(); apiKey = credential.Password; }
            catch
            {
                try { var legacy = vaultStore.Retrieve("ChunPaint.ImageEditing", uri.AbsoluteUri); legacy.RetrievePassword(); apiKey = legacy.Password; }
                catch { throw new InvalidOperationException("この接続先のAPIキーを入力してください。"); }
            }
        }
        if (saveKey.IsChecked == true) vaultStore.Add(new PasswordCredential(AiCredentialResource, uri.AbsoluteUri, apiKey));
        Directory.CreateDirectory(UserDataDirectory);
        var settingsTemporary = preferencesPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(settingsTemporary, JsonSerializer.Serialize(new AiPreferences(uri.AbsoluteUri, model.Text.Trim(), (int)limit.Value)));
        File.Move(settingsTemporary, preferencesPath, true);
        ReserveAiAttempt((int)limit.Value);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var progress = new ContentDialog { XamlRoot = PaintCanvas.XamlRoot, Title = "AI画像編集を実行中", Content = new StackPanel { Spacing = 8, Children = { new ProgressRing { IsActive = true }, new TextBlock { Text = "最大3分で中止します。取消後もサービス側の処理・課金が発生する場合があります。", TextWrapping = TextWrapping.Wrap } } }, CloseButtonText = "処理を中止" };
        progress.CloseButtonClick += (_, _) => cancellation.Cancel();
        var requestFinished = false;
        progress.Closing += (_, _) => { if (!requestFinished) cancellation.Cancel(); };
        ConfigureDialogAccessibility(progress);
        var progressOperation = progress.ShowManagedAsync();
        byte[] result;
        try { result = await new ImageEditingClient().EditAsync(uri.AbsoluteUri, model.Text.Trim(), apiKey, instruction, inputPng, maskPng, true, cancellation.Token); }
        finally { requestFinished = true; progress.Hide(); await progressOperation; }
        cancellation.Token.ThrowIfCancellationRequested();
        if (_activeDocument?.Id != documentId) throw new InvalidOperationException("対象文書が切り替わったため結果を適用できません。");
        var resultDialog = new ContentDialog { XamlRoot = PaintCanvas.XamlRoot, Title = "AI編集結果", PrimaryButtonText = "新規レイヤーに適用", CloseButtonText = "破棄", Content = new StackPanel { Spacing = 8, Children = { await CreateAiPreviewAsync(result), new TextBlock { Text = "結果を送信領域の寸法に合わせて配置します。選択がある場合は選択内だけ適用します。Undoで取り消せます。", TextWrapping = TextWrapping.Wrap } } } };
        ConfigureDialogAccessibility(resultDialog);
        if (await resultDialog.ShowManagedAsync() != ContentDialogResult.Primary) return;
        using var resultStream = await AiBytesStreamAsync(result);
        using var decoded = await CanvasBitmap.LoadAsync(PaintCanvas, resultStream);
        using var fitted = new CanvasRenderTarget(PaintCanvas, bounds.Width, bounds.Height, 96);
        using (var drawing = fitted.CreateDrawingSession()) { drawing.Clear(Microsoft.UI.Colors.Transparent); drawing.DrawImage(decoded, new Windows.Foundation.Rect(0, 0, bounds.Width, bounds.Height)); }
        var fittedPixels = ToPixelSurface(fitted);
        var layerPixels = new PixelSurface(_imageWidth, _imageHeight, Rgba32.Transparent);
        for (var y = 0; y < bounds.Height; y++)
        for (var x = 0; x < bounds.Width; x++)
            if (selection is null || selection.ContainsPixel(bounds.X + x, bounds.Y + y)) layerPixels[bounds.X + x, bounds.Y + y] = fittedPixels[x, y];
        var surface = ToRenderTarget(layerPixels);
        CaptureUndo();
        _layerCount++;
        _layers.Add(new LayerState(erase ? "AI生成消去" : "AI画像編集", surface));
        _activeLayerIndex = _layers.Count - 1; _surface = surface;
        RefreshLayersPanel(); FinishEdit();
    }

    private void ReserveAiAttempt(int limit)
    {
        var path = Path.Combine(UserDataDirectory, "ai-usage.json");
        using var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (file.Length > 4096) throw new InvalidDataException("AI利用回数ファイルが不正です。");
        var day = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var usage = file.Length == 0 ? new AiUsage(day, 0) : JsonSerializer.Deserialize<AiUsage>(file) ?? throw new InvalidDataException("AI利用回数を読み込めません。");
        if (usage.Day != day) usage = new AiUsage(day, 0);
        if (usage.Attempts < 0 || usage.Attempts >= limit) throw new InvalidOperationException("本日のAI送信上限に達しました（UTC日付で更新）。設定の上限を確認してください。");
        file.Position = 0; JsonSerializer.Serialize(file, usage with { Attempts = usage.Attempts + 1 }); file.SetLength(file.Position); file.Flush(true);
    }

    private async Task<byte[]> EncodeAiPngAsync(PixelSurface pixels)
    {
        using var target = ToRenderTarget(pixels);
        using var stream = new InMemoryRandomAccessStream();
        await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);
        if (stream.Size > ImageEditingClient.MaximumImageBytes) throw new InvalidOperationException("送信PNGが20MiBを超えています。");
        stream.Seek(0);
        using var reader = new DataReader(stream);
        await reader.LoadAsync((uint)stream.Size);
        var bytes = new byte[(int)stream.Size]; reader.ReadBytes(bytes); return bytes;
    }

    private static async Task<InMemoryRandomAccessStream> AiBytesStreamAsync(byte[] bytes)
    {
        var stream = new InMemoryRandomAccessStream();
        using var writer = new DataWriter(stream);
        writer.WriteBytes(bytes); await writer.StoreAsync(); writer.DetachStream(); stream.Seek(0); return stream;
    }

    private static async Task<Image> CreateAiPreviewAsync(byte[] bytes)
    {
        using var stream = await AiBytesStreamAsync(bytes);
        var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream);
        return new Image { Source = bitmap, MaxHeight = 280, MaxWidth = 420, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform };
    }
}
