namespace ChunPaint.Core;

public sealed class PixelSurface
{
    public const long MaximumPixelBytes = 256L * 1024 * 1024;
    private readonly byte[] _bgra;

    public PixelSurface(int width, int height, Rgba32 background)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if ((long)width * height * 4 > MaximumPixelBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "The pixel surface exceeds the 256 MiB safety limit.");
        }

        Width = width;
        Height = height;
        _bgra = new byte[checked(width * height * 4)];
        Clear(background);
    }

    public int Width { get; }
    public int Height { get; }

    public Rgba32 this[int x, int y]
    {
        get
        {
            var offset = GetOffset(x, y);
            return new Rgba32(_bgra[offset + 2], _bgra[offset + 1], _bgra[offset], _bgra[offset + 3]);
        }
        set
        {
            var offset = GetOffset(x, y);
            _bgra[offset] = value.B;
            _bgra[offset + 1] = value.G;
            _bgra[offset + 2] = value.R;
            _bgra[offset + 3] = value.A;
        }
    }

    public void Clear(Rgba32 color)
    {
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                this[x, y] = color;
            }
        }
    }

    public byte[] Snapshot() => (byte[])_bgra.Clone();

    public void Restore(ReadOnlySpan<byte> snapshot)
    {
        if (snapshot.Length != _bgra.Length)
        {
            throw new ArgumentException("Snapshot dimensions do not match this surface.", nameof(snapshot));
        }
        snapshot.CopyTo(_bgra);
    }

    public int FloodFill(int startX, int startY, Rgba32 replacement)
    {
        if (this[startX, startY] == replacement)
        {
            return 0;
        }

        var region = FloodFillRegion(startX, startY);
        foreach (var index in region)
        {
            this[index % Width, index / Width] = replacement;
        }
        return region.Length;
    }

    public int[] FloodFillRegion(int startX, int startY)
    {
        var target = this[startX, startY];
        var visited = new bool[checked(Width * Height)];
        var region = new List<int>();
        var pending = new Stack<int>();
        pending.Push((startY * Width) + startX);
        while (pending.Count > 0)
        {
            var index = pending.Pop();
            if (visited[index])
            {
                continue;
            }

            visited[index] = true;
            var x = index % Width;
            var y = index / Width;
            if (this[x, y] != target)
            {
                continue;
            }

            region.Add(index);
            if (x > 0) pending.Push(index - 1);
            if (x + 1 < Width) pending.Push(index + 1);
            if (y > 0) pending.Push(index - Width);
            if (y + 1 < Height) pending.Push(index + Width);
        }
        return region.ToArray();
    }

    private int GetOffset(int x, int y)
    {
        if ((uint)x >= Width) throw new ArgumentOutOfRangeException(nameof(x));
        if ((uint)y >= Height) throw new ArgumentOutOfRangeException(nameof(y));
        return ((y * Width) + x) * 4;
    }
}
