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
}
