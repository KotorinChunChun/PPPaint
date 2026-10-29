using System.Numerics;

namespace PPPaint.Core;

public enum SelectionRegionKind
{
    Rectangle,
    Polygon,
    Mask
}

public enum SelectionHandle
{
    None,
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
    Rotation
}

public sealed class SelectionRegion
{
    private const long MaximumMaskPixels = 64L * 1024 * 1024;
    private readonly Vector2[] _points;
    private readonly bool[]? _mask;
    private readonly byte[]? _coverage;
    private readonly int _maskWidth;
    private readonly int _maskHeight;

    private SelectionRegion(
        SelectionRegionKind kind,
        IReadOnlyList<Vector2> points,
        bool[]? mask = null,
        int maskWidth = 0,
        int maskHeight = 0, byte[]? coverage = null)
    {
        Kind = kind;
        _points = points.ToArray();
        _mask = mask;
        _coverage = coverage;
        _maskWidth = maskWidth;
        _maskHeight = maskHeight;
        Bounds = CalculateBounds(_points);
    }

    public SelectionRegionKind Kind { get; }

    public PixelRect Bounds { get; }

    public IReadOnlyList<Vector2> Points => _points;

    public static SelectionRegion CreateRectangle(PixelRect bounds)
    {
        ValidateBounds(bounds);
        return new SelectionRegion(
            SelectionRegionKind.Rectangle,
            [
                new Vector2(bounds.X, bounds.Y),
                new Vector2(bounds.Right, bounds.Y),
                new Vector2(bounds.Right, bounds.Bottom),
                new Vector2(bounds.X, bounds.Bottom)
            ]);
    }

    public static SelectionRegion CreatePolygon(IEnumerable<Vector2> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var copied = points.ToArray();
        if (copied.Length < 3)
        {
            throw new ArgumentException("A polygon selection needs at least three points.", nameof(points));
        }

        if (copied.Any(point => !float.IsFinite(point.X) || !float.IsFinite(point.Y)))
        {
            throw new ArgumentException("A polygon selection must contain finite points.", nameof(points));
        }

        ValidateBounds(CalculateBounds(copied));
        return new SelectionRegion(SelectionRegionKind.Polygon, copied);
    }

