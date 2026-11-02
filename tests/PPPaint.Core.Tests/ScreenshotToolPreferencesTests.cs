namespace PPPaint.Core.Tests;

[TestClass]
public sealed class ScreenshotToolPreferencesTests
{
    [TestMethod]
    public void PenEraserAndArrowWidthsAreIndependentThroughSerialization()
    {
        var settings = new ScreenshotToolPreferences();
        settings.Set(ScreenshotTool.Pen, ScreenshotStyleProperty.Width, 1);
        settings.Set(ScreenshotTool.Eraser, ScreenshotStyleProperty.Width, 16);
        settings.Set(ScreenshotTool.Arrow, ScreenshotStyleProperty.Width, 5);
        var reopened = ScreenshotToolPreferences.Parse(settings.Serialize());
        Assert.AreEqual(1d, reopened.Get(ScreenshotTool.Pen).Width);
        Assert.AreEqual(16d, reopened.Get(ScreenshotTool.Eraser).Width);
        Assert.AreEqual(5d, reopened.Get(ScreenshotTool.Arrow).Width);
        Assert.AreEqual(3d, reopened.Get(ScreenshotTool.Rectangle).Width);
        settings.Set(ScreenshotTool.Eraser, ScreenshotStyleProperty.Width, 1);
        Assert.AreEqual(1d, settings.Get(ScreenshotTool.Pen).Width);
        Assert.AreEqual(5d, settings.Get(ScreenshotTool.Arrow).Width);
    }

    [TestMethod]
    public void EraserFractionalWidthMatchesItsIntegerFootprintButOtherToolsKeepFractions()
    {
        var migrated = ScreenshotToolPreferences.Parse("""{"Width":1.5}""");
        Assert.AreEqual(2d, migrated.Get(ScreenshotTool.Eraser).Width);
        Assert.AreEqual(1.5d, migrated.Get(ScreenshotTool.Pen).Width);
        Assert.AreEqual(1.5d, migrated.Get(ScreenshotTool.Rectangle).Width);
        migrated.Set(ScreenshotTool.Eraser, ScreenshotStyleProperty.Width, 2.5);
        migrated.Set(ScreenshotTool.Pen, ScreenshotStyleProperty.Width, 2.5);
        var reopened = ScreenshotToolPreferences.Parse(migrated.Serialize());
        Assert.AreEqual(3d, reopened.Get(ScreenshotTool.Eraser).Width);
        Assert.AreEqual(2.5d, reopened.Get(ScreenshotTool.Pen).Width);
    }

    [TestMethod]
    public void FontsEffectsAndOpacityAreStoredPerToolWithoutPersistingDocumentNumber()
    {
        var settings = new ScreenshotToolPreferences { Color = "#112233", Palette = ["#112233", "#FFFFFF"] };
        settings.Set(ScreenshotTool.Text, ScreenshotStyleProperty.FontSize, 40);
        settings.Set(ScreenshotTool.Callout, ScreenshotStyleProperty.FontSize, 18);
        settings.Set(ScreenshotTool.Number, ScreenshotStyleProperty.FontSize, 32);
        settings.Set(ScreenshotTool.Blur, ScreenshotStyleProperty.BlurRadius, 19);
        settings.Set(ScreenshotTool.Mosaic, ScreenshotStyleProperty.MosaicSize, 22);
        settings.Set(ScreenshotTool.Text, ScreenshotStyleProperty.Opacity, 65);
        var json = settings.Serialize(); var reopened = ScreenshotToolPreferences.Parse(json);
        Assert.AreEqual(40d, reopened.Get(ScreenshotTool.Text).FontSize);
        Assert.AreEqual(18d, reopened.Get(ScreenshotTool.Callout).FontSize);
        Assert.AreEqual(32d, reopened.Get(ScreenshotTool.Number).FontSize);
        Assert.AreEqual(19d, reopened.Get(ScreenshotTool.Blur).BlurRadius);
        Assert.AreEqual(22d, reopened.Get(ScreenshotTool.Mosaic).MosaicSize);
        Assert.AreEqual(65d, reopened.Get(ScreenshotTool.Text).Opacity);
        Assert.AreEqual(100d, reopened.Get(ScreenshotTool.Callout).Opacity);
        Assert.AreEqual("#112233", reopened.Color); CollectionAssert.AreEqual(settings.Palette, reopened.Palette);
        Assert.IsFalse(json.Contains("NextNumber", StringComparison.Ordinal));
    }

