using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using PPPaint.Core;
using Windows.Foundation;
using Windows.Graphics.DirectX;
using Windows.UI;

namespace PPPaint.Imaging;

public static class ScreenshotRenderer
{
    public static CanvasBitmap Bitmap(ICanvasResourceCreator device, ScreenshotFrame frame) =>
        CanvasBitmap.CreateFromBytes(device, frame.Pixels.ToArray(), frame.Width, frame.Height, DirectXPixelFormat.B8G8R8A8UIntNormalized, 96, CanvasAlphaMode.Premultiplied);

    public static ScreenshotFrame Frame(CanvasBitmap bitmap) =>
        new((int)bitmap.SizeInPixels.Width, (int)bitmap.SizeInPixels.Height, bitmap.GetPixelBytes());

    public static CanvasRenderTarget Render(ICanvasResourceCreator device, ScreenshotState state, ScreenshotRenderCache? sharedCache = null)
        => RenderScene(device, state, state.Frame.Width, state.Frame.Height, Vector2.Zero, sharedCache);

    public static void DrawCanvasImage(CanvasDrawingSession ds, CanvasBitmap image)
    {
        var bounds = new Rect(0, 0, image.Size.Width, image.Size.Height);
        ds.DrawImage(image, bounds, bounds, 1, CanvasImageInterpolation.NearestNeighbor);
    }

    public static CanvasRenderTarget RenderCanvasPreview(ICanvasResourceCreator device, ScreenshotState state, ScreenshotCanvasResize layout, ScreenshotRenderCache? sharedCache = null)
    {
        layout.ValidateSource(state);
        return RenderScene(device, state, layout.Width, layout.Height, layout.Offset, sharedCache);
    }

    private static CanvasRenderTarget RenderScene(ICanvasResourceCreator device, ScreenshotState state, int width, int height, Vector2 offset, ScreenshotRenderCache? sharedCache)
    {
        var target = new CanvasRenderTarget(device, width, height, 96);
        using var ownedCache = sharedCache is null ? new ScreenshotRenderCache(device) : null;
        var cache = sharedCache ?? ownedCache!;
        CanvasDrawingSession? ds = null;
        try
        {
            ds = target.CreateDrawingSession(); ds.Clear(Color.FromArgb(state.Background.A, state.Background.R, state.Background.G, state.Background.B)); ds.DrawImage(cache.Get(state.Frame), offset);
            foreach (var original in state.Annotations)
            {
                var annotation = offset == Vector2.Zero ? original : original.Move(offset);
                if (annotation.Tool is ScreenshotTool.Blur or ScreenshotTool.Mosaic)
                {
                    ds.Dispose(); ds = null;
                    if (annotation.Tool == ScreenshotTool.Blur) ApplyBlur(target, annotation); else ApplyMosaic(target, annotation);
                    ds = target.CreateDrawingSession();
                }
                else Draw(ds, annotation, cache, state.Background);
            }
        }
        catch { target.Dispose(); throw; }
        finally { ds?.Dispose(); }
        return target;
    }

