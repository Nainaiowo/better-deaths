namespace BetterDeaths.DamageParsing;

using System;

internal static class DamageStatusTiming
{
    // Network and object status durations use the sign independently of remaining lifetime.
    public static float DecodeRemaining(float duration) => float.IsFinite(duration) ? MathF.Abs(duration) : 0;
}
