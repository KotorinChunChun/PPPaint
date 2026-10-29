using System.IO.Compression;
using System.Text;
using System.Text.Json;
using PPPaint.Core;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class PPPaintProjectV4Tests
{
    private static PPPaintProject Project => new(1, 1, 0,
        [new("補正", true, [0, 0, 128, 128], "layer", Mask: [122], BlendMode: LayerBlendMode.Screen,
            Group: "写真", Adjustment: new PhotoAdjustmentSettings(Gamma: 1.2))]);

    [TestMethod]
    public void RoundTripPersistsAllV4MetadataAndSeparateMaskEntry()
    {
        using var stream = new MemoryStream(); PPPaintProjectArchive.Save(stream, Project); stream.Position = 0;
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, true))
        {
            using var manifest = JsonDocument.Parse(zip.GetEntry("manifest.json")!.Open());
            Assert.AreEqual(4, manifest.RootElement.GetProperty("version").GetInt32());
            Assert.AreEqual(1L, zip.GetEntry("masks/0000.gray")!.Length);
            Assert.IsFalse(manifest.RootElement.GetProperty("layers")[0].TryGetProperty("mask", out _));
        }
        stream.Position = 0; var loaded = PPPaintProjectArchive.Load(stream).Layers[0];
        CollectionAssert.AreEqual(new byte[] { 122 }, loaded.Mask);
        Assert.AreEqual(LayerBlendMode.Screen, loaded.BlendMode);
        Assert.AreEqual("写真", loaded.Group);
        Assert.AreEqual(1.2, loaded.Adjustment!.Gamma);
    }

    [TestMethod]
    public void VersionsOneThroughThreeHaveNoMaskGroupOrAdjustment()
    {
        foreach (var version in new[] { 1, 2, 3 })
        {
            using var stream = Archive($$"""{"version":{{version}},"width":1,"height":1,"activeLayerIndex":0,"layers":[{"name":"旧版","isVisible":true,"entry":"layer","id":"layer"}]}""");
            var layer = PPPaintProjectArchive.Load(stream).Layers[0];
            Assert.IsNull(layer.Mask); Assert.IsNull(layer.Group); Assert.IsNull(layer.Adjustment);
            Assert.AreEqual(LayerBlendMode.Normal, layer.BlendMode);
        }
    }

    [TestMethod]
    public void RejectsInvalidMetadataOnSave()
    {
        foreach (var layer in new[]
        {
            Project.Layers[0] with { Mask = [] }, Project.Layers[0] with { BlendMode = (LayerBlendMode)100 },
            Project.Layers[0] with { Group = new string('名', 129) }, Project.Layers[0] with { Group = "不正\n名前" },
            Project.Layers[0] with { Adjustment = new PhotoAdjustmentSettings(Gamma: double.NaN) }
        })
        {
            using var stream = new MemoryStream();
            Assert.Throws<ArgumentException>(() => PPPaintProjectArchive.Save(stream, Project with { Layers = [layer] }));
        }
    }

    [TestMethod]
    public void RejectsMissingWrongLengthAndAliasedMasks()
    {
        foreach (var maskEntry in new[] { "missing", "layer", " " })
        {
            using var stream = Archive($$"""{"version":4,"width":1,"height":1,"activeLayerIndex":0,"layers":[{"name":"不正","entry":"layer","id":"layer","maskEntry":"{{maskEntry}}"}]}""");
            Assert.Throws<InvalidDataException>(() => PPPaintProjectArchive.Load(stream));
        }
        using var wrongLength = Archive("""{"version":4,"width":1,"height":1,"activeLayerIndex":0,"layers":[{"name":"不正","entry":"layer","id":"layer","maskEntry":"mask"}]}""", [1, 2]);
        Assert.Throws<InvalidDataException>(() => PPPaintProjectArchive.Load(wrongLength));
    }

    [TestMethod]
    public void MasksCountTowardTotalBudgetBeforeAllocation()
    {
        using var stream = Archive("""{"version":4,"width":8192,"height":8192,"activeLayerIndex":0,"layers":[{"name":"a","entry":"a","id":"a","maskEntry":"m"},{"name":"b","entry":"b","id":"b"}]}""");
        var error = Assert.Throws<InvalidDataException>(() => PPPaintProjectArchive.Load(stream));
        StringAssert.Contains(error.Message, "合計画素");
    }

    private static MemoryStream Archive(string manifest, byte[]? mask = null)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            using (var entry = zip.CreateEntry("manifest.json").Open()) entry.Write(Encoding.UTF8.GetBytes(manifest));
            using (var entry = zip.CreateEntry("layer").Open()) entry.Write(new byte[4]);
            if (mask is not null) { using var entry = zip.CreateEntry("mask").Open(); entry.Write(mask); }
        }
        stream.Position = 0; return stream;
    }
}
