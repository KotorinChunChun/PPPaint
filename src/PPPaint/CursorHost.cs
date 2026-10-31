using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PPPaint;

public sealed class CursorHost : ContentControl
{
    private InputCursor? _cursor;
    private string? _cursorKey;

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
        SetCursor(
            $"resource:{resourceName}",
            InputDesktopNamedResourceCursor.CreateFromModule(moduleName, resourceName));
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
    }
}
