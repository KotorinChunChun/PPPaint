namespace ChunPaint.Core;

/// <summary>内容を外部へ公開しない共有画素ブロック。参照とLengthで保持容量を集計する。</summary>
public sealed class PixelBlock
{
    private readonly byte[] _bytes;
    internal PixelBlock(ReadOnlySpan<byte> bytes) => _bytes = bytes.ToArray();
    public int Length => _bytes.Length;
    internal bool Matches(ReadOnlySpan<byte> bytes) => bytes.SequenceEqual(_bytes);
    internal void CopyTo(Span<byte> destination) => _bytes.CopyTo(destination);
}

/// <summary>変更のない固定ブロックを前世代と共有する不変BGRAスナップショット。</summary>
public sealed class PixelBlockSnapshot
{
    public const int BlockSize = 64 * 1024;
    public int ByteLength { get; }
    public IReadOnlyList<PixelBlock> Blocks { get; }

    private PixelBlockSnapshot(int byteLength, PixelBlock[] blocks)
    {
        ByteLength = byteLength;
        Blocks = Array.AsReadOnly(blocks);
    }

    public static PixelBlockSnapshot Capture(byte[] pixels, PixelBlockSnapshot? previous = null)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Length % 4 != 0) throw new ArgumentException("BGRA画素は4バイト単位で指定してください。", nameof(pixels));
        var blocks = new PixelBlock[(int)(((long)pixels.Length + BlockSize - 1) / BlockSize)];
        for (var index = 0; index < blocks.Length; index++)
        {
            var offset = index * BlockSize;
            var bytes = pixels.AsSpan(offset, Math.Min(BlockSize, pixels.Length - offset));
            var oldBlock = previous is not null && index < previous.Blocks.Count ? previous.Blocks[index] : null;
            blocks[index] = oldBlock is not null && oldBlock.Matches(bytes) ? oldBlock : new PixelBlock(bytes);
        }
        return new PixelBlockSnapshot(pixels.Length, blocks);
    }

    public byte[] ToArray()
    {
        var pixels = new byte[ByteLength];
        for (var index = 0; index < Blocks.Count; index++)
            Blocks[index].CopyTo(pixels.AsSpan(index * BlockSize, Blocks[index].Length));
        return pixels;
    }
}
