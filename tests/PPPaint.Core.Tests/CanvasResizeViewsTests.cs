using System.Numerics;

namespace PPPaint.Core.Tests;

[TestClass]
public sealed class CanvasResizeViewsTests
{
    [TestMethod]
    public void EightViewsSurroundTheCandidateCornersAndEdgeCenters()
    {
        var anchors = Enum.GetValues<ScreenshotCanvasViewAnchor>();
        var expected = new[]
        {
            new Vector2(0, 0), new Vector2(80, 0), new Vector2(160, 0),
            new Vector2(0, 45), new Vector2(160, 45),
            new Vector2(0, 90), new Vector2(80, 90), new Vector2(160, 90)
        };
        Assert.AreEqual(8, anchors.Length);
        for (var index = 0; index < anchors.Length; index++)
        {
            var source = ScreenshotCanvasResizeViews.DetailSource(160, 90, anchors[index]);
            Assert.AreEqual(new ScreenshotBox(expected[index].X - 12, expected[index].Y - 12, 25, 25), source);
            Assert.AreEqual(100f, source.Width * ScreenshotCanvasResizeViews.DetailScale);
            Assert.AreEqual(100f, source.Height * ScreenshotCanvasResizeViews.DetailScale);
        }
    }

    [TestMethod]
    public void OddDimensionsAndTinyCanvasesKeepEverySampleOnWholePixelBoundaries()
    {
        foreach (var size in new[] { (1, 1), (3, 5), (101, 77), (16384, 1) })
        {
            foreach (var anchor in Enum.GetValues<ScreenshotCanvasViewAnchor>())
            {
                var source = ScreenshotCanvasResizeViews.DetailSource(size.Item1, size.Item2, anchor);
                Assert.AreEqual(MathF.Truncate(source.X), source.X);
                Assert.AreEqual(MathF.Truncate(source.Y), source.Y);
                Assert.AreEqual(25f, source.Width); Assert.AreEqual(25f, source.Height);
            }
        }
        Assert.AreEqual(new ScreenshotBox(38, -12, 25, 25), ScreenshotCanvasResizeViews.DetailSource(101, 77, ScreenshotCanvasViewAnchor.Top));
        Assert.AreEqual(new ScreenshotBox(-12, 26, 25, 25), ScreenshotCanvasResizeViews.DetailSource(101, 77, ScreenshotCanvasViewAnchor.Left));
        Assert.AreEqual(new ScreenshotBox(-11, -11, 25, 25), ScreenshotCanvasResizeViews.DetailSource(1, 1, ScreenshotCanvasViewAnchor.BottomRight));
        Assert.AreEqual(new ScreenshotBox(-12, -12, 25, 25), ScreenshotCanvasResizeViews.DetailSource(1, 1, ScreenshotCanvasViewAnchor.TopLeft));
    }

    [TestMethod]
    public void OnePixelResizeTracksEachMovingEdgeWithoutResamplingItsPixels()
    {
        foreach (var anchor in Enum.GetValues<ScreenshotCanvasViewAnchor>())
        {
            var original = ScreenshotCanvasResizeViews.DetailSource(100, 80, anchor);
            var widthChanged = ScreenshotCanvasResizeViews.DetailSource(101, 80, anchor);
            var heightChanged = ScreenshotCanvasResizeViews.DetailSource(100, 81, anchor);
            var followsRight = anchor is ScreenshotCanvasViewAnchor.TopRight or ScreenshotCanvasViewAnchor.Right or ScreenshotCanvasViewAnchor.BottomRight;
            var followsBottom = anchor is ScreenshotCanvasViewAnchor.BottomLeft or ScreenshotCanvasViewAnchor.Bottom or ScreenshotCanvasViewAnchor.BottomRight;
            Assert.AreEqual(followsRight ? 1f : 0f, widthChanged.X - original.X, anchor.ToString());
            Assert.AreEqual(original.Y, widthChanged.Y, anchor.ToString());
            Assert.AreEqual(followsBottom ? 1f : 0f, heightChanged.Y - original.Y, anchor.ToString());
            Assert.AreEqual(original.X, heightChanged.X, anchor.ToString());
            Assert.AreEqual(original.Width, widthChanged.Width); Assert.AreEqual(original.Height, heightChanged.Height);
        }
        var odd = ScreenshotCanvasResizeViews.DetailSource(101, 81, ScreenshotCanvasViewAnchor.Top);
        var even = ScreenshotCanvasResizeViews.DetailSource(102, 82, ScreenshotCanvasViewAnchor.Top);
        Assert.AreEqual(1f, even.X - odd.X);
    }

