namespace ChunPaint.Editing;

internal sealed record EditorPreferences
{
    public string Brush { get; init; } = "Oil";
    public double StrokeWidth { get; init; } = 3;
    public double Opacity { get; init; } = 100;
    public string PrimaryColor { get; init; } = "#FF000000";
    public string SecondaryColor { get; init; } = "#FFFFFFFF";
    public string[] CustomColors { get; init; } = [];
    public bool Rulers { get; init; }
    public bool Grid { get; init; }
    public bool StatusBar { get; init; } = true;
    public bool PressureWidth { get; init; } = true;
    public bool PressureOpacity { get; init; }
    public double Smoothing { get; init; }
    public int HistoryMiB { get; init; } = 256;
    public int AutosaveSeconds { get; init; } = 60;
}
