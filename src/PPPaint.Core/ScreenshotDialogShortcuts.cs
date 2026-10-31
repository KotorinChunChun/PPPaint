namespace PPPaint.Core;

public enum ScreenshotDialogAction { None, Primary, Secondary, Close }

public sealed record ScreenshotDialogShortcuts(bool AllowPlainKeys = false, char PrimaryKey = 'S', char SecondaryKey = 'D', char CloseKey = 'C')
{
    public ScreenshotDialogAction Resolve(char key, bool control, bool alt, bool shift, bool editingText, bool composing)
    {
        if (control || shift || composing || !char.IsLetter(key)) return ScreenshotDialogAction.None;
        if (!alt && (!AllowPlainKeys || editingText)) return ScreenshotDialogAction.None;

        var letter = char.ToUpperInvariant(key);
        if (letter == char.ToUpperInvariant(PrimaryKey)) return ScreenshotDialogAction.Primary;
        if (letter == char.ToUpperInvariant(SecondaryKey)) return ScreenshotDialogAction.Secondary;
        if (letter == char.ToUpperInvariant(CloseKey)) return ScreenshotDialogAction.Close;
        return ScreenshotDialogAction.None;
    }
}
