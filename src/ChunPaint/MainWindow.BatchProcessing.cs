using ChunPaint.Core;
using ChunPaint.Imaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ChunPaint;

public sealed partial class MainWindow
{
    private async void BatchProcessing_Click(object sender, RoutedEventArgs e)
    {
        if (_documentBusy) return;
        _documentBusy = true; RefreshDocumentTabs();
        try
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            foreach (var extension in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" }) picker.FileTypeFilter.Add(extension);
            var files = await picker.PickMultipleFilesAsync();
            if (files.Count == 0) return;
            var folderPicker = new FolderPicker(); folderPicker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(folderPicker, WindowNative.GetWindowHandle(this));
            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder is null) return;
            var width = new NumberBox { Header = "出力幅 / 最大幅 (px)", Minimum = 1, Maximum = 16384, Value = 1920 };
            var height = new NumberBox { Header = "出力高さ / 最大高さ (px)", Minimum = 1, Maximum = 16384, Value = 1080 };
            var aspect = new CheckBox { Content = "縦横比を維持し、指定枠に収める", IsChecked = true };
            var format = new ComboBox { Header = "出力形式" };
            format.Items.Add("PNG"); format.Items.Add("JPEG"); format.Items.Add("WebP"); format.SelectedIndex = 0;
            var quality = new NumberBox { Header = "JPEG / WebP 画質 (1～100)", Minimum = 1, Maximum = 100, Value = 92 };
            var transparency = new CheckBox { Content = "透過を保持する（PNG / WebP）", IsChecked = true };
            var panel = new StackPanel { Spacing = 10, MinWidth = 360 };
            panel.Children.Add(new TextBlock { Text = $"{files.Count} 件を変換します。出力先: {folder.Path}\n元ファイルは上書きせず、変換後の名前に _converted を付けます。", TextWrapping = TextWrapping.Wrap });
            foreach (var control in new FrameworkElement[] { width, height, aspect, format, quality, transparency }) panel.Children.Add(control);
            var settings = new ContentDialog { XamlRoot = PaintCanvas.XamlRoot, Title = "画像の一括変換", Content = panel, PrimaryButtonText = "変換開始", CloseButtonText = "キャンセル" };
            ConfigureDialogAccessibility(settings);
            if (await settings.ShowAsync() != ContentDialogResult.Primary) return;
            if (!double.IsFinite(width.Value) || !double.IsFinite(height.Value) || !double.IsFinite(quality.Value)) throw new ArgumentException("寸法と画質を数値で入力してください。");
            var requestedWidth = checked((int)width.Value); var requestedHeight = checked((int)height.Value);
            var outputFormat = new[] { DocumentFileFormat.Png, DocumentFileFormat.Jpeg, DocumentFileFormat.Webp }[format.SelectedIndex];
            var outputQuality = (float)(quality.Value / 100); var keepTransparency = transparency.IsChecked == true; var keepAspect = aspect.IsChecked == true;
            _ = BatchImagePlan.GetSize(1, 1, requestedWidth, requestedHeight, keepAspect);
            if (outputQuality is < .01f or > 1) throw new ArgumentOutOfRangeException("画質");
            var occupied = new HashSet<string>((await folder.GetFilesAsync()).Select(file => file.Name), StringComparer.OrdinalIgnoreCase);
            var results = new List<string>();
            var progress = new ProgressBar { Minimum = 0, Maximum = files.Count, Value = 0 };
            var current = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var log = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinWidth = 440, Height = 280 };
            var progressPanel = new StackPanel { Spacing = 10 }; progressPanel.Children.Add(current); progressPanel.Children.Add(progress); progressPanel.Children.Add(log);
            var dialog = new ContentDialog { XamlRoot = PaintCanvas.XamlRoot, Title = "一括変換の進捗", Content = progressPanel, CloseButtonText = "キャンセル" };
            using var cancellation = new CancellationTokenSource();
            var running = true;
            dialog.Closing += (_, args) => { if (running) { args.Cancel = true; cancellation.Cancel(); current.Text = "キャンセルしています…"; } };
            dialog.Opened += async (_, _) =>
            {
                try
                {
                    foreach (var input in files)
                    {
                        if (cancellation.IsCancellationRequested) break;
                        current.Text = $"変換中: {input.Name}";
                        StorageFile? created = null;
                        try
                        {
                            using var inputStream = await input.OpenReadAsync();
                            DocumentFileFormats.ValidateInputSize(checked((long)inputStream.Size));
                            using var bitmap = await Win2DImageCodec.LoadAsync(PaintCanvas, inputStream, cancellation.Token);
                            var size = BatchImagePlan.GetSize(checked((int)bitmap.SizeInPixels.Width), checked((int)bitmap.SizeInPixels.Height), requestedWidth, requestedHeight, keepAspect);
                            var pixels = new PixelSurface(checked((int)bitmap.SizeInPixels.Width), checked((int)bitmap.SizeInPixels.Height), Rgba32.Transparent);
                            pixels.Restore(PixelAlpha.PremultipliedToStraight(bitmap.GetPixelBytes()));
                            var resized = await Task.Run(() => ImageResampling.Resize(pixels, size.Width, size.Height, ResamplingMode.Area, cancellation.Token), cancellation.Token);
                            using var output = ToRenderTarget(resized);
                            cancellation.Token.ThrowIfCancellationRequested();
                            var name = BatchImagePlan.GetOutputName(input.Name, outputFormat, occupied);
                            created = await folder.CreateFileAsync(name, CreationCollisionOption.GenerateUniqueName);
                            occupied.Add(created.Name);
                            await DocumentFileWriter.WriteAsync(created, stream => Win2DImageCodec.SaveWithOptionsAsync(output, stream, outputFormat, outputQuality, keepTransparency, cancellation.Token));
                            results.Add($"成功: {input.Name} → {created.Name}"); created = null;
                        }
                        catch (OperationCanceledException)
                        {
                            if (created is not null) await RemoveFailedBatchOutputAsync(created);
                            results.Add($"取消: {input.Name}"); break;
                        }
                        catch (Exception ex)
                        {
                            if (created is not null) await RemoveFailedBatchOutputAsync(created);
                            results.Add($"失敗: {input.Name} — {ex.Message}");
                        }
                        progress.Value++; log.Text = string.Join(Environment.NewLine, results);
                    }
                    log.Text = string.Join(Environment.NewLine, results);
                    current.Text = cancellation.IsCancellationRequested ? $"中止しました。未処理 {files.Count - results.Count} 件。" : $"完了: 成功 {results.Count(item => item.StartsWith("成功:"))} 件、失敗 {results.Count(item => item.StartsWith("失敗:"))} 件";
                }
                catch (Exception ex) { current.Text = $"処理を終了しました: {ex.Message}"; }
                finally { running = false; dialog.CloseButtonText = "閉じる"; }
            };
            ConfigureDialogAccessibility(dialog);
            await dialog.ShowAsync();
        }
        catch (Exception ex) { await ShowInfoAsync("画像の一括変換", $"処理を開始できませんでした。\n{ex.Message}"); }
        finally { _documentBusy = false; RefreshDocumentTabs(); }
    }

    private static async Task RemoveFailedBatchOutputAsync(StorageFile created)
    {
        try { await created.DeleteAsync(StorageDeleteOption.PermanentDelete); }
        catch { /* 自分で新規作成した出力だけを削除し、削除失敗時は空ファイルを残す。 */ }
    }
}
