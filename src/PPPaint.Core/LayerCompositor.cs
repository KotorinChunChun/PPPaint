namespace PPPaint.Core;

public enum LayerBlendMode { Normal, Multiply, Screen, Overlay }

/// <summary>非乗算BGRAをcoverage alphaと描画モードで合成する。</summary>
public static class LayerCompositor
{
    public static PixelSurface Composite(PixelSurface backdrop, PixelSurface foreground, float opacity = 1,
        LayerBlendMode blendMode = LayerBlendMode.Normal, byte[]? mask = null)
    {
        ArgumentNullException.ThrowIfNull(backdrop);
        ArgumentNullException.ThrowIfNull(foreground);
        if (backdrop.Width != foreground.Width || backdrop.Height != foreground.Height)
            throw new ArgumentException("合成する画像の寸法が一致していません。", nameof(foreground));
        if (!float.IsFinite(opacity) || opacity < 0 || opacity > 1) throw new ArgumentOutOfRangeException(nameof(opacity));
        if (!Enum.IsDefined(blendMode)) throw new ArgumentOutOfRangeException(nameof(blendMode));
        if (mask is not null && mask.Length != checked(backdrop.Width * backdrop.Height))
            throw new ArgumentException("マスクは画素数と同じ長さで指定してください。", nameof(mask));
        var result = new PixelSurface(backdrop.Width, backdrop.Height, Rgba32.Transparent);
        for (var y = 0; y < result.Height; y++)
        for (var x = 0; x < result.Width; x++)
        {
            var back = backdrop[x, y]; var front = foreground[x, y];
            var ab = back.A / 255d;
            var af = front.A / 255d * opacity * (mask is null ? 1d : mask[y * result.Width + x] / 255d);
            if (af == 0) { result[x, y] = back; continue; }
            var alpha = af + ab * (1 - af);
            byte Channel(byte b, byte f)
            {
                var cb = b / 255d; var cf = f / 255d;
                var blended = blendMode switch
                {
                    LayerBlendMode.Multiply => cb * cf,
                    LayerBlendMode.Screen => cb + cf - cb * cf,
                    LayerBlendMode.Overlay => cb <= .5 ? 2 * cb * cf : 1 - 2 * (1 - cb) * (1 - cf),
                    _ => cf
                };
                // 重ならない領域は元色を保持。重なる領域にだけ描画モードを適用する。
                var premultiplied = af * (1 - ab) * cf + af * ab * blended + (1 - af) * ab * cb;
                return (byte)Math.Clamp(Math.Round(premultiplied / alpha * 255), 0, 255);
            }
            result[x, y] = new Rgba32(Channel(back.R, front.R), Channel(back.G, front.G), Channel(back.B, front.B),
                (byte)Math.Clamp(Math.Round(alpha * 255), 0, 255));
        }
        return result;
    }
}
