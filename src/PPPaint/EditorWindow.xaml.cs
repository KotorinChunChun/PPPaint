using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using PPPaint.Core;
using PPPaint.Imaging;
using Windows.Foundation;
using Windows.UI;

namespace PPPaint;

public sealed partial class EditorWindow : Window
{
    private ScreenshotDocument _document;
    private ScreenshotState? _savedState;
    private ScreenshotFrame? _bitmapFrame;
    private CanvasBitmap? _bitmap;
    private ScreenshotTool _tool = ScreenshotTool.Select;
    private Rgba32 _color = new(220, 38, 38);
    private string? _selectedId, _savePath;
    private bool _hasImage, _busy, _dialogOpen, _updatingControls, _allowClose, _closing;
    private double _zoom = 1;
    private readonly List<ToggleButton> _toolButtons = [];
    private readonly TaskCompletionSource _loaded = new();
    private sealed record Preferences(string Color = "#DC2626", double Width = 3, double FontSize = 24);
    private string PreferencesPath => Path.Combine(App.Options.SettingsDirectory, "screenshot-preferences.json");
    internal bool Dirty => !ReferenceEquals(_savedState, _document.State);

    internal EditorWindow(ScreenshotFrame? frame = null)
    {
        _document = new(frame ?? ScreenshotFrame.Blank()); _savedState = frame is null ? _document.State : null; _hasImage = frame is not null;
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 900));
        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.PreferredMinimumWidth = 920;
        BuildTools(); LoadPreferences(); Refresh();
        PaintCanvas.Loaded += (_, _) => { _loaded.TrySetResult(); FitImage(); };
        AppWindow.Closing += Window_Closing;
        Closed += (_, _) => { _bitmap?.Dispose(); PaintCanvas.RemoveFromVisualTree(); };
    }

    private void BuildTools()
    {
        (ScreenshotTool Tool, string Label)[] tools = [(ScreenshotTool.Select, "選択"), (ScreenshotTool.Region, "範囲"), (ScreenshotTool.Pen, "ペン"),
            (ScreenshotTool.Eraser, "消しゴム"), (ScreenshotTool.Arrow, "矢印"), (ScreenshotTool.Rectangle, "四角"), (ScreenshotTool.Ellipse, "丸"),
            (ScreenshotTool.Line, "直線"), (ScreenshotTool.Text, "文字"), (ScreenshotTool.Number, "番号"), (ScreenshotTool.Redact, "情報隠し"), (ScreenshotTool.Highlight, "蛍光ペン")];
        foreach (var (tool, label) in tools)
        {
            var button = new ToggleButton { Content = label, Tag = tool, IsChecked = tool == _tool };
            AutomationProperties.SetAutomationId(button, "Tool" + tool); AutomationProperties.SetName(button, label);
            button.Click += (_, _) => SelectTool(tool); _toolButtons.Add(button); ToolsPanel.Children.Add(button);
        }
        foreach (var hex in new[] { "#DC2626", "#EA580C", "#FACC15", "#16A34A", "#2563EB", "#9333EA", "#000000", "#FFFFFF" })
        {
            var color = ParseColor(hex);
            var button = new Button { Width = 28, Height = 28, MinHeight = 28, Padding = new Thickness(0), Background = new SolidColorBrush(ToColor(color)), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(255, 128, 128, 128)) };
            AutomationProperties.SetName(button, hex); button.Click += (_, _) => SetColor(color); ColorsPanel.Children.Add(button);
        }
    }

    private void SelectTool(ScreenshotTool tool)
    {
        if (_busy) return; CancelGesture(); _tool = tool;
        foreach (var button in _toolButtons) button.IsChecked = (ScreenshotTool)button.Tag == tool;
        if (tool != ScreenshotTool.Select) _selectedId = null;
        if (tool != ScreenshotTool.Region) _region = null;
        TextOptions.Visibility = tool is ScreenshotTool.Text or ScreenshotTool.Number ? Visibility.Visible : Visibility.Collapsed;
        WidthBox.IsEnabled = tool is not (ScreenshotTool.Redact or ScreenshotTool.Highlight or ScreenshotTool.Number or ScreenshotTool.Text);
        Refresh();
    }
    private ScreenshotAnnotation? Selected => _document.State.Annotations.FirstOrDefault(a => a.Id == _selectedId);
    private void Refresh()
    {
        if (PaintCanvas is null) return;
        var frame = _document.State.Frame;
        PaintCanvas.Width = frame.Width * _zoom; PaintCanvas.Height = frame.Height * _zoom;
        DimensionsButton.Content = $"{frame.Width} × {frame.Height} px";
        UndoButton.IsEnabled = _document.CanUndo; RedoButton.IsEnabled = _document.CanRedo;
        CropButton.IsEnabled = _region is { Width: >= 1, Height: >= 1 }; DeleteButton.IsEnabled = Selected is not null || _region is not null;
        Title = WindowTitle.Create((Dirty ? "* " : "") + (_savePath is null ? _sourceName ?? "無題" : Path.GetFileName(_savePath)));
        CustomColorButton.Background = new SolidColorBrush(ToColor(_color));
        CustomColorButton.Foreground = new SolidColorBrush(_color.R * .299 + _color.G * .587 + _color.B * .114 < 145 ? Colors.White : Colors.Black);
        PaintCanvas.Invalidate();
    }
    private void Canvas_CreateResources(CanvasControl sender, Microsoft.Graphics.Canvas.UI.CanvasCreateResourcesEventArgs args) { _bitmapFrame = null; }
    private void Canvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var state = _document.State;
        if (!ReferenceEquals(_bitmapFrame, state.Frame)) { _bitmap?.Dispose(); _bitmap = ScreenshotRenderer.Bitmap(sender, state.Frame); _bitmapFrame = state.Frame; }
        var ds = args.DrawingSession; ds.Clear(Colors.White); ds.Transform = Matrix3x2.CreateScale((float)_zoom);
        ds.DrawImage(_bitmap);
        foreach (var annotation in state.Annotations) ScreenshotRenderer.Draw(ds, _draft?.Id == annotation.Id ? _draft : annotation);
        if (_draft is not null && !state.Annotations.Any(a => a.Id == _draft.Id)) ScreenshotRenderer.Draw(ds, _draft);
        if ((_draft ?? Selected) is { } selected) DrawBox(ds, selected.Bounds, true);
        if (_region is { } region) DrawBox(ds, region, false);
    }
    private void DrawBox(CanvasDrawingSession ds, ScreenshotBox b, bool handles)
    {
        var line = 1.5f / (float)_zoom;
        ds.DrawRectangle(new Rect(b.X, b.Y, Math.Max(1, b.Width), Math.Max(1, b.Height)), Colors.DodgerBlue, line);
        if (!handles) return;
        var size = 8f / (float)_zoom;
        foreach (var p in Corners(b)) { var r = new Rect(p.X - size / 2, p.Y - size / 2, size, size); ds.FillRectangle(r, Colors.White); ds.DrawRectangle(r, Colors.DodgerBlue, line); }
    }
    private static Vector2[] Corners(ScreenshotBox b) => [new(b.X, b.Y), new(b.Right, b.Y), new(b.Right, b.Bottom), new(b.X, b.Bottom)];
    private void Undo_Click(object sender, RoutedEventArgs e) { if (_busy) return; CancelGesture(); _document.Undo(); _selectedId = null; _region = null; Refresh(); }
    private void Redo_Click(object sender, RoutedEventArgs e) { if (_busy) return; CancelGesture(); _document.Redo(); _selectedId = null; _region = null; Refresh(); }
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (Selected is { } selected) { _document.Delete(selected.Id); _selectedId = null; }
        else if (_region is { } region) { _document.Add(new(Guid.NewGuid().ToString("N"), ScreenshotTool.Redact, region, Rgba32.White)); _region = null; }
        Refresh();
    }
    private void New_Click(object sender, RoutedEventArgs e) { if (!_busy) App.Instance.CreateWindow(); }
    private void Style_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_updatingControls || _busy || !double.IsFinite(args.NewValue) || PaintCanvas is null) return;
        if (Selected is { } selected) _document.Replace(selected with { Width = (float)WidthBox.Value, FontSize = (float)FontBox.Value });
        SavePreferences(); Refresh();
    }
    private void TextInput_Changed(object sender, TextChangedEventArgs e)
    {
        if (_updatingControls || _busy) return;
        if (Selected is { Tool: ScreenshotTool.Text or ScreenshotTool.Number } selected) { _document.Replace(selected with { Text = TextInput.Text }); Refresh(); }
    }
    private void SetColor(Rgba32 color)
    {
        _color = color with { A = 255 };
        if (Selected is { } selected) _document.Replace(selected with { Color = _color });
        SavePreferences(); Refresh();
    }
    private static Color ToColor(Rgba32 c) => Color.FromArgb(c.A, c.R, c.G, c.B);
    private static Rgba32 ParseColor(string text)
    {
        text = text.Trim().TrimStart('#');
        if (text.Length != 6 || !uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var c)) throw new ArgumentException("色は6桁の16進数で指定してください。");
        return new((byte)(c >> 16), (byte)(c >> 8), (byte)c);
    }
    private void LoadPreferences()
    {
        try
        {
            if (!File.Exists(PreferencesPath)) return;
            var p = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(PreferencesPath)); if (p is null) return;
            _updatingControls = true; _color = ParseColor(p.Color);
            WidthBox.Value = double.IsFinite(p.Width) ? Math.Clamp(p.Width, 1, 100) : 3;
            FontBox.Value = double.IsFinite(p.FontSize) ? Math.Clamp(p.FontSize, 8, 200) : 24;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
        finally { _updatingControls = false; }
    }
    private void SavePreferences()
    {
        if (WidthBox is null || _updatingControls) return;
        try
        {
            Directory.CreateDirectory(App.Options.SettingsDirectory);
            var temp = PreferencesPath + "." + Environment.ProcessId + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new Preferences($"#{_color.R:X2}{_color.G:X2}{_color.B:X2}", WidthBox.Value, FontBox.Value)));
            File.Move(temp, PreferencesPath, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { StatusText.Text = "設定を保存できませんでした。"; }
    }
    private async Task RunAsync(Func<Task> action)
    {
        if (_busy) return; CancelGesture(); _busy = true;
        try { await action(); }
        catch (Exception error) { await MessageAsync("操作できませんでした", error.Message); }
        finally { _busy = false; Refresh(); }
    }
    private async void Window_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose) return;
        if (_busy || _dialogOpen || _closing) { args.Cancel = true; return; }
        if (!Dirty) return;
        args.Cancel = true; _closing = true;
        try
        {
            var result = await AskAsync("未保存の画像", "画像を保存してから閉じますか？", "保存", "保存せず閉じる");
            var close = result == ContentDialogResult.Secondary;
            if (result == ContentDialogResult.Primary) { await RunAsync(async () => close = await SaveImageAsync()); }
            if (close) { _allowClose = true; Close(); }
        }
        finally { _closing = false; }
    }
    internal bool ManagementReady => PaintCanvas.IsLoaded && !_busy && !_dialogOpen;
    internal string? ManagementBlockReason(bool checkUnsaved = true) => App.Instance.Windows.Any(w => w._busy || w._dialogOpen || w._pointerId is not null) ? "編集・ファイル操作中です。" : checkUnsaved && App.Instance.Windows.Any(w => w.Dirty) ? "未保存の画像があります。" : null;
    internal void ManagementShow() { AppWindow.Show(); Activate(); }
    internal async Task ManagementOpenAsync(string path) { await _loaded.Task; await RunAsync(() => OpenPathAsync(path)); }
    internal void ManagementStop() { if (ManagementBlockReason() is null) foreach (var window in App.Instance.Windows.ToArray()) window.Close(); }
}
