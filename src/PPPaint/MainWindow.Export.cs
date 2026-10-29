using PPPaint.Core;
using PPPaint.Imaging;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace PPPaint;

public sealed partial class MainWindow
{
    private async Task<byte[]?> PrepareExportAsync(CanvasBitmap bitmap, DocumentFileFormat format)
    {
        var quality = new Slider { Header = "画質 (JPEG / WebP)", Minimum = 1, Maximum = 100, Value = 92,
            IsEnabled = format is DocumentFileFormat.Jpeg or DocumentFileFormat.Webp };
        var transparency = new CheckBox { Content = "透過を保持（オフの場合は白背景）", IsChecked = true,
            IsEnabled = format is DocumentFileFormat.Png or DocumentFileFormat.Webp or DocumentFileFormat.Gif };
        var image = new Image { Width = 420, Height = 250, Stretch = Stretch.Uniform };
        var size = new TextBlock { Text = "プレビューを準備しています。" };
        var refresh = new Button { Content = "容量とプレビューを更新" };
        var panel = new StackPanel { Spacing = 12, MinWidth = 430 };
        panel.Children.Add(quality); panel.Children.Add(transparency); panel.Children.Add(refresh); panel.Children.Add(image); panel.Children.Add(size);
        var dialog = new ContentDialog { XamlRoot = PaintCanvas.XamlRoot, Title = $"{format} 書き出し", Content = panel,
            PrimaryButtonText = "保存", CloseButtonText = "キャンセル", IsPrimaryButtonEnabled = false };
        byte[]? encoded = null;
        var busy = false;
        var closeRequested = false;
        using var cancellation = new CancellationTokenSource();
        var revision = 0;
        quality.ValueChanged += (_, _) => { revision++; dialog.IsPrimaryButtonEnabled = false; };
        transparency.Checked += (_, _) => { revision++; dialog.IsPrimaryButtonEnabled = false; };
        transparency.Unchecked += (_, _) => { revision++; dialog.IsPrimaryButtonEnabled = false; };
        async Task Preview()
        {
            if (busy) return;
            busy = true; refresh.IsEnabled = false; dialog.IsPrimaryButtonEnabled = false;
            var captured = revision;
            try
            {
                using var stream = new InMemoryRandomAccessStream();
                await Win2DImageCodec.SaveWithOptionsAsync(bitmap, stream, format, (float)(quality.Value / 100), transparency.IsChecked == true, cancellation.Token);
                using var bytes = new MemoryStream();
                await stream.AsStreamForRead().CopyToAsync(bytes, cancellation.Token);
                encoded = bytes.ToArray();
                using var preview = await Win2DImageCodec.LoadAsync(PaintCanvas, stream, cancellation.Token);
                using var png = new InMemoryRandomAccessStream();
                await preview.SaveAsync(png, CanvasBitmapFileFormat.Png).AsTask(cancellation.Token);
                png.Seek(0);
                var source = new BitmapImage(); await source.SetSourceAsync(png).AsTask(cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                image.Source = source;
                size.Text = $"保存容量: {encoded.Length:N0} バイト ({encoded.Length / 1024d:N1} KiB)";
                dialog.IsPrimaryButtonEnabled = captured == revision;
            }
            catch (OperationCanceledException) { encoded = null; size.Text = "プレビューを中止しました。"; }
            catch (Exception exception) { encoded = null; size.Text = exception.Message; }
            finally
            {
                busy = false; refresh.IsEnabled = !closeRequested;
                if (closeRequested) dialog.Hide();
            }
        }
        dialog.Opened += async (_, _) => await Preview();
        dialog.Closing += (_, args) =>
        {
            if (busy)
            {
                args.Cancel = true; closeRequested = true;
                size.Text = "プレビューを中止しています…";
                cancellation.Cancel();
            }
        };
        refresh.Click += async (_, _) => await Preview();
        ConfigureDialogAccessibility(dialog);
        return await dialog.ShowManagedAsync() == ContentDialogResult.Primary ? encoded : null;
    }
}
