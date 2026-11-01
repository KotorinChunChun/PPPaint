using System.Collections.Immutable;
using System.Numerics;

namespace PPPaint.Core;

public enum ScreenshotTool { Select, Region, Pen, Eraser, Arrow, Rectangle, Ellipse, Line, Text, Number, Redact, Highlight, Image, Blur, Callout, Mosaic, Watermark }

public readonly record struct ScreenshotBox(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;
    public bool Contains(Vector2 p, float padding = 0) => p.X >= X - padding && p.X <= Right + padding && p.Y >= Y - padding && p.Y <= Bottom + padding;
    public bool Intersects(ScreenshotBox b) => X < b.Right && Right > b.X && Y < b.Bottom && Bottom > b.Y;
    public bool Contains(ScreenshotBox b) => b.X >= X && b.Y >= Y && b.Right <= Right && b.Bottom <= Bottom;
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
    public static ScreenshotFrame Transparent(int width, int height)
    {
        ValidateSize(width, height); return new(width, height, new byte[checked(width * height * 4)]);
    }
    public ScreenshotFrame Expand(int width, int height)
    {
        if (width == Width && height == Height) return this;
        if (width < Width || height < Height) throw new ArgumentOutOfRangeException(nameof(width));
        ValidateSize(width, height);
        var bytes = new byte[checked(width * height * 4)];
        for (var y = 0; y < Height; y++) _pixels.AsSpan(y * Width * 4, Width * 4).CopyTo(bytes.AsSpan(y * width * 4));
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
    internal ScreenshotFrame Reframe(int width, int height, int offsetX, int offsetY)
    {
        ValidateSize(width, height);
        var bytes = new byte[checked(width * height * 4)];
        var sourceX = Math.Max(0, -offsetX); var sourceY = Math.Max(0, -offsetY);
        var targetX = Math.Max(0, offsetX); var targetY = Math.Max(0, offsetY);
        var copyWidth = Math.Min(Width - sourceX, width - targetX); var copyHeight = Math.Min(Height - sourceY, height - targetY);
        for (var row = 0; row < copyHeight; row++)
            _pixels.AsSpan(((sourceY + row) * Width + sourceX) * 4, copyWidth * 4).CopyTo(bytes.AsSpan(((targetY + row) * width + targetX) * 4));
        return new(width, height, bytes);
    }
    public ScreenshotFrame Clear(PixelRect area)
    {
        if (area.X < 0 || area.Y < 0 || area.Width < 1 || area.Height < 1 || area.Right > Width || area.Bottom > Height) throw new ArgumentOutOfRangeException(nameof(area));
        var pixels = _pixels.ToArray();
        for (var y = area.Y; y < area.Bottom; y++) pixels.AsSpan((y * Width + area.X) * 4, area.Width * 4).Clear();
        return new(Width, Height, pixels);
    }
    public ScreenshotFrame Over(Rgba32 background)
    {
        if (background.A == 0) return this;
        var pixels = _pixels.ToArray();
        var blue = (background.B * background.A + 127) / 255; var green = (background.G * background.A + 127) / 255; var red = (background.R * background.A + 127) / 255;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var inverse = 255 - pixels[i + 3];
            pixels[i] = (byte)Math.Min(255, pixels[i] + (blue * inverse + 127) / 255);
            pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] + (green * inverse + 127) / 255);
            pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] + (red * inverse + 127) / 255);
            pixels[i + 3] = (byte)Math.Min(255, pixels[i + 3] + (background.A * inverse + 127) / 255);
        }
        return new(Width, Height, pixels);
    }
    public bool HasVisiblePixels()
    {
        for (var i = 3; i < _pixels.Length; i += 4) if (_pixels[i] != 0) return true;
        return false;
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
    string Text = "", ImmutableArray<Vector2> Points = default, float Rotation = 0,
    ScreenshotFrame? Raster = null, bool MirrorX = false, bool MirrorY = false, float BlurRadius = 8,
    Vector2? CalloutTip = null, int MosaicSize = 12, float Opacity = .25f,
    ScreenshotBox? ClipBounds = null, ImmutableArray<ScreenshotBox> Cutouts = default,
    ScreenshotBox? SamplingBounds = null, Rgba32? EraserBackground = null)
{
    public Vector2 Tail => CalloutTip ?? new(Bounds.X + Bounds.Width / 4, Bounds.Bottom + Bounds.Height * .4f);
    public ScreenshotBox VisualBounds
    {
        get
        {
            var b = Tool == ScreenshotTool.Callout ? ScreenshotBox.Between(new(Math.Min(Bounds.X, Tail.X), Math.Min(Bounds.Y, Tail.Y)), new(Math.Max(Bounds.Right, Tail.X), Math.Max(Bounds.Bottom, Tail.Y))) : Bounds;
            if (ClipBounds is not { } clip) return b;
            var left = Math.Max(b.X, clip.X); var top = Math.Max(b.Y, clip.Y);
            return new(left, top, Math.Max(0, Math.Min(b.Right, clip.Right) - left), Math.Max(0, Math.Min(b.Bottom, clip.Bottom) - top));
        }
    }
    public bool VisibleAt(Vector2 point) => (ClipBounds is not { } clip || clip.Contains(point)) && (Cutouts.IsDefaultOrEmpty || !Cutouts.Any(b => b.Contains(point)));
    public Rgba32 OutputColor => Tool == ScreenshotTool.Redact ? Color with { A = 255 } : Color;
    public ScreenshotAnnotation Move(Vector2 delta) => this with
    {
        Bounds = Bounds with { X = Bounds.X + delta.X, Y = Bounds.Y + delta.Y },
        Points = Points.IsDefault ? Points : Points.Select(p => p + delta).ToImmutableArray(),
        CalloutTip = Tool == ScreenshotTool.Callout ? Tail + delta : CalloutTip,
        ClipBounds = ClipBounds is { } clip ? clip with { X = clip.X + delta.X, Y = clip.Y + delta.Y } : null,
        SamplingBounds = SamplingBounds is { } sampling ? sampling with { X = sampling.X + delta.X, Y = sampling.Y + delta.Y } : null,
        Cutouts = Cutouts.IsDefault ? Cutouts : Cutouts.Select(b => b with { X = b.X + delta.X, Y = b.Y + delta.Y }).ToImmutableArray()
    };
    public ScreenshotAnnotation Resize(ScreenshotBox box)
    {
        var sx = box.Width / Math.Max(1, Bounds.Width); var sy = box.Height / Math.Max(1, Bounds.Height);
        ScreenshotBox TransformBox(ScreenshotBox b) => new(box.X + (b.X - Bounds.X) * sx, box.Y + (b.Y - Bounds.Y) * sy, b.Width * sx, b.Height * sy);
        return this with { Bounds = box, Points = Points.IsDefault ? Points : Points.Select(p => new Vector2(box.X + (p.X - Bounds.X) * sx, box.Y + (p.Y - Bounds.Y) * sy)).ToImmutableArray(),
            FontSize = Tool is ScreenshotTool.Text or ScreenshotTool.Number or ScreenshotTool.Callout or ScreenshotTool.Watermark ? Math.Clamp(FontSize * Math.Min(sx, sy), 8, 512) : FontSize,
            CalloutTip = Tool == ScreenshotTool.Callout ? new(box.X + (Tail.X - Bounds.X) * sx, box.Y + (Tail.Y - Bounds.Y) * sy) : CalloutTip,
            ClipBounds = ClipBounds is { } clip ? TransformBox(clip) : null,
            SamplingBounds = SamplingBounds is { } sampling ? TransformBox(sampling) : null,
            Cutouts = Cutouts.IsDefault ? Cutouts : Cutouts.Select(TransformBox).ToImmutableArray() };
    }
    public bool Hit(Vector2 point, float tolerance = 5)
    {
        if (!VisibleAt(point)) return false;
        if (!VisualBounds.Contains(point, Math.Max(tolerance, Width / 2) + (Tool == ScreenshotTool.Arrow ? Math.Max(12, Width * 4) : 0))) return false;
        if (Tool == ScreenshotTool.Callout)
        {
            if (Bounds.Contains(point)) return true;
            var outline = ScreenshotGeometry.CalloutOutline(Bounds, Tail);
            // Include the filled tail, not the whole rectangular gap between it and the body.
            var inside = false;
            for (var i = 0; i < outline.Length; i++)
            {
                var a = outline[i]; var b = outline[(i + 1) % outline.Length];
                if (ScreenshotGeometry.HitSegment(point, a, b, tolerance + Width / 2)) return true;
                if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
            }
            return inside;
        }
        if (Tool == ScreenshotTool.Image && Raster is { } image)
        {
            var center = new Vector2(Bounds.X + Bounds.Width / 2, Bounds.Y + Bounds.Height / 2);
            var local = Vector2.Transform(point, Matrix3x2.CreateRotation(-Rotation * MathF.PI / 180, center));
            var swap = ScreenshotGeometry.SwapsAxes(Rotation);
            var w = swap ? Bounds.Height : Bounds.Width; var h = swap ? Bounds.Width : Bounds.Height;
            var u = (local.X - center.X) / Math.Max(.01f, w) + .5f; var v = (local.Y - center.Y) / Math.Max(.01f, h) + .5f;
            if (u < 0 || u >= 1 || v < 0 || v >= 1) return false;
            if (MirrorX) u = 1 - u; if (MirrorY) v = 1 - v;
            var x = Math.Clamp((int)(u * image.Width), 0, image.Width - 1); var y = Math.Clamp((int)(v * image.Height), 0, image.Height - 1);
            return image.Pixels.Span[(y * image.Width + x) * 4 + 3] >= 16;
        }
        var edge = Math.Max(tolerance, Width / 2);
        if (Tool == ScreenshotTool.Rectangle)
            return Math.Min(Math.Min(Math.Abs(point.X - Bounds.X), Math.Abs(point.X - Bounds.Right)), Math.Min(Math.Abs(point.Y - Bounds.Y), Math.Abs(point.Y - Bounds.Bottom))) <= edge;
        if (Tool == ScreenshotTool.Ellipse)
        {
            var rx = Math.Max(.5f, Bounds.Width / 2); var ry = Math.Max(.5f, Bounds.Height / 2);
            var x = (point.X - Bounds.X - rx) / rx; var y = (point.Y - Bounds.Y - ry) / ry;
            return Math.Abs(MathF.Sqrt(x * x + y * y) - 1) * Math.Min(rx, ry) <= edge;
        }
        if (Tool is ScreenshotTool.Pen or ScreenshotTool.Eraser or ScreenshotTool.Line or ScreenshotTool.Arrow && !Points.IsDefaultOrEmpty)
        {
            if (Points.Length == 1) return Vector2.Distance(point, Points[0]) <= Width / 2 + tolerance;
            for (var i = 1; i < Points.Length; i++)
            {
                if (ScreenshotGeometry.HitSegment(point, Points[i - 1], Points[i], Width / 2 + tolerance)) return true;
            }
            if (Tool == ScreenshotTool.Arrow && Points.Length >= 2)
            {
                var head = ScreenshotGeometry.ArrowHead(Points[0], Points[^1], Width);
                return ScreenshotGeometry.HitSegment(point, Points[^1], head.Left, Width / 2 + tolerance) || ScreenshotGeometry.HitSegment(point, Points[^1], head.Right, Width / 2 + tolerance);
            }
            return false;
        }
        return true;
    }
}

