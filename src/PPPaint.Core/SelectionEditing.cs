using System.Numerics;
namespace PPPaint.Core;

public enum SelectionCombineMode { Replace, Add, Subtract }

public static class SelectionEditing
{
    public static SelectionRegion Ellipse(int width, int height, PixelRect bounds)
    {
        var mask = Allocate(width, height);
        if (bounds.Width < 1 || bounds.Height < 1 || bounds.X < 0 || bounds.Y < 0 || bounds.Right > width || bounds.Bottom > height) throw new ArgumentOutOfRangeException(nameof(bounds));
        for (var y = bounds.Y; y < bounds.Bottom; y++) for (var x = bounds.X; x < bounds.Right; x++)
        {
            var hits = 0;
            for (var sy = 0; sy < 4; sy++) for (var sx = 0; sx < 4; sx++)
            {
                var dx = (x + (sx + .5) / 4 - bounds.X) * 2 / bounds.Width - 1;
                var dy = (y + (sy + .5) / 4 - bounds.Y) * 2 / bounds.Height - 1;
                if (dx * dx + dy * dy <= 1) hits++;
            }
            mask[y * width + x] = (byte)Math.Round(hits * 255d / 16);
        }
        return SelectionRegion.CreateCoverageMask(width, height, mask);
    }

    public static SelectionRegion MagicWand(PixelSurface source, int x, int y, byte tolerance, bool contiguous)
    {
        ArgumentNullException.ThrowIfNull(source);
        if ((uint)x >= source.Width || (uint)y >= source.Height) throw new ArgumentOutOfRangeException(nameof(x));
        var mask = Allocate(source.Width, source.Height); var sample = source[x, y];
        bool Matches(int i)
        {
            var p = source[i % source.Width, i / source.Width];
            return Math.Abs(p.R - sample.R) <= tolerance && Math.Abs(p.G - sample.G) <= tolerance && Math.Abs(p.B - sample.B) <= tolerance && Math.Abs(p.A - sample.A) <= tolerance;
        }
        if (!contiguous)
        {
            for (var i = 0; i < mask.Length; i++) if (Matches(i)) mask[i] = 255;
        }
        else
        {
            var visited = new bool[mask.Length]; var pending = new Queue<int>();
            void Add(int px, int py)
            {
                if ((uint)px >= source.Width || (uint)py >= source.Height) return;
                var index = py * source.Width + px;
                if (visited[index]) return; visited[index] = true;
                if (Matches(index)) pending.Enqueue(index);
            }
            Add(x, y);
            while (pending.TryDequeue(out var index))
            {
                mask[index] = 255;
                var px = index % source.Width; var py = index / source.Width;
                Add(px - 1, py); Add(px + 1, py); Add(px, py - 1); Add(px, py + 1);
            }
        }
        return SelectionRegion.CreateCoverageMask(source.Width, source.Height, mask);
    }

    public static SelectionRegion Combine(SelectionRegion? existing, SelectionRegion incoming, int width, int height, SelectionCombineMode mode)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        var mask = Allocate(width, height);
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var a = existing?.GetCoverage(x, y) ?? 0; var b = incoming.GetCoverage(x, y);
            mask[y * width + x] = mode switch { SelectionCombineMode.Replace => b, SelectionCombineMode.Add => Math.Max(a, b), _ => (byte)Math.Max(0, a - b) };
        }
        return SelectionRegion.CreateCoverageMask(width, height, mask);
    }

    public static SelectionRegion Feather(SelectionRegion region, int width, int height, int radius)
    {
        ArgumentNullException.ThrowIfNull(region);
        if (radius < 0 || radius > 256) throw new ArgumentOutOfRangeException(nameof(radius));
        var mask = Allocate(width, height);
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) mask[y * width + x] = region.GetCoverage(x, y);
        if (radius > 0)
        {
            mask = Blur(mask, width, height, radius, true);
            mask = Blur(mask, width, height, radius, false);
        }
        return SelectionRegion.CreateCoverageMask(width, height, mask);
    }

    private static byte[] Blur(byte[] source, int width, int height, int radius, bool horizontal)
    {
        var result = new byte[source.Length]; int lines = horizontal ? height : width, length = horizontal ? width : height;
        for (var line = 0; line < lines; line++)
        {
            int Index(int p) => horizontal ? line * width + p : p * width + line;
            var sum = 0;
            for (var p = 0; p <= Math.Min(radius, length - 1); p++) sum += source[Index(p)];
            for (var p = 0; p < length; p++)
            {
                result[Index(p)] = (byte)Math.Round((double)sum / (Math.Min(length - 1, p + radius) - Math.Max(0, p - radius) + 1));
                if (p - radius >= 0) sum -= source[Index(p - radius)];
                if (p + radius + 1 < length) sum += source[Index(p + radius + 1)];
            }
        }
        return result;
    }

    private static byte[] Allocate(int width, int height)
    {
        if (width < 1 || height < 1 || (long)width * height > 64L * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(width));
        return new byte[checked(width * height)];
    }
}
