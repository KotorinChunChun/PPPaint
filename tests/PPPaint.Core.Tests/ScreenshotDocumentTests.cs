using System.Collections.Immutable;
using System.Numerics;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class ScreenshotDocumentTests
{
    private static ScreenshotAnnotation Arrow() => new("arrow", ScreenshotTool.Arrow, new(10, 20, 100, 40), new(255, 0, 0), Points: [new(10, 20), new(110, 60)]);
    [TestMethod]
    public void AnnotationEditsUndoIndependentlyAndBranchDropsRedo()
    {
        var doc = new ScreenshotDocument(ScreenshotFrame.Blank(200, 100)); var original = doc.State;
        doc.Add(Arrow()); doc.Replace(Arrow().Move(new(5, 8))); doc.Delete("arrow");
        doc.Undo(); Assert.AreEqual(15f, doc.State.Annotations[0].Bounds.X);
        doc.Undo(); Assert.AreEqual(10f, doc.State.Annotations[0].Bounds.X);
        doc.Redo(); Assert.AreEqual(28f, doc.State.Annotations[0].Bounds.Y);
        doc.Add(Arrow() with { Id = "second" }); Assert.IsFalse(doc.CanRedo);
        doc.Undo(); doc.Undo(); doc.Undo(); Assert.AreSame(original, doc.State);
    }
    [TestMethod]
    public void ResizeMovesArrowEndpointsAndKeepsDirection()
    {
        var arrow = Arrow().Resize(new(30, 40, 200, 80));
        Assert.AreEqual(new Vector2(30, 40), arrow.Points[0]); Assert.AreEqual(new Vector2(230, 120), arrow.Points[1]);
        Assert.IsTrue(arrow.Hit(new(130, 80))); Assert.IsFalse(arrow.Hit(new(30, 120)));
    }
    [TestMethod]
    public void RedactionIsAlwaysOpaqueAndHighlightCanRemainTransparent()
    {
        var a = Arrow() with { Tool = ScreenshotTool.Redact, Color = new(0, 0, 0, 1) };
        Assert.AreEqual((byte)255, a.OutputColor.A);
        Assert.AreEqual((byte)1, (a with { Tool = ScreenshotTool.Highlight }).OutputColor.A);
    }
    [TestMethod]
    public void CropMovesEditableAnnotationsAndUndoRestoresFullImage()
    {
        var doc = new ScreenshotDocument(ScreenshotFrame.Blank(200, 100)); doc.Add(Arrow());
        doc.Add(Arrow() with { Id = "outside", Bounds = new(180, 80, 10, 10) }); var before = doc.State;
        doc.Crop(5, 10, 120, 60);
        Assert.AreEqual(120, doc.State.Frame.Width); Assert.HasCount(1, doc.State.Annotations);
        Assert.AreEqual(new Vector2(5, 10), doc.State.Annotations[0].Points[0]);
        doc.Undo(); Assert.AreSame(before, doc.State);
    }
    [TestMethod]
    public void JoiningDifferentSizesPadsWhiteAndPreservesOrderAndSource()
    {
        var bytes = new byte[] { 0, 0, 255, 255, 0, 255, 0, 255 };
        var a = new ScreenshotFrame(2, 1, bytes); bytes[2] = 0;
        var b = new ScreenshotFrame(1, 1, new byte[] { 255, 0, 0, 255 });
        var vertical = a.Append(b, true); var horizontal = b.Append(a, false);
        Assert.AreEqual((byte)255, a.Pixels.Span[2]);
        CollectionAssert.AreEqual(new byte[] { 0, 0, 255, 255, 0, 255, 0, 255, 255, 0, 0, 255, 255, 255, 255, 255 }, vertical.Pixels.ToArray());
        Assert.AreEqual(3, horizontal.Width); Assert.AreEqual((byte)255, horizontal.Pixels.Span[0]);
        var doc = new ScreenshotDocument(a); doc.Add(Arrow()); var before = doc.State; doc.Append(b, true); doc.Undo(); Assert.AreSame(before, doc.State);
    }
    [TestMethod]
    public void NumberSequenceTracksUndoAndRejectsOversizedFrames()
    {
        var doc = new ScreenshotDocument(ScreenshotFrame.Blank(10, 10)); doc.Add(Arrow() with { Tool = ScreenshotTool.Number });
        Assert.AreEqual(2, doc.State.NextNumber); doc.Undo(); Assert.AreEqual(1, doc.State.NextNumber);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ScreenshotFrame.ValidateSize(16384, 16384));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => doc.Crop(9, 9, 2, 2));
    }
}
