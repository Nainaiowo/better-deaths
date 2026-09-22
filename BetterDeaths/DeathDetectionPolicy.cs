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
    public static bool IsMatchingPlayerWorldObject(
        uint memberEntityId, string memberName, bool isPlayerObject, uint objectEntityId, string objectName)
    {
        return isPlayerObject && memberEntityId != 0 && memberEntityId == objectEntityId &&
            !string.IsNullOrWhiteSpace(memberName) &&
            string.Equals(memberName, objectName, StringComparison.OrdinalIgnoreCase);
    }

    public static bool MatchesCapturedPlayerIdentity(
        string memberName,
        ulong memberContentId,
        uint memberEntityId,
        string capturedName,
        ulong capturedContentId,
        uint capturedEntityId)
    {
        return memberEntityId != 0 && memberEntityId == capturedEntityId &&
            !string.IsNullOrWhiteSpace(memberName) &&
            string.Equals(memberName, capturedName, StringComparison.OrdinalIgnoreCase) &&
            (memberContentId == 0 || capturedContentId == 0 || memberContentId == capturedContentId);
    }

    public static bool EffectResultTargetsMember(uint targetId, uint actorId, uint memberEntityId)
    {
        if (memberEntityId == 0)
        {
            return false;
        }

        return targetId is not (0 or 0xE0000000)
            ? targetId == memberEntityId
            : actorId == memberEntityId;
    }

    public static string BuildDiagnosticCode(
        string signal,
        string identity,
        uint observedHp,
        uint maxHp,
        bool environmentSource = false,
        bool pendingWorldObject = false)
    {
        var hp = maxHp == 0 ? "HU" : observedHp == 0 ? "H0" : "HP";
        return $"D1-{signal}-{identity}-{hp}" +
            (environmentSource ? "-E" : string.Empty) +
            (pendingWorldObject ? "-P" : string.Empty);
    }

    public static string AddEnvironmentEvidence(string? code)
    {
        var savedCode = string.IsNullOrWhiteSpace(code) ? "D0-LEGACY" : code;
        return Array.IndexOf(savedCode.Split('-'), "E") >= 0 ? savedCode : savedCode + "-E";
    }

    public static string DescribeDiagnosticCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return "Legacy pull: the confirmation signal was not saved.";
        }

        var parts = code.Split('-');
        if (parts[0] == "D0")
        {
            return "Legacy pull: the confirmation signal was not saved.";
        }

        if (parts.Length < 4 || parts[0] != "D1")
        {
            return "Unrecognized death confirmation code.";
        }

        var signal = parts[1] switch
        {
            "AC" => "ActorControl death notice",
            "ER" => "matched damage and zero-HP effect result",
            "WO" => "confirmed dead player object",
            "DR" => "dead player object confirmed at duty reset",
            _ => "unknown confirmation signal",
        };
        var identity = parts[2] switch
        {
            "PO" => "matching current party player object",
            "PL" => "matching current party-list member",
            "LO" => "matching local player object",
            "OO" => "matching nearby player object",
            "MD" => "matched action and target",
            _ => "identity source unknown",
        };
        var hp = parts[3] switch
        {
            "HP" => "HP was positive when observed",
            "H0" => "HP was zero when observed",
            _ => "HP was unavailable when observed",
        };
        var details = $"{signal}; {identity}; {hp}.";
        if (Array.IndexOf(parts, "E") >= 0)
        {
            details += " Environment source was reported.";
        }

        if (Array.IndexOf(parts, "P") >= 0)
        {
            details += " A dead-object observation was pending first.";
        }

        return details;
    }

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

    public static bool ShouldRetainPendingWorldObjectDeath(
        bool wasKnownAlive,
        bool isSameActor,
        PlayerDeathObservation observation,
        DateTime firstSeenAtUtc,
        DateTime now,
        TimeSpan retention)
    {
        return wasKnownAlive &&
            isSameActor &&
            observation == PlayerDeathObservation.WorldObjectDead &&
            !IsPendingCandidateExpired(firstSeenAtUtc, now, retention);
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
