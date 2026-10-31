using System.Numerics;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class ScreenshotContextTargetTests
{
    private static ScreenshotAnnotation Shape(string id, float x = 10) => new(id, ScreenshotTool.Rectangle, new(x, 10, 12, 12), Rgba32.Black);
    private static ScreenshotDocument Document()
    {
        var pixels = new byte[80 * 60 * 4];
        for (var index = 0; index < pixels.Length; index += 4)
        { pixels[index] = (byte)(index % 251); pixels[index + 1] = 80; pixels[index + 2] = 160; pixels[index + 3] = 255; }
        var document = new ScreenshotDocument(new(80, 60, pixels)); document.AddMany([Shape("a"), Shape("b", 45)]); return document;
    }
    // Mirrors the two existing deletion APIs, not the right-click event wiring.
    private static void DeleteTarget(ScreenshotDocument document, ScreenshotContextTarget target)
    {
        if (target.Region is { } box) document.DeleteRange(new((int)box.X, (int)box.Y, (int)box.Width, (int)box.Height));
        else document.Delete(target.SelectedIds.ToHashSet());
    }

    [TestMethod]
    public void OutsideRegionRightClickDeletesOnlyClickedShapeAndUndoRestoresPixelsAndBothObjects()
    {
        var document = Document(); var before = document.State; var a = before.Annotations[0]; var b = before.Annotations[1];
        var point = new Vector2(45, 15); Assert.IsTrue(b.Hit(point, 0));
        var target = ScreenshotContextTarget.Resolve(new(10, 10, 15, 15), [a], b, point);
        Assert.IsNull(target.Region); CollectionAssert.AreEqual(new[] { "b" }, target.SelectedIds.ToArray());
        Assert.IsTrue(target.CanDelete); Assert.IsTrue(target.CanReorder);
        DeleteTarget(document, target);
        Assert.HasCount(1, document.State.Annotations); Assert.AreSame(a, document.State.Annotations[0]);
        Assert.AreSame(before.Frame, document.State.Frame); CollectionAssert.AreEqual(before.Frame.Pixels.ToArray(), document.State.Frame.Pixels.ToArray());
        document.Undo(); Assert.AreSame(before, document.State, "削除1回がUndo1回で戻る必要があります。");
        document.Redo(); CollectionAssert.AreEqual(new[] { "a" }, document.State.Annotations.Select(item => item.Id).ToArray());
    }

    [TestMethod]
    public void InsideRegionKeepsAreaAndExistingSelectionEvenWithAnotherHitAndDeletesOnlyAreaPixels()
    {
        var document = Document(); var before = document.State; var a = before.Annotations[0]; var b = before.Annotations[1];
        var area = new ScreenshotBox(10, 10, 15, 15); var overlapped = b with { Bounds = new(10, 10, 15, 15) };
        var point = new Vector2(10, 15); Assert.IsTrue(overlapped.Hit(point, 0));
        var target = ScreenshotContextTarget.Resolve(area, [a], overlapped, point);
        Assert.AreEqual(area, target.Region); CollectionAssert.AreEqual(new[] { "a" }, target.SelectedIds.ToArray());
        DeleteTarget(document, target);
        Assert.IsTrue(document.State.Annotations.Any(item => item.Id == b.Id));
        var expected = before.Frame.Pixels.ToArray(); var actual = document.State.Frame.Pixels.ToArray();
        for (var y = 0; y < 60; y++) for (var x = 0; x < 80; x++) for (var channel = 0; channel < 4; channel++)
        {
            var index = (y * 80 + x) * 4 + channel;
            Assert.AreEqual(x is >= 10 and < 25 && y is >= 10 and < 25 ? (byte)0 : expected[index], actual[index], $"範囲外の画素が変更されました: {x},{y}/{channel}");
        }
        document.Undo(); Assert.AreSame(before, document.State);
    }

    [TestMethod]
    public void EmptyRegionHasDeleteButNoStackOrderCommands()
    {
        var region = new ScreenshotBox(3, 4, 12, 9);
        var target = ScreenshotContextTarget.Resolve(region, [], null, new(5, 6));
        Assert.AreEqual(region, target.Region); Assert.IsTrue(target.CanDelete); Assert.IsFalse(target.CanReorder); Assert.IsTrue(target.SelectedIds.IsEmpty);
        var empty = ScreenshotContextTarget.Resolve(region, [], null, new(50, 50));
        Assert.IsNull(empty.Region); Assert.IsFalse(empty.CanDelete); Assert.IsFalse(empty.CanReorder);
    }

    [TestMethod]
    public void SelectedHitKeepsMultipleObjectsButUnselectedHitReplacesSelection()
    {
        var a = Shape("a"); var b = Shape("b", 45); var c = Shape("c", 65);
        var kept = ScreenshotContextTarget.Resolve(null, [a, b], a, new(10, 15));
        CollectionAssert.AreEqual(new[] { "a", "b" }, kept.SelectedIds.ToArray());
        var replaced = ScreenshotContextTarget.Resolve(null, [a, b], c, new(65, 15));
        CollectionAssert.AreEqual(new[] { "c" }, replaced.SelectedIds.ToArray()); Assert.IsNull(replaced.Region);
    }

    [TestMethod]
    public void EmptyPointInsideGroupKeepsSelectionAndOutsideGroupClearsIt()
    {
        var selected = new[] { Shape("a"), Shape("b", 45) }; var gap = new Vector2(35, 15);
        Assert.IsFalse(selected.Any(item => item.Hit(gap, 0)));
        var kept = ScreenshotContextTarget.Resolve(null, selected, null, gap);
        CollectionAssert.AreEqual(new[] { "a", "b" }, kept.SelectedIds.ToArray());
        var cleared = ScreenshotContextTarget.Resolve(null, selected, null, new(79, 59));
        Assert.IsTrue(cleared.SelectedIds.IsEmpty); Assert.IsFalse(cleared.CanDelete);
        var noSelection = ScreenshotContextTarget.Resolve(null, [], null, Vector2.Zero);
        Assert.IsFalse(noSelection.CanDelete); Assert.IsFalse(noSelection.CanReorder);
    }

    [TestMethod]
    [DataRow(10f, 10f)]
    [DataRow(25f, 25f)]
    public void RegionBoundaryKeepsTheRegion(float x, float y)
    {
        var region = new ScreenshotBox(10, 10, 15, 15); var target = ScreenshotContextTarget.Resolve(region, [], Shape("other", 25), new(x, y));
        Assert.AreEqual(region, target.Region); Assert.IsTrue(target.SelectedIds.IsEmpty);
        var outside = ScreenshotContextTarget.Resolve(region, [], null, new(25.01f, 25)); Assert.IsNull(outside.Region);
    }

    [TestMethod]
    public void ClippedAndCutOutAreasUseVisibleHitResultsAndVisibleSelectionBounds()
    {
        var shape = new ScreenshotAnnotation("clip", ScreenshotTool.Redact, new(0, 0, 30, 30), Rgba32.Black,
            ClipBounds: new(20, 20, 10, 10), Cutouts: [new(24, 24, 3, 3)]);
        var clippedAway = new Vector2(5, 5); Assert.IsFalse(shape.Hit(clippedAway, 0));
        var cleared = ScreenshotContextTarget.Resolve(null, [shape], shape.Hit(clippedAway, 0) ? shape : null, clippedAway);
        Assert.IsTrue(cleared.SelectedIds.IsEmpty, "見えない元のBoundsで選択を維持してはいけません。");
        var hole = new Vector2(25, 25); Assert.IsFalse(shape.Hit(hole, 0));
        Assert.IsTrue(ScreenshotContextTarget.Resolve(null, [], null, hole).SelectedIds.IsEmpty);
        CollectionAssert.AreEqual(new[] { "clip" }, ScreenshotContextTarget.Resolve(null, [shape], null, hole).SelectedIds.ToArray(), "既選択の枠内の空白では選択を維持します。");
        var visible = new Vector2(21, 21); Assert.IsTrue(shape.Hit(visible, 0));
        CollectionAssert.AreEqual(new[] { "clip" }, ScreenshotContextTarget.Resolve(null, [], shape, visible).SelectedIds.ToArray());
    }

    [TestMethod]
    public void TransparentRasterPixelsDoNotBecomeANewContextTarget()
    {
        var image = ScreenshotDocument.ImageObject(new(2, 1, new byte[] { 0, 0, 0, 0, 0, 0, 255, 255 }), Vector2.Zero).Resize(new(0, 0, 20, 10));
        foreach (var point in new[] { new Vector2(5, 5), new Vector2(15, 5) })
        {
            var hit = image.Hit(point, 0) ? image : null;
            var target = ScreenshotContextTarget.Resolve(null, [], hit, point);
            Assert.AreEqual(point.X > 10, target.CanDelete); Assert.AreEqual(point.X > 10, target.CanReorder);
        }
    }
}
