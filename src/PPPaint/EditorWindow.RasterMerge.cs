using Microsoft.UI.Xaml;
using PPPaint.Core;
using PPPaint.Imaging;

namespace PPPaint;

public sealed partial class EditorWindow
{
    private async void MergeSelection_Click(object sender, RoutedEventArgs e) => await RunAsync(MergeSelectionAsync);

    private Task MergeSelectionAsync()
    {
        if (_selectedIds.Count == 0) return Task.CompletedTask;
        var plan = ScreenshotRasterMerge.Create(_document.State, _selectedIds);
        var image = ScreenshotRasterMergeRenderer.Render(PaintCanvas, plan, _renderCache);
        var result = plan.Apply(_document.State, image);
        _document.Apply(result.State);
        _region = null; SetCurrentTool(ScreenshotTool.Select); SetSelection([result.MergedId]);
        _hasImage = true; StatusText.Text = "選択図形を１枚の画像に統合しました。";
        return Task.CompletedTask;
    }
}
