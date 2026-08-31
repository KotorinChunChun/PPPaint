using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ChunPaint.Core;

namespace ChunPaint.Core.Tests;

[TestClass]
public sealed class ChunPaintProjectArchiveTests
{
    [TestMethod]
    public void RoundTripPreservesCanvasAndLayers()
    {
        var project = new ChunPaintProject(
            2,
            2,
            1,
            [
                new ChunPaintProjectLayer("背景", true, Enumerable.Range(0, 16).Select(value => (byte)value).ToArray(), "layer-background"),
                new ChunPaintProjectLayer("線画", false, Enumerable.Range(16, 16).Select(value => (byte)value).ToArray(), "layer-lines")
            ]);

        using var stream = new MemoryStream();
        ChunPaintProjectArchive.Save(stream, project);
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        using (var manifest = JsonDocument.Parse(archive.GetEntry("manifest.json")!.Open()))
        {
            Assert.AreEqual(2, manifest.RootElement.GetProperty("version").GetInt32(),
                "新規保存する.chunpaintはversion 2でなければなりません。");
        }
        stream.Position = 0;
        var restored = ChunPaintProjectArchive.Load(stream);

        Assert.AreEqual(project.Width, restored.Width);
        Assert.AreEqual(project.Height, restored.Height);
        Assert.AreEqual(project.ActiveLayerIndex, restored.ActiveLayerIndex);
        Assert.HasCount(2, restored.Layers);
        Assert.AreEqual("背景", restored.Layers[0].Name);
        Assert.IsTrue(restored.Layers[0].IsVisible);
        CollectionAssert.AreEqual(project.Layers[0].Pixels, restored.Layers[0].Pixels);
        Assert.AreEqual("線画", restored.Layers[1].Name);
        Assert.IsFalse(restored.Layers[1].IsVisible);
        CollectionAssert.AreEqual(project.Layers[1].Pixels, restored.Layers[1].Pixels);
    }

    [TestMethod]
    public void RoundTripPreservesEditableObjects()
    {
        var project = new ChunPaintProject(
            20,
            20,
            0,
            [new ChunPaintProjectLayer("背景", true, new byte[20 * 20 * 4], "layer-0000")],
            [
                EditableObject.CreateText(new ObjectBounds(2, 3, 8, 6), "編集可能", new TextObjectStyle(Bold: true), "text", "layer-0000"),
                EditableObject.CreateShape(EditableShapeKind.Lightning, new ObjectBounds(10, 4, 6, 10), new ShapeObjectStyle(DrawFill: true), id: "shape", layerId: "layer-0000")
            ]);

        using var stream = new MemoryStream();
        ChunPaintProjectArchive.Save(stream, project);
        stream.Position = 0;
        var restored = ChunPaintProjectArchive.Load(stream);

        Assert.IsNotNull(restored.Objects);
        Assert.HasCount(2, restored.Objects);
        Assert.AreEqual(project.Objects![0], restored.Objects[0]);
        Assert.AreEqual(project.Objects[1], restored.Objects[1]);
    }

    [TestMethod]
    public void V2RoundTripPreservesLayerAndObjectOwnershipIds()
    {
        var project = new ChunPaintProject(
            2,
            2,
            1,
            [
                new ChunPaintProjectLayer("下", true, new byte[16], "bottom-id"),
                new ChunPaintProjectLayer("上", false, new byte[16], "top-id")
            ],
            [EditableObject.CreateText(new ObjectBounds(0, 0, 1, 1), "上の文字", id: "object-id", layerId: "top-id")]);

        using var stream = new MemoryStream();
        ChunPaintProjectArchive.Save(stream, project);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            var manifestEntry = archive.GetEntry("manifest.json");
            Assert.IsNotNull(manifestEntry);
            using var manifest = JsonDocument.Parse(manifestEntry!.Open());
            Assert.AreEqual(2, manifest.RootElement.GetProperty("version").GetInt32(),
                "新規保存する.chunpaintはversion 2でなければなりません。");
        }
        stream.Position = 0;
        var restored = ChunPaintProjectArchive.Load(stream);

