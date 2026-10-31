using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PPPaint;

public sealed class CursorHost : ContentControl
{
    private InputCursor? _cursor;
    private string? _cursorKey;
    private nint _cursorModule;

    public CursorHost()
    {
        Unloaded += CursorHost_Unloaded;
    }

    public void SetCursorShape(InputSystemCursorShape shape)
    {
        if (_cursorKey == $"system:{shape}") return;
        SetCursor($"system:{shape}", InputSystemCursor.Create(shape));
    }

    public void SetCursorResource(string resourceName)
    {
        if (_cursorKey == $"resource:{resourceName}") return;
        var moduleName = Path.Combine(AppContext.BaseDirectory, "Assets", "PPPaintCursors.dll");
        if (!File.Exists(moduleName))
        {
            throw new FileNotFoundException("PPPaintのカーソルリソースが見つかりません。", moduleName);
        }
        // Keep the resource module loaded for the lifetime of its input cursors.
        if (_cursorModule == 0) _cursorModule = System.Runtime.InteropServices.NativeLibrary.Load(moduleName);
        var resourceId = resourceName switch
        {
            "PPPaintCursorPencil" => 1001u,
            "PPPaintCursorPicker" => 1002u,
            "PPPaintCursorInvisible" => 1003u,
            _ => throw new ArgumentOutOfRangeException(nameof(resourceName))
        };
        SetCursor($"resource:{resourceName}", InputDesktopResourceCursor.CreateFromModule(moduleName, resourceId));
    }

    private void SetCursor(string key, InputCursor next)
    {
        if (_cursorKey == key)
        {
            next.Dispose();
            return;
        }

        var previous = _cursor;
        _cursor = next;
        _cursorKey = key;
        ProtectedCursor = next;
        previous?.Dispose();
    }

    private void CursorHost_Unloaded(object sender, RoutedEventArgs e)
    {
        ProtectedCursor = null;
        _cursor?.Dispose();
        _cursor = null;
        _cursorKey = null;
        if (_cursorModule != 0) { System.Runtime.InteropServices.NativeLibrary.Free(_cursorModule); _cursorModule = 0; }
    }
}
