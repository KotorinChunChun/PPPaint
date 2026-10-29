using PPPaint.Core;
using Windows.System;

namespace PPPaint;

public sealed partial class MainWindow
{
    private static TransformModifiers GetTransformModifiers(VirtualKeyModifiers keyModifiers)
    {
        var modifiers = TransformModifiers.None;
        if ((keyModifiers & VirtualKeyModifiers.Shift) != 0)
        {
            modifiers |= TransformModifiers.Shift;
        }
        if ((keyModifiers & VirtualKeyModifiers.Control) != 0)
        {
            modifiers |= TransformModifiers.Control;
        }
        if ((keyModifiers & VirtualKeyModifiers.Menu) != 0)
        {
            modifiers |= TransformModifiers.Alt;
        }
        return modifiers;
    }

    private static bool HasModifier(TransformModifiers modifiers, TransformModifiers modifier) =>
        (modifiers & modifier) != 0;
}