    public static SelectionRegion CreateMask(int width, int height, ReadOnlySpan<bool> mask)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if ((long)width * height > MaximumMaskPixels)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "The selection mask exceeds the 64 million pixel safety limit.");
        }

        var expectedLength = width * height;
        if (mask.Length != expectedLength)
        {
            throw new ArgumentException("The mask dimensions do not match the selection size.", nameof(mask));
        }

        return new SelectionRegion(
            SelectionRegionKind.Mask,
            [
                new Vector2(0, 0),
                new Vector2(width, 0),
                new Vector2(width, height),
                new Vector2(0, height)
            ],
            mask.ToArray(),
            width,
            height);
    }

    public static SelectionRegion CreateCoverageMask(int width, int height, ReadOnlySpan<byte> coverage)
    {
        if (width < 1 || height < 1 || (long)width * height > MaximumMaskPixels) throw new ArgumentOutOfRangeException(nameof(width));
        if (coverage.Length != width * height) throw new ArgumentException("マスク寸法が一致しません。", nameof(coverage));
        int left = width, top = height, right = 0, bottom = 0;
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
            if (coverage[y * width + x] != 0) { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1); }
        if (right == 0) { left = top = 0; right = bottom = 1; }
        return new SelectionRegion(SelectionRegionKind.Mask,
            [new(left, top), new(right, top), new(right, bottom), new(left, bottom)],
            maskWidth: width, maskHeight: height, coverage: coverage.ToArray());
    }

    public byte GetCoverage(int x, int y)
    {
        if (Kind == SelectionRegionKind.Mask)
        {
            if ((uint)x >= _maskWidth || (uint)y >= _maskHeight) return 0;
            return _coverage is not null ? _coverage[y * _maskWidth + x] : _mask![y * _maskWidth + x] ? (byte)255 : (byte)0;
        }
        return ContainsPoint(new Vector2(x + .5f, y + .5f)) ? (byte)255 : (byte)0;
    }

    public bool ContainsPixel(int x, int y) => GetCoverage(x, y) > 0;

    public bool ContainsPoint(Vector2 point)
    {
        if (Kind == SelectionRegionKind.Mask)
        {
            return ContainsPixel((int)MathF.Floor(point.X), (int)MathF.Floor(point.Y));
        }

        if (Kind == SelectionRegionKind.Rectangle)
        {
            return point.X >= Bounds.X && point.X < Bounds.Right &&
                   point.Y >= Bounds.Y && point.Y < Bounds.Bottom;
        }

        var inside = false;
        for (var index = 0; index < _points.Length; index++)
        {
            var first = _points[index];
            var second = _points[(index + 1) % _points.Length];
            if (IsPointOnSegment(point, first, second))
            {
                return true;
            }

            var crosses = ((first.Y > point.Y) != (second.Y > point.Y)) &&
                          point.X < ((second.X - first.X) * (point.Y - first.Y) / (second.Y - first.Y)) + first.X;
            if (crosses)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    public SelectionRegion MoveBy(Vector2 offset, PixelRect canvasBounds)
    {
        ValidateBounds(canvasBounds);
        if (Kind == SelectionRegionKind.Mask)
        {
            if (_coverage is not null) return TransformCoverage(CreateRectangle(Bounds).MoveBy(offset, canvasBounds), canvasBounds);
            throw new InvalidOperationException("A mask selection cannot be moved as a single region.");
        }
        ValidateFinite(offset, nameof(offset));

        var constrained = ConstrainOffset(Bounds, offset, canvasBounds);
        return new SelectionRegion(Kind, _points.Select(point => point + constrained).ToArray());
    }

    public SelectionRegion Resize(
        SelectionHandle handle,
        Vector2 delta,
        PixelRect canvasBounds,
        bool preserveAspectRatio = false,
        bool fromCenter = false)
    {
        if (handle is SelectionHandle.None or SelectionHandle.Rotation)
        {
            throw new ArgumentOutOfRangeException(nameof(handle));
        }
        ValidateBounds(canvasBounds);
        if (Kind == SelectionRegionKind.Mask)
        {
            if (_coverage is not null) return TransformCoverage(CreateRectangle(Bounds).Resize(handle, delta, canvasBounds, preserveAspectRatio, fromCenter), canvasBounds);
            throw new InvalidOperationException("A mask selection cannot be resized as a single region.");
        }
        ValidateFinite(delta, nameof(delta));

        var original = GetFloatBounds(_points);
        var resized = EditableObjectTransform.Resize(
            new ObjectBounds(original.Left, original.Top, original.Width, original.Height),
            handle,
            delta,
            new ObjectBounds(canvasBounds.X, canvasBounds.Y, canvasBounds.Width, canvasBounds.Height),
            preserveAspectRatio,
            fromCenter);
        if (Kind == SelectionRegionKind.Rectangle)
        {
            return CreateRectangle(ToPixelRect(resized));
        }

        var width = original.Width;
        var height = original.Height;
        var points = _points.Select(point => new Vector2(
            resized.X + ((point.X - original.Left) / width * resized.Width),
            resized.Y + ((point.Y - original.Top) / height * resized.Height)));
        return new SelectionRegion(SelectionRegionKind.Polygon, points.ToArray());
    }

    public SelectionRegion Rotate(float degrees, PixelRect canvasBounds)
    {
        ValidateBounds(canvasBounds);
        if (Kind == SelectionRegionKind.Mask)
        {
            if (_coverage is not null) return TransformCoverage(CreateRectangle(Bounds).Rotate(degrees, canvasBounds), canvasBounds);
            throw new InvalidOperationException("A mask selection cannot be rotated as a single region.");
        }
        if (!float.IsFinite(degrees))
        {
            throw new ArgumentOutOfRangeException(nameof(degrees));
        }

        var bounds = GetFloatBounds(_points);
        var center = new Vector2((bounds.Left + bounds.Right) / 2, (bounds.Top + bounds.Bottom) / 2);
        var radians = degrees * (MathF.PI / 180f);
        var sin = MathF.Sin(radians);
        var cos = MathF.Cos(radians);
        var rotated = _points.Select(point =>
        {
            var relative = point - center;
            return new Vector2(
                SnapNearInteger(center.X + (relative.X * cos) - (relative.Y * sin)),
                SnapNearInteger(center.Y + (relative.X * sin) + (relative.Y * cos)));
        }).ToArray();

        return new SelectionRegion(
            SelectionRegionKind.Polygon,
            FitAndConstrainPoints(rotated, canvasBounds));
    }

    public SelectionRegion Skew(float degrees, bool horizontal, PixelRect canvasBounds)
    {
        ValidateBounds(canvasBounds);
        if (Kind == SelectionRegionKind.Mask)
        {
            if (_coverage is not null) return TransformCoverage(CreateRectangle(Bounds).Skew(degrees, horizontal, canvasBounds), canvasBounds);
            throw new InvalidOperationException("A mask selection cannot be skewed as a single region.");
        }
        if (!float.IsFinite(degrees) || MathF.Abs(degrees) >= 89.9f)
        {
            throw new ArgumentOutOfRangeException(nameof(degrees));
        }

        var bounds = GetFloatBounds(_points);
        var centerX = (bounds.Left + bounds.Right) / 2;
        var centerY = (bounds.Top + bounds.Bottom) / 2;
        var shear = MathF.Tan(degrees * (MathF.PI / 180f));
        var extent = MathF.Abs(shear * (horizontal ? bounds.Height - 1 : bounds.Width - 1));
        var offset = extent / 2;
        var skewed = _points.Select(point => horizontal
            ? new Vector2(point.X + (shear * (point.Y - centerY)) + offset, point.Y)
            : new Vector2(point.X, point.Y + (shear * (point.X - centerX)) + offset)).ToArray();
        return new SelectionRegion(
            SelectionRegionKind.Polygon,
            FitAndConstrainPoints(skewed, canvasBounds));
    }

    public Vector2 GetHandlePosition(SelectionHandle handle)
    {
        var left = Bounds.X;
        var top = Bounds.Y;
        var right = Bounds.Right;
        var bottom = Bounds.Bottom;
        var centerX = (left + right) / 2f;
        var centerY = (top + bottom) / 2f;
        return handle switch
        {
            SelectionHandle.TopLeft => new Vector2(left, top),
            SelectionHandle.Top => new Vector2(centerX, top),
            SelectionHandle.TopRight => new Vector2(right, top),
            SelectionHandle.Right => new Vector2(right, centerY),
            SelectionHandle.BottomRight => new Vector2(right, bottom),
            SelectionHandle.Bottom => new Vector2(centerX, bottom),
            SelectionHandle.BottomLeft => new Vector2(left, bottom),
            SelectionHandle.Left => new Vector2(left, centerY),
            _ => new Vector2(centerX, top)
        };
    }

    public Vector2 GetRotationHandlePosition(float distance = 24)
    {
        var centerX = (Bounds.X + Bounds.Right) / 2f;
        return new Vector2(centerX, Bounds.Y - distance);
    }

    public bool[] CreateMask(int width, int height) => SelectionOperations.CreateMask(this, width, height);

    private SelectionRegion TransformCoverage(SelectionRegion destination, PixelRect canvas)
    {
        if (canvas.X < 0 || canvas.Y < 0 || (long)canvas.Right * canvas.Bottom > MaximumMaskPixels) throw new ArgumentOutOfRangeException(nameof(canvas));
        var origin = destination.Points[0];
        var u = (destination.Points[1] - origin) / Bounds.Width;
        var v = (destination.Points[3] - origin) / Bounds.Height;
        var matrix = new Matrix3x2(u.X, u.Y, v.X, v.Y, origin.X - u.X * Bounds.X - v.X * Bounds.Y, origin.Y - u.Y * Bounds.X - v.Y * Bounds.Y);
        if (!Matrix3x2.Invert(matrix, out var inverse)) throw new InvalidOperationException("選択範囲を変換できません。");
        var mask = new byte[checked(canvas.Right * canvas.Bottom)];
        for (var y = canvas.Y; y < canvas.Bottom; y++) for (var x = canvas.X; x < canvas.Right; x++)
        {
            var point = Vector2.Transform(new Vector2(x + .5f, y + .5f), inverse) - new Vector2(.5f);
            var ix = (int)MathF.Floor(point.X); var iy = (int)MathF.Floor(point.Y);
            var fx = point.X - ix; var fy = point.Y - iy;
            mask[y * canvas.Right + x] = (byte)Math.Clamp(MathF.Round(
                GetCoverage(ix, iy) * (1 - fx) * (1 - fy) + GetCoverage(ix + 1, iy) * fx * (1 - fy) +
                GetCoverage(ix, iy + 1) * (1 - fx) * fy + GetCoverage(ix + 1, iy + 1) * fx * fy), 0, 255);
        }
        return CreateCoverageMask(canvas.Right, canvas.Bottom, mask);
    }

    private static Vector2 ConstrainOffset(PixelRect bounds, Vector2 offset, PixelRect canvasBounds)
    {
        var minimumX = canvasBounds.X - bounds.X;
        var maximumX = canvasBounds.Right - bounds.Right;
        var minimumY = canvasBounds.Y - bounds.Y;
        var maximumY = canvasBounds.Bottom - bounds.Bottom;
        return new Vector2(
            Math.Clamp(offset.X, minimumX, maximumX),
            Math.Clamp(offset.Y, minimumY, maximumY));
    }

    private static Vector2[] FitAndConstrainPoints(IReadOnlyList<Vector2> points, PixelRect canvasBounds)
    {
        var fitted = points.ToArray();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var pixelBounds = CalculateBounds(fitted);
            if (pixelBounds.Width <= canvasBounds.Width && pixelBounds.Height <= canvasBounds.Height)
            {
                var offset = ConstrainOffset(pixelBounds, Vector2.Zero, canvasBounds);
                return fitted.Select(point => point + offset).ToArray();
            }

            var scale = MathF.Min(
                canvasBounds.Width / (float)pixelBounds.Width,
                canvasBounds.Height / (float)pixelBounds.Height);
            var floatBounds = GetFloatBounds(fitted);
            var center = new Vector2(
                (floatBounds.Left + floatBounds.Right) / 2,
                (floatBounds.Top + floatBounds.Bottom) / 2);
            fitted = fitted.Select(point => center + ((point - center) * scale)).ToArray();
        }

        var finalBounds = CalculateBounds(fitted);
        if (finalBounds.Width > canvasBounds.Width || finalBounds.Height > canvasBounds.Height)
        {
            throw new InvalidOperationException("The transformed selection cannot fit inside the canvas.");
        }
        var finalOffset = ConstrainOffset(finalBounds, Vector2.Zero, canvasBounds);
        return fitted.Select(point => point + finalOffset).ToArray();
    }

    private static PixelRect CalculateBounds(IReadOnlyList<Vector2> points)
    {
        var left = points.Min(point => point.X);
        var top = points.Min(point => point.Y);
        var right = points.Max(point => point.X);
        var bottom = points.Max(point => point.Y);
        var x = (int)MathF.Floor(left);
        var y = (int)MathF.Floor(top);
        var maxX = (int)MathF.Ceiling(right);
        var maxY = (int)MathF.Ceiling(bottom);
        return new PixelRect(x, y, Math.Max(1, maxX - x), Math.Max(1, maxY - y));
    }

    private static FloatBounds GetFloatBounds(IReadOnlyList<Vector2> points)
    {
        return new FloatBounds(
            points.Min(point => point.X),
            points.Min(point => point.Y),
            points.Max(point => point.X),
            points.Max(point => point.Y));
    }

    private static PixelRect ToPixelRect(ObjectBounds bounds)
    {
        var x = (int)MathF.Floor(bounds.X);
        var y = (int)MathF.Floor(bounds.Y);
        var right = (int)MathF.Ceiling(bounds.Right);
        var bottom = (int)MathF.Ceiling(bounds.Bottom);
        return new PixelRect(x, y, Math.Max(1, right - x), Math.Max(1, bottom - y));
    }

    private static bool IsPointOnSegment(Vector2 point, Vector2 first, Vector2 second)
    {
        var lengthSquared = Vector2.DistanceSquared(first, second);
        if (lengthSquared < float.Epsilon)
        {
            return Vector2.DistanceSquared(point, first) < 0.001f;
        }

        var cross = ((point.X - first.X) * (second.Y - first.Y)) - ((point.Y - first.Y) * (second.X - first.X));
        if (MathF.Abs(cross) > 0.001f)
        {
            return false;
        }

        var dot = Vector2.Dot(point - first, second - first);
        return dot >= -0.001f && dot <= lengthSquared + 0.001f;
    }

    private static float SnapNearInteger(float value)
    {
        var rounded = MathF.Round(value);
        return MathF.Abs(value - rounded) < 0.0001f ? rounded : value;
    }

    private static void ValidateBounds(PixelRect bounds)
    {
        if (bounds.Width <= 0) throw new ArgumentOutOfRangeException(nameof(bounds));
        if (bounds.Height <= 0) throw new ArgumentOutOfRangeException(nameof(bounds));
    }

    private static void ValidateFinite(Vector2 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private readonly record struct FloatBounds(float Left, float Top, float Right, float Bottom)
    {
        public float Width => Right - Left;
        public float Height => Bottom - Top;
    }
}
