using ChunPaint.Core;

namespace ChunPaint.Core.Tests;

[TestClass]
public sealed class SelectionOperationsTests
{
    [TestMethod]
    public void Extract_TransparentSelectionRemovesMatchingColor()
    {
        var background = new Rgba32(255, 255, 255);
        var source = new PixelSurface(3, 2, background);
        source[1, 0] = new Rgba32(255, 0, 0);
        var region = SelectionRegion.CreateRectangle(new PixelRect(0, 0, 3, 2));

        var result = SelectionOperations.Extract(source, region, transparentSelection: true, background);

        Assert.AreEqual(Rgba32.Transparent, result[0, 0]);
        Assert.AreEqual(new Rgba32(255, 0, 0), result[1, 0]);
    }

    [TestMethod]
    public void InvertColors_ChangesOnlySelectedPixels()
    {
        var source = new PixelSurface(3, 1, new Rgba32(10, 20, 30));
        var region = SelectionRegion.CreateRectangle(new PixelRect(1, 0, 1, 1));

        SelectionOperations.InvertColors(source, region);

        Assert.AreEqual(new Rgba32(10, 20, 30), source[0, 0]);
        Assert.AreEqual(new Rgba32(245, 235, 225), source[1, 0]);
        Assert.AreEqual(new Rgba32(10, 20, 30), source[2, 0]);
    }

    [TestMethod]
    public void Apply_UsesPolygonMask()
    {
        var destination = new PixelSurface(3, 3, Rgba32.Transparent);
        var selection = new PixelSurface(3, 3, Rgba32.Transparent);
        selection.Clear(new Rgba32(255, 0, 0));
        var region = SelectionRegion.CreatePolygon([
            new System.Numerics.Vector2(0, 0),
            new System.Numerics.Vector2(3, 0),
            new System.Numerics.Vector2(0, 3)
        ]);

        SelectionOperations.Apply(destination, region, selection);

        Assert.AreEqual(new Rgba32(255, 0, 0), destination[0, 0]);
        Assert.AreEqual(new Rgba32(255, 0, 0), destination[1, 0]);
        Assert.AreEqual(Rgba32.Transparent, destination[2, 2]);
    }

    [TestMethod]
    public void CompositeVisibleLayers_SkipsHiddenLayer()
    {
        var hidden = new PixelSurface(2, 1, new Rgba32(255, 0, 0));
        var visible = new PixelSurface(2, 1, new Rgba32(0, 255, 0));

        var result = SelectionOperations.CompositeVisibleLayers([
            (hidden, false),
            (visible, true)
        ]);

        Assert.AreEqual(new Rgba32(0, 255, 0), result[0, 0]);
    }
}
