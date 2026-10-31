namespace PPPaint.Core.Tests;

[TestClass]
public sealed class ScreenshotStyleCaptureTests
{
    private static readonly Rgba32 Red = new(220, 38, 38);
    private static readonly Rgba32 Blue = new(37, 99, 235);
    private static ScreenshotAnnotation Shape(ScreenshotTool tool = ScreenshotTool.Rectangle) =>
        new("selected-shape", tool, new(2, 3, 20, 30), Blue, Width: 11, FontSize: 48,
            Text: "sample", Rotation: 90, MirrorX: true, BlurRadius: 19, MosaicSize: 27, Opacity: .6f,
            ClipBounds: new(3, 4, 10, 12), Cutouts: [new(5, 6, 2, 3)]);

    private static ScreenshotToolPreferences Defaults()
    {
        var defaults = new ScreenshotToolPreferences { Color = "#DC2626", Palette = ["#DC2626", "#FFFFFF"] };
        defaults.Set(ScreenshotTool.Pen, ScreenshotStyleProperty.Width, 1);
        defaults.Set(ScreenshotTool.Rectangle, ScreenshotStyleProperty.Width, 5);
        return defaults;
    }

    [TestMethod]
    public void SelectionAndSelectedEditsOnlyChangeDisplayAndObjectUntilExplicitCapture()
    {
        var defaults = Defaults(); var savedDefaults = defaults.Serialize();
        var document = new ScreenshotDocument(ScreenshotFrame.Transparent(100, 80));
        document.Add(Shape());
        Assert.AreEqual(Blue, ScreenshotStyleCapture.DisplayColor(Red, document.State.Annotations));
        var selected = document.State.Annotations[0];
        document.Replace(ScreenshotStyleEditing.Apply(selected, ScreenshotStyleProperty.Width, 17) with { Color = Rgba32.Black });
        var capture = ScreenshotStyleCapture.Read(document.State.Annotations)!;
        Assert.AreEqual(savedDefaults, defaults.Serialize(), "選択・図形編集・読取だけでは新規作図設定を更新しません。");
        Assert.AreEqual(Red, ScreenshotStyleCapture.DisplayColor(Red, []));
        Assert.AreEqual(1d, defaults.Get(ScreenshotTool.Pen).Width);
        Assert.AreEqual(5d, defaults.Get(ScreenshotTool.Rectangle).Width);
        capture.ApplyTo(defaults);
        Assert.AreEqual(17d, defaults.Get(ScreenshotTool.Rectangle).Width);
        Assert.AreEqual("#000000", defaults.Color);
        Assert.AreEqual(1d, defaults.Get(ScreenshotTool.Pen).Width);
    }

    [TestMethod]
    public void ExplicitCaptureChangesOnlySupportedDefaultsAndPreservesDocumentAndUndoRedo()
    {
        var defaults = Defaults();
        defaults.Set(ScreenshotTool.Callout, ScreenshotStyleProperty.Opacity, 73);
        defaults.Set(ScreenshotTool.Text, ScreenshotStyleProperty.FontSize, 35);
        var document = new ScreenshotDocument(ScreenshotFrame.Transparent(100, 80));
        document.Add(Shape(ScreenshotTool.Callout)); var original = document.State;
        document.Replace(original.Annotations[0] with { Text = "later" }); var later = document.State;
        document.Undo(); Assert.IsTrue(document.CanRedo);
        var capture = ScreenshotStyleCapture.Read(document.State.Annotations)!;
        Assert.AreEqual(ScreenshotTool.Callout, capture.Tool);
        CollectionAssert.AreEquivalent(new[] { ScreenshotStyleProperty.Width, ScreenshotStyleProperty.FontSize }, capture.Numbers.Keys.ToArray());
        capture.ApplyTo(defaults);
        Assert.AreSame(original, document.State); Assert.IsTrue(document.CanUndo); Assert.IsTrue(document.CanRedo);
        Assert.AreEqual("selected-shape", document.State.Annotations[0].Id);
        Assert.AreEqual(11d, defaults.Get(ScreenshotTool.Callout).Width);
        Assert.AreEqual(48d, defaults.Get(ScreenshotTool.Callout).FontSize);
        Assert.AreEqual(73d, defaults.Get(ScreenshotTool.Callout).Opacity);
        Assert.AreEqual(35d, defaults.Get(ScreenshotTool.Text).FontSize);
        Assert.AreEqual(5d, defaults.Get(ScreenshotTool.Rectangle).Width);
        Assert.AreEqual("#2563EB", defaults.Color);
        document.Redo(); Assert.AreSame(later, document.State);
        document.Undo(); document.Undo(); Assert.AreEqual(0, document.State.Annotations.Length);
    }

