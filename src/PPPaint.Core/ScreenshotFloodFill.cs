using System.Collections;
using System.Numerics;
using System.Runtime.InteropServices;

namespace PPPaint.Core;

public static class ScreenshotFloodFill
{
    public static ScreenshotAnnotation? CreatePatch(ReadOnlyMemory<byte> pixels, int width, int height, int x, int y, Rgba32 color)
    {
        ScreenshotFrame.ValidateSize(width, height);
        if (pixels.Length != width * height * 4) throw new ArgumentException("画素数が画像寸法と一致しません。", nameof(pixels));
        if ((uint)x >= (uint)width || (uint)y >= (uint)height) return null;

        var target = MemoryMarshal.Cast<byte, uint>(pixels.Span)[y * width + x];
        ReadOnlySpan<byte> replacementBytes = [color.B, color.G, color.R, 255];
        var replacement = MemoryMarshal.Read<uint>(replacementBytes);
        if (target == replacement) return null;

        var visited = new BitArray(width * height);
        var pending = new Stack<(int Y, int Left, int Right)>();
        var minX = x;
        var maxX = x;
        var minY = y;
        var maxY = y;
        QueueRuns(y, x, x);
        while (pending.TryPop(out var run))
        {
            if (run.Y > 0) QueueRuns(run.Y - 1, run.Left, run.Right);
            if (run.Y + 1 < height) QueueRuns(run.Y + 1, run.Left, run.Right);
        }

        var patchWidth = maxX - minX + 1;
        var patchHeight = maxY - minY + 1;
        var patch = new byte[patchWidth * patchHeight * 4];
        var patchPixels = MemoryMarshal.Cast<byte, uint>(patch.AsSpan());
        for (var row = 0; row < patchHeight; row++)
        {
            var sourceOffset = (minY + row) * width + minX;
            var patchOffset = row * patchWidth;
            for (var column = 0; column < patchWidth; column++)
                if (visited[sourceOffset + column]) patchPixels[patchOffset + column] = replacement;
        }
        return ScreenshotDocument.ImageObject(new ScreenshotFrame(patchWidth, patchHeight, patch), new Vector2(minX, minY), "塗りつぶし");

        void QueueRuns(int row, int first, int last)
        {
            var source = MemoryMarshal.Cast<byte, uint>(pixels.Span);
            var offset = row * width;
            for (var column = first; column <= last; column++)
            {
                if (visited[offset + column] || source[offset + column] != target) continue;
                var left = column;
                var right = column;
                while (left > 0 && !visited[offset + left - 1] && source[offset + left - 1] == target) left--;
                while (right + 1 < width && !visited[offset + right + 1] && source[offset + right + 1] == target) right++;
                for (var filled = left; filled <= right; filled++) visited[offset + filled] = true;
                pending.Push((row, left, right));
                minX = Math.Min(minX, left);
                maxX = Math.Max(maxX, right);
                minY = Math.Min(minY, row);
                maxY = Math.Max(maxY, row);
                column = right;
            }
        }
    }
}
