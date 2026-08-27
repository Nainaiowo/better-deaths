namespace BetterDeaths;

public sealed class CaptureTimingPolicyTests
{
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(3);
    private static readonly DateTime CombatAtUtc = new(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ResetOverrideMasksLingeringCombatUntilTheGameActuallyClearsIt()
    {
        Assert.False(CaptureTimingPolicy.IsEffectiveInCombat(
            reportedInCombat: true,
            awaitingCombatClearAfterReset: true));
        Assert.False(CaptureTimingPolicy.ShouldReleaseResetCombatOverride(
            awaitingCombatClearAfterReset: true,
            reportedInCombat: true));
        Assert.True(CaptureTimingPolicy.ShouldReleaseResetCombatOverride(
            awaitingCombatClearAfterReset: true,
            reportedInCombat: false));
        Assert.True(CaptureTimingPolicy.IsEffectiveInCombat(
            reportedInCombat: true,
            awaitingCombatClearAfterReset: false));
    }

    [Fact]
    public void RawCaptureRejectsIdleDutyTraffic()
    {
        var accepted = CaptureTimingPolicy.ShouldAcceptRawCombatCapture(
            isDutyCaptureActive: true,
            isPvPCaptureBlocked: false,
            captureEnabled: true,
            isInCombat: false,
            lastInCombatAtUtc: null,
            now: CombatAtUtc.AddMinutes(5),
            postCombatGrace: Grace);

        Assert.False(accepted);
    }

    [Fact]
    public void DamageParserCanStageDutyPacketsBeforeCombatIsReported()
    {
        Assert.True(CaptureTimingPolicy.ShouldAcceptDamageParserPackets(
            isDutyCaptureActive: true,
            isPvPCaptureBlocked: false));
        Assert.False(CaptureTimingPolicy.ShouldAcceptDamageParserPackets(
            isDutyCaptureActive: false,
            isPvPCaptureBlocked: false));
        Assert.False(CaptureTimingPolicy.ShouldAcceptDamageParserPackets(
            isDutyCaptureActive: true,
            isPvPCaptureBlocked: true));
    }

    [Theory]
    [InlineData(0.0, true)]
    [InlineData(2.999, true)]
    [InlineData(3.0, true)]
    [InlineData(3.001, false)]
    public void RawCaptureUsesBoundedPostCombatGrace(double secondsAfterCombat, bool expected)
    {
        var accepted = CaptureTimingPolicy.ShouldAcceptRawCombatCapture(
            isDutyCaptureActive: true,
            isPvPCaptureBlocked: false,
            captureEnabled: true,
            isInCombat: false,
            lastInCombatAtUtc: CombatAtUtc,
            now: CombatAtUtc.AddSeconds(secondsAfterCombat),
            postCombatGrace: Grace);

        Assert.Equal(expected, accepted);
    }

    [Fact]
    public void RawCaptureRejectsPvpAndDisabledCapture()
    {
        Assert.False(CaptureTimingPolicy.ShouldAcceptRawCombatCapture(
            true,
            true,
            true,
            true,
            CombatAtUtc,
            CombatAtUtc,
            Grace));
        Assert.False(CaptureTimingPolicy.ShouldAcceptRawCombatCapture(
            true,
            false,
            false,
            true,
            CombatAtUtc,
            CombatAtUtc,
            Grace));
    }

    [Fact]
    public void PullClosesOnlyAfterGraceExpires()
    {
        Assert.False(ShouldCloseAt(CombatAtUtc.AddSeconds(3)));
        Assert.True(ShouldCloseAt(CombatAtUtc.AddSeconds(3.001)));
    }

    [Fact]
    public void PullDoesNotCloseWhileCombatIsActiveOrAlreadyClosed()
    {
        Assert.False(ShouldCloseAt(CombatAtUtc.AddSeconds(4), isInCombat: true));
        Assert.False(ShouldCloseAt(CombatAtUtc.AddSeconds(4), isPullClosed: true));
    }

    [Fact]
    public void CloseTimeIsPinnedToGraceBoundary()
    {
        Assert.Equal(CombatAtUtc.AddSeconds(3), CaptureTimingPolicy.GetPullCloseTime(CombatAtUtc, Grace));
    }

    private static bool ShouldCloseAt(
        DateTime now,
        bool isInCombat = false,
        bool isPullClosed = false)
    {
        return CaptureTimingPolicy.ShouldClosePull(
            isDutyCaptureActive: true,
            isPvPCaptureBlocked: false,
            isInCombat,
            hasStartedPull: true,
            isPullClosed,
            lastInCombatAtUtc: CombatAtUtc,
            now,
            postCombatGrace: Grace);
    }
}
