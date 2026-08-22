namespace BetterDeaths;

using System;

internal enum PlayerDeathObservation
{
    Unknown,
    Alive,
    WorldObjectDead,
}

internal static class DeathDetectionPolicy
{
    public static PlayerDeathObservation ClassifyPolledState(
        bool hasWorldObject,
        bool worldObjectIsDead,
        uint currentHp,
        uint maxHp)
    {
        if (!hasWorldObject)
        {
            return PlayerDeathObservation.Unknown;
        }

        if (worldObjectIsDead)
        {
            return PlayerDeathObservation.WorldObjectDead;
        }

        return maxHp > 0 && currentHp > 0
            ? PlayerDeathObservation.Alive
            : PlayerDeathObservation.Unknown;
    }

    public static bool IsConfirmedLethalDamageResult(
        bool wasKnownAlive,
        bool hasMatchedDamage,
        uint resultCurrentHp,
        uint resultShieldHp,
        uint resultMaxHp)
    {
        return wasKnownAlive &&
            hasMatchedDamage &&
            resultMaxHp > 0 &&
            resultCurrentHp == 0 &&
            resultShieldHp == 0;
    }

    public static bool IsConfirmedAliveResult(uint resultCurrentHp, uint resultMaxHp)
    {
        return resultMaxHp > 0 && resultCurrentHp > 0;
    }

    public static bool ShouldConfirmPendingWorldObjectDeath(
        bool wasKnownAlive,
        bool worldObjectIsStillDead,
        DateTime firstSeenAtUtc,
        DateTime now,
        TimeSpan confirmationDelay)
    {
        return wasKnownAlive &&
            worldObjectIsStillDead &&
            now >= firstSeenAtUtc &&
            now - firstSeenAtUtc >= confirmationDelay;
    }

    public static bool IsPendingCandidateExpired(
        DateTime firstSeenAtUtc,
        DateTime now,
        TimeSpan retention)
    {
        return now >= firstSeenAtUtc &&
            now - firstSeenAtUtc > retention;
    }
}
