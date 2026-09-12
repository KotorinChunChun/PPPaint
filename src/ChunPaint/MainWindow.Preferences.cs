using System.Text.Json;
using ChunPaint.Core;
using ChunPaint.Editing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace ChunPaint;

public sealed partial class MainWindow
{
    private EditorPreferences _preferences = new();
    private bool _preferencesLoaded;
    private static string UserDataDirectory => Environment.GetEnvironmentVariable("CHUNPAINT_DATA_DIRECTORY") is { Length: > 0 } path && Path.IsPathFullyQualified(path)
        ? path : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChunPaint");

    private void LoadPreferences()
    {
        try
        {
            var path = Path.Combine(UserDataDirectory, "preferences.json");
            if (File.Exists(path)) _preferences = JsonSerializer.Deserialize<EditorPreferences>(File.ReadAllText(path)) ?? new();
            ApplyPreferences();
        }
        catch (Exception exception) when (exception is IOException or JsonException or ArgumentException or UnauthorizedAccessException)
        {
            _preferences = new();
            ApplyPreferences();
        }
        _preferencesLoaded = true;
    }

    private void ApplyPreferences()
    {
        static double Finite(double value, double fallback, double min, double max) => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
        StrokeWidthSlider.Value = Finite(_preferences.StrokeWidth, 3, 1, 40);
        OpacitySlider.Value = Finite(_preferences.Opacity, 100, 1, 100);
        if (Enum.TryParse<BrushKind>(_preferences.Brush, out var brush) && Enum.IsDefined(brush)) _brushKind = brush;
        _primaryColor = ParsePreferenceColor(_preferences.PrimaryColor, Color.FromArgb(255, 0, 0, 0));
        _secondaryColor = ParsePreferenceColor(_preferences.SecondaryColor, Color.FromArgb(255, 255, 255, 255));
        _customColors.Clear();
        foreach (var value in (_preferences.CustomColors ?? []).Take(30)) _customColors.Add(ParsePreferenceColor(value, Color.FromArgb(255, 0, 0, 0)));
        _showGrid = _preferences.Grid;
        GridMenuItem.IsChecked = _showGrid;
        RulerMenuItem.IsChecked = _preferences.Rulers;
        ToggleRulers(RulerMenuItem);
        StatusBar.Visibility = _preferences.StatusBar ? Visibility.Visible : Visibility.Collapsed;
        StatusBarMenuItem.IsChecked = _preferences.StatusBar;
        _preferences = _preferences with
        {
            Smoothing = Finite(_preferences.Smoothing, 0, 0, .95),
            HistoryMiB = Math.Clamp(_preferences.HistoryMiB, 16, 2048),
            AutosaveSeconds = Math.Clamp(_preferences.AutosaveSeconds, 15, 600)
        };
        UpdateColorVisuals();
        RebuildCustomColors();
        UpdateCanvasCursor();
    }

    private static Color ParsePreferenceColor(string? value, Color fallback)
    {
        if (value is null || value.Length != 9 || value[0] != '#' ||
            !uint.TryParse(value.AsSpan(1), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var packed)) return fallback;
        return Color.FromArgb((byte)(packed >> 24), (byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);
    }

    private void SavePreferences()
    {
        if (!_preferencesLoaded) return;
        static string Hex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
        _preferences = _preferences with
        {
            Brush = _brushKind.ToString(), StrokeWidth = StrokeWidthSlider.Value, Opacity = OpacitySlider.Value,
            PrimaryColor = Hex(_primaryColor), SecondaryColor = Hex(_secondaryColor),
            CustomColors = _customColors.Select(Hex).ToArray(), Rulers = RulerMenuItem.IsChecked,
            Grid = _showGrid, StatusBar = StatusBar.Visibility == Visibility.Visible
        };
        try
        {
            Directory.CreateDirectory(UserDataDirectory);
            var path = Path.Combine(UserDataDirectory, "preferences.json");
            var temporary = path + "." + Environment.ProcessId + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_preferences));
            File.Move(temporary, path, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            PointerStatusText.Text = $"設定保存失敗: {exception.Message}";
        }
    }

