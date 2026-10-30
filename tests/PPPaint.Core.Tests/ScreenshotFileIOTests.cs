namespace PPPaint.Core.Tests;

[TestClass]
public sealed class ScreenshotFileIOTests
{
    private static async Task WithFolder(Func<string, Task> test)
    {
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath());
        var folder = Path.GetFullPath(Path.Combine(temporaryRoot, "pppaint-file-test-" + Guid.NewGuid().ToString("N")));
        if (!folder.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("試験フォルダーが一時領域の外です。");
        Directory.CreateDirectory(folder);
        try { await test(folder); } finally { Directory.Delete(folder, true); }
    }
    [TestMethod]
    public async Task OverwritesKeepExactOriginalBackupsAndRepeatedBackupsAreIndependent() => await WithFolder(async folder =>
    {
        var path = Path.Combine(folder, "source.jpg"); byte[] original = [1, 2, 3, 4]; await File.WriteAllBytesAsync(path, original);
        var input = await ScreenshotFileIO.ReadAsync(path); CollectionAssert.AreEqual(original, input.Bytes);
        var first = await ScreenshotFileIO.WriteAsync(path, new byte[] { 9, 8 }, input.Revision);
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(first.BackupPath!));
        var second = await ScreenshotFileIO.WriteAsync(path, new byte[] { 7 }, first.Revision);
        Assert.AreNotEqual(first.BackupPath, second.BackupPath); CollectionAssert.AreEqual(new byte[] { 9, 8 }, await File.ReadAllBytesAsync(second.BackupPath!));
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(first.BackupPath!)); CollectionAssert.AreEqual(new byte[] { 7 }, await File.ReadAllBytesAsync(path));
        Assert.HasCount(0, Directory.GetFiles(folder, "*.tmp"));
    });
    [TestMethod]
    public async Task SameLengthExternalChangeDeletionAndNewFileCollisionNeverOverwrite() => await WithFolder(async folder =>
    {
        var path = Path.Combine(folder, "source.png"); await File.WriteAllBytesAsync(path, new byte[] { 1, 2 }); var revision = await ScreenshotFileIO.RevisionAsync(path);
        await File.WriteAllBytesAsync(path, new byte[] { 3, 4 });
        await Assert.ThrowsExactlyAsync<ScreenshotSaveConflictException>(() => ScreenshotFileIO.WriteAsync(path, new byte[] { 9 }, revision));
        CollectionAssert.AreEqual(new byte[] { 3, 4 }, await File.ReadAllBytesAsync(path));
        File.Delete(path); await Assert.ThrowsExactlyAsync<ScreenshotSaveConflictException>(() => ScreenshotFileIO.WriteAsync(path, new byte[] { 9 }, revision)); Assert.IsFalse(File.Exists(path));
        await File.WriteAllBytesAsync(path, new byte[] { 5 });
        await Assert.ThrowsExactlyAsync<ScreenshotSaveConflictException>(() => ScreenshotFileIO.WriteAsync(path, new byte[] { 9 }, ScreenshotFileRevision.Missing));
        CollectionAssert.AreEqual(new byte[] { 5 }, await File.ReadAllBytesAsync(path)); Assert.HasCount(1, Directory.GetFiles(folder));
    });
    [TestMethod]
    public async Task SaveFailureKeepsOriginalAndCanRetryWithoutOrphanedStagingFiles() => await WithFolder(async folder =>
    {
        var path = Path.Combine(folder, "source.png"); await File.WriteAllBytesAsync(path, new byte[] { 1, 2 }); var revision = await ScreenshotFileIO.RevisionAsync(path);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try { await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => ScreenshotFileIO.WriteAsync(path, new byte[] { 9 }, revision)); }
        finally { File.SetAttributes(path, FileAttributes.Normal); }
        CollectionAssert.AreEqual(new byte[] { 1, 2 }, await File.ReadAllBytesAsync(path)); Assert.HasCount(0, Directory.GetFiles(folder, "*.tmp"));
        var result = await ScreenshotFileIO.WriteAsync(path, new byte[] { 9 }, revision); Assert.IsNotNull(result.BackupPath);
    });
    [TestMethod]
    public async Task NewCopyDoesNotTouchOriginalAndConcurrentStaleSavesOnlyCommitOnce() => await WithFolder(async folder =>
    {
        var source = Path.Combine(folder, "source.png"); await File.WriteAllBytesAsync(source, new byte[] { 1, 2 }); var initial = await ScreenshotFileIO.RevisionAsync(source);
        var copy = await ScreenshotFileIO.WriteAsync(Path.Combine(folder, "copy.png"), new byte[] { 8 }, ScreenshotFileRevision.Missing); Assert.IsNull(copy.BackupPath);
        var tasks = new[] { new byte[] { 6 }, new byte[] { 7 } }.Select(async bytes => { try { await ScreenshotFileIO.WriteAsync(source, bytes, initial); return true; } catch (ScreenshotSaveConflictException) { return false; } });
        var results = await Task.WhenAll(tasks); Assert.AreEqual(1, results.Count(r => r));
        var backups = Directory.GetFiles(folder, "*.pppaint-backup-*"); Assert.HasCount(1, backups); CollectionAssert.AreEqual(new byte[] { 1, 2 }, await File.ReadAllBytesAsync(backups[0]));
    });
}
