using System.Numerics;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class ScreenshotPastingTests
{
    [TestMethod]
    public void SmallPastesNeverShrinkAnEmptyCanvasAndPreserveItsBackgroundAndNumber()
    {
        foreach (var background in new[] { Rgba32.White, Rgba32.Transparent, new Rgba32(20, 40, 80, 128) })
        {
            var destination = new ScreenshotState(ScreenshotFrame.Transparent(1000, 650), [], 12, background);
            var external = ScreenshotDocument.FromImage(Pattern(20, 10)).State with { Background = Rgba32.Transparent };
            var editable = new ScreenshotState(ScreenshotFrame.Transparent(30, 15), [Shape("source")], 90, Rgba32.White);
            foreach (var source in new[] { external, editable })
            {
                var result = ScreenshotPasting.Overlay(destination, new(source, Vector2.Zero));
                Assert.AreSame(destination.Frame, result.State.Frame);
                Assert.AreEqual(1000, result.State.Frame.Width); Assert.AreEqual(650, result.State.Frame.Height);
                Assert.AreEqual(background, result.State.Background); Assert.AreEqual(12, result.State.NextNumber);
                Assert.IsFalse(result.AddedIds.IsEmpty); Assert.HasCount(result.State.Annotations.Length, result.AddedIds);
            }
        }
    }

    [TestMethod]
    public void OverlayGrowsOnlyTheRequiredAxesIncludingFractionalPasteOrigins()
    {
        var destination = new ScreenshotState(ScreenshotFrame.Transparent(1000, 650), [], Background: Rgba32.White);
        foreach (var (sourceWidth, sourceHeight, origin, width, height) in new[]
        {
            (1200, 20, Vector2.Zero, 1200, 650),
            (20, 800, Vector2.Zero, 1000, 800),
            (1200, 800, Vector2.Zero, 1200, 800),
            (900, 600, new Vector2(200, 200), 1100, 800),
            (900, 600, new Vector2(100.25f, 50.25f), 1001, 651)
        })
        {
            var source = new ScreenshotTransfer(new(ScreenshotFrame.Transparent(sourceWidth, sourceHeight), []), origin);
            var result = ScreenshotPasting.Overlay(destination, source);
            Assert.AreEqual(width, result.State.Frame.Width); Assert.AreEqual(height, result.State.Frame.Height);
            Assert.AreEqual(1000, destination.Frame.Width); Assert.AreEqual(650, destination.Frame.Height);
        }
    }

    [TestMethod]
    public void ExistingRasterPixelsAndObjectsRemainInPlaceWhenCanvasExpands()
    {
        var frame = Pattern(3, 2); var existing = Shape("existing");
        var destination = new ScreenshotState(frame, [existing], 5, new(10, 20, 30));
        var source = new ScreenshotTransfer(new(ScreenshotFrame.Transparent(4, 3), [Shape("added")], 99, Rgba32.Transparent), new(1, 1));
        var result = ScreenshotPasting.Overlay(destination, source);
        Assert.AreEqual(5, result.State.Frame.Width); Assert.AreEqual(4, result.State.Frame.Height);
        for (var y = 0; y < 4; y++) for (var x = 0; x < 5; x++)
        {
            var expected = x < 3 && y < 2 ? frame.Pixels.Slice((y * 3 + x) * 4, 4).ToArray() : new byte[4];
            CollectionAssert.AreEqual(expected, result.State.Frame.Pixels.Slice((y * 5 + x) * 4, 4).ToArray());
        }
        Assert.AreSame(existing, result.State.Annotations[0]); Assert.AreEqual(destination.Background, result.State.Background);
        Assert.AreEqual(5, result.State.NextNumber); Assert.AreSame(frame, destination.Frame);
    }

    [TestMethod]
    public void ClipboardObjectsKeepTheirAttributesOrderAndMasksButReceiveNewIds()
    {
        var arrow = new ScreenshotAnnotation("arrow", ScreenshotTool.Arrow, new(2, 3, 20, 8), new(70, 80, 90, 128), Width: 7,
            Points: [new(2, 3), new(22, 11)], ClipBounds: new(2, 3, 18, 6), Cutouts: [new(5, 5, 2, 2)], SamplingBounds: new(0, 0, 30, 20));
        var image = ScreenshotDocument.ImageObject(Pattern(2, 1), new(7, 8)) with { Id = "image", Rotation = 90, MirrorX = true };
        var source = new ScreenshotTransfer(new(ScreenshotFrame.Transparent(40, 30), [arrow, image], 99, Rgba32.Transparent), new(30, 40));
        var destination = new ScreenshotState(ScreenshotFrame.Transparent(100, 100), [Shape("old")], 3, Rgba32.White);
        var result = ScreenshotPasting.Overlay(destination, source);
        Assert.HasCount(2, result.AddedIds); Assert.HasCount(3, result.State.Annotations);
        for (var i = 0; i < 2; i++)
        {
            var original = source.Scene.Annotations[i]; var added = result.State.Annotations[i + 1]; var moved = original.Move(source.Origin);
            Assert.AreNotEqual(original.Id, added.Id); Assert.AreEqual(added.Id, result.AddedIds[i]);
            Assert.AreEqual(moved, added with { Id = moved.Id, Points = moved.Points, Cutouts = moved.Cutouts });
            CollectionAssert.AreEqual(moved.Points.IsDefault ? Array.Empty<Vector2>() : moved.Points.ToArray(), added.Points.IsDefault ? Array.Empty<Vector2>() : added.Points.ToArray());
            CollectionAssert.AreEqual(moved.Cutouts.IsDefault ? Array.Empty<ScreenshotBox>() : moved.Cutouts.ToArray(), added.Cutouts.IsDefault ? Array.Empty<ScreenshotBox>() : added.Cutouts.ToArray());
        }
        Assert.AreSame(image.Raster, result.State.Annotations[2].Raster);
        Assert.AreEqual(new ScreenshotBox(2, 3, 18, 6), source.Scene.Annotations[0].ClipBounds);
    }

    [TestMethod]
    public void SourceBackgroundBecomesAnOverlayRasterWithoutReplacingDestinationCanvas()
    {
        var destination = new ScreenshotState(ScreenshotFrame.Transparent(4, 3), [], 7, Rgba32.Transparent);
        var source = new ScreenshotTransfer(new(ScreenshotFrame.Transparent(2, 1), [Shape("shape")], 50, new(100, 60, 20, 128)), Vector2.Zero);
        var result = ScreenshotPasting.Overlay(destination, source);
        Assert.AreSame(destination.Frame, result.State.Frame); Assert.AreEqual(Rgba32.Transparent, result.State.Background);
        Assert.HasCount(2, result.State.Annotations); Assert.AreEqual(ScreenshotTool.Image, result.State.Annotations[0].Tool);
        CollectionAssert.AreEqual(new byte[] { 10, 30, 50, 128 }, result.State.Annotations[0].Raster!.Pixels.Slice(0, 4).ToArray());
        Assert.AreEqual(new ScreenshotBox(0, 0, 2, 1), result.State.Annotations[0].Bounds);
        Assert.AreEqual(ScreenshotTool.Rectangle, result.State.Annotations[1].Tool); Assert.AreEqual(7, result.State.NextNumber);
    }

    [TestMethod]
    public void SameSizeEmptyCanvasDoesNotDiscardSourcePixelsOrChangeDestinationSettings()
    {
        var destination = new ScreenshotState(ScreenshotFrame.Transparent(4, 3), [], 8, Rgba32.White);
        var sourceFrame = Pattern(4, 3);
        var source = new ScreenshotTransfer(new(sourceFrame, [Shape("shape")], 80, Rgba32.White), Vector2.Zero);
        var result = ScreenshotPasting.Overlay(destination, source);
        Assert.AreSame(destination.Frame, result.State.Frame); Assert.HasCount(2, result.State.Annotations);
        CollectionAssert.AreEqual(sourceFrame.Pixels.ToArray(), result.State.Annotations[0].Raster!.Pixels.ToArray());
        Assert.AreEqual(8, result.State.NextNumber); Assert.AreEqual(destination.Background, result.State.Background);
        Assert.AreNotEqual("shape", result.State.Annotations[1].Id);
    }

    [TestMethod]
    public void OneUndoRestoresTheWholePasteAndRedoRestoresTheSameObjects()
    {
        var document = new ScreenshotDocument(Pattern(4, 3)); var before = document.State;
        var source = new ScreenshotTransfer(new(Pattern(8, 2), [Shape("one"), Shape("two")], 80, Rgba32.Transparent), new(2, 1));
        var result = ScreenshotPasting.Overlay(before, source); document.Apply(result.State);
        Assert.HasCount(3, result.AddedIds); var after = document.State;
        document.Undo(); Assert.AreSame(before, document.State); Assert.IsFalse(document.CanUndo);
        document.Redo(); Assert.AreSame(after, document.State); Assert.IsFalse(document.CanRedo);
        CollectionAssert.AreEqual(result.AddedIds.ToArray(), document.State.Annotations.Select(a => a.Id).ToArray());
    }

    [TestMethod]
    public void RepeatedPastesHaveDistinctIdsAndCanBeUndoneSeparately()
    {
        var document = new ScreenshotDocument(ScreenshotFrame.Transparent(30, 20));
        var transfer = new ScreenshotTransfer(new(ScreenshotFrame.Transparent(10, 10), [Shape("shape")], Background: Rgba32.Transparent), Vector2.Zero);
        var first = ScreenshotPasting.Overlay(document.State, transfer); document.Apply(first.State);
        var second = ScreenshotPasting.Overlay(document.State, transfer); document.Apply(second.State);
        Assert.AreNotEqual(first.AddedIds[0], second.AddedIds[0]); Assert.HasCount(2, document.State.Annotations);
        document.Undo(); Assert.AreSame(first.State, document.State); Assert.HasCount(1, document.State.Annotations);
    }

    [TestMethod]
    public void PositionOverrideUsesTheRequestedPointAndNegativeOriginsCannotShrinkTheCanvas()
    {
        var destination = new ScreenshotState(ScreenshotFrame.Transparent(10, 8), [], Background: Rgba32.White);
        var transfer = new ScreenshotTransfer(new(ScreenshotFrame.Transparent(4, 3), [Shape("shape")], Background: Rgba32.Transparent), new(100, 100));
        var clipped = ScreenshotPasting.Overlay(destination, transfer, new(-3, -2));
        Assert.AreSame(destination.Frame, clipped.State.Frame); Assert.AreEqual(new ScreenshotBox(-2, -1, 4, 3), clipped.State.Annotations[0].Bounds);
        var moved = ScreenshotPasting.Overlay(destination, transfer, new(9, 7));
        Assert.AreEqual(13, moved.State.Frame.Width); Assert.AreEqual(10, moved.State.Frame.Height);
        Assert.AreEqual(new ScreenshotBox(10, 8, 4, 3), moved.State.Annotations[0].Bounds);
    }

    [TestMethod]
    public void InvalidOriginsAndOversizedResultsDoNotChangeTheDocumentOrClipboard()
    {
        var document = new ScreenshotDocument(Pattern(4, 3)); var before = document.State;
        var source = new ScreenshotState(Pattern(2, 1), [Shape("source")]);
        foreach (var origin in new[] { new Vector2(float.NaN, 0), new(float.PositiveInfinity, 0), new(0, float.NegativeInfinity), new(16384, 0), new(0, 16384), new(10000, 10000), new(float.MaxValue, 0) })
        {
            var rejected = false;
            try { _ = ScreenshotPasting.Overlay(before, new(source, origin)); }
            catch (Exception error) when (error is ArgumentOutOfRangeException or OverflowException) { rejected = true; }
            Assert.IsTrue(rejected, origin.ToString()); Assert.AreSame(before, document.State); Assert.IsFalse(document.CanUndo);
            Assert.AreEqual("source", source.Annotations[0].Id); Assert.AreEqual(2, source.Frame.Width);
        }
    }

    private static ScreenshotAnnotation Shape(string id) => new(id, ScreenshotTool.Rectangle, new(1, 1, 4, 3), Rgba32.Black);
    private static ScreenshotFrame Pattern(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var index = 0; index < width * height; index++) { pixels[index * 4] = (byte)(index % 256); pixels[index * 4 + 1] = 80; pixels[index * 4 + 2] = 120; pixels[index * 4 + 3] = 255; }
        return new(width, height, pixels);
    }
}