    [TestMethod]
    [DataRow(ScreenshotTool.Eraser, ScreenshotStyleProperty.Width, 11d)]
    [DataRow(ScreenshotTool.Blur, ScreenshotStyleProperty.BlurRadius, 19d)]
    [DataRow(ScreenshotTool.Mosaic, ScreenshotStyleProperty.MosaicSize, 27d)]
    public void ToolsWithoutPaintColorCaptureOnlyTheirActualSetting(ScreenshotTool tool, ScreenshotStyleProperty property, double value)
    {
        var defaults = Defaults(); var item = Shape(tool);
        var capture = ScreenshotStyleCapture.Read([item])!;
        Assert.IsNull(capture.Color); Assert.AreEqual(1, capture.Numbers.Count);
        Assert.AreEqual(value, capture.Numbers[property]);
        Assert.AreEqual(Red, ScreenshotStyleCapture.DisplayColor(Red, [item]));
        capture.ApplyTo(defaults);
        Assert.AreEqual("#DC2626", defaults.Color);
        Assert.AreEqual(value, defaults.Get(tool).Get(property));
        Assert.AreEqual(5d, defaults.Get(ScreenshotTool.Rectangle).Width);
    }

    [TestMethod]
    public void CaptureUsesSupportedAttributesForEveryToolAndConvertsOpacityToPercentage()
    {
        foreach (var tool in Enum.GetValues<ScreenshotTool>())
        {
            var capture = ScreenshotStyleCapture.Read([Shape(tool)]);
            var supported = Enum.GetValues<ScreenshotStyleProperty>().Where(property => ScreenshotStyleEditing.Supports(tool, property)).ToArray();
            if (tool is ScreenshotTool.Select or ScreenshotTool.Region or ScreenshotTool.Image) { Assert.IsNull(capture); continue; }
            Assert.IsNotNull(capture); CollectionAssert.AreEquivalent(supported, capture.Numbers.Keys.ToArray());
            Assert.AreEqual(ScreenshotStyleCapture.UsesColor(tool), capture.Color.HasValue);
        }
        var watermark = ScreenshotStyleCapture.Read([Shape(ScreenshotTool.Watermark)])!;
        Assert.AreEqual(60d, watermark.Numbers[ScreenshotStyleProperty.Opacity], .00001);
        Assert.AreEqual(48d, watermark.Numbers[ScreenshotStyleProperty.FontSize]);
    }

    [TestMethod]
    public void EmptyMultipleAndRasterSelectionsCannotBeCapturedOrReplaceDisplayColor()
    {
        ScreenshotAnnotation[][] selections = [[], [Shape(), Shape(ScreenshotTool.Line)],
            [Shape(ScreenshotTool.Image) with { Raster = ScreenshotFrame.Transparent(2, 2) }]];
        foreach (var selection in selections)
        {
            Assert.IsFalse(ScreenshotStyleCapture.CanCapture(selection)); Assert.IsNull(ScreenshotStyleCapture.Read(selection));
            Assert.AreEqual(Red, ScreenshotStyleCapture.DisplayColor(Red, selection));
        }
    }

    [TestMethod]
    public void CapturedSettingsSurviveSaveMergedAndReloadWithoutChangingOtherTools()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PPPaint-style-capture-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var defaults = Defaults(); Directory.CreateDirectory(directory); File.WriteAllText(path, defaults.Serialize());
            var capture = ScreenshotStyleCapture.Read([Shape(ScreenshotTool.Watermark)])!;
            capture.ApplyTo(defaults);
            var pending = capture.Numbers.ToDictionary(pair => (capture.Tool, pair.Key), pair => pair.Value);
            ScreenshotToolPreferences.SaveMerged(path, pending, defaults.Color, defaults.Palette!);
            var reopened = ScreenshotToolPreferences.Read(path);
            Assert.AreEqual("#2563EB", reopened.Color);
            Assert.AreEqual(48d, reopened.Get(ScreenshotTool.Watermark).FontSize);
            Assert.AreEqual(60d, reopened.Get(ScreenshotTool.Watermark).Opacity, .00001);
            Assert.AreEqual(1d, reopened.Get(ScreenshotTool.Pen).Width);
            Assert.AreEqual(5d, reopened.Get(ScreenshotTool.Rectangle).Width);
            CollectionAssert.AreEqual(defaults.Palette, reopened.Palette);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
