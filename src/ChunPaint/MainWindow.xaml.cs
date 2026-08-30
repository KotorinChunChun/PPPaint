using System.Numerics;
using ChunPaint.Core;
using ChunPaint.Editing;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;
using WinRT.Interop;

namespace ChunPaint;

public sealed partial class MainWindow : Window
{
    private const int InitialWidth = 1152;
    private const int InitialHeight = 648;

    private readonly Stack<DocumentSnapshot> _undo = new();
    private readonly Stack<DocumentSnapshot> _redo = new();
    private readonly List<ToggleButton> _toolButtons;
    private readonly List<LayerState> _layers = [];

    private CanvasRenderTarget? _surface;
    private StorageFile? _currentFile;
    private PaintTool _tool = PaintTool.Pencil;
    private Color _primaryColor = Colors.Black;
    private Color _secondaryColor = Colors.White;
    private Vector2 _lastPoint;
    private bool _isDrawing;
    private int _imageWidth = InitialWidth;
    private int _imageHeight = InitialHeight;
    private double _zoom = 1.0;
    private bool _showGrid;
    private bool _dirty;
    private int _activeLayerIndex;
    private SelectionRegion? _selection;
    private bool _isSelecting;
    private bool _updatingLayersPanel;

    public MainWindow()
    {
        InitializeComponent();

        _toolButtons =
        [
            SelectToolButton,
            PencilToolButton,
            BrushToolButton,
            EraserToolButton,
            FillToolButton,
            PickerToolButton,
            TextToolButton
        ];

        ConfigureWindow();
        ConfigureAccelerators();
        ConfigureWidgetAccelerators();
        LoadRecentFiles();
        UpdateHistoryState();
        LayersList.SelectionChanged += LayersList_SelectionChanged;
        LayersList.RightTapped += LayersList_RightTapped;
        PaintCanvas.RightTapped += PaintCanvas_RightTapped;
    }

