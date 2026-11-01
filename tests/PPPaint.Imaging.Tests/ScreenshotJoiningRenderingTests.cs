using System.Collections.Immutable;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using PPPaint.Core;
using Windows.Storage.Streams;

namespace PPPaint.Imaging.Tests;

[TestClass]
public sealed class ScreenshotJoiningRenderingTests
{
    private static ScreenshotState Scene(int width, int height, Rgba32 background, ScreenshotTool effect, bool inverse)
    {
        var bytes = new byte[width * height * 4];
        for (var y = 2; y < height - 2; y++) for (var x = 2; x < width - 2; x++)
        {
            var i = (y * width + x) * 4; var light = ((x / 3 + y / 2) % 2 == 0) != inverse;
            bytes[i] = (byte)(light ? 220 : 25); bytes[i + 1] = (byte)(light ? 45 : 180); bytes[i + 2] = (byte)(light ? 70 : 230); bytes[i + 3] = 255;
        }
        var pattern = ScreenshotDocument.ImageObject(new(width, height, bytes), Vector2.Zero);
        var line = new ScreenshotAnnotation("line", ScreenshotTool.Pen, new(-4, 3, width + 8, height - 6), new(220, 30, 40), Width: 3, Points: [new(-4, 3), new(width + 4, height - 3)]);
        var operation = new ScreenshotAnnotation("effect", effect, new(-5, -4, width + 10, height + 8), Rgba32.Black,
            Width: 5, Points: [new(-4, height / 2), new(width + 4, height / 2)], BlurRadius: 3, MosaicSize: 7);
        return new(ScreenshotFrame.Transparent(width, height), [pattern, line, operation], Background: background);
    }

    private static Vector2 OldOrigin(ScreenshotJoinDirection direction, ScreenshotState source) => direction switch
    {
        ScreenshotJoinDirection.Top => new(0, source.Frame.Height), ScreenshotJoinDirection.Left => new(source.Frame.Width, 0), _ => Vector2.Zero
    };
    private static Vector2 NewOrigin(ScreenshotJoinDirection direction, ScreenshotState destination) => direction switch
    {
        ScreenshotJoinDirection.Bottom => new(0, destination.Frame.Height), ScreenshotJoinDirection.Right => new(destination.Frame.Width, 0), _ => Vector2.Zero
    };

    private static void AssertPane(byte[] expected, byte[] actual, int width, int height, int outputWidth, Vector2 origin, string message)
    {
        for (var y = 0; y < height; y++)
        {
            var offset = (((int)origin.Y + y) * outputWidth + (int)origin.X) * 4;
            CollectionAssert.AreEqual(expected.AsSpan(y * width * 4, width * 4).ToArray(), actual.AsSpan(offset, width * 4).ToArray(), $"{message}/row={y}");
        }
    }

    private static async Task<byte[]> PngPixels(CanvasBitmap image)
    {
        using var png = new InMemoryRandomAccessStream(); await Win2DImageCodec.SaveAsync(image, png, DocumentFileFormat.Png);
        using var decoded = await Win2DImageCodec.LoadAsync(image.Device, png); return decoded.GetPixelBytes();
    }

