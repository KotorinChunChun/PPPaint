using System.Collections.Immutable;
using System.Numerics;

namespace PPPaint.Core;

public sealed record ScreenshotContextTarget(ScreenshotBox? Region, ImmutableArray<string> SelectedIds)
{
    public bool CanDelete => Region is not null || !SelectedIds.IsDefaultOrEmpty;
    public bool CanReorder => !SelectedIds.IsDefaultOrEmpty;

    // Hit testing is performed by the caller at the current zoom before resolving the menu target.
    public static ScreenshotContextTarget Resolve(ScreenshotBox? region, IReadOnlyList<ScreenshotAnnotation> selected, ScreenshotAnnotation? hit, Vector2 point)
    {
        var ids = selected.Select(item => item.Id).ToImmutableArray();
        if (region is { } area && area.Contains(point)) return new(region, ids);
        if (hit is not null) return new(null, ids.Contains(hit.Id) ? ids : [hit.Id]);
        return new(null, selected.Count > 0 && ScreenshotGeometry.Bounds(selected).Contains(point) ? ids : []);
    }
}
