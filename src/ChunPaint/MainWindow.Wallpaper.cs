using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using Windows.Storage;

namespace ChunPaint;

public sealed partial class MainWindow
{
    private const uint SpiSetDesktopWallpaper = 0x0014;
    private const uint SpifUpdateIniFile = 0x0001;
    private const uint SpifSendChange = 0x0002;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(
        uint action,
        uint parameter,
        string value,
        uint updateFlags);

    private async void Wallpaper_Click(object sender, RoutedEventArgs e)
    {
        var layout = Enum.TryParse((sender as FrameworkElement)?.Tag?.ToString(), out WallpaperLayout selected)
            ? selected
            : WallpaperLayout.Fill;
        var layoutName = GetWallpaperLayoutName(layout);
        var dialog = new ContentDialog
        {
            XamlRoot = PaintCanvas.XamlRoot,
            Title = "デスクトップの背景として設定",
            Content = $"現在の画像を「{layoutName}」でデスクトップの背景に設定します。\n表示中のレイヤーが1枚の画像に合成されます。",
            PrimaryButtonText = "設定",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary
        };
        ConfigureDialogAccessibility(dialog);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            await SetDesktopWallpaperAsync(layout);
            await ShowInfoAsync("デスクトップの背景", $"画像を「{layoutName}」で設定しました。");
        }
        catch (Exception exception)
        {
            await ShowInfoAsync("デスクトップの背景", $"背景を設定できませんでした。\n{exception.Message}");
        }
    }

    private async Task SetDesktopWallpaperAsync(WallpaperLayout layout)
    {
        EnsureSurface(PaintCanvas);
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChunPaint",
            "Wallpaper");
        Directory.CreateDirectory(directory);
        var folder = await StorageFolder.GetFolderFromPathAsync(directory);
        var file = await folder.CreateFileAsync("ChunPaintWallpaper.bmp", CreationCollisionOption.ReplaceExisting);
        using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
        using (var composite = CreateComposite())
        {
            await composite.SaveAsync(stream, CanvasBitmapFileFormat.Bmp);
        }

        using var desktop = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true)
            ?? throw new InvalidOperationException("デスクトップ設定を開けませんでした。");
        var oldStyle = desktop.GetValue("WallpaperStyle");
        var oldTile = desktop.GetValue("TileWallpaper");
        try
        {
            var (style, tile) = layout switch
            {
                WallpaperLayout.Tile => ("0", "1"),
                WallpaperLayout.Center => ("0", "0"),
                _ => ("10", "0")
            };
            desktop.SetValue("WallpaperStyle", style, RegistryValueKind.String);
            desktop.SetValue("TileWallpaper", tile, RegistryValueKind.String);
            if (!SystemParametersInfo(
                    SpiSetDesktopWallpaper,
                    0,
                    file.Path,
                    SpifUpdateIniFile | SpifSendChange))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        catch
        {
            RestoreRegistryValue(desktop, "WallpaperStyle", oldStyle);
            RestoreRegistryValue(desktop, "TileWallpaper", oldTile);
            throw;
        }
    }

    private static void RestoreRegistryValue(RegistryKey key, string name, object? value)
    {
        if (value is null)
        {
            key.DeleteValue(name, throwOnMissingValue: false);
        }
        else
        {
            key.SetValue(name, value, RegistryValueKind.String);
        }
    }

    private static string GetWallpaperLayoutName(WallpaperLayout layout) => layout switch
    {
        WallpaperLayout.Tile => "並べて表示",
        WallpaperLayout.Center => "中央に表示",
        _ => "画面に合わせる"
    };

    private enum WallpaperLayout
    {
        Fill,
        Tile,
        Center
    }
}
