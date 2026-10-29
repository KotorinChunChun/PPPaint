using PPPaint.Core;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Input;
using System.Numerics;

namespace PPPaint;

public sealed partial class MainWindow
{
    private StrokeInputFilter? _strokeFilter;
    private float _strokePressure = 1;

    private Vector2 FilterStrokePoint(PointerRoutedEventArgs args, bool begin = false)
    {
        var pointer = args.GetCurrentPoint(PaintCanvas);
        var point = ToImagePoint(pointer.Position);
        if (begin || _strokeFilter is null) _strokeFilter = new StrokeInputFilter((float)_preferences.Smoothing);
        var sample = _strokeFilter.Add(point, pointer.Properties.Pressure,
            args.Pointer.PointerDeviceType == PointerDeviceType.Pen);
        _strokePressure = sample.Pressure;
        return sample.Point;
    }
}
