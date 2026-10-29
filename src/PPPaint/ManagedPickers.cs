using Windows.Storage;
using Windows.Storage.Pickers;

namespace PPPaint;

internal static class ManagedPickers
{
    internal static int OpenCount { get; private set; }
    internal static async Task<StorageFile?> PickSingleFileManagedAsync(this FileOpenPicker picker)
    {
        OpenCount++;
        try { return await picker.PickSingleFileAsync(); }
        finally { OpenCount--; }
    }
    internal static async Task<IReadOnlyList<StorageFile>> PickMultipleFilesManagedAsync(this FileOpenPicker picker)
    {
        OpenCount++;
        try { return await picker.PickMultipleFilesAsync(); }
        finally { OpenCount--; }
    }
    internal static async Task<StorageFile?> PickSaveFileManagedAsync(this FileSavePicker picker)
    {
        OpenCount++;
        try { return await picker.PickSaveFileAsync(); }
        finally { OpenCount--; }
    }
    internal static async Task<StorageFolder?> PickSingleFolderManagedAsync(this FolderPicker picker)
    {
        OpenCount++;
        try { return await picker.PickSingleFolderAsync(); }
        finally { OpenCount--; }
    }
}
