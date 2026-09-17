using System;
using System.Numerics;

namespace BetterDeaths;

internal enum MeterBackgroundKind
{
    Satin, Petals, Leaves, Heat, Ribbons, Liquid, Moonlight, Daylight,
    Marble, Clouds, Fabric, Depth, Brushed, Fizz, Citrus, Weave, Candy,
}

internal readonly record struct MeterBackgroundParticle(Vector2 Center, float Radius, float Rotation, float Opacity);

internal static class MeterHeaderMotionPolicy
{
    public const int Segments = 32;
    public const int ParticleCount = 16;
    public const double LoopSeconds = 36;

    public static float Phase(double seconds, bool animated, bool reducedMotion) =>
        animated && !reducedMotion && double.IsFinite(seconds) && seconds > 0
            ? (float)(seconds % LoopSeconds / LoopSeconds) : 0;

    public static bool ValidSize(Vector2 size) =>
        float.IsFinite(size.X) && float.IsFinite(size.Y) && size.X > 0 && size.Y > 0;

    public static bool IntersectsClip(Vector2 position, Vector2 size, Vector2 clipMin, Vector2 clipMax) =>
        ValidSize(size) && float.IsFinite(position.X) && float.IsFinite(position.Y) &&
        clipMin.X < clipMax.X && clipMin.Y < clipMax.Y &&
        position.X < clipMax.X && position.Y < clipMax.Y &&
        position.X + size.X > clipMin.X && position.Y + size.Y > clipMin.Y;

    public static float NormalizeScale(float scale) => float.IsFinite(scale) ? Math.Clamp(scale, .5f, 3) : 1;

    private static float Fraction(float value) => value - MathF.Floor(value);

    // Analytic, periodic paths: no particle state, random generator, or per-frame collections.
    public static MeterBackgroundParticle Particle(BetterDeathsTheme theme, int index, Vector2 size, float phase, float scale)
    {
        if (!ValidSize(size))
            return default;
        phase = float.IsFinite(phase) ? Fraction(phase) : 0;
        scale = NormalizeScale(scale);
        index = Math.Clamp(index, 0, ParticleCount - 1);
        var seed = index * .6180339f + (int)theme * .071f;
        var progress = Fraction(seed + phase);
        var x = Fraction(index * .381966f + (int)theme * .039f);
        var angle = (phase + seed) * MathF.Tau;
        x = Math.Clamp(x + MathF.Sin(angle) * .035f, .015f, .985f);
        var kind = MeterThemeStyle.Profile(theme).Background;
        var rising = kind is MeterBackgroundKind.Fizz or MeterBackgroundKind.Citrus or MeterBackgroundKind.Heat;
        var y = rising ? 1 - progress : progress;
        var edge = MathF.Abs(x - .5f) * 2;
        var fade = MathF.Min(1, MathF.Min(progress, 1 - progress) * 10);
        var opacity = fade * (.25f + edge * .75f);
        return new(new Vector2(x * size.X, y * size.Y), (2 + Fraction(seed * 3) * 4) * scale,
            MathF.Sin(angle) * .7f + seed, opacity);
    }

    public static Vector2 Contour(BetterDeathsTheme theme, Vector2 size, int lane, int segment, float phase, int segments = Segments)
    {
        if (!ValidSize(size))
            return default;
        phase = float.IsFinite(phase) ? Fraction(phase) : 0;
        lane = Math.Clamp(lane, 0, 7);
        segments = Math.Clamp(segments, 1, Segments);
        var t = Math.Clamp(segment, 0, segments) / (float)segments;
        var variant = (int)theme * .137f;
        var wave = MathF.Sin((t + phase + lane * .12f + variant) * MathF.Tau);
        var fine = MathF.Sin((t * 3 - phase + variant) * MathF.Tau);
        var kind = MeterThemeStyle.Profile(theme).Background;
        var y = kind switch
        {
            MeterBackgroundKind.Fabric or MeterBackgroundKind.Satin => .20f + lane * .13f + wave * .13f,
            MeterBackgroundKind.Liquid or MeterBackgroundKind.Depth => .68f + lane * .09f + wave * .13f + fine * .035f,
            MeterBackgroundKind.Marble => .12f + lane * .16f + wave * .16f + fine * .035f,
            MeterBackgroundKind.Brushed => .08f + lane * .13f + t * .12f + wave * .006f,
            MeterBackgroundKind.Ribbons or MeterBackgroundKind.Candy => .66f + lane * .13f + wave * .15f,
            MeterBackgroundKind.Clouds => .78f + lane * .09f + wave * .10f,
            _ => .86f + lane * .055f + wave * .04f,
        };
        return new(t * size.X, Math.Clamp(y, 0, 1) * size.Y);
    }
}
