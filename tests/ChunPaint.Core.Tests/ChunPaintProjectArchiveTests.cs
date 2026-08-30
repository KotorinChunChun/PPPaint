using System.IO.Compression;
using System.Text;
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
                new ChunPaintProjectLayer("背景", true, Enumerable.Range(0, 16).Select(value => (byte)value).ToArray()),
                new ChunPaintProjectLayer("線画", false, Enumerable.Range(16, 16).Select(value => (byte)value).ToArray())
            ]);

        using var stream = new MemoryStream();
        ChunPaintProjectArchive.Save(stream, project);
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
            [new ChunPaintProjectLayer("背景", true, new byte[20 * 20 * 4])],
            [
                EditableObject.CreateText(new ObjectBounds(2, 3, 8, 6), "編集可能", new TextObjectStyle(Bold: true), "text"),
                EditableObject.CreateShape(EditableShapeKind.Lightning, new ObjectBounds(10, 4, 6, 10), new ShapeObjectStyle(DrawFill: true), id: "shape")
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
}
