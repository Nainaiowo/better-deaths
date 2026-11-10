using System;

namespace BetterDeaths;

internal static class MitigationReviewPresentation
{
    public const string LateUseTooltip = "Ability was used too late for the fatal snapshot. It remains selectable for simulation purposes.";

    public static string ChatOption(PossibleMitigationSnapshot option) => option.WasUsedTooLate
        ? $"{option.ActionName} ({option.ClassJobName}; used late)"
        : $"{option.ActionName} ({option.ClassJobName})";

    public static float ExpiredPulse(double time, bool reducedMotion) => reducedMotion || !double.IsFinite(time)
        ? .5f : (float)((Math.Sin(time * Math.PI * 2 / 3.5) + 1) * .5);
}
