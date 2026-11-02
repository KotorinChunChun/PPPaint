using System.Numerics;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class RegionLiftTests
{
    [TestMethod]
    public void PreviewDoesNotChangeHistoryAndCommitIsOneUndoableStep()
    {
        var document = ScreenshotDocument.FromImage(ScreenshotFrame.Blank(80, 60));
        var pen = new ScreenshotAnnotation("pen", ScreenshotTool.Pen, new(0, 0, 70, 50), Rgba32.Black, Points: [new(0, 0), new(70, 50)]);
        document.Add(pen); var before = document.State; document.Undo(); document.Redo();
        var plan = new ScreenshotRegionLift(before, before, new(10, 10, 20, 20), ScreenshotFrame.Blank(20, 20));
        var moved = plan.Preview(new(30.4f, 2.6f), false);
        Assert.AreSame(before, document.State);
        Assert.AreEqual(new ScreenshotBox(40, 13, 20, 20), moved.Annotations[^1].Bounds);
        Assert.HasCount(1, moved.Annotations.Single(a => a.Id == pen.Id).Cutouts);
        plan.Commit(document, moved); document.Undo(); Assert.AreSame(before, document.State);
        document.Redo(); Assert.AreSame(moved, document.State);
        var copied = plan.Preview(new(3, 4), true);
        Assert.AreSame(before.Frame, copied.Frame);
        Assert.AreSame(pen, copied.Annotations.Single(a => a.Id == pen.Id));
        Assert.ThrowsExactly<InvalidOperationException>(() => plan.Commit(document, copied));
    }
}
