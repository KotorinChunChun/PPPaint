using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Printing;
using Windows.Graphics.Printing;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace ChunPaint;

public sealed partial class MainWindow
{
    private PrintManager? _printManager;
    private PrintDocument? _printDocument;
    private IPrintDocumentSource? _printDocumentSource;
    private Canvas? _printPage;
    private Border? _printImageArea;
    private BitmapImage? _printBitmap;

    private async void Print_Click(object sender, RoutedEventArgs e)
    {
        await ShowPrintUiAsync();
    }

    private async Task ShowPrintUiAsync()
    {
        try
        {
            EnsurePrintManager();
            await PreparePrintPageAsync();
            var hwnd = WindowNative.GetWindowHandle(this);
            if (!await PrintManagerInterop.ShowPrintUIForWindowAsync(hwnd))
            {
                await ShowInfoAsync("印刷", "印刷ダイアログを開けませんでした。");
            }
        }
        catch (Exception exception)
        {
            await ShowInfoAsync("印刷", $"印刷を開始できませんでした。\n{exception.Message}");
        }
    }

    private void EnsurePrintManager()
    {
        if (_printManager is not null)
        {
            return;
        }

        var hwnd = WindowNative.GetWindowHandle(this);
        _printManager = PrintManagerInterop.GetForWindow(hwnd);
        _printManager.PrintTaskRequested += PrintManager_PrintTaskRequested;

        _printDocument = new PrintDocument();
        _printDocument.Paginate += PrintDocument_Paginate;
        _printDocument.GetPreviewPage += PrintDocument_GetPreviewPage;
        _printDocument.AddPages += PrintDocument_AddPages;
        _printDocumentSource = _printDocument.DocumentSource;
    }

    private async Task PreparePrintPageAsync()
    {
        EnsureSurface(PaintCanvas);
        using var stream = new InMemoryRandomAccessStream();
        using (var composite = CreateComposite())
        {
            await composite.SaveAsync(stream, Microsoft.Graphics.Canvas.CanvasBitmapFileFormat.Png);
        }

        stream.Seek(0);
        _printBitmap = new BitmapImage();
        await _printBitmap.SetSourceAsync(stream);

        var image = new Image
        {
            Source = _printBitmap,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        _printImageArea = new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.White),
            Child = image
        };
        _printPage = new Canvas
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.White)
        };
        _printPage.Children.Add(_printImageArea);

        PrintHost.Children.Clear();
        PrintHost.Children.Add(_printPage);
        PrintHost.InvalidateMeasure();
        PrintHost.UpdateLayout();
        _printDocument?.InvalidatePreview();
    }

    private void PrintManager_PrintTaskRequested(PrintManager sender, PrintTaskRequestedEventArgs args)
    {
        args.Request.CreatePrintTask(
            _currentFile is null ? "ChunPaint - 無題" : $"ChunPaint - {_currentFile.DisplayName}",
            sourceArgs =>
            {
                if (_printDocumentSource is not null)
                {
                    sourceArgs.SetSource(_printDocumentSource);
                }
            });
    }

    private void PrintDocument_Paginate(object sender, PaginateEventArgs args)
    {
        if (_printDocument is null || _printPage is null || _printImageArea is null)
        {
            return;
        }

        var description = args.PrintTaskOptions.GetPageDescription(0);
        _printPage.Width = description.PageSize.Width;
        _printPage.Height = description.PageSize.Height;
        _printImageArea.Width = description.ImageableRect.Width;
        _printImageArea.Height = description.ImageableRect.Height;
        Canvas.SetLeft(_printImageArea, description.ImageableRect.Left);
        Canvas.SetTop(_printImageArea, description.ImageableRect.Top);
        _printPage.Measure(description.PageSize);
        _printPage.Arrange(new Windows.Foundation.Rect(0, 0, description.PageSize.Width, description.PageSize.Height));
        _printPage.UpdateLayout();
        _printDocument.SetPreviewPageCount(1, PreviewPageCountType.Final);
    }

    private void PrintDocument_GetPreviewPage(object sender, GetPreviewPageEventArgs args)
    {
        if (_printPage is not null)
        {
            _printDocument?.SetPreviewPage(args.PageNumber, _printPage);
        }
    }

    private void PrintDocument_AddPages(object sender, AddPagesEventArgs args)
    {
        if (_printPage is not null)
        {
            _printDocument?.AddPage(_printPage);
        }

        _printDocument?.AddPagesComplete();
    }
}
