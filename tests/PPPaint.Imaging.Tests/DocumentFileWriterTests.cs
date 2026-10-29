using PPPaint.Core;
using Windows.Storage;

namespace PPPaint.Imaging.Tests;

[TestClass]
public sealed class DocumentFileWriterTests
{
    [TestMethod]
    public async Task WriteFailure_PreservesExistingFile()
    {
        await WithFileAsync(async (file, path) =>
        {
            await Assert.ThrowsExactlyAsync<IOException>(() => DocumentFileWriter.WriteAsync(file, async target =>
            {
                var stream = target.AsStreamForWrite();
                await stream.WriteAsync(new byte[] { 99, 98 });
                await stream.FlushAsync();
                throw new IOException("書き込み途中の失敗を再現");
            }));
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(path));
        });
    }

    [TestMethod]
    public async Task SuccessfulWrite_ReplacesAndTruncatesExistingFile()
    {
        await WithFileAsync(async (file, path) =>
        {
            await DocumentFileWriter.WriteAsync(file, async target =>
            {
                var stream = target.AsStreamForWrite();
                await stream.WriteAsync(new byte[] { 9 });
                await stream.FlushAsync();
            });
            CollectionAssert.AreEqual(new byte[] { 9 }, await File.ReadAllBytesAsync(path));
        });
    }

    [TestMethod]
    public async Task ProjectWrite_RoundTripsThroughTransaction()
    {
        await WithFileAsync(async (file, path) =>
        {
            var project = new PPPaintProject(1, 1, 0,
                [new PPPaintProjectLayer("背景", true, new byte[] { 3, 2, 1, 255 }, "background")]);
            await DocumentFileWriter.WriteAsync(file, async target =>
            {
                var stream = target.AsStreamForWrite();
                PPPaintProjectArchive.Save(stream, project);
                await stream.FlushAsync();
            });
            using var input = File.OpenRead(path);
            var restored = PPPaintProjectArchive.Load(input);
            CollectionAssert.AreEqual(project.Layers[0].Pixels, restored.Layers[0].Pixels);
        });
    }

    private static async Task WithFileAsync(Func<StorageFile, string, Task> test)
    {
        var path = Path.Combine(Path.GetTempPath(), $"pppaint-save-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3, 4 });
            await test(await StorageFile.GetFileFromPathAsync(path), path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
