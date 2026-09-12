using System.IO.Compression;
using System.Text.Json;

namespace ChunPaint.Core;

public static class ChunPaintProjectArchive
{
    private const int CurrentVersion = 3;
    private const long MaximumTotalPixelBytes = 512L * 1024 * 1024;
    private const int MaximumManifestBytes = 4 * 1024 * 1024;
    private const string ManifestEntryName = "manifest.json";
    private const int MaximumPixelBytes = 256 * 1024 * 1024;

    public static void Save(Stream destination, ChunPaintProject project)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ValidateProject(project);
        var normalizedProject = NormalizeProject(project);
        if (!destination.CanWrite)
        {
            throw new ArgumentException("The destination stream is not writable.", nameof(destination));
        }

        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        var layerEntries = new List<ProjectLayerManifest>(normalizedProject.Layers.Count);
        for (var index = 0; index < normalizedProject.Layers.Count; index++)
        {
            var layer = normalizedProject.Layers[index];
            var entryName = $"layers/{index:D4}.bgra";
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using (var stream = entry.Open())
            {
                stream.Write(layer.Pixels);
            }
            layerEntries.Add(new ProjectLayerManifest(layer.Name, layer.IsVisible, entryName, layer.Id, layer.IsLocked, layer.Opacity));
        }

        var manifest = new ProjectManifest(CurrentVersion, normalizedProject.Width, normalizedProject.Height, normalizedProject.ActiveLayerIndex, layerEntries, normalizedProject.Objects ?? [], normalizedProject.Dpi);
        var manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
        using var manifestStream = manifestEntry.Open();
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        if (manifestBytes.Length > MaximumManifestBytes) throw new InvalidDataException("プロジェクトの管理情報が上限を超えています。");
        manifestStream.Write(manifestBytes);
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
        if (manifestEntry.Length > MaximumManifestBytes) throw new InvalidDataException("プロジェクトの管理情報が上限を超えています。");
        ProjectManifest manifest;
        using (var manifestStream = manifestEntry.Open())
        {
            try
            {
                manifest = JsonSerializer.Deserialize<ProjectManifest>(manifestStream, JsonOptions)
                    ?? throw new InvalidDataException("The project manifest is empty.");
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("The project manifest is not valid JSON.", exception);
            }
        }

