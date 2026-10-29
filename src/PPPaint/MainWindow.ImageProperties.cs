using System.Globalization;
using PPPaint.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PPPaint;

public sealed partial class MainWindow
{
    private async void Properties_Click(object sender, RoutedEventArgs e)
    {
        var savedDate = "未保存";
        var fileSize = "未保存";
        if (_currentFile is not null)
        {
            try
            {
                var properties = await _currentFile.GetBasicPropertiesAsync();
                savedDate = properties.DateModified.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.CurrentCulture);
                fileSize = $"{properties.Size:N0} bytes";
            }
            catch (Exception exception)
            {
                fileSize = $"取得できません ({exception.Message})";
            }
        }

        var unitSelector = new ComboBox
        {
            Header = "単位",
            Width = 220,
            SelectedIndex = 0
        };
        unitSelector.Items.Add("ピクセル");
        unitSelector.Items.Add("インチ");
        unitSelector.Items.Add("センチメートル");
        ConfigureFieldAccessibility(unitSelector, "画像寸法の単位", "幅と高さの表示単位を切り替えます");

        var dimensionsText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        void UpdateDimensions()
        {
            var unit = unitSelector.SelectedIndex switch
            {
                1 => ImageMeasurementUnit.Inch,
                2 => ImageMeasurementUnit.Centimeter,
                _ => ImageMeasurementUnit.Pixel
            };
            var measurement = ImageMeasurements.Convert(_imageWidth, _imageHeight, _imageDpi, unit);
            var format = measurement.FractionDigits == 0 ? "N0" : "N2";
            dimensionsText.Text =
                $"幅: {measurement.Width.ToString(format, CultureInfo.CurrentCulture)} {measurement.UnitSuffix}\n" +
                $"高さ: {measurement.Height.ToString(format, CultureInfo.CurrentCulture)} {measurement.UnitSuffix}";
        }

        unitSelector.SelectionChanged += (_, _) => UpdateDimensions();
        UpdateDimensions();

        var metadataText = new TextBlock
        {
            Text =
                $"保存日時: {savedDate}\n" +
                $"ファイルサイズ: {fileSize}\n" +
                $"解像度: {ImageMeasurements.NormalizeDpi(_imageDpi):0.##} DPI",
            TextWrapping = TextWrapping.Wrap
        };
        var content = new StackPanel { Spacing = 10, MinWidth = 320 };
        content.Children.Add(metadataText);
        content.Children.Add(unitSelector);
        content.Children.Add(dimensionsText);

        var dialog = new ContentDialog
        {
            XamlRoot = PaintCanvas.XamlRoot,
            Title = "画像のプロパティ",
            Content = content,
            CloseButtonText = "閉じる"
        };
        ConfigureDialogAccessibility(dialog);
        await dialog.ShowManagedAsync();
    }
}