    private void ConfigureWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.Resize(new SizeInt32(1280, 820));
        _appWindow.SetIcon("mspaint.exe");
        _appWindow.Closing += AppWindow_Closing;
    }

    private bool _allowWindowClose;
    private bool _closeConfirmationShowing;

    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowWindowClose || !_dirty)
        {
            return;
        }

        args.Cancel = true;
        if (_closeConfirmationShowing)
        {
            return;
        }

        _closeConfirmationShowing = true;
        try
        {
            if (await ConfirmDiscardAsync())
            {
                _allowWindowClose = true;
                Close();
            }
        }
        finally
        {
            _closeConfirmationShowing = false;
        }
    }

    private void ConfigureAccelerators()
    {
        AddAccelerator(VirtualKey.Z, VirtualKeyModifiers.Control, (_, args) =>
        {
            Undo();
            args.Handled = true;
        });
        AddAccelerator(VirtualKey.Y, VirtualKeyModifiers.Control, (_, args) =>
        {
            Redo();
            args.Handled = true;
        });
        AddAccelerator(VirtualKey.N, VirtualKeyModifiers.Control, async (_, args) =>
        {
            args.Handled = true;
            await NewDocumentAsync();
        });
        AddAccelerator(VirtualKey.O, VirtualKeyModifiers.Control, async (_, args) =>
        {
            args.Handled = true;
            await OpenDocumentAsync(false);
        });
        AddAccelerator(VirtualKey.S, VirtualKeyModifiers.Control, async (_, args) =>
        {
            args.Handled = true;
            await TrySaveDocumentAsync(null);
        });
        AddAccelerator(VirtualKey.S, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, async (_, args) =>
        {
            args.Handled = true;
            await TrySaveDocumentAsync("chunpaint");
        });
    }

    private void AddAccelerator(VirtualKey key, VirtualKeyModifiers modifiers, TypedEventHandler<KeyboardAccelerator, KeyboardAcceleratorInvokedEventArgs> handler)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accelerator.Invoked += handler;
        PaintCanvas.KeyboardAccelerators.Add(accelerator);
    }

    private void EnsureSurface(ICanvasResourceCreator resourceCreator)
    {
        if (_surface is not null)
        {
            return;
        }

        _surface = new CanvasRenderTarget(resourceCreator, _imageWidth, _imageHeight, 96);
        using (var session = _surface.CreateDrawingSession())
            session.Clear(Colors.White);
        _layers.Add(new LayerState("背景", _surface));
        _activeLayerIndex = 0;
        RefreshLayersPanel();
    }

    private void PaintCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        EnsureSurface(sender);
        args.DrawingSession.Clear(Colors.Transparent);
        foreach (var layer in _layers)
        {
            if (layer.IsVisible)
                args.DrawingSession.DrawImage(layer.Surface, new Rect(0, 0, PaintCanvas.ActualWidth, PaintCanvas.ActualHeight));
        }

        DrawEditableObjects(args.DrawingSession);

        if (_showGrid && _zoom >= 4)
        {
            var color = Color.FromArgb(70, 80, 80, 80);
            var step = (float)_zoom;
            for (var x = 0f; x <= PaintCanvas.ActualWidth; x += step)
            {
                args.DrawingSession.DrawLine(x, 0, x, (float)PaintCanvas.ActualHeight, color, 0.5f);
            }
            for (var y = 0f; y <= PaintCanvas.ActualHeight; y += step)
            {
                args.DrawingSession.DrawLine(0, y, (float)PaintCanvas.ActualWidth, y, color, 0.5f);
            }
        }

        DrawShapePreview(sender, args);
        DrawFloatingSelection(args.DrawingSession);
        DrawSelectionOverlay(args.DrawingSession);
        DrawEditableObjectOverlay(args.DrawingSession);
    }

    private async void PaintCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        EnsureSurface(PaintCanvas);
        var point = ToImagePoint(e.GetCurrentPoint(PaintCanvas).Position);
        if (!IsInside(point))
        {
            return;
        }

        if (_tool == PaintTool.Select)
        {
            if (HandleEditableObjectPointerPressed(point, e))
            {
                return;
            }
            HandleSelectionPointerPressed(point, e);
            return;
        }

        if (_tool == PaintTool.Shape)
        {
            BeginShape(point, e);
            return;
        }

        if (_tool == PaintTool.Fill)
        {
            CaptureUndo();
            FloodFill((int)point.X, (int)point.Y, WithCurrentOpacity(_primaryColor));
            FinishEdit();
            return;
        }

        if (_tool == PaintTool.Picker)
        {
            _primaryColor = ReadPixel((int)point.X, (int)point.Y);
            PrimaryColorBox.Background = new SolidColorBrush(_primaryColor);
            PrimaryColorIndicator.Fill = new SolidColorBrush(_primaryColor);
            return;
        }

        if (_tool == PaintTool.Text)
        {
            await InsertTextAsync(point);
            return;
        }

        if (_tool is PaintTool.Pencil or PaintTool.Brush or PaintTool.Eraser)
        {
            CaptureUndo();
            _lastPoint = point;
            _isDrawing = true;
            PaintCanvas.CapturePointer(e.Pointer);
            DrawStroke(point, point);
            PaintCanvas.Invalidate();
        }
    }

    private void PaintCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var point = ToImagePoint(e.GetCurrentPoint(PaintCanvas).Position);
        PointerStatusText.Text = $"{Math.Clamp((int)point.X, 0, _imageWidth)}, {Math.Clamp((int)point.Y, 0, _imageHeight)} px";

        if (_isDrawingShape)
        {
            UpdateShape(point);
            return;
        }

        if (HandleEditableObjectPointerMoved(point))
        {
            return;
        }

        if (HandleSelectionPointerMoved(point))
        {
            return;
        }

        if (!_isDrawing || !IsInside(point))
        {
            return;
        }

        DrawStroke(_lastPoint, point);
        _lastPoint = point;
        PaintCanvas.Invalidate();
    }

    private void PaintCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isDrawingShape)
        {
            EndShape(e);
            return;
        }

        if (HandleEditableObjectPointerReleased(e))
        {
            return;
        }

        if (HandleSelectionPointerReleased(e))
        {
            return;
        }

        if (!_isDrawing)
        {
            return;
        }

        _isDrawing = false;
        PaintCanvas.ReleasePointerCapture(e.Pointer);
        FinishEdit();
    }

    private Vector2 ToImagePoint(Point point) => new((float)(point.X / _zoom), (float)(point.Y / _zoom));

    private bool IsInside(Vector2 point) => point.X >= 0 && point.Y >= 0 && point.X < _imageWidth && point.Y < _imageHeight;

    private void DrawStroke(Vector2 from, Vector2 to)
    {
        DrawConfiguredStroke(from, to);
    }

    private void CaptureUndo()
    {
        if (_surface is null)
        {
            return;
        }

        _undo.Push(CaptureDocumentSnapshot());
        _redo.Clear();
        while (_undo.Count > 30)
        {
            var retained = _undo.Reverse().Skip(1).Reverse().ToArray();
            _undo.Clear();
            foreach (var item in retained)
            {
                _undo.Push(item);
            }
        }
        UpdateHistoryState();
    }

    private void Undo()
    {
        if (_surface is null || _undo.Count == 0)
        {
            return;
        }

        _redo.Push(CaptureDocumentSnapshot());
        RestoreDocumentSnapshot(_undo.Pop());
        _dirty = true;
        PaintCanvas.Invalidate();
        UpdateHistoryState();
    }

    private void Redo()
    {
        if (_surface is null || _redo.Count == 0)
        {
            return;
        }

        _undo.Push(CaptureDocumentSnapshot());
        RestoreDocumentSnapshot(_redo.Pop());
        _dirty = true;
        PaintCanvas.Invalidate();
        UpdateHistoryState();
    }

    private DocumentSnapshot CaptureDocumentSnapshot()
    {
        var snapshots = _layers
            .Select(layer => new LayerSnapshot(layer.Name, layer.IsVisible, layer.Surface.GetPixelBytes()))
            .ToArray();
        return new DocumentSnapshot(_imageWidth, _imageHeight, _activeLayerIndex, snapshots, _objects.ToArray());
    }

    private void RestoreDocumentSnapshot(DocumentSnapshot snapshot)
    {
        ClearFloatingSelectionState();
        foreach (var layer in _layers)
            layer.Dispose();
        _layers.Clear();

        _imageWidth = snapshot.Width;
        _imageHeight = snapshot.Height;
        foreach (var saved in snapshot.Layers)
        {
            var surface = new CanvasRenderTarget(PaintCanvas, _imageWidth, _imageHeight, 96);
            surface.SetPixelBytes(saved.Pixels);
            _layers.Add(new LayerState(saved.Name, surface, saved.IsVisible));
        }

        _activeLayerIndex = Math.Clamp(snapshot.ActiveLayerIndex, 0, _layers.Count - 1);
        _surface = _layers[_activeLayerIndex].Surface;
        _objects.Clear();
        if (snapshot.Objects is not null)
        {
            _objects.AddRange(snapshot.Objects);
        }
        _activeObjectId = null;
        _activeObjectOriginal = null;
        _selection = null;
        ApplyCanvasSize();
        RefreshLayersPanel();
        UpdateTitle();
    }

    private void FinishEdit()
    {
        _dirty = true;
        RefreshLayersPanel();
        PaintCanvas.Invalidate();
        UpdateTitle();
        UpdateHistoryState();
    }

    private void UpdateHistoryState()
    {
        UndoMenuItem.IsEnabled = _undo.Count > 0;
        RedoMenuItem.IsEnabled = _redo.Count > 0;
        UpdateSelectionCommandState();
    }

    private void FloodFill(int startX, int startY, Color replacement)
    {
        if (_surface is null)
        {
            return;
        }

        var pixels = _surface.GetPixelBytes();
        var target = ReadBgra(pixels, startX, startY);
        if (target == replacement)
        {
            return;
        }

        var pending = new Stack<int>();
        pending.Push((startY * _imageWidth) + startX);
        while (pending.Count > 0)
        {
            var index = pending.Pop();
            var x = index % _imageWidth;
            var y = index / _imageWidth;
            if (ReadBgra(pixels, x, y) != target)
            {
                continue;
            }

            WriteBgra(pixels, x, y, replacement);
            if (x > 0) pending.Push(index - 1);
            if (x + 1 < _imageWidth) pending.Push(index + 1);
            if (y > 0) pending.Push(index - _imageWidth);
            if (y + 1 < _imageHeight) pending.Push(index + _imageWidth);
        }
        _surface.SetPixelBytes(pixels);
    }

    private Color ReadPixel(int x, int y)
    {
        return _surface is null ? Colors.Black : ReadBgra(_surface.GetPixelBytes(), x, y);
    }

    private Color ReadBgra(byte[] pixels, int x, int y)
    {
        var offset = ((y * _imageWidth) + x) * 4;
        return Color.FromArgb(pixels[offset + 3], pixels[offset + 2], pixels[offset + 1], pixels[offset]);
    }

    private void WriteBgra(byte[] pixels, int x, int y, Color color)
    {
        var offset = ((y * _imageWidth) + x) * 4;
        pixels[offset] = color.B;
        pixels[offset + 1] = color.G;
        pixels[offset + 2] = color.R;
        pixels[offset + 3] = color.A;
    }

    private async Task InsertTextAsync(Vector2 point)
    {
        await ShowTextDialogAndInsertAsync(point);
    }

    private async Task NewDocumentAsync()
    {
        if (!await ConfirmDiscardAsync())
        {
            return;
        }

        CommitFloatingSelection();
        ClearFloatingSelectionState();
        foreach (var layer in _layers)
            layer.Dispose();
        _layers.Clear();
        _surface = null;
        _objects.Clear();
        _activeObjectId = null;
        _activeObjectOriginal = null;
        _selection = null;
        _imageWidth = InitialWidth;
        _imageHeight = InitialHeight;
        _currentFile = null;
        _undo.Clear();
        _redo.Clear();
        _dirty = false;
        ApplyCanvasSize();
        UpdateHistoryState();
        UpdateTitle();
        PaintCanvas.Invalidate();
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!_dirty)
        {
            return true;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = PaintCanvas.XamlRoot,
            Title = "変更を保存しますか?",
            Content = "保存していない変更があります。",
            PrimaryButtonText = "保存",
            SecondaryButtonText = "保存しない",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            return await TrySaveDocumentAsync(null);
        }
        return result == ContentDialogResult.Secondary;
    }

    private async Task OpenDocumentAsync(bool import)
    {
        if (!await ConfirmDiscardAsync())
        {
            return;
        }
        CommitFloatingSelection();

        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        if (!import) picker.FileTypeFilter.Add(".chunpaint");
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".bmp");
        picker.FileTypeFilter.Add(".gif");
        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            await LoadStorageFileAsync(file, import);
        }
        catch (Exception exception)
        {
            await ShowInfoAsync("ファイルを開く", $"ファイルを開けませんでした。\n{exception.Message}");
        }
    }

    private async Task<bool> TrySaveDocumentAsync(string? requestedExtension)
    {
        try
        {
            return await SaveDocumentAsync(requestedExtension);
        }
        catch (Exception exception)
        {
            await ShowInfoAsync("保存", $"ファイルを保存できませんでした。変更は保持されています。\n{exception.Message}");
            return false;
        }
    }

    private async Task<bool> SaveDocumentAsync(string? requestedExtension)
    {
        EnsureSurface(PaintCanvas);
        var file = _currentFile;
        if (file is null || requestedExtension is not null)
        {
            var picker = new FileSavePicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.SuggestedFileName = _currentFile?.DisplayName ?? "無題";
            picker.FileTypeChoices.Add("ChunPaint プロジェクト", [".chunpaint"]);
            picker.FileTypeChoices.Add("PNG 画像", [".png"]);
            picker.FileTypeChoices.Add("JPEG 画像", [".jpg", ".jpeg"]);
            picker.FileTypeChoices.Add("BMP 画像", [".bmp"]);
            picker.FileTypeChoices.Add("GIF 画像", [".gif"]);
            if (requestedExtension is not null)
            {
                picker.DefaultFileExtension = "." + requestedExtension;
            }
            file = await picker.PickSaveFileAsync();
        }

        if (file is null)
        {
            return false;
        }

        CommitFloatingSelection();

        if (IsChunPaintProject(file))
        {
            return await SaveChunPaintProjectAsync(file);
        }
        var format = Path.GetExtension(file.Name).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => CanvasBitmapFileFormat.Jpeg,
            ".bmp" => CanvasBitmapFileFormat.Bmp,
            ".gif" => CanvasBitmapFileFormat.Gif,
            _ => CanvasBitmapFileFormat.Png
        };
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        using var composite = CreateCompositeWithObjects();
        var quality = format == CanvasBitmapFileFormat.Jpeg ? 0.92f : 1f;
        await composite.SaveAsync(stream, format, quality);
        _currentFile = file;
        _dirty = false;
        AddRecentFile(file);
        UpdateTitle();
        return true;
    }

    private CanvasRenderTarget CreateComposite()
    {
        var composite = new CanvasRenderTarget(PaintCanvas, _imageWidth, _imageHeight, 96);
        using var session = composite.CreateDrawingSession();
        session.Clear(Colors.Transparent);
        foreach (var layer in _layers)
            if (layer.IsVisible)
                session.DrawImage(layer.Surface);
        foreach (var editableObject in _objects)
            DrawEditableObject(session, editableObject, 1f);
        return composite;
    }

    private void ApplyCanvasSize()
    {
        if (CanvasSizeText is not null)
        {
            CanvasSizeText.Text = $"{_imageWidth} × {_imageHeight}px";
        }
        PaintCanvas.Width = _imageWidth * _zoom;
        PaintCanvas.Height = _imageHeight * _zoom;
        UpdateRulerDimensions();
    }

    private void UpdateTitle()
    {
        var name = _currentFile?.Name ?? "無題";
        Title = $"{(_dirty ? "*" : string.Empty)}{name} - ChunPaint";
    }

    private async Task ShowInfoAsync(string title, object content)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = PaintCanvas.XamlRoot,
            Title = title,
            Content = content,
            CloseButtonText = "閉じる"
        };
        await dialog.ShowAsync();
    }

    private async void New_Click(object sender, RoutedEventArgs e) => await NewDocumentAsync();
    private async void Open_Click(object sender, RoutedEventArgs e) => await OpenDocumentAsync(false);
    private async void Import_Click(object sender, RoutedEventArgs e) => await OpenDocumentAsync(true);
    private async void Save_Click(object sender, RoutedEventArgs e) => await TrySaveDocumentAsync(null);
    private async void SaveAs_Click(object sender, RoutedEventArgs e) => await TrySaveDocumentAsync((sender as FrameworkElement)?.Tag?.ToString());
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
    private void Undo_Click(object sender, RoutedEventArgs e) => Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => Redo();

    private async void Properties_Click(object sender, RoutedEventArgs e)
    {
        var savedDate = "未保存";
        var fileSize = "未保存";
        if (_currentFile is not null)
        {
            try
            {
                var properties = await _currentFile.GetBasicPropertiesAsync();
                savedDate = properties.DateModified.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss");
                fileSize = $"{properties.Size:N0} bytes";
            }
            catch (Exception exception)
            {
                fileSize = $"取得できません ({exception.Message})";
            }
        }

        await ShowInfoAsync(
            "画像のプロパティ",
            $"保存日時: {savedDate}\nファイルサイズ: {fileSize}\n解像度: 96 DPI\n単位: ピクセル\n幅: {_imageWidth} px\n高さ: {_imageHeight} px");
    }

    private async void Resize_Click(object sender, RoutedEventArgs e)
    {
        await ShowResizeDialogAsync();
    }

    private void Rotate_Click(object sender, RoutedEventArgs e)
    {
        ShowRotateMenu(sender as FrameworkElement);
    }

    private void ToolButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton selected || !Enum.TryParse(selected.Tag?.ToString(), out PaintTool tool))
        {
            return;
        }

        if (tool != PaintTool.Select)
        {
            CommitFloatingSelection();
        }
        _tool = tool;
        if (tool == PaintTool.Select)
        {
            _selectionMode = SelectionMode.Rectangle;
        }
        ClearShapeSelectionVisual();
        foreach (var button in _toolButtons)
        {
            button.IsChecked = ReferenceEquals(button, selected);
        }
    }

    private static Color ParseColor(string value)
    {
        var packed = Convert.ToUInt32(value.AsSpan(1), 16);
        return Color.FromArgb((byte)(packed >> 24), (byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);
    }

    private void Palette_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is string value)
        {
            _primaryColor = ParseColor(value);
            PrimaryColorBox.Background = new SolidColorBrush(_primaryColor);
            PrimaryColorIndicator.Fill = new SolidColorBrush(_primaryColor);
        }
    }

    private void Palette_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(sender as UIElement).Properties.IsRightButtonPressed || sender is not FrameworkElement element || element.Tag is not string value)
        {
            return;
        }
        _secondaryColor = ParseColor(value);
        SecondaryColorBox.Background = new SolidColorBrush(_secondaryColor);
        SecondaryColorIndicator.Fill = new SolidColorBrush(_secondaryColor);
        e.Handled = true;
    }

    private async void EditColors_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ColorPicker { Color = _primaryColor, IsAlphaEnabled = false };
        var dialog = new ContentDialog
        {
            XamlRoot = PaintCanvas.XamlRoot,
            Title = "色の編集",
            Content = picker,
            PrimaryButtonText = "OK",
            CloseButtonText = "キャンセル"
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _primaryColor = picker.Color;
            PrimaryColorBox.Background = new SolidColorBrush(_primaryColor);
            PrimaryColorIndicator.Fill = new SolidColorBrush(_primaryColor);
        }
    }

    private void StrokeWidthSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (StrokeWidthText is not null)
        {
            StrokeWidthText.Text = $"{Math.Round(e.NewValue)} px";
        }
    }

    private void Layers_Click(object sender, RoutedEventArgs e)
    {
        LayersColumn.Width = LayersColumn.Width.Value == 0 ? new GridLength(148) : new GridLength(0);
    }

    private void AddLayer_Click(object sender, RoutedEventArgs e)
    {
        EnsureSurface(PaintCanvas);
        CaptureUndo();
        _layerCount++;
        var layerSurface = new CanvasRenderTarget(PaintCanvas, _imageWidth, _imageHeight, 96);
        using (var drawingSession = layerSurface.CreateDrawingSession())
        {
            drawingSession.Clear(Colors.Transparent);
        }

        _layers.Add(new LayerState($"レイヤー {_layerCount}", layerSurface));
        _activeLayerIndex = _layers.Count - 1;
        _surface = layerSurface;
        RefreshLayersPanel();
        FinishEdit();
    }

    private void ZoomSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) =>
        HandleZoomSliderChanged(e.NewValue);

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomSlider.Value = Math.Max(ZoomSlider.Minimum, ZoomSlider.Value - 25);
    private void ResetZoom_Click(object sender, RoutedEventArgs e) => ZoomSlider.Value = 100;
    private void Grid_Click(object sender, RoutedEventArgs e) { _showGrid = !_showGrid; PaintCanvas.Invalidate(); }
    private void Ruler_Click(object sender, RoutedEventArgs e) => ToggleRulers(sender as ToggleMenuFlyoutItem);
    private void StatusBar_Click(object sender, RoutedEventArgs e) => StatusBar.Visibility = StatusBar.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    private enum PaintTool
    {
        Select,
        Pencil,
        Brush,
        Eraser,
        Fill,
        Picker,
        Text,
        Shape
    }
}
