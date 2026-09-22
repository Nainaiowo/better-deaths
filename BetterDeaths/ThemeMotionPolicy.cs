using System;
using System.Numerics;

namespace BetterDeaths;

internal readonly record struct ThemeParticlePose(Vector2 Center, float Size, float Rotation, float Opacity);

internal static class ThemeMotionPolicy
{
    public const int MaxParticles = 6;
    public const float DefaultStrength = 0.55f;

    public static float NormalizeStrength(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 1) : DefaultStrength;

    public static ThemeParticlePose SampleSignature(ThemeDecoration decoration, Vector2 size, double seconds, bool animated)
    {
        if (decoration is <= ThemeDecoration.None or > ThemeDecoration.Lanterns ||
            !float.IsFinite(size.X) || !float.IsFinite(size.Y) || size.X < 16 || size.Y < 16)
            return default;
        animated = animated && double.IsFinite(seconds) && seconds >= 0;
        var rotation = animated ? (float)Math.Sin(seconds * 0.45 + (int)decoration) * 0.10f : -0.12f;
        var bob = animated ? (float)Math.Sin(seconds * 0.65) * MathF.Min(1.5f, MathF.Min(size.X, size.Y) * 0.06f) : 0;
        return new ThemeParticlePose(size * 0.5f + new Vector2(0, bob), MathF.Min(size.X, size.Y) * 0.78f, rotation, 1);
    }

    public static int GetCount(ThemeDecoration decoration, Vector2 size, float strength, bool compact)
    {
        if (decoration is <= ThemeDecoration.None or > ThemeDecoration.Lanterns || NormalizeStrength(strength) <= 0 ||
            !float.IsFinite(size.X) || !float.IsFinite(size.Y) || size.X < 32 || size.Y < 24)
            return 0;
        var area = Math.Min((double)size.X * size.Y, 1_000_000);
        var maximum = Math.Clamp((int)(area / 120_000) + 2, 2, compact ? 3 : MaxParticles);
        return (int)MathF.Ceiling(maximum * NormalizeStrength(strength));
    }

    public static ThemeParticlePose Sample(ThemeDecoration decoration, int index, Vector2 size, double seconds, bool animated, float uiScale = 1)
    {
        if (index < 0 || index >= MaxParticles || decoration is <= ThemeDecoration.None or > ThemeDecoration.Lanterns ||
            !float.IsFinite(size.X) || !float.IsFinite(size.Y) || size.X < 32 || size.Y < 24)
            return default;
        animated = animated && double.IsFinite(seconds) && seconds >= 0;
        var time = animated ? seconds : 0;
        uiScale = float.IsFinite(uiScale) ? Math.Clamp(uiScale, 0.5f, 3) : 1;
        var seed = index * 0.61803398875 + (int)decoration * 0.137;
        var phase = Fraction(seed);
        var cycle = Fraction(time / (38 + index % 5 * 7) + phase);
        var x = Fraction(seed * 1.71 + 0.21);
        var y = cycle;
        var angle = (float)Math.Sin(time * 0.25 + seed * 12) * 0.38f;

        switch (decoration)
        {
            case ThemeDecoration.Sweets:
            case ThemeDecoration.Lanterns:
                y = 1 - cycle;
                x += Math.Sin(time * 0.23 + seed * 8) * 0.035;
                break;
            case ThemeDecoration.Paper:
                x = cycle;
                y = Fraction(seed * 2.7) * 0.65 + (1 - cycle) * 0.3;
                angle *= 0.3f;
                break;
            case ThemeDecoration.Waves:
            case ThemeDecoration.Circuit:
                x = cycle;
                y = Fraction(seed * 2.7) + Math.Sin(time * 0.2 + seed) * 0.025;
                angle = 0;
                break;
            case ThemeDecoration.Chains:
            case ThemeDecoration.Deco:
            case ThemeDecoration.Thorns:
                y = Fraction(seed * 2.7);
                angle *= 0.35f;
                break;
            case ThemeDecoration.Pixels:
                x = cycle;
                y = Math.Floor(Fraction(seed * 2.7) * 6) / 6;
                angle = 0;
                break;
            case ThemeDecoration.Glass:
                angle = (float)(Fraction(time / 80 + phase) * Math.Tau);
                break;
            case ThemeDecoration.Constellation:
                y = Fraction(seed * 2.7);
                break;
            default:
                x += Math.Sin(time * 0.3 + seed * 8) * 0.055;
                break;
        }

        var diameter = MathF.Min((22 + index % 3 * 8) * uiScale, MathF.Min(size.X, size.Y) * 0.65f);
        // Sparse, asymmetric edge lanes leave the information in the center unobstructed.
        var margin = diameter * 0.72f;
        var travel = MathF.Min(20 * uiScale, MathF.Max(0, size.X * 0.18f - margin * 2));
        var fromEdge = margin + travel * (float)Math.Clamp(x, 0, 1);
        var center = new Vector2(
            index % 3 == 2 ? fromEdge : size.X - fromEdge,
            margin + (size.Y - margin * 2) * (float)Math.Clamp(y, 0, 1));
        var fade = animated ? Math.Min(1, Math.Min(cycle, 1 - cycle) * 12) : 1;
        return new ThemeParticlePose(center, diameter, angle, (float)fade);
    }

    private static double Fraction(double value) => value - Math.Floor(value);
}
