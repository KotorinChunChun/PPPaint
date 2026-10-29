using System.Numerics;
using PPPaint.Core;
using PPPaint.Editing;
using PPPaint.Imaging;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;
using WinRT.Interop;

namespace PPPaint;

public sealed partial class MainWindow : Window
{
    private const int InitialWidth = 1152;
    private const int InitialHeight = 648;
    private static readonly double[] StrokeWidthPresets = [1, 3, 5, 8, 12, 20, 30];

    private readonly Stack<DocumentSnapshot> _undo = new();
    private readonly Stack<DocumentSnapshot> _redo = new();
    private readonly Dictionary<ContentControl, object?> _buttonIconContents = new();
    private readonly List<ToggleButton> _toolButtons;
    private readonly List<ToggleButton> _selectionModeButtons;
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
    private double _imageDpi = ImageMeasurements.DefaultDpi;
    private double _zoom = 1.0;
    private bool _showGrid;
    private bool _dirty;
    private int _activeLayerIndex;
    private IImageAiProvider _imageAiProvider = new DisabledImageAiProvider();
    private SelectionRegion? _selection;
    private bool _isSelecting;
    private bool _updatingLayersPanel;
    private bool _textButtonDisplayMode;

    public MainWindow()
    {
        InitializeComponent();
        Title = WindowTitle.Create();
        ConfigureAccessibility();

        _toolButtons =
        [
            PencilToolButton,
            EraserToolButton,
            FillToolButton,
            PickerToolButton,
            TextToolButton,
            ZoomToolButton
        ];
        _selectionModeButtons =
        [
            RectangleSelectionButton,
            FreeSelectionButton
        ];
        UpdateColorVisuals();
        UpdateStrokeWidthVisuals();

        ConfigureWindow();
        UpdateCanvasCursor();
        ConfigureAccelerators();
        ConfigureWidgetAccelerators();
        LoadRecentFiles();
        LoadPreferences();
        InitializeDocuments();
        InitializeUnifiedIcons();
        InitializeUpdateChecks();
        UpdateHistoryState();
        LayersList.SelectionChanged += LayersList_SelectionChanged;
        LayersList.RightTapped += LayersList_RightTapped;
        var renameLayerAccelerator = new KeyboardAccelerator { Key = VirtualKey.F2 };
        renameLayerAccelerator.Invoked += RenameLayerKeyboardAccelerator_Invoked;
        RootLayout.KeyboardAccelerators.Add(renameLayerAccelerator);
        PaintCanvas.RightTapped += PaintCanvas_RightTapped;
    }

