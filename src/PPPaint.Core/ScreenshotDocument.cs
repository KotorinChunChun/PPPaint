using System.Collections.Immutable;
using System.Numerics;

namespace PPPaint.Core;

public enum ScreenshotTool { Select, Region, Pen, Eraser, Arrow, Rectangle, Ellipse, Line, Text, Number, Redact, Highlight }

public readonly record struct ScreenshotBox(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;
    public bool Contains(Vector2 p, float padding = 0) => p.X >= X - padding && p.X <= Right + padding && p.Y >= Y - padding && p.Y <= Bottom + padding;
    public bool Intersects(ScreenshotBox b) => X < b.Right && Right > b.X && Y < b.Bottom && Bottom > b.Y;
    public static ScreenshotBox Between(Vector2 a, Vector2 b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}

// Pixel bytes are premultiplied BGRA, matching Win2D. History shares immutable frames.
public sealed class ScreenshotFrame
{
    private readonly byte[] _pixels;
    public int Width { get; }
    public int Height { get; }
    public ReadOnlyMemory<byte> Pixels => _pixels;
    public ScreenshotFrame(int width, int height, ReadOnlySpan<byte> pixels)
    {
        ValidateSize(width, height);
        if (pixels.Length != (long)width * height * 4) throw new ArgumentException("画素数が画像寸法と一致しません。");
        Width = width; Height = height; _pixels = pixels.ToArray();
    }
    public static void ValidateSize(int width, int height)
    {
        if (width < 1 || height < 1 || width > 16384 || height > 16384 || (long)width * height * 4 > PixelSurface.MaximumPixelBytes)
            throw new ArgumentOutOfRangeException(nameof(width), "画像は各辺16384px以下、画素データ256MiB以下で指定してください。");
    }
    public static ScreenshotFrame Blank(int width = 1000, int height = 650)
    {
        ValidateSize(width, height);
        var bytes = new byte[checked(width * height * 4)];
        Array.Fill(bytes, (byte)255);
        return new(width, height, bytes);
    }
    public ScreenshotFrame Crop(int x, int y, int width, int height)
    {
        if (x < 0 || y < 0 || width < 1 || height < 1 || (long)x + width > Width || (long)y + height > Height)
            throw new ArgumentOutOfRangeException(nameof(x));
        var bytes = new byte[checked(width * height * 4)];
        for (var row = 0; row < height; row++) _pixels.AsSpan(((row + y) * Width + x) * 4, width * 4).CopyTo(bytes.AsSpan(row * width * 4));
        return new(width, height, bytes);
    }
    public ScreenshotFrame Append(ScreenshotFrame other, bool vertical)
    {
        var width = vertical ? Math.Max(Width, other.Width) : checked(Width + other.Width);
        var height = vertical ? checked(Height + other.Height) : Math.Max(Height, other.Height);
        ValidateSize(width, height);
        var bytes = new byte[checked(width * height * 4)];
        Array.Fill(bytes, (byte)255);
        Copy(this, 0, 0); Copy(other, vertical ? 0 : Width, vertical ? Height : 0);
        return new(width, height, bytes);
        void Copy(ScreenshotFrame frame, int dx, int dy)
        {
            for (var y = 0; y < frame.Height; y++) frame._pixels.AsSpan(y * frame.Width * 4, frame.Width * 4).CopyTo(bytes.AsSpan(((y + dy) * width + dx) * 4));
        }
    }
}

public sealed record ScreenshotAnnotation(
    string Id, ScreenshotTool Tool, ScreenshotBox Bounds, Rgba32 Color, float Width = 3, float FontSize = 24,
    string Text = "", ImmutableArray<Vector2> Points = default, float Rotation = 0)
{
    public Rgba32 OutputColor => Tool == ScreenshotTool.Redact ? Color with { A = 255 } : Color;
    public ScreenshotAnnotation Move(Vector2 delta) => this with
    {
        Bounds = Bounds with { X = Bounds.X + delta.X, Y = Bounds.Y + delta.Y },
        Points = Points.IsDefault ? Points : Points.Select(p => p + delta).ToImmutableArray()
    };
    public ScreenshotAnnotation Resize(ScreenshotBox box)
    {
        var sx = box.Width / Math.Max(1, Bounds.Width); var sy = box.Height / Math.Max(1, Bounds.Height);
        return this with { Bounds = box, Points = Points.IsDefault ? Points : Points.Select(p => new Vector2(box.X + (p.X - Bounds.X) * sx, box.Y + (p.Y - Bounds.Y) * sy)).ToImmutableArray(),
            FontSize = Tool is ScreenshotTool.Text or ScreenshotTool.Number ? Math.Clamp(FontSize * Math.Min(sx, sy), 8, 512) : FontSize };
    }
    public bool Hit(Vector2 point, float tolerance = 5)
    {
        if (!Bounds.Contains(point, Math.Max(tolerance, Width / 2))) return false;
        if (Tool is ScreenshotTool.Pen or ScreenshotTool.Eraser or ScreenshotTool.Line or ScreenshotTool.Arrow && !Points.IsDefaultOrEmpty)
        {
            if (Points.Length == 1) return Vector2.Distance(point, Points[0]) <= Width / 2 + tolerance;
            for (var i = 1; i < Points.Length; i++)
            {
                var a = Points[i - 1]; var line = Points[i] - a;
                var t = line.LengthSquared() < .001f ? 0 : Math.Clamp(Vector2.Dot(point - a, line) / line.LengthSquared(), 0, 1);
                if (Vector2.Distance(point, a + line * t) <= Width / 2 + tolerance) return true;
            }
            return false;
        }
        return true;
    }
}

public sealed record ScreenshotState(ScreenshotFrame Frame, ImmutableArray<ScreenshotAnnotation> Annotations, int NextNumber = 1);

public sealed class ScreenshotDocument
{
    private readonly List<ScreenshotState> _undo = [], _redo = [];
    public ScreenshotState State { get; private set; }
    public bool CanUndo => _undo.Count != 0;
    public bool CanRedo => _redo.Count != 0;
    public ScreenshotDocument(ScreenshotFrame frame) => State = new(frame, []);
    public void Apply(ScreenshotState state)
    {
        if (state == State) return;
        _undo.Add(State); _redo.Clear(); State = state;
        // Count shared raster frames once, rather than charging each annotation edit for the whole image.
        while (_undo.Count > 80 || _undo.Count > 0 && HistoryBytes() > 256L * 1024 * 1024) _undo.RemoveAt(0);
    }
    private long HistoryBytes() => _undo.Append(State).Select(s => s.Frame).Distinct().Sum(f => (long)f.Pixels.Length)
        + _undo.Append(State).Sum(s => s.Annotations.Sum(a => 256L + a.Text.Length * 2L + (a.Points.IsDefault ? 0 : a.Points.Length * 8L)));
    public void Add(ScreenshotAnnotation annotation) => Apply(State with { Annotations = State.Annotations.Add(annotation), NextNumber = State.NextNumber + (annotation.Tool == ScreenshotTool.Number ? 1 : 0) });
    public void Replace(ScreenshotAnnotation annotation)
    {
        var i = State.Annotations.FindIndex(a => a.Id == annotation.Id);
        if (i >= 0 && State.Annotations[i] != annotation) Apply(State with { Annotations = State.Annotations.SetItem(i, annotation) });
    }
    public void Delete(string id) => Apply(State with { Annotations = State.Annotations.RemoveAll(a => a.Id == id) });
    public void Undo() { if (CanUndo) { _redo.Add(State); State = _undo[^1]; _undo.RemoveAt(_undo.Count - 1); } }
    public void Redo() { if (CanRedo) { _undo.Add(State); State = _redo[^1]; _redo.RemoveAt(_redo.Count - 1); } }
    public void Crop(int x, int y, int width, int height)
    {
        var box = new ScreenshotBox(x, y, width, height);
        Apply(State with { Frame = State.Frame.Crop(x, y, width, height), Annotations = State.Annotations.Where(a => a.Bounds.Intersects(box)).Select(a => a.Move(new(-x, -y))).ToImmutableArray() });
    }
    public void Append(ScreenshotFrame other, bool vertical) => Apply(State with { Frame = State.Frame.Append(other, vertical) });
}

internal static class ScreenshotArrayExtensions
{
    internal static int FindIndex<T>(this ImmutableArray<T> array, Predicate<T> predicate)
    {
        for (var i = 0; i < array.Length; i++) if (predicate(array[i])) return i;
        return -1;
    }
}
