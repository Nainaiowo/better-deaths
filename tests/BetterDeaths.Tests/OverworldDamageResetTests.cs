namespace BetterDeaths.Tests;

public sealed class OverworldDamageResetTests
{
    public static IEnumerable<object[]> CaptureStates => Enumerable.Range(0, 64).Select(bits => new object[]
    {
        (bits & 1) != 0, (bits & 2) != 0, (bits & 4) != 0,
        (bits & 8) != 0, (bits & 16) != 0, (bits & 32) != 0,
    });

    [Theory]
    [MemberData(nameof(CaptureStates))]
    public void ManualResetRequiresAnActiveOverworldEncounterAndNoDutyFlags(
        bool loggedIn, bool dutyStarted, bool boundByDuty, bool pvpBlocked, bool overworldTerritory, bool hasEncounter)
    {
        var eligible = CaptureTimingPolicy.IsOverworldDamageMeterCapture(
            loggedIn, dutyStarted, boundByDuty, pvpBlocked, overworldTerritory);
        var expected = loggedIn && !dutyStarted && !boundByDuty && !pvpBlocked && overworldTerritory && hasEncounter;
        var state = new OverworldDamageEncounterState();

        Assert.Equal(expected, state.RequestReset(eligible && hasEncounter));
        Assert.Equal(expected, state.ConsumeReset(eligible && hasEncounter));
        Assert.False(state.ConsumeReset(true));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void EnteringAnyDutyStateDiscardsAPreviouslyQueuedReset(bool dutyStarted, bool boundByDuty, bool overworldTerritory)
    {
        var state = new OverworldDamageEncounterState();
        Assert.True(state.RequestReset(true));
        var eligible = CaptureTimingPolicy.IsOverworldDamageMeterCapture(
            true, dutyStarted, boundByDuty, false, overworldTerritory);

        Assert.False(eligible);
        Assert.False(state.ConsumeReset(eligible));
        Assert.False(state.ConsumeReset(true));
    }

    [Fact]
    public void ZoneOrEncounterBoundaryCancelsQueuedManualReset()
    {
        var state = new OverworldDamageEncounterState();
        Assert.True(state.RequestReset(true));
        state.Reset();
        Assert.False(state.ConsumeReset(true));
    }

    [Fact]
    public void EndedEncounterCannotLeaveAResetQueuedForTheNextEncounter()
    {
        var state = new OverworldDamageEncounterState();
        Assert.True(state.RequestReset(true));
        Assert.False(state.ConsumeReset(false));
        Assert.False(state.ConsumeReset(true));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void OverworldCaptureExitCannotFinishADutyEncounter(bool dutyStarted, bool boundByDuty, bool overworldTerritory)
    {
        Assert.False(CaptureTimingPolicy.CanFinishOverworldOnCaptureExit(
            true, dutyStarted, boundByDuty, overworldTerritory));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void OverworldLogoutOrCaptureLossCanStillSaveItsEncounter(bool loggedIn, bool overworldTerritory)
    {
        Assert.True(CaptureTimingPolicy.CanFinishOverworldOnCaptureExit(
            loggedIn, false, false, overworldTerritory));
    }
}
