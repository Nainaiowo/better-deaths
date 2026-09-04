namespace BetterDeaths.DamageParsing;

using System;

internal static class DamageBaseRatePolicy
{
    private const double MinimumCriticalRate = 0.05;

    public static double EstimateObserved(int hits, int samples, bool critical)
    {
        var minimum = critical ? MinimumCriticalRate : 0.0;
        return samples >= 11
            ? Math.Clamp(hits / (double)samples, minimum, 1.0)
            : critical ? 0.15 : 0.05;
    }

    public static DamageBaseRateSnapshot? FromAttributes(
        int criticalHit,
        int directHit,
        int levelBase,
        int levelDivisor)
    {
        if (criticalHit <= 0 || directHit < 0 || levelBase <= 0 || levelDivisor <= 0)
        {
            return null;
        }

        var criticalThousandths = 50 + 200 * (criticalHit - levelBase) / levelDivisor;
        var directHitThousandths = 550 * (directHit - levelBase) / levelDivisor;
        return new DamageBaseRateSnapshot(
            Math.Clamp(criticalThousandths / 1000.0, MinimumCriticalRate, 1.0),
            Math.Clamp(directHitThousandths / 1000.0, 0.0, 1.0));
    }

    public static double ResolveCriticalLowByte(byte lowByte, double expectedRate)
    {
        var best = expectedRate;
        var bestDistance = double.MaxValue;
        for (var wrap = 0; wrap <= 3; wrap++)
        {
            var candidate = lowByte / 1000.0 + wrap * 0.255;
            if (candidate > 1.0)
            {
                break;
            }

            var distance = Math.Abs(candidate - expectedRate);
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }
}
