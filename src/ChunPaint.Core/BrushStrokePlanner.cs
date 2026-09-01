namespace ChunPaint.Core;

public enum BrushKind
{
    Oil,
    Watercolor,
    Marker,
    NaturalPencil,
    Airbrush,
    Calligraphy,
    Crayon,
    Flat,
    Pixel
}

public enum BrushDabShape
{
    Round,
    Square,
    Flat
}

public readonly record struct BrushStrokeProfile(
    float WidthScale,
    float OpacityScale,
    float SpacingScale,
    float ScatterScale,
    float DabRadiusScale,
    int DabsPerSample,
    BrushDabShape DabShape,
    bool PixelAligned = false);

public readonly record struct BrushDab(
    float X,
    float Y,
    float Radius,
    float OpacityScale,
    BrushDabShape Shape);

public static class BrushStrokePlanner
{
    private const int MaximumSegments = 4096;

    public static BrushStrokeProfile GetProfile(BrushKind kind) => kind switch
    {
        BrushKind.Oil => new(2.25f, 1f, 0.35f, 0f, 0.5f, 1, BrushDabShape.Round),
        BrushKind.Watercolor => new(3.1f, 0.18f, 0.5f, 0.08f, 0.34f, 5, BrushDabShape.Round),
        BrushKind.Marker => new(3.4f, 0.55f, 0.45f, 0f, 0.5f, 1, BrushDabShape.Flat),
        BrushKind.NaturalPencil => new(0.8f, 0.72f, 3f, 0.35f, 0.22f, 1, BrushDabShape.Round),
        BrushKind.Airbrush => new(3.2f, 0.22f, 2f, 0.5f, 0.035f, 8, BrushDabShape.Round),
        BrushKind.Calligraphy => new(2.8f, 1f, 0.4f, 0.28f, 0.09f, 5, BrushDabShape.Flat),
        BrushKind.Crayon => new(1.6f, 0.68f, 1.8f, 0.24f, 0.16f, 3, BrushDabShape.Square),
        BrushKind.Flat => new(2.8f, 0.9f, 0.4f, 0f, 0.5f, 1, BrushDabShape.Flat),
        BrushKind.Pixel => new(1f, 1f, 1f, 0f, 0.5f, 1, BrushDabShape.Square, PixelAligned: true),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static IReadOnlyList<BrushDab> PlanSegment(
        BrushKind kind,
        float fromX,
        float fromY,
        float toX,
        float toY,
        float baseWidth)
    {
        ValidateCoordinate(fromX, nameof(fromX));
        ValidateCoordinate(fromY, nameof(fromY));
        ValidateCoordinate(toX, nameof(toX));
        ValidateCoordinate(toY, nameof(toY));
        if (!float.IsFinite(baseWidth) || baseWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(baseWidth));
        }

        var profile = GetProfile(kind);
        var width = Math.Max(1f, baseWidth * profile.WidthScale);
        var deltaX = toX - fromX;
        var deltaY = toY - fromY;
        var length = MathF.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (!float.IsFinite(length))
        {
            throw new ArgumentOutOfRangeException(nameof(toX));
        }

        var spacing = Math.Max(0.75f, baseWidth * profile.SpacingScale);
        var segments = length == 0
            ? 0
            : Math.Clamp((int)MathF.Ceiling(length / spacing), 1, MaximumSegments);
        var result = new List<BrushDab>((segments + 1) * profile.DabsPerSample);

        for (var sample = 0; sample <= segments; sample++)
        {
            var progress = segments == 0 ? 0f : sample / (float)segments;
            var centerX = fromX + (deltaX * progress);
            var centerY = fromY + (deltaY * progress);

            for (var dabIndex = 0; dabIndex < profile.DabsPerSample; dabIndex++)
            {
                var angle = Unit(kind, sample, dabIndex, 0) * MathF.Tau;
                var radial = MathF.Sqrt(Unit(kind, sample, dabIndex, 1));
                var scatter = width * profile.ScatterScale * radial;
                var x = centerX + (MathF.Cos(angle) * scatter);
                var y = centerY + (MathF.Sin(angle) * scatter);
                if (profile.PixelAligned)
                {
                    x = MathF.Round(x);
                    y = MathF.Round(y);
                }

                var radiusVariation = 0.75f + (Unit(kind, sample, dabIndex, 2) * 0.25f);
                result.Add(new BrushDab(
                    x,
                    y,
                    Math.Max(0.5f, width * profile.DabRadiusScale * radiusVariation),
                    profile.OpacityScale,
                    profile.DabShape));
            }
        }

        return result;
    }

    private static void ValidateCoordinate(float value, string parameterName)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static float Unit(BrushKind kind, int sample, int dab, int channel)
    {
        var value = unchecked(
            2166136261u
            ^ ((uint)kind * 16777619u)
            ^ ((uint)sample * 2246822519u)
            ^ ((uint)dab * 3266489917u)
            ^ ((uint)channel * 668265263u));
        value ^= value >> 16;
        value *= 2246822519u;
        value ^= value >> 13;
        value *= 3266489917u;
        value ^= value >> 16;
        return (value & 0x00FFFFFFu) / 16777216f;
    }
}
