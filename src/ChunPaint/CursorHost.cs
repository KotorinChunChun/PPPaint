using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChunPaint;

public sealed class CursorHost : ContentControl
{
    private InputSystemCursor? _cursor;
    private InputSystemCursorShape? _cursorShape;

    public CursorHost()
    {
        Unloaded += CursorHost_Unloaded;
    }

    public void SetCursorShape(InputSystemCursorShape shape)
    {
        if (_cursorShape == shape)
        {
            return;
        }

        var next = InputSystemCursor.Create(shape);
        var previous = _cursor;
        _cursor = next;
        _cursorShape = shape;
        ProtectedCursor = next;
        previous?.Dispose();
    }

    private void CursorHost_Unloaded(object sender, RoutedEventArgs e)
    {
        ProtectedCursor = null;
        _cursor?.Dispose();
        _cursor = null;
        _cursorShape = null;
    }
}
