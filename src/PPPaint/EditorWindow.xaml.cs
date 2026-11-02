using System.Collections.Immutable;
using System.Numerics;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PPPaint.Core;
using PPPaint.Imaging;
using Windows.UI;

namespace PPPaint;

public sealed partial class EditorWindow : Window
{
    private ScreenshotDocument _document;
    private ScreenshotState? _savedState;
    private ScreenshotRenderCache? _renderCache;
    private ScreenshotTool _tool = ScreenshotTool.Select;
    private readonly HashSet<string> _selectedIds = [];
    private string? _primaryId, _savePath;
    private bool _hasImage, _busy, _dialogOpen, _updatingControls, _allowClose, _closing;
    private double _zoom = 1;
    private readonly List<ToggleButton> _toolButtons = [];
    private readonly TaskCompletionSource _canvasReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal bool Dirty => !ScreenshotEditing.SameImage(_savedState, _document.State);

    internal EditorWindow(ScreenshotFrame? frame = null)
    {
        // A blank window starts saved; a window created for an image starts unsaved until it is saved.
        _document = frame is null ? new(ScreenshotFrame.Transparent(1000, 650)) : ScreenshotDocument.FromImage(frame);
        _savedState = frame is null ? _document.State : null;
        _hasImage = frame is not null;
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 900));
        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.PreferredMinimumWidth = 920;
        BuildTools(); InitializeCommandKeys(); InitializeCanvasEdges(); LoadPreferences(); InitializeClipboard(); Refresh();
        TextInput.TextCompositionStarted += (_, _) => _textComposing = true;
        TextInput.TextCompositionEnded += (_, _) => DispatcherQueue.TryEnqueue(() => _textComposing = false);
        TextInput.LostFocus += (_, _) => _document.EndMerge();
        PaintCanvas.Loaded += (_, _) => FitImage();
        Closed += Window_Closed;
    }

    private void BuildTools()
    {
        foreach (var command in ScreenshotCommands.Tools)
        {
            if (command.Tool == ScreenshotTool.Redact) continue;
            var tool = command.Tool; var accessKey = tool == ScreenshotTool.Blur ? "" : command.Key.ToString();
            var button = new ToggleButton { Content = KeyLabel(command.Name, command.Key.ToString()), Tag = tool, IsChecked = tool == _tool, AccessKey = accessKey };
            var purpose = tool switch { ScreenshotTool.Select => "。図形・画像を選択して移動・サイズ変更", ScreenshotTool.Region => "。画像上の矩形を指定して切り抜き・切り取り・削除", _ => "" };
            var modifiers = tool == ScreenshotTool.Select ? "。Ctrl/Shiftクリックで複数選択、Ctrlドラッグで複製、Shiftで移動方向/比率固定、Ctrlハンドルで中心基準、Altで吸着"
                : tool == ScreenshotTool.Number ? "。クリック位置を中心に配置、押したままドラッグで移動、開始番号を指定可能"
                : tool is ScreenshotTool.Region or ScreenshotTool.Rectangle or ScreenshotTool.Ellipse or ScreenshotTool.Redact or ScreenshotTool.Highlight or ScreenshotTool.Blur or ScreenshotTool.Mosaic or ScreenshotTool.Callout ? "。Shiftで正方形、Ctrlで中心基準、Ctrl+Shiftで併用、Altで吸着" : "";
            var alternateTip = accessKey.Length == 0 ? "" : $" — Alt+{command.Key}でも切替";
            var alternateHelp = accessKey.Length == 0 ? "" : $"、Alt+{command.Key}";
            ToolTipService.SetToolTip(button, $"{command.Name} ({command.Key}){alternateTip}{purpose}{modifiers}");
            AutomationProperties.SetAcceleratorKey(button, command.Key.ToString()); AutomationProperties.SetHelpText(button, $"{command.Name} ({command.Key}){alternateHelp}{purpose}{modifiers}");
            AutomationProperties.SetAutomationId(button, "Tool" + tool); AutomationProperties.SetName(button, command.Name);
            button.AccessKeyInvoked += (_, args) => { SelectTool(tool); args.Handled = true; };
            button.Checked += (_, _) => { if (_tool != tool) SelectTool(tool); };
            button.Click += (_, _) => SelectTool(tool); _toolButtons.Add(button);
            if (tool is ScreenshotTool.Rectangle or ScreenshotTool.Ellipse or ScreenshotTool.Arrow or ScreenshotTool.Number)
            {
                var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 }; group.Children.Add(button);
                var styles = new Button { Content = "▼", Padding = new Thickness(6, 5, 6, 5), MinWidth = 26 };
                AutomationProperties.SetAutomationId(styles, "Tool" + tool + "Styles"); AutomationProperties.SetName(styles, command.Name + "のスタイル");
                ToolTipService.SetToolTip(styles, command.Name + "の種類・塗りつぶし");
                styles.Click += (_, _) => ShowShapeMenu(tool, styles); group.Children.Add(styles); ToolsPanel.Children.Add(group);
            }
            else ToolsPanel.Children.Add(button);
        }
        ToolsPanel.Children.Remove(EyedropperButton); ToolsPanel.Children.Insert(4, EyedropperButton);
        var redactKey = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = Windows.System.VirtualKey.K, Modifiers = Windows.System.VirtualKeyModifiers.Menu };
        redactKey.Invoked += (_, args) => { if (!_busy && !_dialogOpen) SelectTool(ScreenshotTool.Redact); args.Handled = true; };
        Root.KeyboardAccelerators.Add(redactKey);
        BuildPalette();
    }

    private void SelectTool(ScreenshotTool tool)
    {
        if (_busy || _dialogOpen || _pointerId is not null) { SetCurrentTool(_tool); return; }
        ReloadToolPreferences();
        if (tool == ScreenshotTool.Redact)
        {
            tool = ScreenshotTool.Rectangle; var shape = _toolPreferences.GetShape(tool) with { Filled = true };
            _toolPreferences.SetShape(tool, shape); _shapePreferenceChanges[tool] = shape; SavePreferences();
        }
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
    private void Refresh()
    {
        if (PaintCanvas is null) return;
        if (_selectedIds.RemoveWhere(id => !_document.State.Annotations.Any(a => a.Id == id)) > 0) ShowSelectedOptions();
        var frame = _document.State.Frame;
        var canvasWidth = frame.Width; var canvasHeight = frame.Height;
        if (_fitToWindow && !_fitting && (canvasWidth != _fitCanvasWidth || canvasHeight != _fitCanvasHeight)) FitImage();
        PaintCanvas.Width = canvasWidth * _zoom; PaintCanvas.Height = canvasHeight * _zoom;
        InteractionCanvas.Width = PaintCanvas.Width + CanvasImageBorder.Padding.Left + CanvasImageBorder.Padding.Right;
        InteractionCanvas.Height = PaintCanvas.Height + CanvasImageBorder.Padding.Top + CanvasImageBorder.Padding.Bottom;
        DimensionsButton.Content = KeyLabel($"{canvasWidth} × {canvasHeight} px", "Alt+Z");
        UndoButton.IsEnabled = _document.CanUndo; RedoButton.IsEnabled = _document.CanRedo;
        CropButton.IsEnabled = CropArea is not null; DeleteButton.IsEnabled = _selectedIds.Count > 0 || _region is not null;
        CutButton.IsEnabled = _selectedIds.Count > 0 || _region is not null;
        UpdatePasteButtons();
        OrderButton.IsEnabled = _selectedIds.Count > 0;
        MergeSelectionMenuItem.IsEnabled = _selectedIds.Count > 0;
        AutomationProperties.SetItemStatus(InteractionCanvas, _selectedIds.Count > 0 ? $"選択: {_selectedIds.Count}個" : _region is not null ? "範囲選択" : "選択なし");
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
        var state = _liftPreview ?? _document.State;
        var ds = args.DrawingSession; ds.Clear(Colors.White); ds.Transform = Matrix3x2.CreateScale((float)_zoom);
        _renderCache ??= new ScreenshotRenderCache(sender);
        _renderCache.Retain(state.Annotations, state.Frame);
        var objects = _duplicating ? state.Annotations.AddRange(_previews.Values) : state.Annotations.Select(a => _previews.GetValueOrDefault(a.Id, a)).ToImmutableArray();
        if (_draft is not null) objects = objects.Add(_draft);
        DrawCanvasBackground(ds, true);
        ScreenshotRenderer.DrawCanvasImage(ds, _renderCache.GetScene(state with { Annotations = objects }));
    }
    private void Undo_Click(object sender, RoutedEventArgs e) => StepHistory(_document.Undo);
    private void Redo_Click(object sender, RoutedEventArgs e) => StepHistory(_document.Redo);
    private void StepHistory(Action step)
    {
        if (_busy) return;
        CancelGesture(); step();
        _selectedIds.Clear(); _region = null;
        ShowSelectedOptions(); Refresh();
    }
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (_region is not null)
        {
            if (RegionArea is { } area) _document.DeleteRange(area);
            _selectedIds.Clear(); _region = null;
        }
        else if (_selectedIds.Count > 0) { _document.Delete(_selectedIds); _selectedIds.Clear(); }
        ShowSelectedOptions(); Refresh();
    }
    private void New_Click(object sender, RoutedEventArgs e) { if (!_busy) App.Instance.CreateWindow(); }
    private async Task RunAsync(Func<Task> action)
    {
        if (_busy) return; CancelGesture(); _busy = true; UpdatePasteButtons();
        try { await _canvasReady.Task; await action(); }
        catch (Exception error) { await MessageAsync("操作できませんでした", string.IsNullOrWhiteSpace(error.Message) ? "操作を完了できませんでした。" : error.Message); }
        finally { _busy = false; Refresh(); }
    }
    private async void Window_Closed(object sender, WindowEventArgs args)
    {
        if (_allowClose || !Dirty && !_busy && !_dialogOpen && !_closing)
        {
            StopTrackingClipboard(); ClearColorCapture(); _renderCache?.Dispose(); CanvasResizeOverlay.RemoveFromVisualTree(); InteractionCanvas.RemoveFromVisualTree(); PaintCanvas.RemoveFromVisualTree(); return;
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