    [TestMethod]
    [DataRow(ScreenshotTool.Eraser, ScreenshotJoinDirection.Top)]
    [DataRow(ScreenshotTool.Eraser, ScreenshotJoinDirection.Bottom)]
    [DataRow(ScreenshotTool.Eraser, ScreenshotJoinDirection.Left)]
    [DataRow(ScreenshotTool.Eraser, ScreenshotJoinDirection.Right)]
    [DataRow(ScreenshotTool.Blur, ScreenshotJoinDirection.Top)]
    [DataRow(ScreenshotTool.Blur, ScreenshotJoinDirection.Bottom)]
    [DataRow(ScreenshotTool.Blur, ScreenshotJoinDirection.Left)]
    [DataRow(ScreenshotTool.Blur, ScreenshotJoinDirection.Right)]
    [DataRow(ScreenshotTool.Mosaic, ScreenshotJoinDirection.Top)]
    [DataRow(ScreenshotTool.Mosaic, ScreenshotJoinDirection.Bottom)]
    [DataRow(ScreenshotTool.Mosaic, ScreenshotJoinDirection.Left)]
    [DataRow(ScreenshotTool.Mosaic, ScreenshotJoinDirection.Right)]
    public async Task EveryDirectionPreservesOriginalPanePixelsAndAlphaInPng(ScreenshotTool effect, ScreenshotJoinDirection direction)
    {
        using var device = new CanvasDevice();
        foreach (var backgrounds in new[] {
            (Rgba32.White, Rgba32.White), (new Rgba32(25, 65, 130), new Rgba32(180, 110, 45)),
            (Rgba32.Transparent, Rgba32.Transparent), (Rgba32.Transparent, new Rgba32(40, 100, 180, 128)) })
        {
            var destination = Scene(32, 24, backgrounds.Item1, effect, false); var source = Scene(24, 32, backgrounds.Item2, effect, true);
            using var oldImage = ScreenshotRenderer.Render(device, destination); using var newImage = ScreenshotRenderer.Render(device, source);
            var result = ScreenshotJoining.Join(destination, new(source, new(99, -57)), direction);
            using var image = ScreenshotRenderer.Render(device, result.State); var actual = image.GetPixelBytes();
            var width = result.State.Frame.Width; var height = result.State.Frame.Height;
            AssertPane(oldImage.GetPixelBytes(), actual, 32, 24, width, OldOrigin(direction, source), $"existing/{effect}/{direction}/{backgrounds}");
            AssertPane(newImage.GetPixelBytes(), actual, 24, 32, width, NewOrigin(direction, destination), $"incoming/{effect}/{direction}/{backgrounds}");
            var oldBox = new ScreenshotBox(OldOrigin(direction, source).X, OldOrigin(direction, source).Y, 32, 24);
            var newBox = new ScreenshotBox(NewOrigin(direction, destination).X, NewOrigin(direction, destination).Y, 24, 32);
            var empty = new ScreenshotState(ScreenshotFrame.Transparent(width, height), [], Background: destination.Background);
            using var background = ScreenshotRenderer.Render(device, empty); var backgroundPixels = background.GetPixelBytes();
            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                if (!oldBox.Contains(new Vector2(x + .5f, y + .5f)) && !newBox.Contains(new Vector2(x + .5f, y + .5f)))
                    CollectionAssert.AreEqual(backgroundPixels.AsSpan((y * width + x) * 4, 4).ToArray(), actual.AsSpan((y * width + x) * 4, 4).ToArray(), "効果が隣接領域/余白へ漏れました。");
            var pngPixels = await PngPixels(image);
            // PNG stores straight RGB. Compare the joined export with separately
            // exported originals so codec premultiplication rounding is identical.
            AssertPane(await PngPixels(oldImage), pngPixels, 32, 24, width, OldOrigin(direction, source), $"PNG existing/{effect}/{direction}/{backgrounds}");
            AssertPane(await PngPixels(newImage), pngPixels, 24, 32, width, NewOrigin(direction, destination), $"PNG incoming/{effect}/{direction}/{backgrounds}");
            for (var i = 3; i < actual.Length; i += 4) Assert.AreEqual(actual[i], pngPixels[i], "PNGの透明度が変わりました。");
            if (backgrounds.Item1.A == 255 && backgrounds.Item2.A == 255) CollectionAssert.AreEqual(actual, pngPixels);
            Assert.IsTrue(result.State.Annotations.Count(a => a.Tool == effect) >= 2, "元の効果は編集可能なオブジェクトとして保持する必要があります。");
            Assert.AreEqual(3, destination.Annotations.Length); Assert.AreEqual(3, source.Annotations.Length);
        }
    }

    [TestMethod]
    [DataRow(ScreenshotTool.Eraser)]
    [DataRow(ScreenshotTool.Blur)]
    [DataRow(ScreenshotTool.Mosaic)]
    public void OriginalPaneMetadataPreventsTheLegacyBoundaryAndBackgroundChange(ScreenshotTool tool)
    {
        using var device = new CanvasDevice();
        var destination = Scene(32, 24, new(20, 60, 140), tool, false); var source = Scene(24, 32, new(180, 110, 45), tool, true);
        var joined = ScreenshotJoining.Join(destination, new(source, Vector2.Zero), ScreenshotJoinDirection.Right).State;
        using var preserved = ScreenshotRenderer.Render(device, joined);
        var legacy = joined with { Annotations = joined.Annotations.Select(a => a with { SamplingBounds = null, EraserBackground = null }).ToImmutableArray() };
        using var changed = ScreenshotRenderer.Render(device, legacy);
        Assert.IsFalse(preserved.GetPixelBytes().SequenceEqual(changed.GetPixelBytes()), "元Pane境界/消去背景のない従来描画では連結により画素が変わる再現です。");
    }

