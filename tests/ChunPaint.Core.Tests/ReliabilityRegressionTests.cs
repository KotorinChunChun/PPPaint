using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using ChunPaint.Core;

namespace ChunPaint.Core.Tests;

[TestClass]
public sealed class ReliabilityRegressionTests
{
    [TestMethod]
    public void PixelSurface_RejectsOversizedAllocationBeforeCreatingArray()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new PixelSurface(8193, 8192, Rgba32.Transparent));
    }

    [TestMethod]
    public void SelectionMask_RejectsOversizedAllocationBeforeCreatingArray()
    {
        var region = SelectionRegion.CreateRectangle(new PixelRect(0, 0, 1, 1));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            SelectionOperations.CreateMask(region, 8193, 8192));
    }

    [TestMethod]
    public void DocumentFileFormats_RejectsEmptyAndNegativeInputs()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => DocumentFileFormats.ValidateInputSize(0));
        Assert.ThrowsExactly<InvalidDataException>(() => DocumentFileFormats.ValidateInputSize(-1));
        Assert.IsFalse(DocumentFileFormats.TryGetFormat(null!, out _));
    }

    [TestMethod]
    public void SelectionAndEditableObjects_RejectNonFiniteValues()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            SelectionRegion.CreatePolygon([
                new System.Numerics.Vector2(0, 0),
                new System.Numerics.Vector2(float.NaN, 1),
                new System.Numerics.Vector2(2, 2)
            ]));

        var region = SelectionRegion.CreateRectangle(new PixelRect(0, 0, 4, 4));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            region.MoveBy(new System.Numerics.Vector2(float.NaN, 0), new PixelRect(0, 0, 8, 8)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            region.Resize(SelectionHandle.Right, new System.Numerics.Vector2(float.PositiveInfinity, 0), new PixelRect(0, 0, 8, 8)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => region.Rotate(float.NegativeInfinity, new PixelRect(0, 0, 8, 8)));

        var invalidBounds = EditableObject.CreateText(
            new ObjectBounds(float.NaN, 0, 1, 1),
            "text",
            id: "invalid-bounds");
        Assert.ThrowsExactly<ArgumentException>(() => invalidBounds.Validate());

        var invalidStyle = EditableObject.CreateText(
            new ObjectBounds(0, 0, 1, 1),
            "text",
            new TextObjectStyle(FontSize: float.PositiveInfinity),
            "invalid-style");
        Assert.ThrowsExactly<ArgumentException>(() => invalidStyle.Validate());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            invalidBounds.MoveBy(new System.Numerics.Vector2(float.NaN, 0), new ObjectBounds(0, 0, 4, 4)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => invalidStyle.RotateBy(float.NaN));
    }

    [TestMethod]
    public void ProjectArchive_ConvertsMalformedJsonToSafeDataError()
    {
        using var stream = CreateArchive("{not valid json");

        Assert.ThrowsExactly<InvalidDataException>(() => ChunPaintProjectArchive.Load(stream));
    }

    [TestMethod]
    public void ProjectArchive_RejectsCorruptZipWithoutAllocatingProject()
    {
        using var stream = new MemoryStream("not a zip archive"u8.ToArray());

        Assert.ThrowsExactly<InvalidDataException>(() => ChunPaintProjectArchive.Load(stream));
    }

    [TestMethod]
    public void ProjectArchive_RejectsIncorrectLayerLength()
    {
        using var stream = CreateArchive(
            "{\"version\":1,\"width\":2,\"height\":2,\"activeLayerIndex\":0,\"layers\":[{\"name\":\"背景\",\"isVisible\":true,\"entry\":\"layers/0000.bgra\"}]}",
            ("layers/0000.bgra", new byte[3]));

        Assert.ThrowsExactly<InvalidDataException>(() => ChunPaintProjectArchive.Load(stream));
    }

    [TestMethod]
    public void ProjectArchive_RejectsReadOnlyDestination()
    {
        var project = new ChunPaintProject(
            1,
            1,
            0,
            [new ChunPaintProjectLayer("背景", true, new byte[4])]);
        using var stream = new MemoryStream(Array.Empty<byte>(), writable: false);

        Assert.ThrowsExactly<ArgumentException>(() => ChunPaintProjectArchive.Save(stream, project));
    }

    [TestMethod]
    public void LongMixedHistory_RestoresPixelLayerAndObjectState()
    {
        var history = new History<ChunPaintProject>(capacity: 96);
        var first = CreateProject("背景", 1, withObject: false);
        history.Capture(first);
        var last = first;
        for (var index = 1; index <= 48; index++)
        {
            last = CreateProject($"レイヤー{index}", (byte)index, withObject: index % 2 == 0);
            history.Capture(last);
        }

        var undoCount = 0;
        var current = CreateProject("現在", 255, withObject: true);
        while (history.TryUndo(current, out var restored))
        {
            current = restored;
            undoCount++;
        }

        Assert.AreEqual(49, undoCount);
        Assert.AreEqual("背景", current.Layers[0].Name);
        Assert.AreEqual((byte)1, current.Layers[0].Pixels[0]);
        Assert.IsEmpty(current.Objects!);

        var redoCount = 0;
        while (history.TryRedo(current, out var redone))
        {
            current = redone;
            redoCount++;
        }

        Assert.AreEqual(49, redoCount);
        Assert.AreEqual("現在", current.Layers[0].Name);
        Assert.AreEqual(1, current.Objects?.Count);
    }

    [TestMethod]
    [DataRow(32, 32)]
    [DataRow(256, 128)]
    [DataRow(1024, 512)]
    public void CanvasSizesAndLayerMix_PreserveSelectionAndComposite(int width, int height)
    {
        var bottom = new PixelSurface(width, height, new Rgba32(20, 30, 40, 255));
        var top = new PixelSurface(width, height, new Rgba32(200, 210, 220, 128));
        var hidden = new PixelSurface(width, height, new Rgba32(255, 0, 0, 255));
        var region = SelectionRegion.CreateRectangle(new PixelRect(width / 4, height / 4, width / 2, height / 2));

        var composite = SelectionOperations.CompositeVisibleLayers([
            (bottom, true),
            (top, true),
            (hidden, false)
        ]);
        var selected = SelectionOperations.Extract(composite, region);
        SelectionOperations.InvertColors(selected, SelectionRegion.CreateRectangle(new PixelRect(0, 0, selected.Width, selected.Height)));

        Assert.AreEqual(width / 2, selected.Width);
        Assert.AreEqual(height / 2, selected.Height);
        Assert.AreNotEqual(hidden[0, 0], composite[0, 0]);
        Assert.AreEqual((byte)255, selected[0, 0].A);
    }

    [TestMethod]
    public void TransformAndBackgroundRemoval_StayWithinLightweightBudget()
    {
        const int width = 512;
        const int height = 512;
        var source = new PixelSurface(width, height, new Rgba32(245, 245, 245, 255));
        source[width / 2, height / 2] = new Rgba32(20, 30, 40, 255);

        _ = ImageTransforms.RotateNearestNeighbor(source, 7);
        _ = BackgroundRemovalOperations.RemoveFromEdges(source, tolerance: 8);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var rotated = ImageTransforms.RotateNearestNeighbor(source, 7);
        var removed = BackgroundRemovalOperations.RemoveFromEdges(source, tolerance: 8);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.IsTrue(rotated.Width >= width);
        Assert.IsTrue(rotated.Height >= height);
        Assert.AreEqual(height, removed.Height);
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Core transform budget exceeded: {stopwatch.Elapsed}.");
        Assert.IsTrue(allocatedBytes < 64L * 1024 * 1024, $"Core allocation budget exceeded: {allocatedBytes:N0} bytes.");
    }

    [TestMethod]
    public void SupportedImageHeaders_AreCheckedAsSmallRealFileInputs()
    {
        var samples = new (DocumentFileFormat Format, byte[] Header)[]
        {
            (DocumentFileFormat.Png, [137, 80, 78, 71, 13, 10, 26, 10]),
            (DocumentFileFormat.Jpeg, [0xFF, 0xD8, 0xFF, 0xE0]),
            (DocumentFileFormat.Bmp, [(byte)'B', (byte)'M', 0, 0]),
            (DocumentFileFormat.Gif, "GIF89a"u8.ToArray())
        };

        foreach (var sample in samples)
        {
            using var stream = new MemoryStream(sample.Header);
            DocumentFileFormats.ValidateInputSize(stream.Length);
            Assert.IsTrue(DocumentFileFormats.HasExpectedImageSignature(sample.Format, sample.Header));
        }
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

    private static ChunPaintProject CreateProject(string name, byte pixel, bool withObject)
    {
        return new ChunPaintProject(
            1,
            1,
            0,
            [new ChunPaintProjectLayer(name, true, [pixel, 0, 0, 255])],
            withObject
                ? [EditableObject.CreateText(new ObjectBounds(0, 0, 1, 1), name, id: name)]
                : []);
    }
}