        if (manifest.Version is not 1 and not 2 and not 3)
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
        if ((long)expectedLength * manifest.Layers.Count > MaximumTotalPixelBytes) throw new InvalidDataException("レイヤーの合計画素データが上限を超えています。");
        var entryNames = new HashSet<string>(StringComparer.Ordinal);
        var layers = new List<ChunPaintProjectLayer>(manifest.Layers.Count);
        var layerIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < manifest.Layers.Count; index++)
        {
            var savedLayer = manifest.Layers[index];
            if (savedLayer is null || string.IsNullOrWhiteSpace(savedLayer.Name) || string.IsNullOrWhiteSpace(savedLayer.Entry))
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
            var layerId = manifest.Version == 1 ? CreateLegacyLayerId(index) : savedLayer.Id;
            if (string.IsNullOrWhiteSpace(layerId) || !layerIds.Add(layerId))
            {
                throw new InvalidDataException("Project layer ids must be non-empty and unique.");
            }
            layers.Add(new ChunPaintProjectLayer(savedLayer.Name, savedLayer.IsVisible, pixels, layerId, manifest.Version >= 3 && savedLayer.IsLocked, manifest.Version >= 3 ? savedLayer.Opacity : 1f));
        }

        var objects = NormalizeLoadedObjects(manifest.Objects ?? [], layers, manifest.ActiveLayerIndex);
        var project = new ChunPaintProject(manifest.Width, manifest.Height, manifest.ActiveLayerIndex, layers, objects, manifest.Version >= 3 ? manifest.Dpi : 96);
        try
        {
            ValidateProject(project);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The project contains invalid layer or editable-object data.", exception);
        }
        return project;
    }

    private static ChunPaintProject NormalizeProject(ChunPaintProject project)
    {
        var layerIds = new HashSet<string>(StringComparer.Ordinal);
        var layers = new List<ChunPaintProjectLayer>(project.Layers.Count);
        for (var index = 0; index < project.Layers.Count; index++)
        {
            var layer = project.Layers[index];
            if (layer.Id is not null && string.IsNullOrWhiteSpace(layer.Id))
            {
                throw new ArgumentException("Project layer ids cannot be empty.", nameof(project));
            }
            var layerId = layer.Id ?? CreateLegacyLayerId(index);
            if (!layerIds.Add(layerId))
            {
                throw new ArgumentException("Project layer ids must be unique.", nameof(project));
            }
            layers.Add(layer with { Id = layerId });
        }

        var activeLayerId = layers[project.ActiveLayerIndex].Id!;
        var objects = (project.Objects ?? []).Select(editableObject =>
        {
            if (editableObject is null)
            {
                throw new ArgumentException("The project contains a null editable object.", nameof(project));
            }
            if (editableObject.LayerId is not null && string.IsNullOrWhiteSpace(editableObject.LayerId))
            {
                throw new ArgumentException("Editable object layer ids cannot be empty.", nameof(project));
            }
            var layerId = editableObject.LayerId ?? activeLayerId;
            if (!layerIds.Contains(layerId))
            {
                throw new ArgumentException("An editable object references a missing layer.", nameof(project));
            }
            return editableObject with { LayerId = layerId };
        }).ToArray();

        return project with { Layers = layers, Objects = objects };
    }

    private static IReadOnlyList<EditableObject> NormalizeLoadedObjects(
        IReadOnlyList<EditableObject> objects,
        IReadOnlyList<ChunPaintProjectLayer> layers,
        int activeLayerIndex)
    {
        var layerIds = layers.Select(layer => layer.Id!).ToHashSet(StringComparer.Ordinal);
        var activeLayerId = layers[activeLayerIndex].Id!;
        var objectIds = new HashSet<string>(StringComparer.Ordinal);
        var normalized = new List<EditableObject>(objects.Count);
        foreach (var editableObject in objects)
        {
            if (editableObject is null)
            {
                throw new InvalidDataException("The project contains a null editable object.");
            }
            if (!objectIds.Add(editableObject.Id))
            {
                throw new InvalidDataException("Editable object ids must be unique.");
            }
            if (editableObject.LayerId is not null && string.IsNullOrWhiteSpace(editableObject.LayerId))
            {
                throw new InvalidDataException("Editable object layer ids cannot be empty.");
            }

            // v1 has no layer ownership. A missing ownership value in either version
            // follows the documented legacy rule and belongs to the active layer.
            var layerId = editableObject.LayerId ?? activeLayerId;
            if (!layerIds.Contains(layerId))
            {
                throw new InvalidDataException("An editable object references a missing layer.");
            }
            normalized.Add(editableObject with { LayerId = layerId });
        }
        return normalized;
    }

    private static string CreateLegacyLayerId(int index) => $"layer-{index:D4}";

    private static void ValidateProject(ChunPaintProject project, bool requireLayerIds = false)
    {
        ArgumentNullException.ThrowIfNull(project);
        ValidateDimensions(project.Width, project.Height);
        if (!double.IsFinite(project.Dpi) || project.Dpi <= 0 || project.Dpi > 100_000) throw new ArgumentException("DPIが有効な範囲外です。", nameof(project));
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
        if ((long)expectedLength * project.Layers.Count > MaximumTotalPixelBytes) throw new ArgumentException("レイヤーの合計画素データが上限を超えています。", nameof(project));
        var layerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var layer in project.Layers)
        {
            if (layer is null || string.IsNullOrWhiteSpace(layer.Name))
            {
                throw new ArgumentException("Every project layer must have a name.", nameof(project));
            }
            if (!float.IsFinite(layer.Opacity) || layer.Opacity < 0 || layer.Opacity > 1) throw new ArgumentException("レイヤー不透明度が有効な範囲外です。", nameof(project));
            if (layer.Pixels is null || layer.Pixels.Length != expectedLength)
            {
                throw new ArgumentException("Every project layer must contain width × height × 4 BGRA bytes.", nameof(project));
            }
            if (layer.Id is not null && string.IsNullOrWhiteSpace(layer.Id))
            {
                throw new ArgumentException("Project layer ids cannot be empty.", nameof(project));
            }
            if (requireLayerIds && layer.Id is null)
            {
                throw new ArgumentException("Every project layer must have a non-empty id.", nameof(project));
            }
            if (layer.Id is not null && !layerIds.Add(layer.Id))
            {
                throw new ArgumentException("Project layer ids must be unique.", nameof(project));
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
            if (editableObject.LayerId is not null && string.IsNullOrWhiteSpace(editableObject.LayerId))
            {
                throw new ArgumentException("Editable object layer ids cannot be empty.", nameof(project));
            }
            if (editableObject.LayerId is not null && !layerIds.Contains(editableObject.LayerId))
            {
                throw new ArgumentException("An editable object references a missing layer.", nameof(project));
            }
            if (requireLayerIds && editableObject.LayerId is null)
            {
                throw new ArgumentException("Every persisted editable object must have a layer id.", nameof(project));
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
        IReadOnlyList<EditableObject>? Objects = null, double Dpi = 96);

    private sealed record ProjectLayerManifest(string Name, bool IsVisible, string Entry, string? Id = null, bool IsLocked = false, float Opacity = 1f);
}
