using System.Collections.Immutable;
using System.Numerics;

namespace PPPaint.Core;

public sealed record ScreenshotRasterMergeImage(ScreenshotFrame Frame, PixelRect Bounds);
public sealed record ScreenshotRasterMergeResult(ScreenshotState State, string MergedId);

public sealed class ScreenshotRasterMerge
{
    private readonly ScreenshotState _source;
    private readonly ImmutableHashSet<string> _selectedIds;
    public PixelRect Bounds { get; }
    public ScreenshotState Scene { get; }

    private ScreenshotRasterMerge(ScreenshotState source, ImmutableHashSet<string> selectedIds, PixelRect bounds)
    {
        _source = source; _selectedIds = selectedIds; Bounds = bounds;
        // Keep the original sampling domain. Cropping before blur/mosaic or text
        // layout can change the image, so the renderer crops the final pixels.
        Scene = new(ScreenshotFrame.Transparent(source.Frame.Width, source.Frame.Height),
            source.Annotations.Where(item => selectedIds.Contains(item.Id))
                .Select(item => item.Tool == ScreenshotTool.Eraser ? item with { EraserBackground = Rgba32.Transparent } : item).ToImmutableArray(),
            source.NextNumber, Rgba32.Transparent);
    }

    public static ScreenshotRasterMerge Create(ScreenshotState source, IReadOnlySet<string> selectedIds)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(selectedIds);
        if (selectedIds.Count == 0) throw new ArgumentException("統合する図形を選択してください。", nameof(selectedIds));
        var ids = selectedIds.ToImmutableHashSet(StringComparer.Ordinal);
        var selected = source.Annotations.Where(item => ids.Contains(item.Id)).ToArray();
        if (selected.Length != ids.Count) throw new ArgumentException("選択した図形が見つかりません。", nameof(selectedIds));
        var canvas = new ScreenshotBox(0, 0, source.Frame.Width, source.Frame.Height);
        ScreenshotBox? combined = null;
        foreach (var item in selected)
        {
            var drawing = ScreenshotEditing.DrawingBounds(item);
            if (!float.IsFinite(drawing.X) || !float.IsFinite(drawing.Y) || !float.IsFinite(drawing.Right) || !float.IsFinite(drawing.Bottom))
                throw new ArgumentException("図形の座標が不正です。", nameof(source));
            var visible = ScreenshotEditing.Intersection(drawing, canvas);
            if (visible is { } area && item.ClipBounds is { } clip) visible = ScreenshotEditing.Intersection(area, clip);
            if (visible is not { } next) continue;
            combined = combined is { } previous
                ? new(Math.Min(previous.X, next.X), Math.Min(previous.Y, next.Y), Math.Max(previous.Right, next.Right) - Math.Min(previous.X, next.X), Math.Max(previous.Bottom, next.Bottom) - Math.Min(previous.Y, next.Y))
                : next;
        }
        if (combined is not { } bounds) throw new InvalidOperationException("選択図形にキャンバス内の描画領域がありません。");
        var left = (int)Math.Floor(bounds.X); var top = (int)Math.Floor(bounds.Y);
        var right = (int)Math.Ceiling(bounds.Right); var bottom = (int)Math.Ceiling(bounds.Bottom);
        return new(source, ids, new(left, top, right - left, bottom - top));
    }

    public ScreenshotRasterMergeResult Apply(ScreenshotState current, ScreenshotRasterMergeImage image)
    {
        ArgumentNullException.ThrowIfNull(current); ArgumentNullException.ThrowIfNull(image); ArgumentNullException.ThrowIfNull(image.Frame);
        if (!ReferenceEquals(current, _source)) throw new InvalidOperationException("編集中の画像が変わったため統合できません。");
        var box = image.Bounds;
        if (box.X < 0 || box.Y < 0 || box.Width < 1 || box.Height < 1 || (long)box.X + box.Width > current.Frame.Width || (long)box.Y + box.Height > current.Frame.Height
            || image.Frame.Width != box.Width || image.Frame.Height != box.Height)
            throw new ArgumentException("統合画像の寸法と配置が一致しません。", nameof(image));
        var merged = ScreenshotDocument.ImageObject(image.Frame, new Vector2(box.X, box.Y), "統合した図形");
        var front = -1;
        for (var index = 0; index < current.Annotations.Length; index++) if (_selectedIds.Contains(current.Annotations[index].Id)) front = index;
        var annotations = ImmutableArray.CreateBuilder<ScreenshotAnnotation>();
        for (var index = 0; index < current.Annotations.Length; index++)
        {
            var item = current.Annotations[index];
            if (!_selectedIds.Contains(item.Id)) annotations.Add(item);
            else if (index == front) annotations.Add(merged);
        }
        return new(current with { Annotations = annotations.ToImmutable() }, merged.Id);
    }
}