    [TestMethod]
    public void JoinedEffectsRemainEditableAndUndoRestoresOriginalPixels()
    {
        using var device = new CanvasDevice();
        var destination = Scene(32, 24, Rgba32.White, ScreenshotTool.Blur, false);
        var source = Scene(24, 32, new(120, 80, 40), ScreenshotTool.Mosaic, true);
        var document = new ScreenshotDocument(destination.Frame); document.Apply(destination);
        using var original = ScreenshotRenderer.Render(device, destination);
        var joined = ScreenshotJoining.Join(destination, new(source, Vector2.Zero), ScreenshotJoinDirection.Left); document.Apply(joined.State);
        using var combined = ScreenshotRenderer.Render(device, document.State); var combinedPixels = combined.GetPixelBytes();
        var mosaic = document.State.Annotations.Single(a => a.Tool == ScreenshotTool.Mosaic);
        document.Replace(mosaic with { Bounds = new(2, 2, 12, 12), MosaicSize = 3 });
        using var edited = ScreenshotRenderer.Render(device, document.State); Assert.IsFalse(combinedPixels.SequenceEqual(edited.GetPixelBytes()));
        document.Undo(); using (var undoneEdit = ScreenshotRenderer.Render(device, document.State)) CollectionAssert.AreEqual(combinedPixels, undoneEdit.GetPixelBytes());
        document.Undo(); using (var undoneJoin = ScreenshotRenderer.Render(device, document.State)) CollectionAssert.AreEqual(original.GetPixelBytes(), undoneJoin.GetPixelBytes());
        document.Redo(); using var redone = ScreenshotRenderer.Render(device, document.State); CollectionAssert.AreEqual(combinedPixels, redone.GetPixelBytes());
    }

    [TestMethod]
    public void SamplingBoundsClampInputsAndOutputsEvenWithoutAnObjectClip()
    {
        using var device = new CanvasDevice();
        foreach (var tool in new[] { ScreenshotTool.Blur, ScreenshotTool.Mosaic })
        {
            var first = Scene(24, 24, Rgba32.White, tool, false);
            using var expected = ScreenshotRenderer.Render(device, first);
            var second = new ScreenshotState(ScreenshotFrame.Transparent(24, 24), [new("neighbor", ScreenshotTool.Redact, new(0, 0, 24, 24), new(10, 220, 30))], Background: Rgba32.White);
            var joined = ScreenshotJoining.Join(first, new(second, Vector2.Zero), ScreenshotJoinDirection.Right).State;
            var item = joined.Annotations.Single(a => a.Tool == tool);
            joined = joined with { Annotations = joined.Annotations.Remove(item).Add(item with { ClipBounds = null, Bounds = new(-5, -5, 58, 34) }) };
            using var actual = ScreenshotRenderer.Render(device, joined);
            AssertPane(expected.GetPixelBytes(), actual.GetPixelBytes(), 24, 24, 48, Vector2.Zero, tool.ToString());
            for (var y = 0; y < 24; y++) for (var x = 24; x < 48; x++)
                CollectionAssert.AreEqual(new byte[] { 30, 220, 10, 255 }, actual.GetPixelBytes(x, y, 1, 1), "SamplingBounds外を効果が変更しました。");
        }
    }

    [TestMethod]
    [DataRow(ScreenshotTool.Blur)]
    [DataRow(ScreenshotTool.Mosaic)]
    public void EmptyFractionalOrOutsideSamplingBoundsNeverChangePixelsAfterMoveOrResize(ScreenshotTool tool)
    {
        using var device = new CanvasDevice();
        var scene = Scene(24, 24, Rgba32.White, tool, false);
        scene = scene with { Annotations = scene.Annotations.Where(a => a.Tool != tool).ToImmutableArray() };
        using var original = ScreenshotRenderer.Render(device, scene); var expected = original.GetPixelBytes();
        foreach (var bounds in new ScreenshotBox[] {
            new(.5f, .5f, 0, 0), new(8.5f, 2.5f, 0, 16), new(2.5f, 8.5f, 16, 0),
            new(8.5f, 2.5f, -1, 16), new(2.5f, 8.5f, 16, -1),
            new(-8.5f, -8.5f, 3, 3), new(32.5f, .5f, 4, 8), new(.5f, 32.5f, 8, 4) })
        {
            var effect = new ScreenshotAnnotation("empty", tool, new(-8, -8, 48, 48), Rgba32.Black, BlurRadius: 3, MosaicSize: 7, SamplingBounds: bounds);
            foreach (var operation in new[] { effect, effect.Move(new(.25f, .75f)), effect.Resize(new(-8.5f, -8.5f, 60, 60)) })
            {
                using var result = ScreenshotRenderer.Render(device, scene with { Annotations = scene.Annotations.Add(operation) });
                CollectionAssert.AreEqual(expected, result.GetPixelBytes(), $"空/キャンバス外のサンプル領域が画素を変更しました: {tool}/{bounds}/{operation.SamplingBounds}");
            }
        }
    }
}
