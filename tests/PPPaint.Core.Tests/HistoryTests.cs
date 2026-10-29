using PPPaint.Core;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class HistoryTests
{
    [TestMethod]
    public void CaptureUndoRedo_FollowsEditingOrder()
    {
        var history = new History<string>();
        history.Capture("before");

        Assert.IsTrue(history.TryUndo("after", out var undo));
        Assert.AreEqual("before", undo);
        Assert.IsTrue(history.TryRedo("before", out var redo));
        Assert.AreEqual("after", redo);
    }

    [TestMethod]
    public void NewCapture_ClearsRedo()
    {
        var history = new History<int>();
        history.Capture(1);
        Assert.IsTrue(history.TryUndo(2, out _));

        history.Capture(3);

        Assert.IsFalse(history.CanRedo);
    }

    [TestMethod]
    public void Capacity_DropsOldestState()
    {
        var history = new History<int>(2);
        history.Capture(1);
        history.Capture(2);
        history.Capture(3);

        Assert.IsTrue(history.TryUndo(4, out var first));
        Assert.AreEqual(3, first);
        Assert.IsTrue(history.TryUndo(first, out var second));
        Assert.AreEqual(2, second);
        Assert.IsFalse(history.TryUndo(second, out _));
    }
}
