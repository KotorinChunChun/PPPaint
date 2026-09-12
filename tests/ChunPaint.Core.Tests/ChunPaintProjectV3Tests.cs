using System.IO.Compression;
using System.Text;
using ChunPaint.Core;

namespace ChunPaint.Core.Tests;

[TestClass]
public sealed class ChunPaintProjectV3Tests
{
    private static ChunPaintProject Sample => new(1, 1, 0,
        [new("レイヤー", true, new byte[4], "layer", true, .35f)], Dpi: 300);

    [TestMethod]
    public void PreservesDpiLockAndOpacity()
    {
        using var stream = new MemoryStream();
        ChunPaintProjectArchive.Save(stream, Sample);
        stream.Position = 0;
        var loaded = ChunPaintProjectArchive.Load(stream);
        Assert.AreEqual(300d, loaded.Dpi);
        Assert.IsTrue(loaded.Layers[0].IsLocked);
        Assert.AreEqual(.35f, loaded.Layers[0].Opacity);
    }

    [TestMethod]
    public void LegacyDefaultsRemainOpaqueUnlockedAnd96Dpi()
    {
        foreach (var version in new[] { 1, 2 })
        {
            using var stream = Archive($$"""{"version":{{version}},"width":1,"height":1,"activeLayerIndex":0,"layers":[{"name":"旧版","isVisible":true,"entry":"layer","id":"layer"}]}""");
            var loaded = ChunPaintProjectArchive.Load(stream);
            Assert.AreEqual(96d, loaded.Dpi);
            Assert.IsFalse(loaded.Layers[0].IsLocked);
            Assert.AreEqual(1f, loaded.Layers[0].Opacity);
        }
    }

    [TestMethod]
    public void RejectsNonFiniteAndOutOfRangeMetadata()
    {
        foreach (var dpi in new[] { double.NaN, double.PositiveInfinity, 0d, -1d })
        {
            using var stream = new MemoryStream();
            Assert.Throws<ArgumentException>(() => ChunPaintProjectArchive.Save(stream, Sample with { Dpi = dpi }));
        }
        foreach (var opacity in new[] { float.NaN, float.PositiveInfinity, -1f, 1.01f })
        {
            using var stream = new MemoryStream();
            Assert.Throws<ArgumentException>(() => ChunPaintProjectArchive.Save(stream,
                Sample with { Layers = [Sample.Layers[0] with { Opacity = opacity }] }));
        }
    }

    [TestMethod]
    public void RejectsOversizedManifestBeforeParsing()
    {
        using var stream = Archive(new string(' ', 4 * 1024 * 1024 + 1));
        Assert.Throws<InvalidDataException>(() => ChunPaintProjectArchive.Load(stream));
    }

    [TestMethod]
    public void MissingOptionalVersion3MetadataUsesDefaults()
    {
        using var stream = Archive("""{"version":3,"width":1,"height":1,"activeLayerIndex":0,"layers":[{"name":"既定","isVisible":true,"entry":"layer","id":"layer"}]}""");
        var loaded = ChunPaintProjectArchive.Load(stream);
        Assert.AreEqual(96d, loaded.Dpi);
        Assert.AreEqual(1f, loaded.Layers[0].Opacity);
        Assert.IsFalse(loaded.Layers[0].IsLocked);
    }

    [TestMethod]
    public void RejectsInvalidLoadedMetadata()
    {
        foreach (var metadata in new[] { "\"dpi\":0", "\"dpi\":1e309" })
        {
            using var stream = Archive($$"""{"version":3,"width":1,"height":1,"activeLayerIndex":0,{{metadata}},"layers":[{"name":"不正","entry":"layer","id":"layer"}]}""");
            Assert.Throws<InvalidDataException>(() => ChunPaintProjectArchive.Load(stream));
        }
        using var invalidOpacity = Archive("""{"version":3,"width":1,"height":1,"activeLayerIndex":0,"layers":[{"name":"不正","entry":"layer","id":"layer","opacity":1.1}]}""");
        Assert.Throws<InvalidDataException>(() => ChunPaintProjectArchive.Load(invalidOpacity));
    }

    [TestMethod]
    public void RejectsTotalPixelBudgetBeforeAllocatingLayers()
    {
        using var stream = Archive("""{"version":3,"width":8192,"height":8192,"activeLayerIndex":0,"layers":[{"name":"a","entry":"a","id":"a"},{"name":"b","entry":"b","id":"b"},{"name":"c","entry":"c","id":"c"}]}""");
        var error = Assert.Throws<InvalidDataException>(() => ChunPaintProjectArchive.Load(stream));
        StringAssert.Contains(error.Message, "合計画素");
    }

    private static MemoryStream Archive(string manifest)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            using (var entry = zip.CreateEntry("manifest.json").Open()) entry.Write(Encoding.UTF8.GetBytes(manifest));
            using (var entry = zip.CreateEntry("layer").Open()) entry.Write(new byte[4]);
        }
        stream.Position = 0;
        return stream;
    }
}