public sealed record ScreenshotState(ScreenshotFrame Frame, ImmutableArray<ScreenshotAnnotation> Annotations, int NextNumber = 1, Rgba32 Background = default);

public sealed class ScreenshotDocument
{
    private readonly List<ScreenshotState> _undo = [], _redo = [];
    public ScreenshotState State { get; private set; }
    public bool CanUndo => _undo.Count != 0;
    public bool CanRedo => _redo.Count != 0;
    public ScreenshotDocument(ScreenshotFrame frame) => State = new(frame, [], Background: Rgba32.White);
    public static ScreenshotDocument FromImage(ScreenshotFrame image, string name = "画像")
    {
        var doc = new ScreenshotDocument(ScreenshotFrame.Transparent(image.Width, image.Height));
        doc.State = doc.State with { Annotations = [ImageObject(image, Vector2.Zero, name)] }; return doc;
    }
    public static ScreenshotAnnotation ImageObject(ScreenshotFrame image, Vector2 position, string name = "画像") =>
        new(Guid.NewGuid().ToString("N"), ScreenshotTool.Image, new(position.X, position.Y, image.Width, image.Height), Rgba32.White, Text: name, Raster: image);
    public string InsertImage(ScreenshotFrame image, Vector2 position, string name = "画像")
    {
        position = Vector2.Max(Vector2.Zero, position);
        var item = ImageObject(image, position, name);
        var width = Math.Max(State.Frame.Width, checked((int)Math.Ceiling(item.Bounds.Right)));
        var height = Math.Max(State.Frame.Height, checked((int)Math.Ceiling(item.Bounds.Bottom)));
        Apply(State with { Frame = State.Frame.Expand(width, height), Annotations = State.Annotations.Add(item) }); return item.Id;
    }
    public void Apply(ScreenshotState state)
    {
        if (state == State) return;
        if (Frames([state]).Sum(f => (long)f.Pixels.Length) > 512L * 1024 * 1024) throw new InvalidOperationException("編集中の画像の合計が512MiBを超えます。");
        _undo.Add(State); _redo.Clear(); State = state;
        // Count shared raster frames once, rather than charging each annotation edit for the whole image.
        while (_undo.Count > 80 || _undo.Count > 0 && HistoryBytes() > 256L * 1024 * 1024) _undo.RemoveAt(0);
    }
    private static IEnumerable<ScreenshotFrame> Frames(IEnumerable<ScreenshotState> states) => states.SelectMany(s => s.Annotations.Where(a => a.Raster is not null).Select(a => a.Raster!).Append(s.Frame)).Distinct();
    private long HistoryBytes() => Frames(_undo.Append(State)).Sum(f => (long)f.Pixels.Length)
        + _undo.Append(State).Sum(s => s.Annotations.Sum(a => 256L + a.Text.Length * 2L + (a.Points.IsDefault ? 0 : a.Points.Length * 8L) + (a.Cutouts.IsDefault ? 0 : a.Cutouts.Length * 16L)));
    public void Add(ScreenshotAnnotation annotation) => Apply(State with { Annotations = State.Annotations.Add(annotation), NextNumber = checked(State.NextNumber + (annotation.Tool == ScreenshotTool.Number ? 1 : 0)) });
    public void SetNextNumber(int number)
    {
        if (number < 1) throw new ArgumentOutOfRangeException(nameof(number));
        if (number != State.NextNumber) Apply(State with { NextNumber = number });
    }
    public void SetBackground(Rgba32 color) { if (color != State.Background) Apply(State with { Background = color }); }
    public void DeleteRange(PixelRect area) => Apply(ScreenshotEditing.RemoveRange(State, area));
    public void AddMany(IEnumerable<ScreenshotAnnotation> annotations)
    {
        var items = annotations.ToImmutableArray(); if (items.IsEmpty) return;
        var ids = State.Annotations.Select(a => a.Id).ToHashSet();
        if (items.Any(a => !ids.Add(a.Id))) throw new ArgumentException("オブジェクトIDが重複しています。", nameof(annotations));
        Apply(State with { Annotations = State.Annotations.AddRange(items) });
    }
    public void Replace(ScreenshotAnnotation annotation)
    {
        var i = State.Annotations.FindIndex(a => a.Id == annotation.Id);
        if (i >= 0 && State.Annotations[i] != annotation) Apply(State with { Annotations = State.Annotations.SetItem(i, annotation) });
    }
    public void Delete(string id) => Apply(State with { Annotations = State.Annotations.RemoveAll(a => a.Id == id) });
    public void Delete(IReadOnlySet<string> ids) => Apply(State with { Annotations = State.Annotations.RemoveAll(a => ids.Contains(a.Id)) });
    public void ReplaceMany(IEnumerable<ScreenshotAnnotation> annotations)
    {
        var map = annotations.ToDictionary(a => a.Id);
        var next = State.Annotations.Select(a => map.GetValueOrDefault(a.Id, a)).ToImmutableArray();
        if (!next.SequenceEqual(State.Annotations)) Apply(State with { Annotations = next });
    }
    public void Reorder(IReadOnlySet<string> selected, ScreenshotStackOrder order)
    {
        var items = State.Annotations.ToList();
        if (order == ScreenshotStackOrder.Front) items = items.Where(a => !selected.Contains(a.Id)).Concat(items.Where(a => selected.Contains(a.Id))).ToList();
        else if (order == ScreenshotStackOrder.Back) items = items.Where(a => selected.Contains(a.Id)).Concat(items.Where(a => !selected.Contains(a.Id))).ToList();
        else if (order == ScreenshotStackOrder.Forward)
        {
            for (var i = items.Count - 2; i >= 0; i--) if (selected.Contains(items[i].Id) && !selected.Contains(items[i + 1].Id)) (items[i], items[i + 1]) = (items[i + 1], items[i]);
        }
        else for (var i = 1; i < items.Count; i++) if (selected.Contains(items[i].Id) && !selected.Contains(items[i - 1].Id)) (items[i], items[i - 1]) = (items[i - 1], items[i]);
        if (!items.SequenceEqual(State.Annotations)) Apply(State with { Annotations = items.ToImmutableArray() });
    }
    public void Undo() { if (CanUndo) { _redo.Add(State); State = _undo[^1]; _undo.RemoveAt(_undo.Count - 1); } }
    public void Redo() { if (CanRedo) { _undo.Add(State); State = _redo[^1]; _redo.RemoveAt(_redo.Count - 1); } }
    public void Crop(int x, int y, int width, int height)
    {
        var box = new ScreenshotBox(x, y, width, height);
        Apply(State with { Frame = State.Frame.Crop(x, y, width, height), Annotations = State.Annotations.Where(a => ScreenshotEditing.DrawingBounds(a).Intersects(box)).Select(a => a.Move(new(-x, -y))).ToImmutableArray() });
    }
    public void ResizeCanvas(int width, int height, bool centered) => Apply(ScreenshotCanvasResize.Create(State, width, height, centered).Apply(State));
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
