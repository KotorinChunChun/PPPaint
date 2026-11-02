namespace PPPaint.Core.Tests;

[TestClass]
public sealed class UnifiedTextTests
{
    [TestMethod]
    public void OnlyOneTextToolIsOfferedAndFormerWatermarkKeyIsUnassigned()
    {
        Assert.HasCount(1, ScreenshotCommands.Tools.Where(c => c.Tool == ScreenshotTool.Text));
        Assert.IsFalse(ScreenshotCommands.Tools.Any(c => c.Name == "透かし" || c.Key == 'S'));
        Assert.IsFalse(ScreenshotCommands.TryTool('S', false, false, false, false, out _));
        Assert.IsTrue(ScreenshotCommands.TryTool('T', false, false, false, false, out var tool));
        Assert.AreEqual(ScreenshotTool.Text, tool);
        Assert.AreEqual(1f, new ScreenshotAnnotation("t", tool, new(0, 0, 50, 30), Rgba32.Black).Opacity);
        Assert.AreEqual(100d, new ScreenshotToolPreferences().Get(tool).Opacity);
    }

    [TestMethod]
    [DataRow("2")]
    [DataRow("null")]
    [DataRow("\"invalid\"")]
    public void LegacyUnusedTextOpacityDoesNotMakeTextTranslucent(string version)
    {
        var settings = ScreenshotToolPreferences.Parse("{\"Version\":" + version + ",\"Color\":\"#112233\",\"Tools\":{\"Text\":{\"Opacity\":25,\"FontSize\":40},\"Pen\":{\"Width\":7},\"Watermark\":{\"Opacity\":65,\"FontSize\":80}}}");
        Assert.AreEqual(100d, settings.Get(ScreenshotTool.Text).Opacity);
        Assert.AreEqual(40d, settings.Get(ScreenshotTool.Text).FontSize);
        Assert.AreEqual(7d, settings.Get(ScreenshotTool.Pen).Width);
        Assert.AreEqual("#112233", settings.Color);
        var serialized = settings.Serialize();
        Assert.IsFalse(serialized.Contains("Watermark", StringComparison.Ordinal));
        var reopened = ScreenshotToolPreferences.Parse(serialized);
        Assert.AreEqual(settings.Get(ScreenshotTool.Text), reopened.Get(ScreenshotTool.Text));
    }

    [TestMethod]
    public void TextOpacityPersistsAndEditingSelectionDoesNotChangeDrawingDefaults()
    {
        var settings = new ScreenshotToolPreferences();
        settings.Set(ScreenshotTool.Text, ScreenshotStyleProperty.Opacity, 65);
        var reopened = ScreenshotToolPreferences.Parse(settings.Serialize());
        Assert.AreEqual(65d, reopened.Get(ScreenshotTool.Text).Opacity);
        var original = new ScreenshotAnnotation("t", ScreenshotTool.Text, new(0, 0, 50, 30), Rgba32.Black, Text: "text");
        var edited = ScreenshotStyleEditing.Apply(original, ScreenshotStyleProperty.Opacity, .35f);
        Assert.AreEqual(.35f, edited.Opacity);
        Assert.AreEqual(1f, original.Opacity);
        Assert.AreEqual(65d, reopened.Get(ScreenshotTool.Text).Opacity);
        ScreenshotStyleCapture.Read([edited])!.ApplyTo(reopened);
        Assert.AreEqual(35d, reopened.Get(ScreenshotTool.Text).Opacity, .00001);
    }
}
