using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PPPaint.Core;
using PPPaint.Imaging;
using Windows.System;
using Windows.UI.Core;

namespace PPPaint;

internal sealed class CanvasResizePreviewPane : IDisposable
{
    private readonly ScreenshotState _source;
    private readonly ICanvasResourceCreatorWithDpi _creator;
    private readonly ScreenshotRenderCache? _cache;
    private readonly List<(Image Image, CursorHost Host, ScreenshotCanvasViewAnchor Anchor)> _views = [];
    private ScreenshotCanvasResize? _layout, _dragStart;
    private CanvasRenderTarget? _original;
    private CursorHost? _dragHost;
    private ScreenshotBox _dragViewport;
    private Vector2 _press;
    private ScreenshotCanvasHandle _dragHandle;
    private uint? _pointer;
    private float _dpi;
    private int _revision;
    private float _magnification = 4;
    public bool FromCenter { get; set; }
    public bool KeepRatio { get; set; }
    public event Action<ScreenshotCanvasResize>? LayoutRequested;
    public Grid View { get; } = new() { ColumnSpacing = 8, RowSpacing = 6 };

    public CanvasResizePreviewPane(ICanvasResourceCreatorWithDpi creator, ScreenshotState source, ScreenshotRenderCache? cache)
    {
        _creator = creator; _source = source; _cache = cache;
        for (var i = 0; i < 3; i++) View.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        for (var i = 0; i < 3; i++) View.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Add("左上", ScreenshotCanvasViewAnchor.TopLeft, 0, 0); Add("上中央", ScreenshotCanvasViewAnchor.Top, 0, 1); Add("右上", ScreenshotCanvasViewAnchor.TopRight, 0, 2);
        Add("左中央", ScreenshotCanvasViewAnchor.Left, 1, 0); Add("右中央", ScreenshotCanvasViewAnchor.Right, 1, 2);
        Add("左下", ScreenshotCanvasViewAnchor.BottomLeft, 2, 0); Add("下中央", ScreenshotCanvasViewAnchor.Bottom, 2, 1); Add("右下", ScreenshotCanvasViewAnchor.BottomRight, 2, 2);
    }
    public void SetMagnification(float magnification)
    {
        if (!float.IsFinite(magnification) || magnification is < .25f or > 16) return;
        if (_magnification == magnification) return;
        _magnification = magnification;
        foreach (var (image, host, anchor) in _views)
        {
            AutomationProperties.SetHelpText(host, $"{magnification}倍表示、境界をドラッグで変更");
        }
        if (_layout is { } layout) SetLayout(layout, true);
    }
    private ScreenshotBox Viewport(Image view, ScreenshotCanvasViewAnchor anchor, ScreenshotCanvasResize layout) =>
        ScreenshotResizePreviewGeometry.FixedDetail(layout, anchor, (float)view.Width / _magnification, .2f);
    public void SetLayout(ScreenshotCanvasResize layout, bool force = false)
    {
        if (!force && _layout == layout && _dpi == _creator.Dpi) return;
        _layout = layout; _dpi = _creator.Dpi; _revision++;
        _original ??= ScreenshotRenderer.Render(_creator, _source, _cache);
        foreach (var (view, host, anchor) in _views)
        {
            var surface = view.Source as CanvasImageSource;
            if (surface is null || surface.Dpi != _dpi || surface.Size.Width != view.Width || surface.Size.Height != view.Height)
                view.Source = surface = new CanvasImageSource(_creator, (float)view.Width, (float)view.Height);
            var viewport = Viewport(view, anchor, layout);
            using (var ds = surface.CreateDrawingSession(Microsoft.UI.Colors.Transparent))
                ScreenshotResizePreviewDrawing.DrawFixedComparison(ds, _original, layout, viewport, (float)view.Width, (float)view.Height);
            var status = $"候補: {layout.Width} × {layout.Height} px、画像の移動 {layout.OffsetX}, {layout.OffsetY} px";
            AutomationProperties.SetItemStatus(host, status + $"、元 {layout.SourceWidth} × {layout.SourceHeight} px、拡大範囲 {viewport.X}, {viewport.Y}, {viewport.Width}, {viewport.Height} px、固定元範囲 {viewport.X - layout.OffsetX}, {viewport.Y - layout.OffsetY}, {viewport.Width}, {viewport.Height} px、描画 {_revision}");
        }
    }
    private static bool Down(VirtualKey key) => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
    private void Add(string name, ScreenshotCanvasViewAnchor anchor, int row, int column)
    {
        var image = new Image { Width = 100, Height = 100, Stretch = Stretch.None, UseLayoutRounding = true };
        AutomationProperties.SetAccessibilityView(image, AccessibilityView.Raw);
        var host = new CursorHost { Content = image, IsTabStop = true, HorizontalContentAlignment = HorizontalAlignment.Center, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        AutomationProperties.SetAutomationId(host, "CanvasResizeDetail" + anchor);
        AutomationProperties.SetName(host, name + "の拡大図");
        ToolTipService.SetToolTip(host, "橙の破線: 元の境界、青: 変更後の境界。青い境界をドラッグで変更");
        var border = new Border { BorderThickness = new(1), BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.LightGray), Child = host, HorizontalAlignment = HorizontalAlignment.Center };
        Grid.SetRow(border, row); Grid.SetColumn(border, column); View.Children.Add(border); _views.Add((image, host, anchor));
        Vector2 Point(PointerRoutedEventArgs e) { var p = e.GetCurrentPoint(host).Position; return new((float)p.X, (float)p.Y); }
        void UpdateDrag(Vector2 point)
        {
            if (_dragHost != host || _dragStart is not { } start) return;
            try
            {
                var delta = ScreenshotResizePreviewGeometry.ImagePoint(_dragViewport, point, (float)image.Width, (float)image.Height) - _press;
                LayoutRequested?.Invoke(ScreenshotResizePreviewGeometry.Drag(_source, start, _dragHandle, delta, FromCenter || Down(VirtualKey.Control), KeepRatio || Down(VirtualKey.Shift)));
            }
            catch (ArgumentException) { /* Keep the last valid candidate at the source/size limit. */ }
        }
        host.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            if (_pointer is not null || _layout is not { } layout || !e.GetCurrentPoint(host).Properties.IsLeftButtonPressed) return;
            var viewport = Viewport(image, anchor, layout); var point = Point(e);
            if (ScreenshotResizePreviewGeometry.Hit(layout, viewport, point, (float)image.Width, (float)image.Height) is not { } handle) return;
            _dragStart = layout; _dragViewport = viewport; _press = ScreenshotResizePreviewGeometry.ImagePoint(viewport, point, (float)image.Width, (float)image.Height);
            _dragHandle = handle; _dragHost = host; _pointer = e.Pointer.PointerId;
            host.Focus(FocusState.Pointer);
            if (!host.CapturePointer(e.Pointer)) { _pointer = null; _dragHost = null; _dragStart = null; return; }
            e.Handled = true;
        }), true);
        host.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((_, e) =>
        {
            if (_layout is not { } layout) return;
            var viewport = Viewport(image, anchor, layout); var point = Point(e);
            var handle = ScreenshotResizePreviewGeometry.Hit(layout, viewport, point, (float)image.Width, (float)image.Height);
            host.SetCursorShape((host == _dragHost ? _dragHandle : handle) switch
            {
                ScreenshotCanvasHandle.Left or ScreenshotCanvasHandle.Right => InputSystemCursorShape.SizeWestEast,
                ScreenshotCanvasHandle.Top or ScreenshotCanvasHandle.Bottom => InputSystemCursorShape.SizeNorthSouth,
                ScreenshotCanvasHandle.TopLeft or ScreenshotCanvasHandle.BottomRight => InputSystemCursorShape.SizeNorthwestSoutheast,
                ScreenshotCanvasHandle.TopRight or ScreenshotCanvasHandle.BottomLeft => InputSystemCursorShape.SizeNortheastSouthwest,
                _ => InputSystemCursorShape.Arrow
            });
            if (host != _dragHost || _pointer != e.Pointer.PointerId) return;
            UpdateDrag(point);
            e.Handled = true;
        }), true);
        void End(bool cancel)
        {
            if (_dragHost != host) return;
            var start = _dragStart; _pointer = null; _dragHost = null; _dragStart = null; host.ReleasePointerCaptures();
            if (cancel && start is not null) LayoutRequested?.Invoke(start);
            if (_layout is { } current) SetLayout(current, true);
        }
        host.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, e) => { if (_pointer == e.Pointer.PointerId) { UpdateDrag(Point(e)); End(false); e.Handled = true; } }), true);
        host.PointerCanceled += (_, _) => End(true); host.PointerCaptureLost += (_, _) => End(true);
    }
    public void Dispose()
    {
        var host = _dragHost; _dragHost = null; host?.ReleasePointerCaptures();
        foreach (var (image, _, _) in _views) image.Source = null;
        _views.Clear(); _original?.Dispose(); _original = null;
    }
}
