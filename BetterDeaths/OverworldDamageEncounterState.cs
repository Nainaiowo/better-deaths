namespace BetterDeaths;

using System;

internal sealed class OverworldDamageEncounterState
{
    internal static readonly TimeSpan EndDelay = TimeSpan.FromSeconds(3);
    private DateTime? inactiveSinceUtc;
    private DateTime? lastDamageAtUtc;
    private bool resetRequested;

    public bool RequestReset(bool canReset)
    {
        if (!canReset)
            return false;

        resetRequested = true;
        return true;
    }

    public bool ConsumeReset(bool canReset)
    {
        var requested = resetRequested;
        resetRequested = false;
        return requested && canReset;
    }

    public bool ShouldEnd(bool hasEncounter, bool participantInCombat, DateTime? latestDamageAtUtc, DateTime nowUtc)
    {
        if (!hasEncounter)
        {
            Reset();
            return false;
        }

        var newDamage = latestDamageAtUtc != lastDamageAtUtc;
        lastDamageAtUtc = latestDamageAtUtc;
        if (participantInCombat || newDamage)
        {
            inactiveSinceUtc = null;
            return false;
        }

        inactiveSinceUtc ??= nowUtc;
        return nowUtc - inactiveSinceUtc.Value >= EndDelay;
    }

    public void Reset()
    {
        inactiveSinceUtc = null;
        lastDamageAtUtc = null;
        resetRequested = false;
    }
}
