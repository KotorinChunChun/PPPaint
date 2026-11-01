namespace PPPaint.Core.Tests;

// v0.2.30: typing into one text object is a single undo step.
[TestClass]
public sealed class MergedHistoryTests
{
    private static ScreenshotAnnotation Text(string id, string text) => new(id, ScreenshotTool.Text, new(0, 0, 100, 30), Rgba32.Black, Text: text);

    [TestMethod]
    public void TypingIntoOneObjectIsOneUndoStepAndDoesNotEvictHistory()
    {
        var document = new ScreenshotDocument(ScreenshotFrame.Transparent(200, 100));
        document.Add(Text("other", "keep"));
        var beforeText = document.State;
        document.Add(Text("t", "テキスト"));
        var created = document.State;
        var typed = "";
        foreach (var character in new string('あ', 200)) { typed += character; document.Replace(Text("t", typed), "text:t"); }
        Assert.AreEqual(typed, document.State.Annotations[^1].Text);
        document.Undo(); Assert.AreSame(created, document.State);
        document.Undo(); Assert.AreSame(beforeText, document.State);
        document.Redo(); document.Redo(); Assert.AreEqual(typed, document.State.Annotations[^1].Text);
    }

    [TestMethod]
    public void OtherEditsKeysAndEndMergeStartNewSteps()
    {
        var document = new ScreenshotDocument(ScreenshotFrame.Transparent(200, 100));
        document.Add(Text("a", "")); document.Add(Text("b", ""));
        document.Replace(Text("a", "1"), "text:a"); var afterA = document.State;
        document.Replace(Text("b", "2"), "text:b"); var afterB = document.State;
        document.Replace(Text("b", "23"), "text:b");
        document.Undo(); Assert.AreSame(afterA, document.State);
        document.Redo(); Assert.AreEqual("23", document.State.Annotations[1].Text);

        document.EndMerge();
        var beforeSecondSession = document.State;
        document.Replace(Text("b", "234"), "text:b");
        document.Undo(); Assert.AreSame(beforeSecondSession, document.State);

        // An unmerged edit in between ends the typing session too.
        document.Replace(Text("b", "x"), "text:b"); var typed = document.State;
        document.SetBackground(Rgba32.Transparent);
        document.Replace(Text("b", "xy"), "text:b");
        document.Undo(); Assert.AreEqual(Rgba32.Transparent, document.State.Background);
        document.Undo(); Assert.AreSame(typed, document.State);
        Assert.AreNotSame(afterB, document.State);
    }

    [TestMethod]
    public void UndoEndsTheTypingSessionSoRedoIsNotOverwritten()
    {
        var document = new ScreenshotDocument(ScreenshotFrame.Transparent(200, 100));
        document.Add(Text("t", ""));
        document.Replace(Text("t", "a"), "text:t"); document.Replace(Text("t", "ab"), "text:t");
        document.Undo();
        Assert.AreEqual("", document.State.Annotations[0].Text);
        document.Replace(Text("t", "c"), "text:t");
        Assert.IsFalse(document.CanRedo);
        document.Undo(); Assert.AreEqual("", document.State.Annotations[0].Text);
    }
}
