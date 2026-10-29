using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using PPPaint.Core;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.Storage.Streams;
using Windows.UI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class BrushComparison
{
    [TestMethod]
    public async Task GenerateNineBrushFixtures()
    {
        var output = Environment.GetEnvironmentVariable("PPPAINT_BRUSH_OUTPUT")!;
        var reference = Environment.GetEnvironmentVariable("PPPAINT_BRUSH_REFERENCE");
        using var device = new CanvasDevice(true);
        var reports = new List<object>();
        foreach (var kind in Enum.GetValues<BrushKind>())
        {
            using var first = Render(device, kind);
            using var second = Render(device, kind);
            var actual = first.GetPixelBytes();
            CollectionAssert.AreEqual(actual, second.GetPixelBytes(), $"{kind}: 同条件2回描画が不一致");
            await SavePng(first, Path.Combine(output, kind + ".png"));
            var referencePath = string.IsNullOrWhiteSpace(reference) ? null : Path.Combine(reference, kind + ".png");
            object comparison = new { Status = "reference-not-provided" };
            if (referencePath is not null && File.Exists(referencePath))
            {
                using var file = File.OpenRead(referencePath);
                using var random = file.AsRandomAccessStream();
                using var expectedImage = await CanvasBitmap.LoadAsync(device, random);
                if (expectedImage.SizeInPixels.Width != 512 || expectedImage.SizeInPixels.Height != 160)
                    throw new InvalidDataException($"{kind}: 参照画像は512×160が必要です。自動拡大縮小はしません。");
                var expected = expectedImage.GetPixelBytes();
                var difference = new byte[actual.Length];
                long total = 0; int maximum = 0; int changedPixels = 0;
                for (var index = 0; index < actual.Length; index += 4)
                {
                    var changed = false;
                    for (var channel = 0; channel < 4; channel++)
                    {
                        var delta = Math.Abs(actual[index + channel] - expected[index + channel]);
                        total += delta; maximum = Math.Max(maximum, delta); changed |= delta != 0;
                        if (channel < 3) difference[index + channel] = (byte)delta;
                    }
                    difference[index + 3] = 255;
                    if (changed) changedPixels++;
                }
                using var diff = new CanvasRenderTarget(device, 512, 160, 96);
                diff.SetPixelBytes(difference);
                await SavePng(diff, Path.Combine(output, "difference-" + kind + ".png"));
                comparison = new { Status = "measured-no-acceptance-threshold", ChangedPixels = changedPixels, MaximumChannelDifference = maximum,
                    MeanAbsoluteChannelDifference = total / (double)actual.Length, ReferenceSha256 = Convert.ToHexString(SHA256.HashData(expected)) };
            }
            reports.Add(new { Brush = kind.ToString(), PixelSha256 = Convert.ToHexString(SHA256.HashData(actual)), DeterministicWithinRun = true, Comparison = comparison });
        }
        File.WriteAllText(Path.Combine(output, "comparison.json"), JsonSerializer.Serialize(new
        {
            Width = 512, Height = 160, Dpi = 96, BaseWidth = 4, Color = "#FF2864B4", Background = "#FFFFFFFF",
            Pressure = "disabled", Smoothing = "disabled", Renderer = "Win2D software / application Draw methods extracted verbatim",
            Points = MakePoints(), Results = reports
        }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
    }

    private static Vector2[] MakePoints() => Enumerable.Range(0, 61)
        .Select(index => new Vector2(24 + index * 7.6f, 80 + 35 * MathF.Sin(index * MathF.Tau / 30))).ToArray();

    private static CanvasRenderTarget Render(CanvasDevice device, BrushKind kind)
    {
        var target = new CanvasRenderTarget(device, 512, 160, 96);
        using var session = target.CreateDrawingSession();
        session.Clear(Color.FromArgb(255, 255, 255, 255));
        var points = MakePoints();
        for (var index = 1; index < points.Length; index++)
            Draw(session, kind, points[index - 1], points[index], Color.FromArgb(255, 40, 100, 180), 4);
        return target;
    }

    private static async Task SavePng(CanvasBitmap image, string path)
    {
        using var memory = new InMemoryRandomAccessStream();
        await image.SaveAsync(memory, CanvasBitmapFileFormat.Png);
        memory.Seek(0);
        using var file = File.Create(path);
        await memory.AsStreamForRead().CopyToAsync(file);
    }

    private static void Draw(CanvasDrawingSession session, BrushKind kind, Vector2 from, Vector2 to, Color color, float width)
    {
/*DRAW_SWITCH*/
    }
/*DRAW_METHODS*/
}
