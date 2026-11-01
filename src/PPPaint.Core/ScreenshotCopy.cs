namespace PPPaint.Core;

public static class ScreenshotCopy
{
    public static ScreenshotTransfer Capture(ScreenshotState state, ScreenshotBox? region, IReadOnlySet<string> selectedIds)
    {
        // A rectangular selection retains the existing full-canvas Ctrl+C contract.
        if (region is not null || selectedIds.Count == 0)
            return ScreenshotEditing.Capture(state, new(0, 0, state.Frame.Width, state.Frame.Height));
        var area = ScreenshotCrop.Resolve(state, null, selectedIds)
            ?? throw new InvalidOperationException("選択図形にコピーできる範囲がありません。");
        return ScreenshotEditing.Capture(state, area, selectedIds);
    }
}