    private static void ApplyMosaic(CanvasRenderTarget target, ScreenshotAnnotation annotation)
    {
        if (annotation.MosaicSize is < 2 or > 128) throw new ArgumentOutOfRangeException(nameof(annotation.MosaicSize));
        var b = annotation.Bounds; var width = (int)target.SizeInPixels.Width; var height = (int)target.SizeInPixels.Height;
        var domain = SamplingArea(annotation, width, height);
        var left = Math.Clamp((int)Math.Floor(b.X), domain.Left, domain.Right); var top = Math.Clamp((int)Math.Floor(b.Y), domain.Top, domain.Bottom);
        var right = Math.Clamp((int)Math.Ceiling(b.Right), left, domain.Right); var bottom = Math.Clamp((int)Math.Ceiling(b.Bottom), top, domain.Bottom);
        if (left == right || top == bottom) return;
        var columns = Math.Max(1, (right - left + annotation.MosaicSize - 1) / annotation.MosaicSize);
        var rows = Math.Max(1, (bottom - top + annotation.MosaicSize - 1) / annotation.MosaicSize);
        // Cubic sampling can read beyond its source rectangle. Isolate the original
        // canvas so the resampler clamps at that edge instead of reading a joined pane.
        using var isolated = annotation.SamplingBounds is null ? null : new CanvasRenderTarget(target.Device, domain.Right - domain.Left, domain.Bottom - domain.Top, 96);
        if (isolated is not null)
        {
            using var copy = isolated.CreateDrawingSession(); copy.Clear(Color.FromArgb(0, 0, 0, 0));
            copy.DrawImage(target, new Rect(0, 0, isolated.Size.Width, isolated.Size.Height), new Rect(domain.Left, domain.Top, isolated.Size.Width, isolated.Size.Height));
        }
        using var pixels = new CanvasRenderTarget(target.Device, columns, rows, 96);
        using (var sample = pixels.CreateDrawingSession())
        {
            sample.Clear(Color.FromArgb(0, 0, 0, 0));
            sample.DrawImage(isolated ?? target, new Rect(0, 0, columns, rows), new Rect(left - (isolated is null ? 0 : domain.Left), top - (isolated is null ? 0 : domain.Top), right - left, bottom - top), 1, CanvasImageInterpolation.HighQualityCubic);
        }
        using var ds = target.CreateDrawingSession();
        using var mask = EffectMask(ds, annotation, new(left, top, right - left, bottom - top));
        ds.DrawImage(pixels, new Rect(left, top, right - left, bottom - top), new Rect(0, 0, columns, rows), 1, CanvasImageInterpolation.NearestNeighbor, EffectComposite(annotation));
    }

    private static void ApplyBlur(CanvasRenderTarget target, ScreenshotAnnotation annotation)
    {
        if (!float.IsFinite(annotation.BlurRadius) || annotation.BlurRadius is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(annotation.BlurRadius));
        var bounds = annotation.Bounds; var width = (int)target.SizeInPixels.Width; var height = (int)target.SizeInPixels.Height;
        var domain = SamplingArea(annotation, width, height);
        var left = Math.Clamp((int)Math.Floor(bounds.X), domain.Left, domain.Right); var top = Math.Clamp((int)Math.Floor(bounds.Y), domain.Top, domain.Bottom);
        var right = Math.Clamp((int)Math.Ceiling(bounds.Right), left, domain.Right); var bottom = Math.Clamp((int)Math.Ceiling(bounds.Bottom), top, domain.Bottom);
        if (right == left || bottom == top) return;
        var padding = (int)Math.Ceiling(annotation.BlurRadius * 3);
        var sx = Math.Max(domain.Left, left - padding); var sy = Math.Max(domain.Top, top - padding);
        var sw = Math.Min(domain.Right, right + padding) - sx; var sh = Math.Min(domain.Bottom, bottom + padding) - sy;
        // Snapshot only the affected neighborhood. Reading and drawing the same target would form a D2D cycle.
        using var sample = new CanvasRenderTarget(target.Device, sw, sh, 96);
        using (var copy = sample.CreateDrawingSession()) { copy.Clear(Color.FromArgb(0, 0, 0, 0)); copy.DrawImage(target, new Rect(0, 0, sw, sh), new Rect(sx, sy, sw, sh)); }
        using var blur = new GaussianBlurEffect { Source = sample, BlurAmount = annotation.BlurRadius, BorderMode = EffectBorderMode.Hard };
        using var ds = target.CreateDrawingSession();
        using var mask = EffectMask(ds, annotation, new(left, top, right - left, bottom - top));
        ds.DrawImage(blur, new Rect(left, top, right - left, bottom - top), new Rect(left - sx, top - sy, right - left, bottom - top), 1, CanvasImageInterpolation.Linear, EffectComposite(annotation));
    }

