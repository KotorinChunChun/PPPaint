using System.Numerics;

namespace ChunPaint.Core;

public enum EditableObjectKind
{
    Text,
    Shape
}

public enum EditableTextAlignment
{
    Left,
    Center,
    Right
}

public enum EditableShapeKind
{
    Line,
    Curve,
    Ellipse,
    Rectangle,
    RoundedRectangle,
    Parallelogram,
    Triangle,
    RightTriangle,
    Diamond,
    Pentagon,
    Hexagon,
    RightArrow,
    LeftArrow,
    UpArrow,
    DownArrow,
    FourPointStar,
    Star,
    FilledStar,
    Callout,
    Oval,
    Cloud,
    Heart,
    Lightning
}

public readonly record struct ObjectPoint(float X, float Y)
{
    public static ObjectPoint FromVector(Vector2 point) => new(point.X, point.Y);

    public Vector2 ToVector2() => new(X, Y);
}

public readonly record struct ObjectBounds(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;
    public Vector2 Center => new(X + (Width / 2), Y + (Height / 2));

    public bool Contains(Vector2 point) =>
        point.X >= X && point.X <= Right && point.Y >= Y && point.Y <= Bottom;
}

public sealed record TextObjectStyle(
    string FontFamily = "Segoe UI",
    float FontSize = 24,
    bool Bold = false,
    bool Italic = false,
    bool Underline = false,
    bool Strikethrough = false,
    EditableTextAlignment Alignment = EditableTextAlignment.Left,
    Rgba32 Foreground = default,
    bool HasBackground = false,
    Rgba32 Background = default);

public enum ShapeStrokeKind
{
    Solid,
    Crayon,
    Marker,
    Oil,
    Pencil,
    Watercolor
}

public enum ShapeFillKind
{
    Solid,
    Crayon,
    Marker,
    Oil,
    Pencil,
    Watercolor
}

public sealed record ShapeObjectStyle(
    Rgba32 OutlineColor = default,
    Rgba32 FillColor = default,
    float StrokeWidth = 1,
    bool DrawOutline = true,
    bool DrawFill = false,
    ShapeStrokeKind OutlineKind = ShapeStrokeKind.Solid,
    ShapeFillKind FillKind = ShapeFillKind.Solid);

public sealed record EditableObject(
    string Id,
    EditableObjectKind Kind,
    ObjectBounds Bounds,
    float RotationDegrees = 0,
    string? Text = null,
    TextObjectStyle? TextStyle = null,
    EditableShapeKind? Shape = null,
    ShapeObjectStyle? ShapeStyle = null,
    ObjectPoint? StartPoint = null,
    ObjectPoint? EndPoint = null,
    string? LayerId = null)
{
    public static EditableObject CreateText(
        ObjectBounds bounds,
        string text,
        TextObjectStyle? style = null,
        string? id = null,
        string? layerId = null) =>
        new(id ?? Guid.NewGuid().ToString("N"), EditableObjectKind.Text, bounds, Text: text, TextStyle: style ?? new(), LayerId: layerId);

    public static EditableObject CreateShape(
        EditableShapeKind shape,
        ObjectBounds bounds,
        ShapeObjectStyle? style = null,
        ObjectPoint? startPoint = null,
        ObjectPoint? endPoint = null,
        string? id = null,
        string? layerId = null) =>
        new(id ?? Guid.NewGuid().ToString("N"), EditableObjectKind.Shape, bounds, Shape: shape,
            ShapeStyle: style ?? new(), StartPoint: startPoint, EndPoint: endPoint, LayerId: layerId);

    public EditableObject MoveBy(Vector2 offset, ObjectBounds canvasBounds) =>
        this with { Bounds = EditableObjectTransform.Move(Bounds, offset, canvasBounds) };

    public EditableObject Resize(
        SelectionHandle handle,
        Vector2 delta,
        ObjectBounds canvasBounds,
        bool preserveAspectRatio = false,
        bool fromCenter = false)
    {
        var resized = EditableObjectTransform.Resize(
            Bounds,
            handle,
            delta,
            canvasBounds,
            preserveAspectRatio,
            fromCenter);
        ObjectPoint? start = StartPoint is { } first ? ScalePoint(first, Bounds, resized) : null;
        ObjectPoint? end = EndPoint is { } second ? ScalePoint(second, Bounds, resized) : null;
        return this with { Bounds = resized, StartPoint = start, EndPoint = end };
    }

    public EditableObject RotateBy(float degrees) =>
        this with { RotationDegrees = EditableObjectTransform.NormalizeDegrees(RotationDegrees + degrees) };

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id)) throw new ArgumentException("An editable object needs an id.", nameof(Id));
        if (!float.IsFinite(Bounds.X) || !float.IsFinite(Bounds.Y) ||
            !float.IsFinite(Bounds.Width) || !float.IsFinite(Bounds.Height) ||
            Bounds.Width <= 0 || Bounds.Height <= 0)
            throw new ArgumentException("An editable object needs positive bounds.", nameof(Bounds));
        if (!float.IsFinite(RotationDegrees))
            throw new ArgumentException("An editable object rotation must be finite.", nameof(RotationDegrees));

        switch (Kind)
        {
            case EditableObjectKind.Text:
                if (string.IsNullOrEmpty(Text)) throw new ArgumentException("A text object needs text.", nameof(Text));
                if (TextStyle is null) throw new ArgumentException("A text object needs text style.", nameof(TextStyle));
                if (!float.IsFinite(TextStyle.FontSize) || TextStyle.FontSize <= 0 || string.IsNullOrWhiteSpace(TextStyle.FontFamily))
                    throw new ArgumentException("A text object has invalid font settings.", nameof(TextStyle));
                break;
            case EditableObjectKind.Shape:
                if (Shape is null) throw new ArgumentException("A shape object needs a shape kind.", nameof(Shape));
                if (ShapeStyle is null) throw new ArgumentException("A shape object needs shape style.", nameof(ShapeStyle));
                if (ShapeStyle.StrokeWidth <= 0 || !float.IsFinite(ShapeStyle.StrokeWidth))
                    throw new ArgumentException("A shape object has an invalid stroke width.", nameof(ShapeStyle));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(Kind));
        }
    }

    private static ObjectPoint ScalePoint(ObjectPoint point, ObjectBounds original, ObjectBounds resized)
    {
        var x = resized.X + ((point.X - original.X) / original.Width * resized.Width);
        var y = resized.Y + ((point.Y - original.Y) / original.Height * resized.Height);
        return new ObjectPoint(x, y);
    }
}

