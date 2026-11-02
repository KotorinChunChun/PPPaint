namespace PPPaint.Core.Tests;

[TestClass]
public sealed class ScreenshotStyleEditingTests
{
    private static ScreenshotAnnotation[] Selection()
    {
        var original = new ScreenshotAnnotation("line", ScreenshotTool.Rectangle, new(5, 7, 20, 30), new(190, 10, 40),
            Width: 2, FontSize: 77, Text: "保持", Points: [new(5, 7), new(25, 37)], Rotation: 90,
            MirrorX: true, BlurRadius: 6, MosaicSize: 8, Opacity: .4f, ClipBounds: new(7, 8, 16, 24), Cutouts: [new(10, 12, 2, 3)]);
        return [original,
            original with { Id = "text", Tool = ScreenshotTool.Text, Width = 9, FontSize = 40 },
            original with { Id = "callout", Tool = ScreenshotTool.Callout, Width = 4, FontSize = 32, CalloutTip = new(11, 45) },
            original with { Id = "number", Tool = ScreenshotTool.Number, FontSize = 36, Text = "7" },
            original with { Id = "blur", Tool = ScreenshotTool.Blur, BlurRadius = 18 },
            original with { Id = "mosaic", Tool = ScreenshotTool.Mosaic, MosaicSize = 24 },
            original with { Id = "translucent-text", Tool = ScreenshotTool.Text, FontSize = 28, Opacity = .65f },
            original with { Id = "redact", Tool = ScreenshotTool.Redact },
            original with { Id = "image", Tool = ScreenshotTool.Image, Raster = ScreenshotFrame.Transparent(2, 2) }];
    }

    [TestMethod]
    [DataRow(ScreenshotStyleProperty.Width, 5f)]
    [DataRow(ScreenshotStyleProperty.FontSize, 48f)]
    [DataRow(ScreenshotStyleProperty.BlurRadius, 12f)]
    [DataRow(ScreenshotStyleProperty.MosaicSize, 16f)]
    [DataRow(ScreenshotStyleProperty.Opacity, .8f)]
    public void MixedSelectionChangesOnlyTheRequestedPropertyOnCompatibleObjectsAndUndoRedoPreservesEverythingElse(ScreenshotStyleProperty property, float value)
    {
        var originals = Selection(); var document = new ScreenshotDocument(ScreenshotFrame.Transparent(100, 80));
        document.AddMany(originals); var before = document.State;
        document.ReplaceMany(originals.Select(item => ScreenshotStyleEditing.Apply(item, property, value)));
        var changed = document.State;
        for (var index = 0; index < originals.Length; index++)
        {
            var item = originals[index]; var result = changed.Annotations[index];
            var expected = property switch
            {
                ScreenshotStyleProperty.Width when item.Id is "line" or "callout" => item with { Width = 5 },
                ScreenshotStyleProperty.FontSize when item.Id is "text" or "callout" or "number" or "translucent-text" => item with { FontSize = 48 },
                ScreenshotStyleProperty.BlurRadius when item.Id == "blur" => item with { BlurRadius = 12 },
                ScreenshotStyleProperty.MosaicSize when item.Id == "mosaic" => item with { MosaicSize = 16 },
                ScreenshotStyleProperty.Opacity when item.Tool == ScreenshotTool.Text => item with { Opacity = .8f },
                _ => item
            };
            Assert.AreEqual(expected, result, $"対象/無関係な属性が変わりました: {property}/{item.Id}");
            Assert.AreEqual(item.Id, result.Id); Assert.AreSame(item.Raster, result.Raster);
            Assert.AreEqual(item.ClipBounds, result.ClipBounds); Assert.AreEqual(item.Cutouts, result.Cutouts);
        }
        document.Undo(); Assert.AreSame(before, document.State, "一括変更が1回のUndoで戻る必要があります。");
        document.Redo(); Assert.AreSame(changed, document.State);
    }

    [TestMethod]
    public void UnsupportedStylesKeepOriginalObjectAndDoNotCreateHistory()
    {
        var image = Selection().Single(item => item.Tool == ScreenshotTool.Image);
        var document = new ScreenshotDocument(ScreenshotFrame.Transparent(100, 80)); document.Add(image); var before = document.State;
        foreach (var property in Enum.GetValues<ScreenshotStyleProperty>())
        {
            Assert.IsFalse(ScreenshotStyleEditing.Supports(ScreenshotTool.Image, property));
            Assert.AreSame(image, ScreenshotStyleEditing.Apply(image, property, 20));
            document.ReplaceMany([ScreenshotStyleEditing.Apply(image, property, 20)]);
            Assert.AreSame(before, document.State);
        }
        document.Undo(); Assert.AreEqual(0, document.State.Annotations.Length, "無効なスタイル変更がUndoへ積まれています。");
    }

    [TestMethod]
    public void WidthCoversPenEraserArrowLineRectangleEllipseAndCalloutButNotOtherTools()
    {
        var expected = new[] { ScreenshotTool.Pen, ScreenshotTool.Eraser, ScreenshotTool.Arrow, ScreenshotTool.Line, ScreenshotTool.Rectangle, ScreenshotTool.Ellipse, ScreenshotTool.Callout };
        foreach (var tool in Enum.GetValues<ScreenshotTool>())
        {
            Assert.AreEqual(expected.Contains(tool), ScreenshotStyleEditing.Supports(tool, ScreenshotStyleProperty.Width), tool.ToString());
            var item = new ScreenshotAnnotation("item", tool, new(0, 0, 10, 10), Rgba32.Black, Width: 7, FontSize: 40);
            var changed = ScreenshotStyleEditing.Apply(item, ScreenshotStyleProperty.Width, 12);
            Assert.AreEqual(expected.Contains(tool) ? 12f : 7f, changed.Width); Assert.AreEqual(40f, changed.FontSize);
        }
    }

    [TestMethod]
    public void FiniteValuesUseUiLimitsAndMixedPlaceholderCannotBecomeAnnotationData()
    {
        var selection = Selection();
        Assert.AreEqual(1f, ScreenshotStyleEditing.Apply(selection[0], ScreenshotStyleProperty.Width, 0).Width);
        var eraser = selection[0] with { Tool = ScreenshotTool.Eraser };
        Assert.AreEqual(2f, ScreenshotStyleEditing.Apply(eraser, ScreenshotStyleProperty.Width, 1.5f).Width);
        Assert.AreEqual(1.5f, ScreenshotStyleEditing.Apply(selection[0], ScreenshotStyleProperty.Width, 1.5f).Width);
        Assert.AreEqual(200f, ScreenshotStyleEditing.Apply(selection[1], ScreenshotStyleProperty.FontSize, 500).FontSize);
        Assert.AreEqual(64f, ScreenshotStyleEditing.Apply(selection[4], ScreenshotStyleProperty.BlurRadius, 100).BlurRadius);
        Assert.AreEqual(17, ScreenshotStyleEditing.Apply(selection[5], ScreenshotStyleProperty.MosaicSize, 16.9f).MosaicSize);
        Assert.AreEqual(.05f, ScreenshotStyleEditing.Apply(selection[6], ScreenshotStyleProperty.Opacity, 0).Opacity);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ScreenshotStyleEditing.Apply(selection[0], ScreenshotStyleProperty.Width, float.NaN));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ScreenshotStyleEditing.Apply(selection[0], ScreenshotStyleProperty.Width, float.PositiveInfinity));
    }
}