    private static (int Left, int Top, int Right, int Bottom) SamplingArea(ScreenshotAnnotation annotation, int width, int height)
    {
        if (annotation.SamplingBounds is not { } bounds) return (0, 0, width, height);
        if (bounds.Width <= 0 || bounds.Height <= 0) return (0, 0, 0, 0);
        var left = Math.Clamp((int)Math.Floor(bounds.X), 0, width); var top = Math.Clamp((int)Math.Floor(bounds.Y), 0, height);
        return (left, top, Math.Clamp((int)Math.Ceiling(bounds.Right), left, width), Math.Clamp((int)Math.Ceiling(bounds.Bottom), top, height));
    }

    private static CanvasGeometry RestrictGeometry(CanvasDrawingSession ds, CanvasGeometry geometry, ScreenshotAnnotation item)
    {
        if (item.ClipBounds is { } clip)
        {
            using var limit = CanvasGeometry.CreateRectangle(ds, new Rect(clip.X, clip.Y, clip.Width, clip.Height));
            var next = geometry.CombineWith(limit, Matrix3x2.Identity, CanvasGeometryCombine.Intersect); geometry.Dispose(); geometry = next;
        }
        if (!item.Cutouts.IsDefaultOrEmpty) foreach (var box in item.Cutouts)
        {
            using var hole = CanvasGeometry.CreateRectangle(ds, new Rect(box.X, box.Y, box.Width, box.Height));
            var next = geometry.CombineWith(hole, Matrix3x2.Identity, CanvasGeometryCombine.Exclude); geometry.Dispose(); geometry = next;
        }
        return geometry;
    }
    private static bool HasMask(ScreenshotAnnotation item) => item.ClipBounds is not null || !item.Cutouts.IsDefaultOrEmpty;
    // DrawImage Copy extends transparent pixels to the clip, erasing content outside the effect.
    private static CanvasComposite EffectComposite(ScreenshotAnnotation item) => HasMask(item) ? CanvasComposite.SourceOver : CanvasComposite.BoundedCopy;
    private static CanvasActiveLayer? EffectMask(CanvasDrawingSession ds, ScreenshotAnnotation item, ScreenshotBox box)
    {
        if (!HasMask(item)) return null;
        using var geometry = RestrictGeometry(ds, CanvasGeometry.CreateRectangle(ds, new Rect(box.X, box.Y, box.Width, box.Height)), item);
        // Clear only the masked destination first so partially transparent effects replace it exactly.
        ds.Blend = CanvasBlend.Copy; ds.FillGeometry(geometry, Color.FromArgb(0, 0, 0, 0)); ds.Blend = CanvasBlend.SourceOver;
        return ds.CreateLayer(1, geometry);
    }
    private static CanvasActiveLayer? ObjectMask(CanvasDrawingSession ds, ScreenshotAnnotation item)
    {
        if (!HasMask(item)) return null;
        var b = ScreenshotEditing.DrawingBounds(item);
        using var geometry = RestrictGeometry(ds, CanvasGeometry.CreateRectangle(ds, new Rect(b.X, b.Y, Math.Max(.1f, b.Width), Math.Max(.1f, b.Height))), item);
        return ds.CreateLayer(1, geometry);
    }
    private static void DrawSquareEraser(CanvasDrawingSession ds, ScreenshotAnnotation item, Rgba32 background)
    {
        if (item.Points.IsDefaultOrEmpty) return;
        using var path = new CanvasPathBuilder(ds);
        path.SetFilledRegionDetermination(CanvasFilledRegionDetermination.Winding);
        void AddSweep(Vector2 from, Vector2 to)
        {
            var polygon = ScreenshotEraserGeometry.Sweep(from, to, item.Width);
            path.BeginFigure(polygon[0]); foreach (var point in polygon.Skip(1)) path.AddLine(point); path.EndFigure(CanvasFigureLoop.Closed);
        }
        AddSweep(item.Points[0], item.Points[0]);
        for (var i = 1; i < item.Points.Length; i++)
            AddSweep(item.Points[i - 1], item.Points[i]);
        using var clipped = RestrictGeometry(ds, CanvasGeometry.CreatePath(path), item);
        var blend = ds.Blend; var antialiasing = ds.Antialiasing;
        try { ds.Blend = CanvasBlend.Copy; ds.Antialiasing = CanvasAntialiasing.Aliased; ds.FillGeometry(clipped, Color.FromArgb(background.A, background.R, background.G, background.B)); }
        finally { ds.Blend = blend; ds.Antialiasing = antialiasing; }
    }
    public static void Draw(CanvasDrawingSession ds, ScreenshotAnnotation a, ScreenshotRenderCache? cache = null, Rgba32 background = default)
    {
        if (a.Tool == ScreenshotTool.Eraser) { DrawSquareEraser(ds, a, a.EraserBackground ?? background); return; }
        using var mask = ObjectMask(ds, a);
        var c = a.OutputColor; var color = Color.FromArgb(c.A, c.R, c.G, c.B);
        var b = a.Bounds; var rect = new Rect(b.X, b.Y, Math.Max(.1f, b.Width), Math.Max(.1f, b.Height));
        switch (a.Tool)
        {
            case ScreenshotTool.Image:
                if (a.Raster is null) return;
                var bitmap = cache?.Get(a.Raster) ?? Bitmap(ds, a.Raster);
                var previous = ds.Transform;
                var center = new Vector2(b.X + b.Width / 2, b.Y + b.Height / 2);
                var swap = ScreenshotGeometry.SwapsAxes(a.Rotation);
                var width = swap ? b.Height : b.Width; var height = swap ? b.Width : b.Height;
                ds.Transform = Matrix3x2.CreateScale(a.MirrorX ? -1 : 1, a.MirrorY ? -1 : 1, center)
                    * Matrix3x2.CreateRotation(a.Rotation * MathF.PI / 180, center) * previous;
                try { ds.DrawImage(bitmap, new Rect(center.X - width / 2, center.Y - height / 2, Math.Max(.1f, width), Math.Max(.1f, height)), new Rect(0, 0, a.Raster.Width, a.Raster.Height)); }
                finally { ds.Transform = previous; if (cache is null) bitmap.Dispose(); }
                break;
            case ScreenshotTool.Redact: ds.FillRectangle(rect, color); break;
            case ScreenshotTool.Highlight: ds.FillRectangle(rect, Color.FromArgb(85, c.R, c.G, c.B)); break;
            case ScreenshotTool.Rectangle: ds.DrawRectangle(rect, color, a.Width); break;
            case ScreenshotTool.Ellipse: ds.DrawEllipse(new(b.X + b.Width / 2, b.Y + b.Height / 2), b.Width / 2, b.Height / 2, color, a.Width); break;
            case ScreenshotTool.Number:
                ds.FillEllipse(new(b.X + b.Width / 2, b.Y + b.Height / 2), b.Width / 2, b.Height / 2, Color.FromArgb(255, c.R, c.G, c.B));
                DrawText(ds, a with { Color = Rgba32.White }, true); break;
            case ScreenshotTool.Text:
                if (!float.IsFinite(a.Opacity) || a.Opacity is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(a.Opacity));
                DrawText(ds, a with { Color = c with { A = (byte)Math.Round(c.A * a.Opacity) } }, false); break;
            case ScreenshotTool.Callout:
                var outline = ScreenshotGeometry.CalloutOutline(b, a.Tail);
                using (var path = new CanvasPathBuilder(ds))
                {
                    path.BeginFigure(outline[0]); foreach (var point in outline.Skip(1)) path.AddLine(point); path.EndFigure(CanvasFigureLoop.Closed);
                    using var geometry = CanvasGeometry.CreatePath(path); ds.FillGeometry(geometry, Color.FromArgb(255, 255, 255, 255)); ds.DrawGeometry(geometry, color, a.Width);
                }
                DrawText(ds, a with { Bounds = new(b.X + 6, b.Y + 5, Math.Max(1, b.Width - 12), Math.Max(1, b.Height - 10)) }, false); break;
            case ScreenshotTool.Arrow:
            case ScreenshotTool.Line:
            case ScreenshotTool.Pen:
                if (a.Points.IsDefaultOrEmpty) return;
                if (a.Tool == ScreenshotTool.Pen && a.Width == 1) { DrawPixelPen(ds, a, color); break; }
                var oldBlend = ds.Blend;
                try
                {
                    var radius = a.Width / 2;
                    ds.FillCircle(a.Points[0], radius, color);
                    for (var i = 1; i < a.Points.Length; i++) { ds.DrawLine(a.Points[i - 1], a.Points[i], color, a.Width); ds.FillCircle(a.Points[i], radius, color); }
                    if (a.Tool == ScreenshotTool.Arrow && a.Points.Length >= 2)
                    {
                        var end = a.Points[^1]; var delta = end - a.Points[0];
                        if (delta.LengthSquared() > 1)
                        {
                            var head = ScreenshotGeometry.ArrowHead(a.Points[0], end, a.Width);
                            ds.DrawLine(end, head.Left, color, a.Width); ds.DrawLine(end, head.Right, color, a.Width);
                        }
                    }
                }
                finally { ds.Blend = oldBlend; }
                break;
        }
    }