    private void ConfigureWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.Resize(new SizeInt32(1280, 820));
        _appWindow.Closing += AppWindow_Closing;
    }

    private void ButtonDisplayModeToggle_Click(object sender, RoutedEventArgs e)
    {
        _textButtonDisplayMode = !_textButtonDisplayMode;
        ButtonDisplayModeToggleButton.IsChecked = _textButtonDisplayMode;
        ApplyButtonDisplayMode();
    }

    private void ApplyButtonDisplayMode()
    {
        foreach (var control in EnumerateVisualDescendants(RootLayout).OfType<ContentControl>())
        {
            if (control is not ButtonBase || ReferenceEquals(control, ButtonDisplayModeToggleButton))
            {
                continue;
            }

            if (!_buttonIconContents.TryGetValue(control, out var iconContent))
            {
                iconContent = control.Content;
                _buttonIconContents[control] = iconContent;
            }

            var name = AutomationProperties.GetName(control);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            control.Content = _textButtonDisplayMode
                ? new TextBlock
                {
                    Text = name,
                    FontSize = 10,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
                : iconContent;
        }
    }

    private static IEnumerable<DependencyObject> EnumerateVisualDescendants(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in EnumerateVisualDescendants(child))
            {
                yield return descendant;
            }
        }
    }

    private bool _allowWindowClose;
    private bool _closeConfirmationShowing;

    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowWindowClose)
        {
            return;
        }

        args.Cancel = true;
        if (_closeConfirmationShowing || _documentBusy || _backupBusy)
        {
            return;
        }

        _closeConfirmationShowing = true;
        try
        {
            foreach (var document in _documents.ToArray())
            {
                SwitchDocument(document);
                if (!await ConfirmDiscardAsync()) return;
            }
            foreach (var document in _documents) DeleteRecovery(document.Id);
            _allowWindowClose = true;
            Close();
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
            await TrySaveDocumentAsync("pppaint");
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
        DrawDocumentStack(args.DrawingSession);


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
        DrawGuides(args.DrawingSession);
    }

    private async void PaintCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        EnsureSurface(PaintCanvas);
        if (_documentBusy) return;
        var point = ToImagePoint(e.GetCurrentPoint(PaintCanvas).Position);
        if (!IsInside(point))
        {
            return;
        }

        if (AdvancedPointerPressed(point, e)) return;

        if (_tool == PaintTool.Zoom)
        {
            var properties = e.GetCurrentPoint(PaintCanvas).Properties;
            var delta = properties.IsLeftButtonPressed ? 10d : properties.IsRightButtonPressed ? -10d : 0d;
            if (delta != 0d)
            {
                ZoomSlider.Value = Math.Clamp(ZoomSlider.Value + delta, ZoomSlider.Minimum, ZoomSlider.Maximum);
            }
            e.Handled = true;
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

        if (_tool != PaintTool.Picker && !CanEditActiveLayer) return;

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
            SetColorTarget(ColorTarget.Primary, ReadPixel((int)point.X, (int)point.Y));
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
            point = FilterStrokePoint(e, true);
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
        UpdateCanvasCursor(point);

        if (AdvancedPointerMoved(point, e))
        {
            return;
        }

        if (_isDrawingShape)
        {
            UpdateShape(point);
            return;
        }

        if (HandleEditableObjectPointerMoved(point, e.KeyModifiers))
        {
            return;
        }

        if (HandleSelectionPointerMoved(point, e.KeyModifiers))
        {
            return;
        }

        if (!_isDrawing || !IsInside(point))
        {
            return;
        }

        point = FilterStrokePoint(e);
        DrawStroke(_lastPoint, point);
        _lastPoint = point;
        PaintCanvas.Invalidate();
    }

    private void PaintCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var point = ToImagePoint(e.GetCurrentPoint(PaintCanvas).Position);
        if (AdvancedPointerReleased(e))
        {
            UpdateCanvasCursor(point);
            return;
        }

        if (_isDrawingShape)
        {
            EndShape(e);
            UpdateCanvasCursor(point);
            return;
        }

        if (HandleEditableObjectPointerReleased(e))
        {
            UpdateCanvasCursor(point);
            return;
        }

        if (HandleSelectionPointerReleased(e))
        {
            UpdateCanvasCursor(point);
            return;
        }

        if (!_isDrawing)
        {
            UpdateCanvasCursor(point);
            return;
        }

        if (_strokeFilter is not null)
        {
            var end = FilterStrokePoint(e);
            var finalPoint = _strokeFilter.Flush();
            _strokePressure = finalPoint.Pressure;
            DrawStroke(_lastPoint, finalPoint.Point);
            _strokeFilter = null;
        }
        _isDrawing = false;
        PaintCanvas.ReleasePointerCapture(e.Pointer);
        FinishEdit();
        UpdateCanvasCursor(point);
    }

    private Vector2 ToImagePoint(Point point) => new((float)(point.X / _zoom), (float)(point.Y / _zoom));

    private bool IsInside(Vector2 point) => point.X >= 0 && point.Y >= 0 && point.X < _imageWidth && point.Y < _imageHeight;

    private void DrawStroke(Vector2 from, Vector2 to)
    {
        _stackRevision++;
        DrawConfiguredStroke(from, to);
    }

    private void CaptureUndo([System.Runtime.CompilerServices.CallerMemberName] string operation = "編集")
    {
        if (_surface is null)
        {
            return;
        }

        var captured = CaptureDocumentSnapshot(); RememberHistory(captured, operation); _undo.Push(captured);
        _redo.Clear();
        TrimDocumentHistory();
        UpdateHistoryState();
    }

    private void Undo()
    {
        if (_documentBusy || _surface is null || _undo.Count == 0)
        {
            return;
        }

        var undoState = _undo.Pop(); var redoSnapshot = CaptureDocumentSnapshot(); RememberHistory(redoSnapshot, HistoryName(undoState)); _redo.Push(redoSnapshot); RestoreDocumentSnapshot(undoState);
        _dirty = true;
        UpdateTitle();
        TrimDocumentHistory();
        PaintCanvas.Invalidate();
        UpdateHistoryState();
    }

    private void Redo()
    {
        if (_documentBusy || _surface is null || _redo.Count == 0)
        {
            return;
        }

        var redoState = _redo.Pop(); var captured = CaptureDocumentSnapshot(); RememberHistory(captured, HistoryName(redoState)); _undo.Push(captured); RestoreDocumentSnapshot(redoState);
        _dirty = true;
        UpdateTitle();
        TrimDocumentHistory();
        PaintCanvas.Invalidate();
        UpdateHistoryState();
    }

    private DocumentSnapshot CaptureDocumentSnapshot()
    {
        var previous = _undo.TryPeek(out var last) ? last : null;
        var snapshots = _layers
            .Select(layer => new LayerSnapshot(layer.Name, layer.IsVisible,
                PixelBlockSnapshot.Capture(layer.Surface.GetPixelBytes(),
                    previous?.Layers.FirstOrDefault(saved => saved.Id == layer.Id)?.PixelBlocks),
                layer.Id, layer.IsLocked, layer.Opacity, layer.Mask is null ? null : PixelBlockSnapshot.CaptureBytes(layer.Mask, previous?.Layers.FirstOrDefault(saved => saved.Id == layer.Id)?.MaskBlocks), layer.BlendMode, layer.Group, layer.Adjustment))
            .ToArray();
        return new DocumentSnapshot(_imageWidth, _imageHeight, _activeLayerIndex, snapshots, _objects.ToArray(), _imageDpi);
    }

    private void RestoreDocumentSnapshot(DocumentSnapshot snapshot)
    {
        var restored = new List<LayerState>();
        try
        {
            foreach (var saved in snapshot.Layers)
            {
                var surface = new CanvasRenderTarget(PaintCanvas, snapshot.Width, snapshot.Height, 96);
                try { surface.SetPixelBytes(saved.Pixels); }
                catch { surface.Dispose(); throw; }
                restored.Add(new LayerState(saved.Name, surface, saved.IsVisible, saved.Id)
                    { IsLocked = saved.IsLocked, Opacity = saved.Opacity, Mask = saved.Mask, BlendMode = saved.BlendMode, Group = saved.Group, Adjustment = saved.Adjustment });
            }
        }
        catch { foreach (var layer in restored) layer.Dispose(); throw; }
        ClearFloatingSelectionState();
        foreach (var layer in _layers)
            layer.Dispose();
        _layers.Clear();

        _imageWidth = snapshot.Width;
        _imageHeight = snapshot.Height;
        _imageDpi = snapshot.Dpi;
        _layers.AddRange(restored);

        _activeLayerIndex = Math.Clamp(snapshot.ActiveLayerIndex, 0, _layers.Count - 1);
        _surface = _layers[_activeLayerIndex].Surface;
        _objects.Clear();
        if (snapshot.Objects is not null)
        {
            _objects.AddRange(snapshot.Objects);
        }
        NormalizeObjectLayerIds();
        _activeObjectId = null;
        _activeObjectOriginal = null;
        _selection = null;
        ApplyCanvasSize();
        RefreshLayersPanel();
        UpdateTitle();
    }

    private void FinishEdit()
    {
        _stackRevision++;
        _dirty = true;
        RefreshLayersPanel();
        PaintCanvas.Invalidate();
        UpdateTitle();
        UpdateHistoryState();
    }

    private void UpdateHistoryState()
    {
        UndoMenuItem.IsEnabled = !_documentBusy && _undo.Count > 0;
        RedoMenuItem.IsEnabled = !_documentBusy && _redo.Count > 0;
        UpdateSelectionCommandState();
    }

    private void FloodFill(int startX, int startY, Color replacement)
    {
        if (_surface is null)
        {
            return;
        }

        using var composite = CreateCompositeWithObjects();
        var visibleSurface = ToPixelSurface(composite);
        var region = visibleSurface.FloodFillRegion(startX, startY);
        if (region.Length == 0)
        {
            return;
        }

        var pixels = _surface.GetPixelBytes();
        foreach (var index in region)
        {
            var x = index % _imageWidth;
            var y = index / _imageWidth;
            WriteBgra(pixels, x, y, replacement);
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
        var alpha = pixels[offset + 3];
        byte Straight(byte value) => alpha == 0 ? (byte)0 : (byte)Math.Min(255, (value * 255 + alpha / 2) / alpha);
        return Color.FromArgb(alpha, Straight(pixels[offset + 2]), Straight(pixels[offset + 1]), Straight(pixels[offset]));
    }

    private void WriteBgra(byte[] pixels, int x, int y, Color color)
    {
        var offset = ((y * _imageWidth) + x) * 4;
        pixels[offset] = (byte)((color.B * color.A + 127) / 255);
        pixels[offset + 1] = (byte)((color.G * color.A + 127) / 255);
        pixels[offset + 2] = (byte)((color.R * color.A + 127) / 255);
        pixels[offset + 3] = color.A;
    }

    private async Task InsertTextAsync(Vector2 point)
    {
        await ShowTextDialogAndInsertAsync(point);
    }

    private async Task NewDocumentAsync()
    {
        if (_documentBusy) return;
        StoreActiveDocument();
        _activeDocument = new DocumentSession();
        _documents.Add(_activeDocument);
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
        _imageDpi = ImageMeasurements.DefaultDpi;
        _currentFile = null;
        _undo.Clear();
        _redo.Clear();
        _dirty = false;
        ApplyCanvasSize();
        UpdateHistoryState();
        UpdateTitle();
        PaintCanvas.Invalidate();
        await Task.CompletedTask;
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!_dirty && _floatingSelectionPixels is null && _activeObjectOriginal is null)
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
        ConfigureDialogAccessibility(dialog);
        var result = await dialog.ShowManagedAsync();
        if (result == ContentDialogResult.Primary)
        {
            return await TrySaveDocumentAsync(null);
        }
        return result == ContentDialogResult.Secondary;
    }

    private async Task OpenDocumentAsync(bool import)
    {
        try
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            if (!import)
            {
                picker.FileTypeFilter.Add(".pppaint");
                picker.FileTypeFilter.Add(".chunpaint");
            }
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".bmp");
            picker.FileTypeFilter.Add(".gif");
            picker.FileTypeFilter.Add(".webp");
            var file = await picker.PickSingleFileManagedAsync();
            if (file is null)
            {
                return;
            }

            await LoadStorageFileAsync(file, import);
        }
        catch (OperationCanceledException)
        {
            // A canceled picker or load leaves the current document untouched.
        }
        catch (Exception exception)
        {
            await ShowInfoAsync("ファイルを開く", $"ファイルを開けませんでした。\n{exception.Message}");
        }
    }

    private async Task<bool> TrySaveDocumentAsync(string? requestedExtension)
    {
        if (_documentBusy) return false;
        _documentBusy = true;
        RefreshDocumentTabs();
        try
        {
            return await SaveDocumentAsync(requestedExtension);
        }
        catch (Exception exception)
        {
            await ShowInfoAsync("保存", $"ファイルを保存できませんでした。変更は保持されています。\n{exception.Message}");
            return false;
        }
        finally { _documentBusy = false; RefreshDocumentTabs(); }
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
            var preferredExtension = "." + (requestedExtension ?? "pppaint").TrimStart('.').ToLowerInvariant();
            (string Label, string[] Extensions)[] choices =
            [
                ("PPPaint プロジェクト", [".pppaint"]),
                ("PNG 画像", [".png"]),
                ("JPEG 画像", [".jpg", ".jpeg"]),
                ("BMP 画像", [".bmp"]),
                ("GIF 画像", [".gif"]),
                ("WebP 画像", [".webp"])
            ];
            if (!choices.Any(choice => choice.Extensions.Contains(preferredExtension, StringComparer.OrdinalIgnoreCase)))
                throw new ArgumentException("対応していない保存形式です。", nameof(requestedExtension));
            // FileSavePickerは最初に追加した形式を初期選択する。既定拡張子だけでなく順序も合わせる。
            foreach (var choice in choices.OrderByDescending(choice => choice.Extensions.Contains(preferredExtension, StringComparer.OrdinalIgnoreCase)))
                picker.FileTypeChoices.Add(choice.Label, choice.Extensions);
            picker.DefaultFileExtension = preferredExtension;
            file = await picker.PickSaveFileManagedAsync();
        }

        if (file is null)
        {
            return false;
        }

        CommitFloatingSelection();

        if (IsPPPaintProject(file))
        {
            return await SavePPPaintProjectAsync(file);
        }
        if (!DocumentFileFormats.TryGetFormat(file.Name, out var format) || format == DocumentFileFormat.PPPaint)
        {
            throw new InvalidDataException("対応する画像形式を判定できません。");
        }
        using var composite = CreateCompositeWithObjects();
        var encoded = await PrepareExportAsync(composite, format);
        if (encoded is null) return false;
        await DocumentFileWriter.WriteAsync(file, async stream =>
        {
            var output = stream.AsStreamForWrite();
            await output.WriteAsync(encoded);
            await output.FlushAsync();
        });
        _imageDpi = ImageMeasurements.DefaultDpi;
        _currentFile = file;
        _dirty = false;
        if (_activeDocument is not null) DeleteRecovery(_activeDocument.Id);
        AddRecentFile(file);
        UpdateTitle();
        return true;
    }

    private CanvasRenderTarget CreateComposite() => CreateStackComposite(_layers);

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
        Title = WindowTitle.Create();
        RefreshDocumentTabs();
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
        ConfigureDialogAccessibility(dialog);
        await dialog.ShowManagedAsync();
    }

    private async void New_Click(object sender, RoutedEventArgs e) => await NewDocumentAsync();
    private async void Open_Click(object sender, RoutedEventArgs e) => await OpenDocumentAsync(false);
    private async void Import_Click(object sender, RoutedEventArgs e) => await OpenDocumentAsync(true);
    private async void Save_Click(object sender, RoutedEventArgs e) => await TrySaveDocumentAsync(null);
    private async void SaveAs_Click(object sender, RoutedEventArgs e) => await TrySaveDocumentAsync((sender as FrameworkElement)?.Tag?.ToString());
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
    private void Undo_Click(object sender, RoutedEventArgs e) => Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => Redo();

    private async void Copilot_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureSurface(PaintCanvas);
            using var composite = CreateComposite();
            var request = new ImageAiRequest(
                ImageAiFeature.CopilotEdit,
                ToPixelSurface(composite),
                _selection,
                Diagnostics: new Dictionary<string, string>
                {
                    ["source"] = "copilot-button",
                    ["networkAccess"] = "none"
                });
            var result = await _imageAiProvider.ExecuteAsync(request);
            var content = new StackPanel { Spacing = 8 };
            content.Children.Add(new TextBlock { Text = result.Message, TextWrapping = TextWrapping.Wrap });
            content.Children.Add(new TextBlock
            {
                Text = "将来の接続先は IImageAiProvider の差し替え箇所です。現在は画像を外部へ送信せず、編集結果も変更しません。",
                TextWrapping = TextWrapping.Wrap
            });
            var dialog = new ContentDialog
            {
                XamlRoot = PaintCanvas.XamlRoot,
                Title = "Copilot / 生成消去",
                Content = content,
                CloseButtonText = "キャンセル"
            };
            await dialog.ShowManagedAsync();
        }
        catch (Exception exception)
        {
            await ShowInfoAsync("Copilot / 生成消去", $"画像AIを利用できませんでした。画像は外部へ送信されていません。\n{exception.Message}");
        }
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
        CancelAdvancedStroke(); _advancedTool = null;
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
        UpdateSelectionModeButtonState();
        UpdateSelectionCommandState();
        UpdateCanvasCursor();
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
            SetActiveColor(ParseColor(value));
            ColorPaletteFlyout?.Hide();
        }
    }

    private void Palette_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(sender as UIElement).Properties.IsRightButtonPressed || sender is not FrameworkElement element || element.Tag is not string value)
        {
            return;
        }
        SetColorTarget(ColorTarget.Secondary, ParseColor(value));
        e.Handled = true;
    }

    private async void EditColors_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ColorPicker { Color = GetActiveColor(), IsAlphaEnabled = false };
        ColorPaletteFlyout?.Hide();
        var dialog = new ContentDialog
        {
            XamlRoot = PaintCanvas.XamlRoot,
            Title = "色の編集",
            Content = picker,
            PrimaryButtonText = "OK",
            CloseButtonText = "キャンセル"
        };
        if (await dialog.ShowManagedAsync() == ContentDialogResult.Primary)
        {
            SetActiveColor(picker.Color, addCustomColor: true);
        }
    }

    private void StrokeWidthSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        UpdateStrokeWidthVisuals();
    }

    private void StrokeWidthButton_Click(object sender, RoutedEventArgs e)
    {
        var flyout = new MenuFlyout();
        foreach (var width in StrokeWidthPresets)
        {
            var previewLength = Math.Clamp((int)Math.Round(width / 2), 1, 8);
            var item = new ToggleMenuFlyoutItem
            {
                Text = $"{width:0} px   {new string('━', previewLength)}",
                IsChecked = Math.Abs(StrokeWidthSlider.Value - width) < 0.01
            };
            item.Click += (_, _) => StrokeWidthSlider.Value = width;
            flyout.Items.Add(item);
        }

        flyout.ShowAt(StrokeWidthButton, new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft });
    }

    private void UpdateStrokeWidthVisuals()
    {
        if (StrokeWidthSlider is null)
        {
            return;
        }

        var width = Math.Round(StrokeWidthSlider.Value);
        if (StrokeWidthName is not null)
        {
            StrokeWidthName.Text = $"{width:0} px";
        }
        if (StrokeWidthPreview is not null)
        {
            StrokeWidthPreview.Height = Math.Clamp(width, 1, 8);
        }
        if (StrokeWidthText is not null)
        {
            StrokeWidthText.Text = $"{width:0} px";
        }
        if (StrokeWidthGlyph is not null)
        {
            StrokeWidthGlyph.Text = _drawShapeOutline ? GetShapeStrokeGlyph(_shapeOutlineKind) : "╱";
        }
    }

    private void Layers_Click(object sender, RoutedEventArgs e)
    {
        LayersColumn.Width = LayersColumn.Width.Value == 0 ? new GridLength(148) : new GridLength(0);
    }

    private void AddLayer_Click(object sender, RoutedEventArgs e)
    {
        if (_documentBusy) return;
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
        Shape,
        Zoom
    }
}