        Assert.AreEqual("bottom-id", restored.Layers[0].Id);
        Assert.AreEqual("top-id", restored.Layers[1].Id);
        Assert.AreEqual("top-id", restored.Objects![0].LayerId);
    }

    [TestMethod]
    public void SaveRejectsEmptyAndDuplicateLayerIds()
    {
        using var emptyIdStream = new MemoryStream();
        var emptyIdProject = new ChunPaintProject(1, 1, 0,
            [new ChunPaintProjectLayer("背景", true, new byte[4], "")]);
        Assert.ThrowsExactly<ArgumentException>(() => ChunPaintProjectArchive.Save(emptyIdStream, emptyIdProject));

        using var duplicateIdStream = new MemoryStream();
        var duplicateIdProject = new ChunPaintProject(1, 1, 0,
            [
                new ChunPaintProjectLayer("下", true, new byte[4], "same"),
                new ChunPaintProjectLayer("上", true, new byte[4], "same")
            ]);
        Assert.ThrowsExactly<ArgumentException>(() => ChunPaintProjectArchive.Save(duplicateIdStream, duplicateIdProject));
    }

    [TestMethod]
    public void SaveRejectsEmptyOrMissingObjectLayerOwnership()
    {
        var emptyObjectLayerId = new ChunPaintProject(
            1, 1, 0,
            [new ChunPaintProjectLayer("背景", true, new byte[4], "layer")],
            [EditableObject.CreateText(new ObjectBounds(0, 0, 1, 1), "text", id: "empty", layerId: "")]);
        using var emptyStream = new MemoryStream();
        Assert.ThrowsExactly<ArgumentException>(() => ChunPaintProjectArchive.Save(emptyStream, emptyObjectLayerId));

        var missingObjectLayer = new ChunPaintProject(
            1, 1, 0,
            [new ChunPaintProjectLayer("背景", true, new byte[4], "layer")],
            [EditableObject.CreateText(new ObjectBounds(0, 0, 1, 1), "text", id: "missing")]);
        using var missingStream = new MemoryStream();
        ChunPaintProjectArchive.Save(missingStream, missingObjectLayer);
        missingStream.Position = 0;
        Assert.AreEqual("layer", ChunPaintProjectArchive.Load(missingStream).Objects![0].LayerId);
    }

    [TestMethod]
    public void V1LoadMigratesLayerIdsDeterministicallyAndAssignsObjectsToLegacyActiveLayer()
    {
        var objectJson = SerializeFixtureObject("legacy-object");
        using var stream = CreateArchive(
            $"{{\"version\":1,\"width\":1,\"height\":1,\"activeLayerIndex\":1,\"layers\":[{{\"name\":\"下\",\"isVisible\":true,\"entry\":\"layers/0000.bgra\"}},{{\"name\":\"上\",\"isVisible\":false,\"entry\":\"layers/0001.bgra\"}}],\"objects\":[{objectJson}]}}",
            ("layers/0000.bgra", new byte[4]),
            ("layers/0001.bgra", new byte[4]));

        var loaded = ChunPaintProjectArchive.Load(stream);

        Assert.AreEqual("layer-0000", loaded.Layers[0].Id);
        Assert.AreEqual("layer-0001", loaded.Layers[1].Id);
        Assert.AreEqual("layer-0001", loaded.Objects![0].LayerId);
    }

    [TestMethod]
    public void V2LoadCompletesMissingObjectLayerIdUsingActiveLayer()
    {
        var objectJson = SerializeFixtureObject("v2-object");
        using var stream = CreateArchive(
            $"{{\"version\":2,\"width\":1,\"height\":1,\"activeLayerIndex\":1,\"layers\":[{{\"name\":\"下\",\"isVisible\":true,\"entry\":\"layers/0000.bgra\",\"id\":\"bottom\"}},{{\"name\":\"上\",\"isVisible\":true,\"entry\":\"layers/0001.bgra\",\"id\":\"top\"}}],\"objects\":[{objectJson}]}}",
            ("layers/0000.bgra", new byte[4]),
            ("layers/0001.bgra", new byte[4]));

        Assert.AreEqual("top", ChunPaintProjectArchive.Load(stream).Objects![0].LayerId);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(3)]
    [DataRow(999)]
    public void LoadRejectsVersionsOtherThanOneAndTwo(int version)
    {
        using var stream = CreateArchive(
            $"{{\"version\":{version},\"width\":1,\"height\":1,\"activeLayerIndex\":0,\"layers\":[]}}");

        Assert.ThrowsExactly<InvalidDataException>(() => ChunPaintProjectArchive.Load(stream));
    }

    [TestMethod]
    public void LoadRejectsInvalidLayerIdsAndObjectReferences()
    {
        var cases = new[]
        {
            "[{\"name\":\"下\",\"isVisible\":true,\"entry\":\"layers/0000.bgra\",\"id\":\"\"}]",
            "[{\"name\":\"下\",\"isVisible\":true,\"entry\":\"layers/0000.bgra\",\"id\":\"same\"},{\"name\":\"上\",\"isVisible\":true,\"entry\":\"layers/0001.bgra\",\"id\":\"same\"}]"
        };
        foreach (var layers in cases)
        {
            var entries = layers.Contains("0001", StringComparison.Ordinal)
                ? new[] { ("layers/0000.bgra", new byte[4]), ("layers/0001.bgra", new byte[4]) }
                : new[] { ("layers/0000.bgra", new byte[4]) };
            using var stream = CreateArchive(
                $"{{\"version\":2,\"width\":1,\"height\":1,\"activeLayerIndex\":0,\"layers\":{layers}}}", entries);
            Assert.ThrowsExactly<InvalidDataException>(() => ChunPaintProjectArchive.Load(stream));
        }

        var objectJson = SerializeFixtureObject("orphan", "missing-layer");
        using var orphanStream = CreateArchive(
            $"{{\"version\":2,\"width\":1,\"height\":1,\"activeLayerIndex\":0,\"layers\":[{{\"name\":\"背景\",\"isVisible\":true,\"entry\":\"layers/0000.bgra\",\"id\":\"known\"}}],\"objects\":[{objectJson}]}}",
            ("layers/0000.bgra", new byte[4]));
        Assert.ThrowsExactly<InvalidDataException>(() => ChunPaintProjectArchive.Load(orphanStream));
    }

    [TestMethod]
    public void SaveRejectsIncorrectPixelLength()
    {
        var project = new ChunPaintProject(2, 2, 0, [new ChunPaintProjectLayer("不正", true, new byte[15])]);

        using var stream = new MemoryStream();
        Assert.ThrowsExactly<ArgumentException>(() => ChunPaintProjectArchive.Save(stream, project));
    }

    [TestMethod]
    public void LoadRejectsUnsupportedVersion()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("manifest.json");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write("{\"version\":999,\"width\":1,\"height\":1,\"activeLayerIndex\":0,\"layers\":[]}");
        }
        stream.Position = 0;

        Assert.ThrowsExactly<InvalidDataException>(() => ChunPaintProjectArchive.Load(stream));
    }

    [TestMethod]
    public void LoadRejectsMissingLayerEntry()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("manifest.json");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write("{\"version\":1,\"width\":1,\"height\":1,\"activeLayerIndex\":0,\"layers\":[{\"name\":\"背景\",\"isVisible\":true,\"entry\":\"layers/0000.bgra\"}]}");
        }
        stream.Position = 0;

        Assert.ThrowsExactly<InvalidDataException>(() => ChunPaintProjectArchive.Load(stream));
    }

    [TestMethod]
    public void LoadRejectsProjectsThatExceedPixelSafetyLimit()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("manifest.json");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write("{\"version\":1,\"width\":8192,\"height\":8192,\"activeLayerIndex\":0,\"layers\":[]}");
        }
        stream.Position = 0;

        Assert.ThrowsExactly<InvalidDataException>(() => ChunPaintProjectArchive.Load(stream));
    }

    [TestMethod]
    public void LoadRejectsInvalidEditableObjectsFromUntrustedManifest()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifestEntry = archive.CreateEntry("manifest.json");
            using (var writer = new StreamWriter(manifestEntry.Open(), Encoding.UTF8))
            {
                writer.Write("""
                    {"version":1,"width":1,"height":1,"activeLayerIndex":0,
                     "layers":[{"name":"背景","isVisible":true,"entry":"layers/0000.bgra"}],
                     "objects":[{"id":"invalid","kind":0,"bounds":{"x":0,"y":0,"width":1,"height":1},
                                 "rotationDegrees":0,"text":"text without a required style"}]}
                    """);
            }
            var layerEntry = archive.CreateEntry("layers/0000.bgra");
            using var layerStream = layerEntry.Open();
            layerStream.Write(new byte[4]);
        }
        stream.Position = 0;

        Assert.ThrowsExactly<InvalidDataException>(() => ChunPaintProjectArchive.Load(stream));
    }

    [TestMethod]
    public void LoadRejectsUndefinedEditableObjectEnums()
    {
        using var stream = CreateArchive(
            """
            {"version":2,"width":1,"height":1,"activeLayerIndex":0,
             "layers":[{"name":"背景","isVisible":true,"entry":"layers/0000.bgra","id":"layer"}],
             "objects":[{"id":"invalid-enum","kind":1,"bounds":{"x":0,"y":0,"width":1,"height":1},
                         "rotationDegrees":0,"shape":999,"shapeStyle":{"outlineColor":{},"fillColor":{},
                         "strokeWidth":1,"drawOutline":true,"drawFill":false,"outlineKind":0,"fillKind":0},
                         "layerId":"layer"}]}
            """,
            ("layers/0000.bgra", new byte[4]));

        Assert.ThrowsExactly<InvalidDataException>(() => ChunPaintProjectArchive.Load(stream));
    }

    private static string SerializeFixtureObject(string id, string? layerId = null)
    {
        var editableObject = EditableObject.CreateText(
            new ObjectBounds(0, 0, 1, 1), "fixture", id: id, layerId: layerId);
        return JsonSerializer.Serialize(editableObject, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
    }

    private static MemoryStream CreateArchive(
        string manifest,
        params (string Name, byte[] Data)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifestEntry = archive.CreateEntry("manifest.json");
            using (var writer = new StreamWriter(manifestEntry.Open(), Encoding.UTF8))
            {
                writer.Write(manifest);
            }

            foreach (var (name, data) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var entryStream = entry.Open();
                entryStream.Write(data);
            }
        }
        stream.Position = 0;
        return stream;
    }
}
