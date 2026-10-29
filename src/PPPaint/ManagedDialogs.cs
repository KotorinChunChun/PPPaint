using Microsoft.UI.Xaml.Controls;

namespace PPPaint;

internal static class ManagedDialogs
{
    internal static int OpenCount { get; private set; }
    public static async Task<ContentDialogResult> ShowManagedAsync(this ContentDialog dialog)
    {
        dialog.Title = WindowTitle.Create(dialog.Title?.ToString());
        OpenCount++;
        try { return await dialog.ShowAsync(); }
        finally { OpenCount--; }
    }
}
