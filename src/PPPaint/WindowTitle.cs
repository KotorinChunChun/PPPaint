namespace PPPaint;

internal static class WindowTitle
{
    public static string Version { get; } = typeof(WindowTitle).Assembly.GetName().Version!.ToString(3);
    public static string Create(string? screen = null) => (string.IsNullOrWhiteSpace(screen) ? "" : screen + " - ") + "PPPaint - ver " + Version;
}