    private async void AdvancedSettings_Click(object sender, RoutedEventArgs e)
    {
        var pressureWidth = new CheckBox { Content = "筆圧で線幅を変える", IsChecked = _preferences.PressureWidth };
        var pressureOpacity = new CheckBox { Content = "筆圧で濃さを変える", IsChecked = _preferences.PressureOpacity };
        var smoothing = new Slider { Header = "手ぶれ補正", Minimum = 0, Maximum = .95, StepFrequency = .05, Value = _preferences.Smoothing };
        var memory = new NumberBox { Header = "履歴メモリ上限 (MiB)", Minimum = 16, Maximum = 2048, Value = _preferences.HistoryMiB };
        var interval = new NumberBox { Header = "自動保存間隔 (秒)", Minimum = 15, Maximum = 600, Value = _preferences.AutosaveSeconds };
        var panel = new StackPanel { Spacing = 12, MinWidth = 350 };
        foreach (var control in new FrameworkElement[] { pressureWidth, pressureOpacity, smoothing, memory, interval }) panel.Children.Add(control);
        var dialog = new ContentDialog { XamlRoot = PaintCanvas.XamlRoot, Title = "描画・保存設定", Content = panel,
            PrimaryButtonText = "適用", SecondaryButtonText = "初期値へ戻す", CloseButtonText = "キャンセル" };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.None) return;
        if (result == ContentDialogResult.Secondary)
        {
            _preferences = new();
            ApplyPreferences();
        }
        else
        {
            if (!double.IsFinite(memory.Value) || !double.IsFinite(interval.Value)) return;
            _preferences = _preferences with { PressureWidth = pressureWidth.IsChecked == true, PressureOpacity = pressureOpacity.IsChecked == true,
                Smoothing = smoothing.Value, HistoryMiB = (int)Math.Clamp(memory.Value, 16, 2048), AutosaveSeconds = (int)Math.Clamp(interval.Value, 15, 600) };
        }
        SavePreferences();
        TrimDocumentHistory();
    }

    private sealed class HistoryAge(long value) { public long Value { get; } = value; }
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<DocumentSnapshot, HistoryAge> _historyAges = new();
    private long _nextHistoryAge;

    private void TrimDocumentHistory()
    {
        var inactive = _documents.Where(document => !ReferenceEquals(document, _activeDocument)).ToArray();
        long Age(DocumentSnapshot snapshot) => _historyAges.GetValue(snapshot, _ => new HistoryAge(++_nextHistoryAge)).Value;
        IEnumerable<DocumentSnapshot> Histories() => _undo.Reverse().Concat(_redo.Reverse())
            .Concat(inactive.SelectMany(document => document.Undo.Concat(document.Redo)));
        // 弱参照の年齢台帳で、破棄した履歴を管理用データから保持し続けない。
        foreach (var snapshot in Histories()) _ = Age(snapshot);
        long CountBytes()
        {
            var blocks = new HashSet<PixelBlock>();
            foreach (var state in Histories())
                foreach (var layer in state.Layers)
                    foreach (var block in layer.PixelBlocks.Blocks) blocks.Add(block);
            // タブ本体を維持するために不可欠な画素は、履歴の追加メモリに数えない。
            foreach (var document in inactive)
                if (document.Snapshot is { } current)
                    foreach (var layer in current.Layers)
                        foreach (var block in layer.PixelBlocks.Blocks) blocks.Remove(block);
            return blocks.Sum(block => (long)block.Length);
        }
        static void DropOldest(Stack<DocumentSnapshot> stack)
        {
            var retained = stack.Reverse().Skip(1).ToArray();
            stack.Clear(); foreach (var snapshot in retained) stack.Push(snapshot);
        }
        while (_undo.Count > 100) DropOldest(_undo);
        while (_redo.Count > 100) DropOldest(_redo);
        foreach (var document in inactive)
        {
            if (document.Undo.Length > 100) document.Undo = document.Undo[^100..];
            if (document.Redo.Length > 100) document.Redo = document.Redo[^100..];
        }
        while (CountBytes() > _preferences.HistoryMiB * 1024L * 1024)
        {
            var candidates = new List<(DocumentSnapshot Snapshot, Action Remove)>();
            if (_undo.Count > 0) candidates.Add((_undo.Last(), () => DropOldest(_undo)));
            if (_redo.Count > 0) candidates.Add((_redo.Last(), () => DropOldest(_redo)));
            foreach (var document in inactive)
            {
                if (document.Undo.Length > 0) candidates.Add((document.Undo[0], () => document.Undo = document.Undo[1..]));
                if (document.Redo.Length > 0) candidates.Add((document.Redo[0], () => document.Redo = document.Redo[1..]));
            }
            if (candidates.Count == 0) break;
            candidates.MinBy(candidate => Age(candidate.Snapshot)).Remove();
        }
    }
}