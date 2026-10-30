using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.Effects;
using PPPaint.Core;
using Windows.Foundation;
using Windows.Graphics.DirectX;
using Windows.UI;

namespace PPPaint.Imaging;

public static class ScreenshotRenderer
{
    public static CanvasBitmap Bitmap(ICanvasResourceCreator device, ScreenshotFrame frame) =>
        CanvasBitmap.CreateFromBytes(device, frame.Pixels.ToArray(), frame.Width, frame.Height, DirectXPixelFormat.B8G8R8A8UIntNormalized, 96, CanvasAlphaMode.Premultiplied);

    public static CanvasRenderTarget Render(ICanvasResourceCreator device, ScreenshotState state, ScreenshotRenderCache? sharedCache = null)
    {
        var target = new CanvasRenderTarget(device, state.Frame.Width, state.Frame.Height, 96);
        using var ownedCache = sharedCache is null ? new ScreenshotRenderCache(device) : null;
        var cache = sharedCache ?? ownedCache!;
        CanvasDrawingSession? ds = null;
        try
        {
            ds = target.CreateDrawingSession(); ds.Clear(Color.FromArgb(0, 0, 0, 0)); ds.DrawImage(cache.Get(state.Frame));
            foreach (var annotation in state.Annotations)
            {
                if (annotation.Tool == ScreenshotTool.Blur)
                {
                    ds.Dispose(); ds = null; ApplyBlur(target, annotation); ds = target.CreateDrawingSession();
                }
                else Draw(ds, annotation, cache);
            }
        }
        catch { target.Dispose(); throw; }
        finally { ds?.Dispose(); }
        return target;
    }

    private static void ApplyBlur(CanvasRenderTarget target, ScreenshotAnnotation annotation)
    {
        if (!float.IsFinite(annotation.BlurRadius) || annotation.BlurRadius is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(annotation.BlurRadius));
        var bounds = annotation.Bounds; var width = (int)target.SizeInPixels.Width; var height = (int)target.SizeInPixels.Height;
        var left = Math.Clamp((int)Math.Floor(bounds.X), 0, width); var top = Math.Clamp((int)Math.Floor(bounds.Y), 0, height);
        var right = Math.Clamp((int)Math.Ceiling(bounds.Right), left, width); var bottom = Math.Clamp((int)Math.Ceiling(bounds.Bottom), top, height);
        if (right == left || bottom == top) return;
        var padding = (int)Math.Ceiling(annotation.BlurRadius * 3);
        var sx = Math.Max(0, left - padding); var sy = Math.Max(0, top - padding);
        var sw = Math.Min(width, right + padding) - sx; var sh = Math.Min(height, bottom + padding) - sy;
        // Snapshot only the affected neighborhood. Reading and drawing the same target would form a D2D cycle.
        using var sample = new CanvasRenderTarget(target.Device, sw, sh, 96);
        using (var copy = sample.CreateDrawingSession()) { copy.Clear(Color.FromArgb(0, 0, 0, 0)); copy.DrawImage(target, new Rect(0, 0, sw, sh), new Rect(sx, sy, sw, sh)); }
        using var blur = new GaussianBlurEffect { Source = sample, BlurAmount = annotation.BlurRadius, BorderMode = EffectBorderMode.Hard };
        using var ds = target.CreateDrawingSession(); ds.Blend = CanvasBlend.Copy;
        ds.DrawImage(blur, new Rect(left, top, right - left, bottom - top), new Rect(left - sx, top - sy, right - left, bottom - top));
    }

    public static void Draw(CanvasDrawingSession ds, ScreenshotAnnotation a, ScreenshotRenderCache? cache = null)
    {
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
            case ScreenshotTool.Text: DrawText(ds, a, false); break;
            case ScreenshotTool.Arrow:
            case ScreenshotTool.Line:
            case ScreenshotTool.Pen:
            case ScreenshotTool.Eraser:
                if (a.Points.IsDefaultOrEmpty) return;
                var oldBlend = ds.Blend;
                if (a.Tool == ScreenshotTool.Eraser) { ds.Blend = CanvasBlend.Copy; color = Color.FromArgb(0, 0, 0, 0); }
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
