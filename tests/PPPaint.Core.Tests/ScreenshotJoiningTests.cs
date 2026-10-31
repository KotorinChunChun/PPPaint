using System.Collections.Immutable;
using System.Numerics;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class ScreenshotJoiningTests
{
    private static ScreenshotFrame Pattern(int width, int height)
    {
        var bytes = new byte[width * height * 4];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var i = (y * width + x) * 4;
            bytes[i] = (byte)(x + 1); bytes[i + 1] = (byte)(y + 2); bytes[i + 2] = 31; bytes[i + 3] = (byte)((x + y) % 2 == 0 ? 64 : 192);
        }
        return new(width, height, bytes);
    }

    [TestMethod]
    [DataRow(ScreenshotJoinDirection.Top, 6, 4, 3, 2, 6, 6, 0, 2, 0, 0)]
    [DataRow(ScreenshotJoinDirection.Bottom, 6, 4, 3, 2, 6, 6, 0, 0, 0, 4)]
    [DataRow(ScreenshotJoinDirection.Left, 6, 4, 3, 2, 9, 4, 3, 0, 0, 0)]
    [DataRow(ScreenshotJoinDirection.Right, 6, 4, 3, 2, 9, 4, 0, 0, 6, 0)]
    [DataRow(ScreenshotJoinDirection.Top, 3, 2, 6, 4, 6, 6, 0, 4, 0, 0)]
    [DataRow(ScreenshotJoinDirection.Bottom, 3, 2, 6, 4, 6, 6, 0, 0, 0, 2)]
    [DataRow(ScreenshotJoinDirection.Left, 3, 2, 6, 4, 9, 4, 6, 0, 0, 0)]
    [DataRow(ScreenshotJoinDirection.Right, 3, 2, 6, 4, 9, 4, 0, 0, 3, 0)]
    public void EveryDirectionHandlesBothSizeOrdersWithoutResamplingOrUsingCopyOrigin(
        ScreenshotJoinDirection direction, int oldWidth, int oldHeight, int sourceWidth, int sourceHeight,
        int width, int height, int oldX, int oldY, int sourceX, int sourceY)
    {
        var original = Pattern(oldWidth, oldHeight); var incoming = Pattern(sourceWidth, sourceHeight);
        var destination = new ScreenshotState(original, [], 12, new(12, 34, 56, 128));
        var sourceScene = new ScreenshotState(incoming, [], 57, Rgba32.Transparent);
        var source = new ScreenshotTransfer(sourceScene, new(1234, -999));
        var originalBytes = original.Pixels.ToArray(); var incomingBytes = incoming.Pixels.ToArray();
        var result = ScreenshotJoining.Join(destination, source, direction);
        Assert.AreEqual(width, result.State.Frame.Width); Assert.AreEqual(height, result.State.Frame.Height);
        Assert.AreEqual(destination.Background, result.State.Background); Assert.AreEqual(12, result.State.NextNumber);
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var actual = result.State.Frame.Pixels.Slice((y * width + x) * 4, 4).ToArray();
            var expected = x >= oldX && x < oldX + oldWidth && y >= oldY && y < oldY + oldHeight
                ? original.Pixels.Slice(((y - oldY) * oldWidth + x - oldX) * 4, 4).ToArray() : new byte[4];
            CollectionAssert.AreEqual(expected, actual);
        }
        Assert.HasCount(1, result.State.Annotations); Assert.HasCount(1, result.AddedIds);
        var pasted = result.State.Annotations[0];
        Assert.AreEqual(ScreenshotTool.Image, pasted.Tool); Assert.AreSame(incoming, pasted.Raster);
        Assert.AreEqual(new ScreenshotBox(sourceX, sourceY, sourceWidth, sourceHeight), pasted.Bounds);
        Assert.AreEqual(pasted.Bounds, pasted.ClipBounds); Assert.AreEqual(pasted.Id, result.AddedIds[0]);
        Assert.AreSame(original, destination.Frame); Assert.AreSame(sourceScene, source.Scene);
        CollectionAssert.AreEqual(originalBytes, original.Pixels.ToArray()); CollectionAssert.AreEqual(incomingBytes, incoming.Pixels.ToArray());
    }

    [TestMethod]
    public void EditableObjectsKeepOrderAttributesAndRasterWhileAllGeometryMovesTogether()
    {
        var raster = Pattern(2, 2);
        ScreenshotAnnotation[] oldObjects =
        [
            new("old-arrow", ScreenshotTool.Arrow, new(-3, -2, 12, 8), new(20, 40, 60), Width: 7, FontSize: 32, Text: "arrow", Points: [new(-3, -2), new(9, 6)], ClipBounds: new(-1, -1, 12, 8), Cutouts: [new(2, 3, 1, 2)]),
            new("old-callout", ScreenshotTool.Callout, new(1, 1, 3, 3), Rgba32.Black, CalloutTip: new(20, -5), ClipBounds: new(1, 1, 3, 3)),
            new("old-hidden", ScreenshotTool.Rectangle, new(30, 40, 5, 5), Rgba32.Black, ClipBounds: new(30, 40, 5, 5))
        ];
        ScreenshotAnnotation[] newObjects =
        [
            new("source-arrow", ScreenshotTool.Arrow, new(-5, 1, 12, 5), new(90, 80, 70), Width: 5, Points: [new(-5, 1), new(7, 6)], ClipBounds: new(-1, -2, 4, 4), Cutouts: [new(1, 1, 1, 1)]),
            new("source-callout", ScreenshotTool.Callout, new(0, 0, 3, 2), Rgba32.Black, Text: "callout"),
            new("source-image", ScreenshotTool.Image, new(1, 1, 2, 2), Rgba32.White, Raster: raster, Rotation: 90, MirrorX: true, MirrorY: true, BlurRadius: 17, MosaicSize: 29, Opacity: .6f)
        ];
        var destination = new ScreenshotState(Pattern(8, 6), oldObjects.ToImmutableArray(), 9, Rgba32.White);
        var source = new ScreenshotTransfer(new(ScreenshotFrame.Transparent(4, 3), newObjects.ToImmutableArray(), 40, Rgba32.Transparent), new(71, 92));
        foreach (var direction in Enum.GetValues<ScreenshotJoinDirection>())
        {
            var oldOffset = direction switch { ScreenshotJoinDirection.Top => new Vector2(0, 3), ScreenshotJoinDirection.Left => new Vector2(4, 0), _ => Vector2.Zero };
            var newOffset = direction switch { ScreenshotJoinDirection.Bottom => new Vector2(0, 6), ScreenshotJoinDirection.Right => new Vector2(8, 0), _ => Vector2.Zero };
            var joined = ScreenshotJoining.Join(destination, source, direction); var objects = joined.State.Annotations;
            CollectionAssert.AreEqual(oldObjects.Select(a => a.Id).ToArray(), objects.Take(3).Select(a => a.Id).ToArray());
            CollectionAssert.AreEqual(joined.AddedIds.ToArray(), objects.Skip(3).Select(a => a.Id).ToArray());
            Assert.AreEqual(6, objects.Select(a => a.Id).Distinct().Count());
            for (var i = 0; i < 3; i++)
            {
                AssertObjectExceptClipAndId(oldOffset == Vector2.Zero ? oldObjects[i] : oldObjects[i].Move(oldOffset), objects[i]);
                AssertObjectExceptClipAndId(newObjects[i].Move(newOffset), objects[i + 3]);
                Assert.AreNotEqual(newObjects[i].Id, objects[i + 3].Id);
            }
            Assert.AreEqual(new ScreenshotBox(oldOffset.X, oldOffset.Y, 8, 6), objects[0].ClipBounds);
            Assert.AreEqual(new ScreenshotBox(1 + oldOffset.X, 1 + oldOffset.Y, 3, 3), objects[1].ClipBounds);
            Assert.AreEqual(new ScreenshotBox(oldOffset.X, oldOffset.Y, 0, 0), objects[2].ClipBounds);
            Assert.AreEqual(new ScreenshotBox(newOffset.X, newOffset.Y, 3, 2), objects[3].ClipBounds);
            Assert.AreEqual(new ScreenshotBox(newOffset.X, newOffset.Y, 4, 3), objects[4].ClipBounds);
            Assert.AreSame(raster, objects[5].Raster);
        }
        Assert.AreEqual(new ScreenshotBox(-1, -1, 12, 8), destination.Annotations[0].ClipBounds);
        Assert.AreEqual(new ScreenshotBox(-1, -2, 4, 4), source.Scene.Annotations[0].ClipBounds);
        Assert.IsNull(source.Scene.Annotations[1].CalloutTip);
    }

    private static void AssertObjectExceptClipAndId(ScreenshotAnnotation expected, ScreenshotAnnotation actual)
    {
        CollectionAssert.AreEqual(expected.Points.IsDefault ? Array.Empty<Vector2>() : expected.Points.ToArray(), actual.Points.IsDefault ? Array.Empty<Vector2>() : actual.Points.ToArray());
        CollectionAssert.AreEqual(expected.Cutouts.IsDefault ? Array.Empty<ScreenshotBox>() : expected.Cutouts.ToArray(), actual.Cutouts.IsDefault ? Array.Empty<ScreenshotBox>() : actual.Cutouts.ToArray());
        Assert.AreEqual(expected, actual with { Id = expected.Id, Points = expected.Points, Cutouts = expected.Cutouts, ClipBounds = expected.ClipBounds });
    }

    [TestMethod]
    public void EveryPaneClipsEffectsAndHiddenObjectPartsWithoutDroppingIds()
    {
        var tools = new[] { ScreenshotTool.Pen, ScreenshotTool.Eraser, ScreenshotTool.Blur, ScreenshotTool.Mosaic };
        var objects = tools.Select((tool, i) => new ScreenshotAnnotation("effect-" + i, tool, new(-4, -3, 12, 10), Rgba32.Black, Points: [new(-4, -3), new(8, 7)])).ToImmutableArray();
        var scene = new ScreenshotState(ScreenshotFrame.Transparent(4, 3), objects, Background: Rgba32.Transparent);
        var joined = ScreenshotJoining.Join(scene, new(scene, Vector2.Zero), ScreenshotJoinDirection.Right);
        Assert.HasCount(8, joined.State.Annotations);
        for (var i = 0; i < 4; i++)
        {
            var original = joined.State.Annotations[i]; var added = joined.State.Annotations[i + 4];
            Assert.AreEqual(new ScreenshotBox(0, 0, 4, 3), original.ClipBounds);
            Assert.AreEqual(new ScreenshotBox(4, 0, 4, 3), added.ClipBounds);
            Assert.IsFalse(original.VisibleAt(new(4.5f, 1))); Assert.IsFalse(added.VisibleAt(new(3.5f, 1)));
            Assert.IsFalse(original.VisibleAt(new(1, 3.5f))); Assert.IsFalse(added.VisibleAt(new(5, 3.5f)));
        }
    }

    [TestMethod]
    public void SamplingLimitsAndEraserColorsStayWithTheirOriginalPane()
    {
        var destination = new ScreenshotState(ScreenshotFrame.Transparent(4, 3),
        [
            new("old-blur", ScreenshotTool.Blur, new(-1, -1, 8, 7), Rgba32.Black, SamplingBounds: new(1, -1, 3, 4)),
            new("old-mosaic", ScreenshotTool.Mosaic, new(-1, -1, 8, 7), Rgba32.Black),
            new("old-eraser", ScreenshotTool.Eraser, new(0, 0, 4, 3), Rgba32.Black, Points: [new(1, 1)])
        ], 7, new(10, 20, 30));
        var originalEraseColor = new Rgba32(90, 80, 70, 128);
        var incoming = new ScreenshotState(ScreenshotFrame.Transparent(3, 2),
        [
            new("new-blur", ScreenshotTool.Blur, new(-1, -1, 8, 7), Rgba32.Black, SamplingBounds: new(-1, 1, 3, 5)),
            new("new-mosaic", ScreenshotTool.Mosaic, new(-1, -1, 8, 7), Rgba32.Black),
            new("new-eraser", ScreenshotTool.Eraser, new(0, 0, 3, 2), Rgba32.Black, Points: [new(1, 1)]),
            new("new-override-eraser", ScreenshotTool.Eraser, new(0, 0, 3, 2), Rgba32.Black, Points: [new(1, 1)], EraserBackground: originalEraseColor)
        ], 80, new(200, 100, 50));
        foreach (var direction in Enum.GetValues<ScreenshotJoinDirection>())
        {
            var oldOffset = direction switch { ScreenshotJoinDirection.Top => new Vector2(0, 2), ScreenshotJoinDirection.Left => new Vector2(3, 0), _ => Vector2.Zero };
            var newOffset = direction switch { ScreenshotJoinDirection.Bottom => new Vector2(0, 3), ScreenshotJoinDirection.Right => new Vector2(4, 0), _ => Vector2.Zero };
            var joined = ScreenshotJoining.Join(destination, new(incoming, new(99, 99)), direction);
            var objects = joined.State.Annotations;
            Assert.AreEqual(new ScreenshotBox(1 + oldOffset.X, oldOffset.Y, 3, 3), objects[0].SamplingBounds);
            Assert.AreEqual(new ScreenshotBox(oldOffset.X, oldOffset.Y, 4, 3), objects[1].SamplingBounds);
            Assert.IsNull(objects[2].EraserBackground);
            Assert.AreEqual(ScreenshotTool.Image, objects[3].Tool, "The incoming background is beneath its editable objects.");
            Assert.AreEqual(new ScreenshotBox(newOffset.X, 1 + newOffset.Y, 2, 1), objects[4].SamplingBounds);
            Assert.AreEqual(new ScreenshotBox(newOffset.X, newOffset.Y, 3, 2), objects[5].SamplingBounds);
            Assert.AreEqual(incoming.Background, objects[6].EraserBackground);
            Assert.AreEqual(originalEraseColor, objects[7].EraserBackground);
        }
        Assert.IsNull(destination.Annotations[1].SamplingBounds); Assert.IsNull(incoming.Annotations[1].SamplingBounds);
        Assert.IsNull(incoming.Annotations[2].EraserBackground);
    }

    [TestMethod]
    public void SourceBackgroundIsPreservedAsPremultipliedRasterAndUnusedPaddingStaysTransparent()
    {
        foreach (var background in new[] { Rgba32.White, Rgba32.Transparent, new Rgba32(10, 30, 50, 128) })
        {
            var original = new ScreenshotState(ScreenshotFrame.Transparent(2, 4), [], 5, background);
            var incoming = new ScreenshotState(ScreenshotFrame.Transparent(4, 2), [], 30, new(150, 100, 50, 128));
            var joined = ScreenshotJoining.Join(original, new(incoming, Vector2.Zero), ScreenshotJoinDirection.Bottom);
            Assert.AreEqual(background, joined.State.Background); Assert.AreEqual(5, joined.State.NextNumber);
            Assert.IsTrue(joined.State.Frame.Pixels.Span.IndexOfAnyExcept((byte)0) < 0);
            var raster = joined.State.Annotations[0].Raster!;
            CollectionAssert.AreEqual(new byte[] { 25, 50, 75, 128 }, raster.Pixels.Slice(0, 4).ToArray());
            Assert.AreEqual(new ScreenshotBox(0, 4, 4, 2), joined.State.Annotations[0].ClipBounds);
            Assert.AreEqual((byte)0, joined.State.Frame.Pixels.Span[(1 * 4 + 3) * 4 + 3], "The gap follows the destination background instead of baking white into the frame.");
        }
    }

    [TestMethod]
    public void EmptyTransparentSourceStillResizesAndRepeatedJoinsDoNotWidenEarlierClips()
    {
        var original = new ScreenshotState(Pattern(3, 2), [new("old", ScreenshotTool.Rectangle, new(-1, -1, 8, 8), Rgba32.Black)]);
        var empty = new ScreenshotTransfer(new(ScreenshotFrame.Transparent(2, 4), [], Background: Rgba32.Transparent), new(99, 99));
        var first = ScreenshotJoining.Join(original, empty, ScreenshotJoinDirection.Left);
        Assert.IsEmpty(first.AddedIds); Assert.HasCount(1, first.State.Annotations);
        Assert.AreEqual(new ScreenshotBox(2, 0, 3, 2), first.State.Annotations[0].ClipBounds);
        var second = ScreenshotJoining.Join(first.State, empty, ScreenshotJoinDirection.Top);
        Assert.IsEmpty(second.AddedIds); Assert.AreEqual("old", second.State.Annotations[0].Id);
        Assert.AreEqual(new ScreenshotBox(2, 4, 3, 2), second.State.Annotations[0].ClipBounds);
        Assert.AreEqual(new ScreenshotBox(-1, -1, 8, 8), original.Annotations[0].Bounds);
    }

    [TestMethod]
    public void JoiningIsPureUntilOneApplyAndUndoRedoRestoresTheWholeEdit()
    {
        var document = new ScreenshotDocument(Pattern(5, 3)); document.SetBackground(new(12, 34, 56));
        document.Add(new("old", ScreenshotTool.Number, new(1, 1, 2, 2), Rgba32.Black, Text: "6"));
        var before = document.State;
        var incoming = new ScreenshotTransfer(new(Pattern(2, 4), [new("new", ScreenshotTool.Rectangle, new(0, 0, 2, 4), Rgba32.Black)], 80, Rgba32.White), Vector2.Zero);
        var result = ScreenshotJoining.Join(before, incoming, ScreenshotJoinDirection.Top);
        Assert.AreSame(before, document.State); Assert.AreEqual(before.NextNumber, result.State.NextNumber);
        document.Apply(result.State); Assert.AreSame(result.State, document.State);
        document.Undo(); Assert.AreSame(before, document.State);
        document.Redo(); Assert.AreSame(result.State, document.State);
        Assert.AreEqual("old", document.State.Annotations[0].Id); Assert.AreEqual(2, result.AddedIds.Length);
    }

    [TestMethod]
    public void InvalidDirectionsAndDimensionOrByteLimitsLeaveBothInputsUntouched()
    {
        var small = new ScreenshotState(ScreenshotFrame.Transparent(1, 1), []);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ScreenshotJoining.Join(small, new(small, Vector2.Zero), (ScreenshotJoinDirection)123));
        foreach (var (destination, source, direction) in new[]
        {
            (new ScreenshotState(ScreenshotFrame.Transparent(16384, 1), []), small, ScreenshotJoinDirection.Right),
            (new ScreenshotState(ScreenshotFrame.Transparent(1, 16384), []), small, ScreenshotJoinDirection.Bottom),
            (new ScreenshotState(ScreenshotFrame.Transparent(8192, 1), []), new ScreenshotState(ScreenshotFrame.Transparent(1, 8192), []), ScreenshotJoinDirection.Right),
            (new ScreenshotState(ScreenshotFrame.Transparent(1, 8192), []), new ScreenshotState(ScreenshotFrame.Transparent(8192, 1), []), ScreenshotJoinDirection.Bottom)
        })
        {
            var oldFrame = destination.Frame; var sourceFrame = source.Frame; var sourceTransfer = new ScreenshotTransfer(source, Vector2.Zero);
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ScreenshotJoining.Join(destination, sourceTransfer, direction));
            Assert.AreSame(oldFrame, destination.Frame); Assert.AreSame(sourceFrame, sourceTransfer.Scene.Frame);
            Assert.IsEmpty(destination.Annotations); Assert.IsEmpty(sourceTransfer.Scene.Annotations);
        }
    }
}