public static class EditableObjectTransform
{
    public static ObjectBounds Move(ObjectBounds bounds, Vector2 offset, ObjectBounds canvasBounds)
    {
        Validate(bounds);
        Validate(canvasBounds);
        ValidateFinite(offset, nameof(offset));
        var x = Constrain(bounds.X + offset.X, bounds.Width, canvasBounds.X, canvasBounds.Right);
        var y = Constrain(bounds.Y + offset.Y, bounds.Height, canvasBounds.Y, canvasBounds.Bottom);
        return bounds with { X = x, Y = y };
    }

    public static ObjectBounds Resize(
        ObjectBounds bounds,
        SelectionHandle handle,
        Vector2 delta,
        ObjectBounds canvasBounds,
        bool preserveAspectRatio = false,
        bool fromCenter = false)
    {
        Validate(bounds);
        Validate(canvasBounds);
        ValidateFinite(delta, nameof(delta));
        if (handle is SelectionHandle.None or SelectionHandle.Rotation)
            throw new ArgumentOutOfRangeException(nameof(handle));

        var left = bounds.X;
        var top = bounds.Y;
        var right = bounds.Right;
        var bottom = bounds.Bottom;
        if (fromCenter)
        {
            if (UsesLeftEdge(handle))
            {
                left += delta.X;
                right -= delta.X;
            }
            if (UsesRightEdge(handle))
            {
                left -= delta.X;
                right += delta.X;
            }
            if (UsesTopEdge(handle))
            {
                top += delta.Y;
                bottom -= delta.Y;
            }
            if (UsesBottomEdge(handle))
            {
                top -= delta.Y;
                bottom += delta.Y;
            }
        }
        else
        {
            if (UsesLeftEdge(handle)) left += delta.X;
            if (UsesRightEdge(handle)) right += delta.X;
            if (UsesTopEdge(handle)) top += delta.Y;
            if (UsesBottomEdge(handle)) bottom += delta.Y;
        }

        if (preserveAspectRatio)
        {
            ApplyAspectRatio(ref left, ref top, ref right, ref bottom, bounds, handle, fromCenter);
        }

        const float minimumSize = 1;
        if (right - left < minimumSize)
        {
            if (UsesLeftEdge(handle)) left = right - minimumSize;
            else right = left + minimumSize;
        }
        if (bottom - top < minimumSize)
        {
            if (UsesTopEdge(handle)) top = bottom - minimumSize;
            else bottom = top + minimumSize;
        }

        left = Math.Clamp(left, canvasBounds.X, canvasBounds.Right - minimumSize);
        top = Math.Clamp(top, canvasBounds.Y, canvasBounds.Bottom - minimumSize);
        right = Math.Clamp(right, left + minimumSize, canvasBounds.Right);
        bottom = Math.Clamp(bottom, top + minimumSize, canvasBounds.Bottom);
        return new ObjectBounds(left, top, right - left, bottom - top);
    }

