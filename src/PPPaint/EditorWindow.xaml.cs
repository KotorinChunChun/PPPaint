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
    private ScreenshotRenderCache? _renderCache;
    private ScreenshotTool _tool = ScreenshotTool.Select;
    private Rgba32 _color = new(220, 38, 38);
    private readonly HashSet<string> _selectedIds = [];
    private string? _primaryId, _savePath;
    private bool _hasImage, _busy, _dialogOpen, _updatingControls, _allowClose, _closing;
    private double _zoom = 1;
    private readonly List<ToggleButton> _toolButtons = [];
    private readonly TaskCompletionSource _canvasReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private sealed record Preferences(string Color = "#DC2626", double Width = 3, double FontSize = 24, double BlurRadius = 8, double MosaicSize = 12, double Opacity = 25, string[]? Palette = null);
    private string PreferencesPath => Path.Combine(App.Options.SettingsDirectory, "screenshot-preferences.json");
    internal bool Dirty => !ScreenshotEditing.SameImage(_savedState, _document.State);

    internal EditorWindow(ScreenshotFrame? frame = null)
    {
        _document = frame is null ? new(ScreenshotFrame.Transparent(1000, 650)) : ScreenshotDocument.FromImage(frame); _savedState = frame is null ? _document.State : null; _hasImage = frame is not null;
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 900));
        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.PreferredMinimumWidth = 920;
        BuildTools(); InitializeCommandKeys(); InitializeCanvasEdges(); LoadPreferences(); Refresh();
        PaintCanvas.Loaded += (_, _) => FitImage();
        Closed += Window_Closed;
    }

    private void BuildTools()
    {
        foreach (var command in ScreenshotCommands.Tools)
        {
            var tool = command.Tool; var button = new ToggleButton { Content = KeyLabel(command.Name, command.Key.ToString()), Tag = tool, IsChecked = tool == _tool, AccessKey = command.Key.ToString() };
            var purpose = tool switch { ScreenshotTool.Select => "。図形・画像を選択して移動・サイズ変更", ScreenshotTool.Region => "。画像上の矩形を指定して切り抜き・切り取り・削除", _ => "" };
            var modifiers = tool == ScreenshotTool.Select ? "。Ctrl/Shiftクリックで複数選択、Ctrlドラッグで複製、Shiftで移動方向/比率固定、Ctrlハンドルで中心基準、Altで吸着"
                : tool == ScreenshotTool.Number ? "。クリック位置を中心に配置、押したままドラッグで移動、開始番号を指定可能"
                : tool is ScreenshotTool.Region or ScreenshotTool.Rectangle or ScreenshotTool.Ellipse or ScreenshotTool.Redact or ScreenshotTool.Highlight or ScreenshotTool.Blur or ScreenshotTool.Mosaic or ScreenshotTool.Callout ? "。Shiftで正方形、Ctrlで中心基準、Ctrl+Shiftで併用、Altで吸着" : "";
            ToolTipService.SetToolTip(button, $"{command.Name} ({command.Key}) — Alt+{command.Key}でも切替{purpose}{modifiers}");
            AutomationProperties.SetAcceleratorKey(button, command.Key.ToString()); AutomationProperties.SetHelpText(button, $"{command.Name} ({command.Key})、Alt+{command.Key}{purpose}{modifiers}");
            AutomationProperties.SetAutomationId(button, "Tool" + tool); AutomationProperties.SetName(button, command.Name);
            button.AccessKeyInvoked += (_, args) => { SelectTool(tool); args.Handled = true; };
            button.Checked += (_, _) => { if (_tool != tool) SelectTool(tool); };
            button.Click += (_, _) => SelectTool(tool); _toolButtons.Add(button); ToolsPanel.Children.Add(button);
        }
        ToolsPanel.Children.Remove(EyedropperButton); ToolsPanel.Children.Insert(4, EyedropperButton);
        BuildPalette();
    }

    private void SelectTool(ScreenshotTool tool)
    {
        if (_busy || _dialogOpen || _pointerId is not null) { SetCurrentTool(_tool); return; }
        SetColorPicking(false);
        CancelGesture(); SetCurrentTool(tool);
        if (tool != ScreenshotTool.Select) _selectedIds.Clear();
        if (tool != ScreenshotTool.Region) _region = null;
        ShowSelectedOptions();
        Refresh();
        if (PaintCanvas.IsLoaded) InteractionCanvas.Focus(FocusState.Programmatic);
    }
    private void SetCurrentTool(ScreenshotTool tool)
    {
        _tool = tool;
        foreach (var button in _toolButtons) button.IsChecked = (ScreenshotTool)button.Tag == tool;
        if (PaintCanvas.IsLoaded) UpdateCursor(_hoverPoint);
    }
    private ScreenshotAnnotation[] SelectedObjects => _document.State.Annotations.Where(a => _selectedIds.Contains(a.Id)).ToArray();
    private ScreenshotAnnotation? Selected => _selectedIds.Count == 1 ? SelectedObjects.FirstOrDefault() : null;
    private float CurrentWidth => (float)(double.IsFinite(WidthBox.Value) ? Math.Clamp(WidthBox.Value, 1, 100) : 3);
    private float CurrentFontSize => (float)(double.IsFinite(FontBox.Value) ? Math.Clamp(FontBox.Value, 8, 200) : 24);
    private float CurrentBlurRadius => (float)(BlurRadiusBox is not null && double.IsFinite(BlurRadiusBox.Value) ? Math.Clamp(BlurRadiusBox.Value, 1, 64) : 8);
    private int CurrentMosaicSize => (int)(MosaicSizeBox is not null && double.IsFinite(MosaicSizeBox.Value) ? Math.Clamp(Math.Round(MosaicSizeBox.Value), 2, 128) : 12);
    private float CurrentOpacity => (float)(OpacityBox is not null && double.IsFinite(OpacityBox.Value) ? Math.Clamp(OpacityBox.Value / 100, .05, 1) : .25);
    private void Refresh()
    {
        if (PaintCanvas is null) return;
        _selectedIds.RemoveWhere(id => !_document.State.Annotations.Any(a => a.Id == id));
        var frame = _document.State.Frame;
        var canvasWidth = _canvasResizePreview?.Width ?? frame.Width; var canvasHeight = _canvasResizePreview?.Height ?? frame.Height;
        PaintCanvas.Width = canvasWidth * _zoom; PaintCanvas.Height = canvasHeight * _zoom;
        InteractionCanvas.Width = PaintCanvas.Width + CanvasImageBorder.Padding.Left + CanvasImageBorder.Padding.Right;
        InteractionCanvas.Height = PaintCanvas.Height + CanvasImageBorder.Padding.Top + CanvasImageBorder.Padding.Bottom;
        DimensionsButton.Content = KeyLabel($"{canvasWidth} × {canvasHeight} px", "Alt+Z");
        UndoButton.IsEnabled = _document.CanUndo; RedoButton.IsEnabled = _document.CanRedo;
        CropButton.IsEnabled = _region is { Width: >= 1, Height: >= 1 }; DeleteButton.IsEnabled = _selectedIds.Count > 0 || _region is not null;
        CutButton.IsEnabled = _selectedIds.Count > 0 || _region is not null;
        OrderButton.IsEnabled = _selectedIds.Count > 0;
        OpenBackupMenuItem.IsEnabled = _backupPath is not null && File.Exists(_backupPath);
        Title = WindowTitle.Create((Dirty ? "* " : "") + (_savePath is null ? _sourceName ?? "無題" : Path.GetFileName(_savePath)));
        CustomColorButton.Background = new SolidColorBrush(ToColor(_color));
        CustomColorButton.Foreground = new SolidColorBrush(_color.R * .299 + _color.G * .587 + _color.B * .114 < 145 ? Colors.White : Colors.Black);
        RefreshPalette();
        InvalidateCanvas();
    }
    private void Canvas_CreateResources(CanvasControl sender, Microsoft.Graphics.Canvas.UI.CanvasCreateResourcesEventArgs args) { _renderCache?.Dispose(); _renderCache = null; _canvasReady.TrySetResult(); }
    private void Canvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var state = _document.State;
        var ds = args.DrawingSession; ds.Clear(Colors.White); ds.Transform = Matrix3x2.CreateScale((float)_zoom);
        _renderCache ??= new ScreenshotRenderCache(sender);
        _renderCache.Retain(state.Annotations, state.Frame);
        if (_canvasResizePreview is { } layout)
        {
            DrawCanvasBackground(ds, true, layout.Width, layout.Height);
            ScreenshotRenderer.DrawCanvasImage(ds, CanvasResizePreviewImage());
            ds.DrawRectangle(new Rect(1 / _zoom, 1 / _zoom, Math.Max(0, layout.Width - 2 / _zoom), Math.Max(0, layout.Height - 2 / _zoom)), Colors.DodgerBlue, 2 / (float)_zoom);
            return;
        }
        var objects = _duplicating ? state.Annotations.AddRange(_previews.Values) : state.Annotations.Select(a => _previews.GetValueOrDefault(a.Id, a)).ToImmutableArray();
        if (_draft is not null) objects = objects.Add(_draft);
        DrawCanvasBackground(ds, true);
        ScreenshotRenderer.DrawCanvasImage(ds, _renderCache.GetScene(state with { Annotations = objects }));
    }
    private static Vector2[] Corners(ScreenshotBox b) => [new(b.X, b.Y), new(b.Right, b.Y), new(b.Right, b.Bottom), new(b.X, b.Bottom)];
    private void Undo_Click(object sender, RoutedEventArgs e) { if (_busy) return; CancelGesture(); _document.Undo(); _selectedIds.Clear(); _region = null; ShowSelectedOptions(); Refresh(); }
    private void Redo_Click(object sender, RoutedEventArgs e) { if (_busy) return; CancelGesture(); _document.Redo(); _selectedIds.Clear(); _region = null; ShowSelectedOptions(); Refresh(); }
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (_region is { } region) { ClampRegion(); region = _region!.Value; _document.DeleteRange(new((int)region.X, (int)region.Y, Math.Max(1, (int)region.Width), Math.Max(1, (int)region.Height))); _selectedIds.Clear(); _region = null; }
        else if (_selectedIds.Count > 0) { _document.Delete(_selectedIds); _selectedIds.Clear(); }
        Refresh();
    }
    private void New_Click(object sender, RoutedEventArgs e) { if (!_busy) App.Instance.CreateWindow(); }
    private void Style_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_updatingControls || _busy || !double.IsFinite(args.NewValue) || PaintCanvas is null) return;
        _document.ReplaceMany(SelectedObjects.Where(a => a.Tool != ScreenshotTool.Image).Select(a => a with { Width = CurrentWidth, FontSize = CurrentFontSize, BlurRadius = CurrentBlurRadius, MosaicSize = CurrentMosaicSize, Opacity = CurrentOpacity }));
        SavePreferences(); Refresh();
    }
    private void NumberStart_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_updatingControls || _busy || _pointerId is not null || !double.IsFinite(args.NewValue) || args.NewValue != Math.Truncate(args.NewValue) || args.NewValue is < 1 or > int.MaxValue) return;
        _document.SetNextNumber((int)args.NewValue); Refresh();
    }
    private void TextInput_Changed(object sender, TextChangedEventArgs e)
    {
        if (_updatingControls || _busy) return;
        if (Selected is { Tool: ScreenshotTool.Text or ScreenshotTool.Number or ScreenshotTool.Callout or ScreenshotTool.Watermark } selected)
        {
            var bounds = selected.Bounds;
            if (selected.Tool is ScreenshotTool.Text or ScreenshotTool.Watermark)
            {
                var lines = TextInput.Text.Replace("\r", "").Split('\n');
                var width = Math.Min(Math.Max(bounds.Width, lines.Max(line => line.Length) * selected.FontSize), Math.Max(32, _document.State.Frame.Width - bounds.X));
                var rows = lines.Sum(line => Math.Max(1, (int)Math.Ceiling(line.Length * selected.FontSize / width)));
                bounds = bounds with { Width = width, Height = Math.Max(bounds.Height, rows * selected.FontSize * 1.6f) };
            }
            _document.Replace(selected with { Text = TextInput.Text, Bounds = bounds }); Refresh();
        }
    }
    private void SetColor(Rgba32 color)
    {
        _color = color with { A = 255 };
        _document.ReplaceMany(SelectedObjects.Where(a => a.Tool != ScreenshotTool.Image).Select(a => a with { Color = _color }));
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
            BlurRadiusBox.Value = double.IsFinite(p.BlurRadius) ? Math.Clamp(p.BlurRadius, 1, 64) : 8;
            MosaicSizeBox.Value = double.IsFinite(p.MosaicSize) ? Math.Clamp(p.MosaicSize, 2, 128) : 12;
            OpacityBox.Value = double.IsFinite(p.Opacity) ? Math.Clamp(p.Opacity, 5, 100) : 25;
            if (p.Palette is { } palette)
                for (var i = 0; i < Math.Min(palette.Length, _palette.Length); i++)
                    try { if (!string.IsNullOrWhiteSpace(palette[i])) _palette[i] = ParseColor(palette[i]); } catch (ArgumentException) { }
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
            File.WriteAllText(temp, JsonSerializer.Serialize(new Preferences(ColorHex(_color), CurrentWidth, CurrentFontSize, CurrentBlurRadius, CurrentMosaicSize, CurrentOpacity * 100, _palette.Select(ColorHex).ToArray())));
            File.Move(temp, PreferencesPath, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { StatusText.Text = "設定を保存できませんでした。"; }
    }
    private async Task RunAsync(Func<Task> action)
    {
        if (_busy) return; CancelGesture(); _busy = true;
        try { await _canvasReady.Task; await action(); }
        catch (Exception error) { await MessageAsync("操作できませんでした", string.IsNullOrWhiteSpace(error.Message) ? "操作を完了できませんでした。" : error.Message); }
        finally { _busy = false; Refresh(); }
    }
    private async void Window_Closed(object sender, WindowEventArgs args)
    {
        if (_allowClose || !Dirty && !_busy && !_dialogOpen && !_closing)
        {
            _renderCache?.Dispose(); InteractionCanvas.RemoveFromVisualTree(); PaintCanvas.RemoveFromVisualTree(); return;
        }
        // WinUI Closed is cancellable and occurs before AppWindow.Closing. All cleanup must respect Handled.
        args.Handled = true;
        if (_busy || _dialogOpen || _closing) return;
        _closing = true;
        try
        {
            var result = await AskAsync("未保存の画像", "画像を保存してから閉じますか？", "保存", "保存せずに閉じる");
            var close = result == ContentDialogResult.Secondary;
            if (result == ContentDialogResult.Primary) { await RunAsync(async () => close = await SaveImageAsync()); }
            if (close) { _allowClose = true; Close(); }
        }
        finally { _closing = false; }
    }
    internal bool ManagementReady => PaintCanvas.IsLoaded && _canvasReady.Task.IsCompletedSuccessfully && !_busy && !_dialogOpen;
    internal string? ManagementBlockReason(bool checkUnsaved = true) => App.Instance.Windows.Any(w => w._busy || w._dialogOpen || w._pointerId is not null) ? "編集・ファイル操作中です。" : checkUnsaved && App.Instance.Windows.Any(w => w.Dirty) ? "未保存の画像があります。" : null;
    internal void ManagementShow() { AppWindow.Show(); Activate(); }
    internal async Task ManagementOpenAsync(string path) { await _canvasReady.Task; await RunAsync(() => OpenPathAsync(path)); }
    internal void ManagementStop() { if (ManagementBlockReason() is null) foreach (var window in App.Instance.Windows.ToArray()) window.Close(); }
}
