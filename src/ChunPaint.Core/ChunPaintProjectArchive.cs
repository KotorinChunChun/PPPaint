using System.IO.Compression;
using System.Text.Json;

namespace ChunPaint.Core;

public static class ChunPaintProjectArchive
{
    private const int CurrentVersion = 1;
    private const string ManifestEntryName = "manifest.json";
    private const int MaximumPixelBytes = 256 * 1024 * 1024;

    public static void Save(Stream destination, ChunPaintProject project)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ValidateProject(project);
        if (!destination.CanWrite)
        {
            throw new ArgumentException("The destination stream is not writable.", nameof(destination));
        }

        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        var layerEntries = new List<ProjectLayerManifest>(project.Layers.Count);
        for (var index = 0; index < project.Layers.Count; index++)
        {
            var layer = project.Layers[index];
            var entryName = $"layers/{index:D4}.bgra";
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using (var stream = entry.Open())
            {
                stream.Write(layer.Pixels);
            }
            layerEntries.Add(new ProjectLayerManifest(layer.Name, layer.IsVisible, entryName));
        }

        var manifest = new ProjectManifest(CurrentVersion, project.Width, project.Height, project.ActiveLayerIndex, layerEntries, project.Objects ?? []);
        var manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
        using var manifestStream = manifestEntry.Open();
        JsonSerializer.Serialize(manifestStream, manifest, JsonOptions);
    }

    public static ChunPaintProject Load(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
        {
            throw new ArgumentException("The source stream is not readable.", nameof(source));
        }

        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        var manifestEntry = archive.GetEntry(ManifestEntryName)
            ?? throw new InvalidDataException("The project manifest is missing.");
        ProjectManifest manifest;
        using (var manifestStream = manifestEntry.Open())
        {
            manifest = JsonSerializer.Deserialize<ProjectManifest>(manifestStream, JsonOptions)
                ?? throw new InvalidDataException("The project manifest is empty.");
        }

        if (manifest.Version != CurrentVersion)
        {
            throw new InvalidDataException($"Unsupported ChunPaint project version: {manifest.Version}.");
        }
        ValidateDimensions(manifest.Width, manifest.Height);
        if (manifest.Layers is null || manifest.Layers.Count == 0)
        {
            throw new InvalidDataException("The project must contain at least one layer.");
        }
        if ((uint)manifest.ActiveLayerIndex >= manifest.Layers.Count)
        {
            throw new InvalidDataException("The active layer index is outside the layer collection.");
        }

        var expectedLength = GetExpectedPixelLength(manifest.Width, manifest.Height);
        var entryNames = new HashSet<string>(StringComparer.Ordinal);
        var layers = new List<ChunPaintProjectLayer>(manifest.Layers.Count);
        foreach (var savedLayer in manifest.Layers)
        {
            if (string.IsNullOrWhiteSpace(savedLayer.Name) || string.IsNullOrWhiteSpace(savedLayer.Entry))
            {
                throw new InvalidDataException("A project layer has invalid metadata.");
            }
            if (!entryNames.Add(savedLayer.Entry))
            {
                throw new InvalidDataException("Project layers reference the same data entry.");
            }
            var entry = archive.GetEntry(savedLayer.Entry)
                ?? throw new InvalidDataException($"Layer data is missing: {savedLayer.Entry}.");
            if (entry.Length != expectedLength)
            {
                throw new InvalidDataException($"Layer data has an invalid length: {savedLayer.Entry}.");
            }

            var pixels = new byte[expectedLength];
            using var stream = entry.Open();
            stream.ReadExactly(pixels);
            layers.Add(new ChunPaintProjectLayer(savedLayer.Name, savedLayer.IsVisible, pixels));
        }

        return new ChunPaintProject(manifest.Width, manifest.Height, manifest.ActiveLayerIndex, layers, manifest.Objects ?? []);
    }

    private static void ValidateProject(ChunPaintProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        ValidateDimensions(project.Width, project.Height);
        ArgumentNullException.ThrowIfNull(project.Layers);
        if (project.Layers.Count == 0)
        {
            throw new ArgumentException("The project must contain at least one layer.", nameof(project));
        }
        if ((uint)project.ActiveLayerIndex >= project.Layers.Count)
        {
            throw new ArgumentException("The active layer index is outside the layer collection.", nameof(project));
        }

        var expectedLength = GetExpectedPixelLength(project.Width, project.Height);
        foreach (var layer in project.Layers)
        {
            if (layer is null || string.IsNullOrWhiteSpace(layer.Name))
            {
                throw new ArgumentException("Every project layer must have a name.", nameof(project));
            }
            if (layer.Pixels is null || layer.Pixels.Length != expectedLength)
            {
                throw new ArgumentException("Every project layer must contain width × height × 4 BGRA bytes.", nameof(project));
            }
        }

        if (project.Objects is null)
        {
            return;
        }
        var objectIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var editableObject in project.Objects)
        {
            if (editableObject is null)
            {
                throw new ArgumentException("The project contains a null editable object.", nameof(project));
            }
            editableObject.Validate();
            if (!objectIds.Add(editableObject.Id))
            {
                throw new ArgumentException("Editable object ids must be unique.", nameof(project));
            }
            if (editableObject.Bounds.X < 0 || editableObject.Bounds.Y < 0 ||
                editableObject.Bounds.Right > project.Width || editableObject.Bounds.Bottom > project.Height)
            {
                throw new ArgumentException("Editable objects must be inside the project canvas.", nameof(project));
            }
        }
    }

    private static void ValidateDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0 || width > 100_000 || height > 100_000)
        {
            throw new InvalidDataException("The project dimensions are invalid.");
        }
        _ = GetExpectedPixelLength(width, height);
    }

    private static int GetExpectedPixelLength(int width, int height)
    {
        try
        {
            var length = checked(width * height * 4);
            if (length > MaximumPixelBytes)
            {
                throw new InvalidDataException($"The project pixel data exceeds the {MaximumPixelBytes / (1024 * 1024)} MiB safety limit.");
            }
            return length;
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("The project dimensions are too large.", exception);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private sealed record ProjectManifest(
        int Version,
        int Width,
        int Height,
        int ActiveLayerIndex,
        IReadOnlyList<ProjectLayerManifest> Layers,
        IReadOnlyList<EditableObject>? Objects = null);

    private sealed record ProjectLayerManifest(string Name, bool IsVisible, string Entry);
}
