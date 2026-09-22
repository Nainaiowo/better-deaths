using System;
using System.Numerics;

namespace BetterDeaths;

internal enum MeterBackgroundKind
{
    Satin, Petals, Leaves, Heat, Ribbons, Liquid, Moonlight, Daylight,
    Marble, Clouds, Fabric, Depth, Brushed, Fizz, Citrus, Weave, Candy,
    Bubblegum, Wisteria, Hearts, Spice, Mint, Peach, Grapes, Cotton, Banana,
}

internal readonly record struct MeterBackgroundParticle(Vector2 Center, float Radius, float Rotation, float Opacity);

internal static class MeterHeaderMotionPolicy
{
    public const int Segments = 32;
    public const int ParticleCount = 8;
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

    public static int DensityCount(int maximum, float strength)
    {
        maximum = Math.Clamp(maximum, 0, ParticleCount);
        return (int)MathF.Ceiling(maximum * ThemeMotionPolicy.NormalizeStrength(strength));
    }

    public static float DensityWeight(int index, int maximum, float strength)
    {
        maximum = Math.Clamp(maximum, 0, ParticleCount);
        return index < 0 || index >= maximum ? 0 :
            Math.Clamp(maximum * ThemeMotionPolicy.NormalizeStrength(strength) - index, 0, 1);
    }

    private static float Fraction(float value) => value - MathF.Floor(value);

    public static float DetailVisibility(Vector2 center, Vector2 size, bool thumbnail)
    {
        if (!ValidSize(size) || !float.IsFinite(center.X) || !float.IsFinite(center.Y))
            return 0;
        var x = center.X / size.X;
        if (thumbnail) return .65f;
        if (size.X < 7 * size.Y)
            return .55f * Math.Clamp((x - .7f) / .1f, 0, 1) * Math.Clamp((.98f - x) / .06f, 0, 1);
        // Wide headers reserve the left title and right-side metrics for text.
        return Math.Clamp((x - .25f) / .1f, 0, 1) * Math.Clamp((.86f - x) / .12f, 0, 1);
    }

    // Analytic, periodic paths: no particle state, random generator, or per-frame collections.
    public static MeterBackgroundParticle Particle(BetterDeathsTheme theme, int index, Vector2 size, float phase, float scale)
    {
        if (!ValidSize(size))
            return default;
        phase = float.IsFinite(phase) ? Fraction(phase) : 0;
        scale = NormalizeScale(scale);
        index = Math.Clamp(index, 0, ParticleCount - 1);
        var kind = MeterThemeStyle.Profile(theme).Background;
        var seed = Fraction(index * .6180339f + (int)theme * .071f);
        var lane = Fraction(index * .381966f + (int)theme * .039f);
        var speed = 1 + (index * 2 + (int)theme) % 3;
        var progress = Fraction(seed + phase * speed);
        var angle = (phase * (1 + (index + (int)theme) % 2) + seed * 1.7f) * MathF.Tau;
        var narrow = size.X < 7 * size.Y;
        var bubbles = kind is MeterBackgroundKind.Fizz or MeterBackgroundKind.Citrus or MeterBackgroundKind.Liquid or
            MeterBackgroundKind.Bubblegum or MeterBackgroundKind.Depth;
        var rising = bubbles || kind is MeterBackgroundKind.Heat or MeterBackgroundKind.Spice;
        var drifting = kind is MeterBackgroundKind.Clouds or MeterBackgroundKind.Cotton;
        var glint = kind == MeterBackgroundKind.Moonlight;
        var sway = (bubbles ? .025f : .01f) + Fraction(seed * 4.7f) * (bubbles ? .015f : .01f);
        var x = narrow ? .78f + lane * .14f : .33f + lane * .4f;
        x = drifting ? (narrow ? .74f + progress * .2f : .28f + progress * .5f) + MathF.Sin(angle) * sway * .3f
            : x + MathF.Sin(angle) * sway;
        var y = glint ? .2f + lane * .6f : drifting ? .25f + lane * .5f + MathF.Sin(angle) * .04f
            : rising ? 1 - progress : progress;
        var fade = MathF.Min(1, MathF.Min(progress, 1 - progress) * 10);
        var pulse = .5f + .5f * MathF.Sin((phase * speed + seed) * MathF.Tau);
        var opacity = (glint ? .3f + pulse * .7f : fade) * (.6f + Fraction(seed * 2.9f) * .4f);
        var radius = (bubbles ? 5 + Fraction(seed * 3) * 4 : 2 + Fraction(seed * 3) * 4) * scale;
        return new(new Vector2(x * size.X, y * size.Y), radius, MathF.Sin(angle) * .7f + seed, opacity);
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
        var wave = MathF.Sin((t + phase * (1 + lane % 2) + lane * .12f + variant) * MathF.Tau);
        var fine = MathF.Sin((t * 3 - phase * (1 + (lane + 1) % 2) + lane * .17f + variant) * MathF.Tau);
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
