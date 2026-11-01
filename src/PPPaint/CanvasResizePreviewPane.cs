using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PPPaint.Core;
using PPPaint.Imaging;

namespace PPPaint;

internal sealed class CanvasResizePreviewPane : IDisposable
{
    private readonly ScreenshotState _source;
    private readonly ICanvasResourceCreatorWithDpi _creator;
    private readonly ScreenshotRenderCache? _cache;
    private readonly List<(Image Image, ContentControl Host, ScreenshotCanvasViewAnchor? Anchor)> _views = [];
    private ScreenshotCanvasResize? _layout;
    private float _dpi;
    private int _revision;
    public Grid View { get; } = new() { ColumnSpacing = 8, RowSpacing = 6 };

    public CanvasResizePreviewPane(ICanvasResourceCreatorWithDpi creator, ScreenshotState source, ScreenshotRenderCache? cache)
    {
        _creator = creator; _source = source; _cache = cache;
        View.ColumnDefinitions.Add(new() { Width = new(102) });
        View.ColumnDefinitions.Add(new() { Width = new(222) });
        View.ColumnDefinitions.Add(new() { Width = new(102) });
        for (var i = 0; i < 3; i++) View.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Add("左上", ScreenshotCanvasViewAnchor.TopLeft, 0, 0); Add("上中央", ScreenshotCanvasViewAnchor.Top, 0, 1); Add("右上", ScreenshotCanvasViewAnchor.TopRight, 0, 2);
        Add("左中央", ScreenshotCanvasViewAnchor.Left, 1, 0); Add("全体", null, 1, 1); Add("右中央", ScreenshotCanvasViewAnchor.Right, 1, 2);
        Add("左下", ScreenshotCanvasViewAnchor.BottomLeft, 2, 0); Add("下中央", ScreenshotCanvasViewAnchor.Bottom, 2, 1); Add("右下", ScreenshotCanvasViewAnchor.BottomRight, 2, 2);
    }

    public void SetLayout(ScreenshotCanvasResize layout)
    {
        if (_layout == layout && _dpi == _creator.Dpi) return;
        _layout = layout; _dpi = _creator.Dpi; _revision++;
        // CanvasControl.Invalidate does not reliably redraw inside a modal popup.
        // Draw the small image sources explicitly, using the host's display DPI.
        using var image = ScreenshotRenderer.RenderCanvasPreview(_creator, _source, layout, _cache);
        foreach (var (view, host, anchor) in _views)
        {
            var surface = view.Source as CanvasImageSource;
            if (surface is null || surface.Dpi != _dpi) view.Source = surface = new CanvasImageSource(_creator, (float)view.Width, (float)view.Height);
            using (var ds = surface.CreateDrawingSession(Microsoft.UI.Colors.Transparent))
            {
                if (anchor is { } detail) ScreenshotResizePreviewDrawing.DrawDetail(ds, image, detail);
                else ScreenshotResizePreviewDrawing.DrawOverview(ds, image, (float)view.Width, (float)view.Height);
            }
            var status = $"候補: {layout.Width} × {layout.Height} px、画像の移動 {layout.OffsetX}, {layout.OffsetY} px";
            if (anchor is { } direction)
            {
                var box = ScreenshotCanvasResizeViews.DetailSource(layout.Width, layout.Height, direction);
                status += $"、拡大範囲 {box.X}, {box.Y}, {box.Width}, {box.Height} px";
            }
            AutomationProperties.SetItemStatus(host, status + $"、描画 {_revision}");
        }
    }

    private void Add(string name, ScreenshotCanvasViewAnchor? anchor, int row, int column)
    {
        var image = new Image { Width = anchor is null ? 220 : 100, Height = 100, Stretch = Stretch.None, UseLayoutRounding = true };
        AutomationProperties.SetAccessibilityView(image, AccessibilityView.Raw);
        var host = new ContentControl { Content = image, IsTabStop = false, HorizontalContentAlignment = HorizontalAlignment.Center };
        AutomationProperties.SetAutomationId(host, anchor is { } direction ? "CanvasResizeDetail" + direction : "CanvasResizeThumbnail");
        AutomationProperties.SetName(host, anchor is null ? "変更後のキャンバス全体" : name + "の拡大図");
        AutomationProperties.SetHelpText(host, anchor is null ? "変更後の全体図" : "画像25 × 25 pxを4倍表示");
        ToolTipService.SetToolTip(host, anchor is null ? "変更後の全体図" : name + "（4倍、25 × 25画像px）");
        var panel = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(new TextBlock { Text = name, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(new Border { BorderThickness = new(1), BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.LightGray), Child = host });
        Grid.SetRow(panel, row); Grid.SetColumn(panel, column); View.Children.Add(panel); _views.Add((image, host, anchor));
    }

    public void Dispose()
    {
        foreach (var (image, _, _) in _views) image.Source = null;
        _views.Clear();
    }
}
