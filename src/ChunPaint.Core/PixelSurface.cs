namespace ChunPaint.Core;

public sealed class PixelSurface
{
    private readonly byte[] _bgra;

    public PixelSurface(int width, int height, Rgba32 background)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

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
        var target = this[startX, startY];
        if (target == replacement)
        {
            return 0;
        }

        var filled = 0;
        var pending = new Stack<int>();
        pending.Push((startY * Width) + startX);
        while (pending.Count > 0)
        {
            var index = pending.Pop();
            var x = index % Width;
            var y = index / Width;
            if (this[x, y] != target)
            {
                continue;
            }

            this[x, y] = replacement;
            filled++;
            if (x > 0) pending.Push(index - 1);
            if (x + 1 < Width) pending.Push(index + 1);
            if (y > 0) pending.Push(index - Width);
            if (y + 1 < Height) pending.Push(index + Width);
        }
        return filled;
    }

    private int GetOffset(int x, int y)
    {
        if ((uint)x >= Width) throw new ArgumentOutOfRangeException(nameof(x));
        if ((uint)y >= Height) throw new ArgumentOutOfRangeException(nameof(y));
        return ((y * Width) + x) * 4;
    }
}