    [TestMethod]
    public void LegacySharedNumbersSeedEveryToolThenBecomeIndependent()
    {
        var settings = ScreenshotToolPreferences.Parse("""{"Color":"#224466","Width":9,"FontSize":36,"BlurRadius":12,"MosaicSize":20,"Opacity":45,"Palette":["#112233"]}""");
        foreach (var tool in Enum.GetValues<ScreenshotTool>())
        {
            Assert.AreEqual(new ScreenshotToolNumbers(9, 36, 12, 20, tool == ScreenshotTool.Text ? 100 : 45), settings.Get(tool));
        }
        settings.Set(ScreenshotTool.Eraser, ScreenshotStyleProperty.Width, 24);
        var migrated = ScreenshotToolPreferences.Parse(settings.Serialize());
        Assert.AreEqual(9d, migrated.Get(ScreenshotTool.Pen).Width); Assert.AreEqual(24d, migrated.Get(ScreenshotTool.Eraser).Width);
        Assert.AreEqual("#224466", migrated.Color); Assert.AreEqual("#112233", migrated.Palette![0]);
    }

    [TestMethod]
    public void InvalidAndUnknownFieldsDoNotOverwriteOtherToolSettings()
    {
        var settings = ScreenshotToolPreferences.Parse("""{"Width":7,"Color":"bad","Palette":["bad","#123456",17],"Tools":{"Pen":{"Width":1,"FontSize":"wrong"},"Eraser":{"Width":999},"Text":{"FontSize":1},"Blur":{"BlurRadius":1e400},"Mosaic":{"MosaicSize":16.9},"Watermark":{"Opacity":-1},"UnknownTool":{"Width":80},"999":{"Width":80}}}""");
        Assert.AreEqual(1d, settings.Get(ScreenshotTool.Pen).Width); Assert.AreEqual(24d, settings.Get(ScreenshotTool.Pen).FontSize);
        Assert.AreEqual(100d, settings.Get(ScreenshotTool.Eraser).Width); Assert.AreEqual(8d, settings.Get(ScreenshotTool.Text).FontSize);
        Assert.AreEqual(8d, settings.Get(ScreenshotTool.Blur).BlurRadius); Assert.AreEqual(17d, settings.Get(ScreenshotTool.Mosaic).MosaicSize);
        Assert.AreEqual(100d, settings.Get(ScreenshotTool.Text).Opacity); Assert.AreEqual(7d, settings.Get(ScreenshotTool.Line).Width);
        Assert.AreEqual("#DC2626", settings.Color); CollectionAssert.AreEqual(new[] { "", "#123456", "" }, settings.Palette);
        Assert.IsFalse(settings.Serialize().Contains("UnknownTool", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("not json")]
    [DataRow("null")]
    [DataRow("[]")]
    [DataRow("{\"Tools\":false,\"Width\":null}")]
    public void CorruptOrMissingNumericDataRestoresSafeDefaults(string json)
    {
        var settings = ScreenshotToolPreferences.Parse(json);
        Assert.AreEqual(new ScreenshotToolNumbers(), settings.Get(ScreenshotTool.Eraser));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => settings.Set(ScreenshotTool.Pen, ScreenshotStyleProperty.Width, double.NaN));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => settings.Set((ScreenshotTool)999, ScreenshotStyleProperty.Width, 3));
    }

