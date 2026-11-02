using PPPaint.Core;

namespace PPPaint;

public sealed partial class EditorWindow
{
    private ScreenshotToolPreferences _toolPreferences = new();
    private readonly Dictionary<(ScreenshotTool Tool, ScreenshotStyleProperty Property), double> _numericPreferenceChanges = [];
    private bool _resetNumericPreferences;
    private readonly Dictionary<ScreenshotTool, ScreenshotShapeStyle> _shapePreferenceChanges = [];
    private string PreferencesPath => Path.Combine(App.Options.SettingsDirectory, "screenshot-preferences.json");
    private void RememberDrawingNumber(ScreenshotTool tool, ScreenshotStyleProperty property, double value)
    {
        _toolPreferences.Set(tool, property, value);
        _numericPreferenceChanges[(tool, property)] = value;
    }
    private void ReloadToolPreferences()
    {
        if (_numericPreferenceChanges.Count == 0 && _shapePreferenceChanges.Count == 0 && !_resetNumericPreferences)
            try { _toolPreferences = ScreenshotToolPreferences.Read(PreferencesPath); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
    private void ResetDrawingPreferences()
    {
        _palette = DefaultPalette(); _activePaletteIndex = 0; _drawingColor = _palette[0];
        _toolPreferences.ResetNumbers(); _numericPreferenceChanges.Clear(); _resetNumericPreferences = true;
        SavePreferences(); ShowSelectedOptions(); Refresh();
    }
    private void LoadPreferences()
    {
        try
        {
            var p = ScreenshotToolPreferences.Read(PreferencesPath); _toolPreferences = p;
            _updatingControls = true; _drawingColor = _color = ParseColor(p.Color);
            var numbers = p.Get(_tool);
            WidthBox.Value = numbers.Width; FontBox.Value = numbers.FontSize; BlurRadiusBox.Value = numbers.BlurRadius;
            MosaicSizeBox.Value = numbers.MosaicSize; OpacityBox.Value = numbers.Opacity;
            if (p.Palette is { } palette)
                for (var i = 0; i < Math.Min(palette.Length, _palette.Length); i++)
                    try { if (!string.IsNullOrWhiteSpace(palette[i])) _palette[i] = ParseColor(palette[i]); } catch (ArgumentException) { }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { }
        finally { _updatingControls = false; }
    }
    private void SavePreferences()
    {
        if (WidthBox is null || _updatingControls) return;
        try
        {
            _toolPreferences = ScreenshotToolPreferences.SaveMerged(PreferencesPath, _numericPreferenceChanges,
                ColorHex(_drawingColor), _palette.Select(ColorHex).ToArray(), _resetNumericPreferences, _shapePreferenceChanges);
            _numericPreferenceChanges.Clear(); _shapePreferenceChanges.Clear(); _resetNumericPreferences = false;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { StatusText.Text = "設定を保存できませんでした。"; }
    }
}
