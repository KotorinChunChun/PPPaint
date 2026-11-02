namespace PPPaint.Core.Tests;

[TestClass]
public sealed class ShapeStylesTests
{
    [TestMethod]
    public void ShapeDefaultsAndStylesRoundTripIndependentlyOfNumbers()
    {
        var prefs = ScreenshotToolPreferences.Parse("""{"Tools":{"Pen":{"Width":7}}}""");
        Assert.IsFalse(prefs.GetShape(ScreenshotTool.Rectangle).Filled); Assert.IsTrue(prefs.GetShape(ScreenshotTool.Number).Filled);
        var arrow = new ScreenshotShapeStyle(true, ScreenshotArrowStyle.ThreeDimensional);
        var number = new ScreenshotShapeStyle(false, Number: ScreenshotNumberStyle.Square);
        prefs.SetShape(ScreenshotTool.Arrow, arrow); prefs.SetShape(ScreenshotTool.Number, number);
        var loaded = ScreenshotToolPreferences.Parse(prefs.Serialize());
        Assert.AreEqual(arrow, loaded.GetShape(ScreenshotTool.Arrow)); Assert.AreEqual(number, loaded.GetShape(ScreenshotTool.Number));
        Assert.AreEqual(7d, loaded.Get(ScreenshotTool.Pen).Width);
        var invalid = ScreenshotToolPreferences.Parse("""{"Styles":{"Arrow":{"Arrow":"invalid"},"Number":{"Number":"999"},"Unknown":{"Filled":true}}}""");
        Assert.AreEqual(ScreenshotArrowStyle.Line, invalid.GetShape(ScreenshotTool.Arrow).Arrow);
        Assert.AreEqual(ScreenshotNumberStyle.Circle, invalid.GetShape(ScreenshotTool.Number).Number);
    }
    [TestMethod]
    public void SelectedStyleEditsAreUndoableAndDoNotReplaceDrawingDefaults()
    {
        var prefs = new ScreenshotToolPreferences(); var document = new ScreenshotDocument(ScreenshotFrame.Transparent(100, 80));
        var shape = new ScreenshotAnnotation("r", ScreenshotTool.Rectangle, new(10, 10, 40, 30), new(200, 30, 10, 5)); document.Add(shape);
        var original = document.State; var filled = new ScreenshotShapeStyle(true).Apply(shape);
        document.Replace(filled); Assert.IsFalse(prefs.GetShape(ScreenshotTool.Rectangle).Filled);
        Assert.AreEqual((byte)255, filled.OutputColor.A); Assert.IsTrue(filled.Hit(new(30, 25), 0)); Assert.IsFalse(shape.Hit(new(30, 25), 0));
        document.Undo(); Assert.AreSame(original, document.State); document.Redo();
        ScreenshotStyleCapture.Read(document.State.Annotations)!.ApplyTo(prefs); Assert.IsTrue(prefs.GetShape(ScreenshotTool.Rectangle).Filled);
        var copy = ScreenshotManipulation.Duplicate(document.State.Annotations)[0]; Assert.IsTrue(copy.IsFilled);
    }
    [TestMethod]
    public void FilledEllipsesAndBlockArrowsHaveMatchingInteriorHitTargets()
    {
        var ellipse = new ScreenshotAnnotation("e", ScreenshotTool.Ellipse, new(10, 10, 60, 40), Rgba32.Black);
        Assert.IsFalse(ellipse.Hit(new(40, 30), 0)); Assert.IsTrue((ellipse with { Filled = true }).Hit(new(40, 30), 0));
        var arrow = new ScreenshotAnnotation("a", ScreenshotTool.Arrow, new(10, 40, 80, 0), Rgba32.Black, Width: 3, Points: [new(10, 40), new(90, 40)], ArrowStyle: ScreenshotArrowStyle.Block);
        Assert.IsFalse(arrow.Hit(new(40, 40), 0)); Assert.IsTrue((arrow with { Filled = true }).Hit(new(40, 40), 0));
        Assert.HasCount(7, ScreenshotArrowGeometry.Block(new(10, 40), new(90, 40), 3));
    }
}
