using Windows.Storage;
using Windows.Storage.Streams;

namespace PPPaint.Imaging;

public static class DocumentFileWriter
{
    public static async Task WriteAsync(StorageFile file, Func<IRandomAccessStream, Task> write)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(write);

        using var transaction = await file.OpenTransactedWriteAsync();
        transaction.Stream.Size = 0;
        transaction.Stream.Seek(0);
        await write(transaction.Stream);
        await transaction.CommitAsync();
    }
}
