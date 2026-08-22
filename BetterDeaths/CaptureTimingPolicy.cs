namespace BetterDeaths;

using System;

internal static class CaptureTimingPolicy
{
    public static bool IsEffectiveInCombat(bool reportedInCombat, bool awaitingCombatClearAfterReset)
    {
        return reportedInCombat && !awaitingCombatClearAfterReset;
    }

    public static bool ShouldReleaseResetCombatOverride(bool awaitingCombatClearAfterReset, bool reportedInCombat)
    {
        return awaitingCombatClearAfterReset && !reportedInCombat;
    }

    public static bool IsLiveCombatCapture(
        bool isDutyCaptureActive,
        bool isPvPCaptureBlocked,
        bool isInCombat,
        DateTime? lastInCombatAtUtc,
        DateTime now,
        TimeSpan postCombatGrace)
    {
        return isDutyCaptureActive &&
            !isPvPCaptureBlocked &&
            (isInCombat || IsWithinPostCombatGrace(lastInCombatAtUtc, now, postCombatGrace));
    }

    public static bool ShouldAcceptRawCombatCapture(
        bool isDutyCaptureActive,
        bool isPvPCaptureBlocked,
        bool captureEnabled,
        bool isInCombat,
        DateTime? lastInCombatAtUtc,
        DateTime now,
        TimeSpan postCombatGrace)
    {
        return captureEnabled &&
            IsLiveCombatCapture(
                isDutyCaptureActive,
                isPvPCaptureBlocked,
                isInCombat,
                lastInCombatAtUtc,
                now,
                postCombatGrace);
    }

    public static bool ShouldClosePull(
        bool isDutyCaptureActive,
        bool isPvPCaptureBlocked,
        bool isInCombat,
        bool hasStartedPull,
        bool isPullClosed,
        DateTime? lastInCombatAtUtc,
        DateTime now,
        TimeSpan postCombatGrace)
    {
        return isDutyCaptureActive &&
            !isPvPCaptureBlocked &&
            !isInCombat &&
            hasStartedPull &&
            !isPullClosed &&
            lastInCombatAtUtc is { } lastInCombat &&
            now > lastInCombat + postCombatGrace;
    }

    public static DateTime GetPullCloseTime(DateTime lastInCombatAtUtc, TimeSpan postCombatGrace)
    {
        return lastInCombatAtUtc + postCombatGrace;
    }

    private static bool IsWithinPostCombatGrace(
        DateTime? lastInCombatAtUtc,
        DateTime now,
        TimeSpan postCombatGrace)
    {
        return lastInCombatAtUtc is { } lastInCombat &&
            now >= lastInCombat &&
            now - lastInCombat <= postCombatGrace;
    }
}
