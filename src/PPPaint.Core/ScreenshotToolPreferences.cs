using System.Text.Json;

namespace PPPaint.Core;

public sealed record ScreenshotToolNumbers(double Width = 3, double FontSize = 24, double BlurRadius = 8, double MosaicSize = 12, double Opacity = 25)
{
    public double Get(ScreenshotStyleProperty property) => property switch
    {
        ScreenshotStyleProperty.Width => Width,
        ScreenshotStyleProperty.FontSize => FontSize,
        ScreenshotStyleProperty.BlurRadius => BlurRadius,
        ScreenshotStyleProperty.MosaicSize => MosaicSize,
        _ => Opacity
    };

    public ScreenshotToolNumbers With(ScreenshotStyleProperty property, double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        return property switch
        {
            ScreenshotStyleProperty.Width => this with { Width = Math.Clamp(value, 1, 100) },
            ScreenshotStyleProperty.FontSize => this with { FontSize = Math.Clamp(value, 8, 200) },
            ScreenshotStyleProperty.BlurRadius => this with { BlurRadius = Math.Clamp(value, 1, 64) },
            ScreenshotStyleProperty.MosaicSize => this with { MosaicSize = Math.Clamp(Math.Round(value), 2, 128) },
            ScreenshotStyleProperty.Opacity => this with { Opacity = Math.Clamp(value, 5, 100) },
            _ => throw new ArgumentOutOfRangeException(nameof(property))
        };
    }
}

// New drawing defaults only. Selecting an annotation must never write into this model.
public sealed class ScreenshotToolPreferences
{
    private readonly Dictionary<ScreenshotTool, ScreenshotToolNumbers> _tools = [];
    public string Color { get; set; } = "#DC2626";
    public string[]? Palette { get; set; }
    public ScreenshotToolNumbers Get(ScreenshotTool tool)
    {
        var numbers = _tools.GetValueOrDefault(tool) ?? new();
        return tool == ScreenshotTool.Eraser
            ? numbers with { Width = Math.Clamp(Math.Round(numbers.Width, MidpointRounding.AwayFromZero), 1, 100) }
            : numbers;
    }
    public void Set(ScreenshotTool tool, ScreenshotStyleProperty property, double value)
    {
        if (!Enum.IsDefined(tool)) throw new ArgumentOutOfRangeException(nameof(tool));
        _tools[tool] = Get(tool).With(property, value);
    }
    public void ResetNumbers() => _tools.Clear();

    public static ScreenshotToolPreferences Parse(string json)
    {
        var result = new ScreenshotToolPreferences();
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return result;
            if (root.TryGetProperty("Color", out var color) && color.ValueKind == JsonValueKind.String && ValidColor(color.GetString())) result.Color = color.GetString()!;
            if (root.TryGetProperty("Palette", out var palette) && palette.ValueKind == JsonValueKind.Array)
                result.Palette = palette.EnumerateArray().Select(p => p.ValueKind == JsonValueKind.String && ValidColor(p.GetString()) ? p.GetString()! : "").ToArray();
            var legacy = Numbers(root, new());
            foreach (var tool in Enum.GetValues<ScreenshotTool>()) result._tools[tool] = legacy;
            if (root.TryGetProperty("Tools", out var tools) && tools.ValueKind == JsonValueKind.Object)
                foreach (var entry in tools.EnumerateObject())
                    if (Enum.TryParse<ScreenshotTool>(entry.Name, true, out var tool) && Enum.IsDefined(tool) && entry.Value.ValueKind == JsonValueKind.Object)
                        result._tools[tool] = Numbers(entry.Value, legacy);
        }
        catch (JsonException) { }
        return result;
    }

    private static bool ValidColor(string? text) => text is { Length: 7 } && text[0] == '#' && uint.TryParse(text.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out _);
    private static ScreenshotToolNumbers Numbers(JsonElement json, ScreenshotToolNumbers fallback)
    {
        var result = fallback;
        foreach (var property in Enum.GetValues<ScreenshotStyleProperty>())
            if (json.TryGetProperty(property.ToString(), out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number))
                result = result.With(property, number);
        return result;
    }
    public string Serialize() => JsonSerializer.Serialize(new
    {
        Version = 2, Color, Palette,
        Tools = Enum.GetValues<ScreenshotTool>().ToDictionary(tool => tool.ToString(), Get)
    });

    public static ScreenshotToolPreferences Read(string path) => File.Exists(path) ? Parse(File.ReadAllText(path)) : new();

    // Reload before applying only the fields edited by this window. Other windows' tools survive.
    public static ScreenshotToolPreferences SaveMerged(string path, IReadOnlyDictionary<(ScreenshotTool Tool, ScreenshotStyleProperty Property), double> changes,
        string color, string[] palette, bool resetNumbers = false)
    {
        var result = Read(path);
        if (resetNumbers) result.ResetNumbers();
        foreach (var change in changes) result.Set(change.Key.Tool, change.Key.Property, change.Value);
        result.Color = color; result.Palette = palette;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + "." + Environment.ProcessId + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, result.Serialize()); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return result;
    }
}
