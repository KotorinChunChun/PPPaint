using System.Security.Cryptography;

namespace PPPaint.Core;

public sealed record ScreenshotFileRevision(bool Exists, long Length, string Sha256)
{
    public static ScreenshotFileRevision Missing { get; } = new(false, 0, "");
    public static ScreenshotFileRevision FromBytes(ReadOnlySpan<byte> bytes) => new(true, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)));
}
public sealed record ScreenshotFileInput(byte[] Bytes, ScreenshotFileRevision Revision);
public sealed record ScreenshotFileWrite(ScreenshotFileRevision Revision, string? BackupPath);
public sealed class ScreenshotSaveConflictException() : IOException("元のファイルが編集開始後に変更または削除されています。別名で保存してください。");

public static class ScreenshotFileIO
{
    private static readonly SemaphoreSlim SaveGate = new(1, 1);
    public static async Task<ScreenshotFileInput> ReadAsync(string path)
    {
        await using var stream = OpenRead(path);
        DocumentFileFormats.ValidateInputSize(stream.Length);
        var bytes = new byte[checked((int)stream.Length)]; await stream.ReadExactlyAsync(bytes);
        return new(bytes, ScreenshotFileRevision.FromBytes(bytes));
    }
    public static async Task<ScreenshotFileRevision> RevisionAsync(string path)
    {
        try
        {
            await using var stream = OpenRead(path);
            var length = stream.Length; var hash = await SHA256.HashDataAsync(stream);
            return new(true, length, Convert.ToHexString(hash));
        }
        catch (FileNotFoundException) { return ScreenshotFileRevision.Missing; }
        catch (DirectoryNotFoundException) { return ScreenshotFileRevision.Missing; }
    }
    private static FileStream OpenRead(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);

    public static async Task<ScreenshotFileWrite> WriteAsync(string path, ReadOnlyMemory<byte> encoded, ScreenshotFileRevision expected)
    {
        if (encoded.IsEmpty || encoded.Length > DocumentFileFormats.MaximumInputBytes) throw new InvalidDataException("保存する画像のサイズが不正です。");
        path = Path.GetFullPath(path);
        await SaveGate.WaitAsync(); string? staging = null;
        try
        {
            if (await RevisionAsync(path) != expected) throw new ScreenshotSaveConflictException();
            staging = Path.Combine(Path.GetDirectoryName(path)!, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            await using (var output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            { await output.WriteAsync(encoded); await output.FlushAsync(); output.Flush(true); }
            if (await RevisionAsync(path) != expected) throw new ScreenshotSaveConflictException();
            string? backup = null;
            if (expected.Exists)
            {
                backup = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + ".pppaint-backup-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8] + Path.GetExtension(path));
                File.Replace(staging, path, backup);
            }
            else
            {
                try { File.Move(staging, path, false); }
                catch (IOException) when (File.Exists(path)) { throw new ScreenshotSaveConflictException(); }
            }
            staging = null;
            return new(ScreenshotFileRevision.FromBytes(encoded.Span), backup);
        }
        finally
        {
            if (staging is not null) { try { File.Delete(staging); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            SaveGate.Release();
        }
    }
}
