using System.Numerics;

namespace PPPaint.Core;

// A non-destructive drag plan: the source document changes only on commit.
public sealed class ScreenshotRegionLift
{
    public ScreenshotState Source { get; }
    public ScreenshotState Cut { get; }
    public ScreenshotAnnotation Image { get; }
    public ScreenshotRegionLift(ScreenshotState source, ScreenshotState prepared, PixelRect area, ScreenshotFrame pixels)
    {
        if (pixels.Width != area.Width || pixels.Height != area.Height) throw new ArgumentException("選択範囲と画像寸法が一致しません。");
        Source = source; Cut = ScreenshotEditing.RemoveRange(prepared, area);
        Image = ScreenshotDocument.ImageObject(pixels, new(area.X, area.Y), "切り離した範囲");
    }
    public ScreenshotState Preview(Vector2 delta, bool copy) => (copy ? Source : Cut) with
    {
        Annotations = (copy ? Source : Cut).Annotations.Add(Image.Move(ScreenshotManipulation.WholePixels(delta)))
    };
    public void Commit(ScreenshotDocument document, ScreenshotState preview)
    {
        if (!ReferenceEquals(document.State, Source)) throw new InvalidOperationException("編集中の画像が変わったため範囲移動を確定できません。");
        document.Apply(preview);
    }
}