    public static float SnapDegrees(float degrees, float increment = 15)
    {
        if (!float.IsFinite(degrees) || !float.IsFinite(increment) || increment <= 0)
            throw new ArgumentOutOfRangeException(nameof(degrees));
        return MathF.Round(degrees / increment, MidpointRounding.AwayFromZero) * increment;
    }

    public static float NormalizeDegrees(float degrees)
    {
        if (!float.IsFinite(degrees)) throw new ArgumentOutOfRangeException(nameof(degrees));
        var normalized = degrees % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    private static float Constrain(float value, float size, float minimum, float maximum)
    {
        if (size >= maximum - minimum) return minimum;
        return Math.Clamp(value, minimum, maximum - size);
    }

    private static bool UsesLeftEdge(SelectionHandle handle) =>
        handle is SelectionHandle.TopLeft or SelectionHandle.Left or SelectionHandle.BottomLeft;

    private static bool UsesRightEdge(SelectionHandle handle) =>
        handle is SelectionHandle.TopRight or SelectionHandle.Right or SelectionHandle.BottomRight;

    private static bool UsesTopEdge(SelectionHandle handle) =>
        handle is SelectionHandle.TopLeft or SelectionHandle.Top or SelectionHandle.TopRight;

    private static bool UsesBottomEdge(SelectionHandle handle) =>
        handle is SelectionHandle.BottomLeft or SelectionHandle.Bottom or SelectionHandle.BottomRight;

    private static void ApplyAspectRatio(
        ref float left,
        ref float top,
        ref float right,
        ref float bottom,
        ObjectBounds original,
        SelectionHandle handle,
        bool fromCenter)
    {
        var rawWidth = MathF.Max(1, right - left);
        var rawHeight = MathF.Max(1, bottom - top);
        var aspectRatio = original.Width / original.Height;
        var changesWidth = UsesLeftEdge(handle) || UsesRightEdge(handle);
        var changesHeight = UsesTopEdge(handle) || UsesBottomEdge(handle);
        var width = rawWidth;
        var height = rawHeight;

        if (changesWidth && changesHeight)
        {
            var widthChange = MathF.Abs(rawWidth - original.Width) / original.Width;
            var heightChange = MathF.Abs(rawHeight - original.Height) / original.Height;
            if (widthChange >= heightChange)
                height = MathF.Max(1, width / aspectRatio);
            else
                width = MathF.Max(1, height * aspectRatio);
        }
        else if (changesWidth)
        {
            height = MathF.Max(1, width / aspectRatio);
        }
        else if (changesHeight)
        {
            width = MathF.Max(1, height * aspectRatio);
        }

        if (fromCenter)
        {
            var centerX = original.X + (original.Width / 2);
            var centerY = original.Y + (original.Height / 2);
            left = centerX - (width / 2);
            right = centerX + (width / 2);
            top = centerY - (height / 2);
            bottom = centerY + (height / 2);
            return;
        }

        if (UsesLeftEdge(handle))
        {
            left = original.Right - width;
            right = original.Right;
        }
        else if (UsesRightEdge(handle))
        {
            left = original.X;
            right = original.X + width;
        }
        else
        {
            var centerX = original.X + (original.Width / 2);
            left = centerX - (width / 2);
            right = centerX + (width / 2);
        }

        if (UsesTopEdge(handle))
        {
            top = original.Bottom - height;
            bottom = original.Bottom;
        }
        else if (UsesBottomEdge(handle))
        {
            top = original.Y;
            bottom = original.Y + height;
        }
        else
        {
            var centerY = original.Y + (original.Height / 2);
            top = centerY - (height / 2);
            bottom = centerY + (height / 2);
        }
    }

    private static void Validate(ObjectBounds bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0 ||
            !float.IsFinite(bounds.X) || !float.IsFinite(bounds.Y) ||
            !float.IsFinite(bounds.Width) || !float.IsFinite(bounds.Height))
            throw new ArgumentOutOfRangeException(nameof(bounds));
    }

    private static void ValidateFinite(Vector2 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
            throw new ArgumentOutOfRangeException(parameterName);
    }
}