    [TestMethod]
    public void CenteredOddExpansionShowsTheExtraPixelOnRightAndBottom()
    {
        var document = new ScreenshotDocument(ScreenshotFrame.Blank(20, 16)); var before = document.State;
        var one = ScreenshotCanvasResize.Create(before, 21, 17, true);
        var two = ScreenshotCanvasResize.Create(before, 22, 18, true);
        Assert.AreEqual(Vector2.Zero, one.Offset);
        Assert.AreEqual(Vector2.One, two.Offset);
        // A source pixel's local location is offset - view origin. With a one
        // pixel expansion the left/top view stays put while right/bottom moves
        // by one source pixel; the next increment moves the opposite edge.
        var originalLeft = ScreenshotCanvasResizeViews.DetailSource(20, 16, ScreenshotCanvasViewAnchor.TopLeft);
        var originalRight = ScreenshotCanvasResizeViews.DetailSource(20, 16, ScreenshotCanvasViewAnchor.BottomRight);
        var oneLeft = ScreenshotCanvasResizeViews.DetailSource(one.Width, one.Height, ScreenshotCanvasViewAnchor.TopLeft);
        var oneRight = ScreenshotCanvasResizeViews.DetailSource(one.Width, one.Height, ScreenshotCanvasViewAnchor.BottomRight);
        var twoLeft = ScreenshotCanvasResizeViews.DetailSource(two.Width, two.Height, ScreenshotCanvasViewAnchor.TopLeft);
        var twoRight = ScreenshotCanvasResizeViews.DetailSource(two.Width, two.Height, ScreenshotCanvasViewAnchor.BottomRight);
        Assert.AreEqual(-Origin(originalLeft), one.Offset - Origin(oneLeft));
        Assert.AreEqual(-Origin(originalRight) - Vector2.One, one.Offset - Origin(oneRight));
        Assert.AreEqual(one.Offset - Origin(oneLeft) + Vector2.One, two.Offset - Origin(twoLeft));
        Assert.AreEqual(one.Offset - Origin(oneRight), two.Offset - Origin(twoRight));
        Assert.AreSame(before, document.State); Assert.IsFalse(document.CanUndo);
    }

    [TestMethod]
    public void CenteredOddTrimShowsTheExtraRemovedPixelOnRightAndBottom()
    {
        var document = new ScreenshotDocument(ScreenshotFrame.Blank(20, 16)); var before = document.State;
        var one = ScreenshotCanvasResize.Create(before, 19, 15, true);
        var two = ScreenshotCanvasResize.Create(before, 18, 14, true);
        Assert.AreEqual(Vector2.Zero, one.Offset); Assert.AreEqual(-Vector2.One, two.Offset);
        foreach (var anchor in new[] { ScreenshotCanvasViewAnchor.TopLeft, ScreenshotCanvasViewAnchor.BottomRight })
        {
            var first = one.Offset - Origin(ScreenshotCanvasResizeViews.DetailSource(one.Width, one.Height, anchor));
            var second = two.Offset - Origin(ScreenshotCanvasResizeViews.DetailSource(two.Width, two.Height, anchor));
            Assert.AreEqual(anchor == ScreenshotCanvasViewAnchor.TopLeft ? first - Vector2.One : first, second);
        }
        Assert.AreSame(before, document.State); Assert.IsFalse(document.CanUndo);
    }

    [TestMethod]
    public void LeftTopAnchoredResizeKeepsOriginalTopLeftPixelsStable()
    {
        var document = new ScreenshotDocument(ScreenshotFrame.Blank(20, 16));
        foreach (var size in new[] { (19, 15), (21, 17), (1, 1) })
        {
            var layout = ScreenshotCanvasResize.Create(document.State, size.Item1, size.Item2, false);
            var source = ScreenshotCanvasResizeViews.DetailSource(layout.Width, layout.Height, ScreenshotCanvasViewAnchor.TopLeft);
            Assert.AreEqual(new Vector2(12, 12), layout.Offset - Origin(source));
        }
    }

    [TestMethod]
    public void InvalidDimensionsAndUnknownAnchorsAreRejected()
    {
        foreach (var size in new[] { (0, 1), (1, 0), (-1, 1), (16385, 1), (1, 16385), (16384, 16384) })
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ScreenshotCanvasResizeViews.DetailSource(size.Item1, size.Item2, ScreenshotCanvasViewAnchor.TopLeft));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ScreenshotCanvasResizeViews.DetailSource(1, 1, (ScreenshotCanvasViewAnchor)99));
    }

    private static Vector2 Origin(ScreenshotBox box) => new(box.X, box.Y);
}