    private static void DrawPixelPen(CanvasDrawingSession ds, ScreenshotAnnotation pen, Color color)
    {
        var points = pen.Points.Select(p => new Vector2(MathF.Floor(p.X) + .5f, MathF.Floor(p.Y) + .5f)).ToArray();
        var previous = ds.Antialiasing;
        try
        {
            ds.Antialiasing = CanvasAntialiasing.Aliased;
            if (points.All(p => p == points[0]))
                ds.FillRectangle(points[0].X - .5f, points[0].Y - .5f, 1, 1, color);
            else
            {
                using var path = new CanvasPathBuilder(ds);
                path.BeginFigure(points[0]); foreach (var point in points.Skip(1)) path.AddLine(point); path.EndFigure(CanvasFigureLoop.Open);
                using var geometry = CanvasGeometry.CreatePath(path);
                using var style = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Square, EndCap = CanvasCapStyle.Square, LineJoin = CanvasLineJoin.Round };
                ds.DrawGeometry(geometry, color, 1, style);
            }
        }
        finally { ds.Antialiasing = previous; }
    }

    private static void DrawText(CanvasDrawingSession ds, ScreenshotAnnotation a, bool number)
    {
        var b = a.Bounds; var c = a.Color;
        using var format = new CanvasTextFormat
        {
            FontFamily = "Yu Gothic UI", FontSize = a.FontSize,
            FontWeight = new Windows.UI.Text.FontWeight { Weight = (ushort)(number ? 700 : 400) },
            HorizontalAlignment = number ? CanvasHorizontalAlignment.Center : CanvasHorizontalAlignment.Left,
            VerticalAlignment = number ? CanvasVerticalAlignment.Center : CanvasVerticalAlignment.Top,
            WordWrapping = CanvasWordWrapping.Wrap
        };
        var transform = ds.Transform;
        var center = new Vector2(b.X + b.Width / 2, b.Y + b.Height / 2);
        var swap = Math.Abs(a.Rotation % 180) > 45 && Math.Abs(a.Rotation % 180) < 135;
        var width = swap ? b.Height : b.Width; var height = swap ? b.Width : b.Height;
        ds.Transform = Matrix3x2.CreateRotation(a.Rotation * MathF.PI / 180, center) * transform;
        try { ds.DrawText(a.Text, new Rect(center.X - width / 2, center.Y - height / 2, Math.Max(1, width), Math.Max(1, height)), Color.FromArgb(c.A, c.R, c.G, c.B), format); }
        finally { ds.Transform = transform; }
    }
}