    [TestMethod]
    public void MergingStaleWindowsPreservesOtherToolsAndOtherAttributesAcrossRestarts()
    {
        WithFile(path =>
        {
            var oldWindow = ScreenshotToolPreferences.Read(path); var otherWindow = ScreenshotToolPreferences.Read(path);
            var palette = new[] { "#112233" };
            ScreenshotToolPreferences.SaveMerged(path, new Dictionary<(ScreenshotTool, ScreenshotStyleProperty), double> { [(ScreenshotTool.Eraser, ScreenshotStyleProperty.Width)] = 16 }, "#112233", palette);
            // Both windows loaded before either change. Save only each window's actual edited fields.
            oldWindow.Set(ScreenshotTool.Pen, ScreenshotStyleProperty.Width, 1);
            ScreenshotToolPreferences.SaveMerged(path, new Dictionary<(ScreenshotTool, ScreenshotStyleProperty), double> { [(ScreenshotTool.Pen, ScreenshotStyleProperty.Width)] = oldWindow.Get(ScreenshotTool.Pen).Width }, "#112233", palette);
            otherWindow.Set(ScreenshotTool.Text, ScreenshotStyleProperty.FontSize, 40);
            ScreenshotToolPreferences.SaveMerged(path, new Dictionary<(ScreenshotTool, ScreenshotStyleProperty), double> { [(ScreenshotTool.Text, ScreenshotStyleProperty.FontSize)] = otherWindow.Get(ScreenshotTool.Text).FontSize }, "#112233", palette);
            var restored = ScreenshotToolPreferences.Read(path);
            Assert.AreEqual(1d, restored.Get(ScreenshotTool.Pen).Width); Assert.AreEqual(16d, restored.Get(ScreenshotTool.Eraser).Width);
            Assert.AreEqual(40d, restored.Get(ScreenshotTool.Text).FontSize); Assert.AreEqual(24d, restored.Get(ScreenshotTool.Callout).FontSize);
            ScreenshotToolPreferences.SaveMerged(path, new Dictionary<(ScreenshotTool, ScreenshotStyleProperty), double>(), "#445566", palette);
            var recolored = ScreenshotToolPreferences.Read(path);
            Assert.AreEqual(restored.Get(ScreenshotTool.Pen), recolored.Get(ScreenshotTool.Pen)); Assert.AreEqual(restored.Get(ScreenshotTool.Eraser), recolored.Get(ScreenshotTool.Eraser));
            Assert.AreEqual("#445566", recolored.Color); Assert.AreEqual(0, Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").Length);
        });
    }

    [TestMethod]
    public void ResetClearsEveryToolNumericDefaultWithoutChangingAnyDocument()
    {
        WithFile(path =>
        {
            var document = new ScreenshotDocument(ScreenshotFrame.Transparent(20, 20));
            document.Add(new("line", ScreenshotTool.Line, new(2, 2, 10, 10), Rgba32.Black, Width: 12, Points: [new(2, 2), new(12, 12)]));
            var original = document.State;
            ScreenshotToolPreferences.SaveMerged(path, new Dictionary<(ScreenshotTool, ScreenshotStyleProperty), double> { [(ScreenshotTool.Eraser, ScreenshotStyleProperty.Width)] = 32, [(ScreenshotTool.Text, ScreenshotStyleProperty.FontSize)] = 80 }, "#112233", ["#112233"]);
            var reset = ScreenshotToolPreferences.SaveMerged(path, new Dictionary<(ScreenshotTool, ScreenshotStyleProperty), double>(), "#DC2626", ["#DC2626"], resetNumbers: true);
            foreach (var tool in Enum.GetValues<ScreenshotTool>()) Assert.AreEqual(new ScreenshotToolNumbers(), reset.Get(tool));
            var reopened = ScreenshotToolPreferences.Read(path); Assert.AreEqual(new ScreenshotToolNumbers(), reopened.Get(ScreenshotTool.Eraser));
            Assert.AreSame(original, document.State); Assert.AreEqual(12f, document.State.Annotations[0].Width);
        });
    }

    private static void WithFile(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "PPPaint-preferences-" + Guid.NewGuid().ToString("N"));
        try { action(Path.Combine(directory, "settings.json")); }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
