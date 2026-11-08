using BetterDeaths.PartyCooldowns;

namespace BetterDeaths.Tests;

public sealed class PartyCooldownTrackerTests
{
    private static readonly PartyCooldownProfile Rampart = new(7531, 47, 90, 1);
    private static readonly PartyCooldownProfile Oblation = new(25754, 19, 60, 2);

    [Fact]
    public void MissingHistoryIsUnknownRatherThanInventedReady()
    {
        var tracker = new PartyCooldownTracker();
        var status = tracker.Get(1, Rampart, 100);
        Assert.False(status.IsKnown);
        Assert.False(status.IsReady);
        Assert.Equal(0, status.Charges);
    }

    [Fact]
    public void UnobservedCooldownIsNotDimmedOrReportedAsKnownReady()
    {
        var tracker = new PartyCooldownTracker();
        var status = tracker.Get(1, Rampart, 100);
        Assert.True(status.RemainingSeconds > 0);
        Assert.False(status.IsKnown);
        Assert.False(status.IsReady);
        Assert.False(status.IsOnCooldown);
        Assert.False(default(PartyCooldownStatus).IsOnCooldown);
    }

    [Fact]
    public void ConfirmedReadyDimsOnlyAfterUseAndReturnsToFullColorAfterRecharge()
    {
        var tracker = new PartyCooldownTracker();
        tracker.ConfirmReady(1, Rampart, 0);
        Assert.False(tracker.Get(1, Rampart, 10).IsOnCooldown);
        tracker.Observe(1, Rampart, 10, 1);
        Assert.True(tracker.Get(1, Rampart, 11).IsOnCooldown);
        Assert.False(tracker.Get(1, Rampart, 100).IsOnCooldown);
        Assert.True(tracker.Get(1, Rampart, 100).IsReady);
    }

    [Fact]
    public void ChargedAbilityStaysFullColorUntilAllKnownChargesAreSpent()
    {
        var tracker = new PartyCooldownTracker();
        tracker.ConfirmReady(1, Oblation, 0);
        tracker.Observe(1, Oblation, 10, 1);
        Assert.False(tracker.Get(1, Oblation, 20).IsOnCooldown);
        tracker.Observe(1, Oblation, 30, 2);
        Assert.True(tracker.Get(1, Oblation, 40).IsOnCooldown);
        Assert.False(tracker.Get(1, Oblation, 70).IsOnCooldown);
    }

    [Fact]
    public void UncertainChargesDoNotLookLikeAConfirmedUnavailableAbility()
    {
        var tracker = new PartyCooldownTracker();
        tracker.Observe(1, Oblation, 10, 1);
        var status = tracker.Get(1, Oblation, 11);
        Assert.False(status.IsKnown);
        Assert.False(status.IsReady);
        Assert.False(status.IsOnCooldown);
    }

    [Fact]
    public void PrePullUseStaysDimmedUntilRechargeOrConfirmedReset()
    {
        var tracker = new PartyCooldownTracker();
        tracker.Observe(1, Rampart, 10, 1);
        Assert.True(tracker.Get(1, Rampart, 20).IsOnCooldown);
        Assert.True(tracker.Get(1, Rampart, 30).IsOnCooldown);
        tracker.ConfirmReady(1, Rampart, 31);
        Assert.False(tracker.Get(1, Rampart, 31).IsOnCooldown);
    }

    [Theory]
    [InlineData(0, 1, 10, 90, true)]
    [InlineData(1, 1, 0, 0, false)]
    [InlineData(0, 1, 0, 0, false)]
    [InlineData(1, 2, 10, 120, false)]
    [InlineData(0, 2, 10, 120, true)]
    public void NativeIconDimmingRequiresAnActiveCooldownAndNoAvailableCharge(int charges, int maximum,
        double elapsed, double total, bool dimmed)
    {
        Assert.Equal(dimmed, PartyCooldownStatus.FromNative(charges, maximum, elapsed, total).IsOnCooldown);
    }

    [Fact]
    public void SingleChargeObservationEstablishesExactTimerWithoutCombatFlag()
    {
        var tracker = new PartyCooldownTracker();
        tracker.Observe(1, Rampart, 100, 10);
        var status = tracker.Get(1, Rampart, 125);
        Assert.True(status.IsKnown);
        Assert.False(status.IsReady);
        Assert.Equal(65, status.RemainingSeconds);
        Assert.True(tracker.Get(1, Rampart, 190).IsReady);
    }

    [Fact]
    public void PassingCombatEndDoesNotResetTimers()
    {
        var tracker = new PartyCooldownTracker();
        tracker.Observe(1, Rampart, 10, 1);
        Assert.Equal(70, tracker.Get(1, Rampart, 30).RemainingSeconds);
        Assert.Equal(40, tracker.Get(1, Rampart, 60).RemainingSeconds);
        Assert.True(tracker.Get(1, Rampart, 100).IsReady);
    }

    [Fact]
    public void SharedRecastActionsConsumeOneCommonTimer()
    {
        var tracker = new PartyCooldownTracker();
        var bloodwhetting = new PartyCooldownProfile(25751, 7, 25, 1);
        var nascent = bloodwhetting with { ActionId = 16464 };
        tracker.Observe(1, bloodwhetting, 10, 1);
        Assert.Equal(20, tracker.Get(1, nascent, 15).RemainingSeconds);
        Assert.False(tracker.Get(1, nascent, 15).IsReady);
        tracker.Observe(1, nascent, 36, 2);
        Assert.Equal(24, tracker.Get(1, bloodwhetting, 37).RemainingSeconds);
    }

    [Fact]
    public void SharedGroupWithDifferentRecastsUsesTheAbilityActuallyCast()
    {
        var tracker = new PartyCooldownTracker();
        var levelFiveDeath = new PartyCooldownProfile(18312, 17, 180, 1);
        var ultravibration = new PartyCooldownProfile(23277, 17, 120, 1);
        tracker.Observe(1, levelFiveDeath, 10, 1);
        Assert.Equal(170, tracker.Get(1, ultravibration, 20).RemainingSeconds);
        tracker.Observe(1, ultravibration, 200, 2);
        Assert.Equal(110, tracker.Get(1, levelFiveDeath, 210).RemainingSeconds);
    }

    [Fact]
    public void UnknownSharedGroupWaitsForItsLongestPossibleInitialRecast()
    {
        var tracker = new PartyCooldownTracker();
        var shorter = new PartyCooldownProfile(23277, 17, 120, 1);
        var longer = new PartyCooldownProfile(18312, 17, 180, 1);
        _ = tracker.Get(1, shorter, 0);
        _ = tracker.Get(1, longer, 0);
        Assert.False(tracker.Get(1, shorter, 130).IsKnown);
        Assert.True(tracker.Get(1, shorter, 180).IsReady);
        tracker.ConfirmReady(1, shorter, 200);
        Assert.True(tracker.Get(1, longer, 200).IsReady);
    }

    [Fact]
    public void OblationKeepsSecondChargeAndRechargesSequentially()
    {
        var tracker = new PartyCooldownTracker();
        tracker.ConfirmReady(1, Oblation, 0);
        tracker.Observe(1, Oblation, 10, 1);
        Assert.Equal(new PartyCooldownStatus(1, 2, 50, true), tracker.Get(1, Oblation, 20));
        tracker.Observe(1, Oblation, 30, 2);
        Assert.Equal(new PartyCooldownStatus(0, 2, 30, true), tracker.Get(1, Oblation, 40));
        Assert.Equal(new PartyCooldownStatus(1, 2, 60, true), tracker.Get(1, Oblation, 70));
        Assert.Equal(new PartyCooldownStatus(2, 2, 0, true), tracker.Get(1, Oblation, 130));
    }

    [Fact]
    public void ThreeChargesUseOneContinuousRechargeClock()
    {
        var tracker = new PartyCooldownTracker();
        var dash = new PartyCooldownProfile(1, 2, 30, 3);
        tracker.ConfirmReady(1, dash, 0);
        tracker.Observe(1, dash, 1, 1);
        tracker.Observe(1, dash, 2, 2);
        tracker.Observe(1, dash, 3, 3);
        Assert.Equal(0, tracker.Get(1, dash, 30).Charges);
        Assert.Equal(1, tracker.Get(1, dash, 31).Charges);
        Assert.Equal(2, tracker.Get(1, dash, 61).Charges);
        Assert.Equal(3, tracker.Get(1, dash, 91).Charges);
    }

    [Fact]
    public void UnobservedChargedAbilityStaysUncertainUntilFullRechargeCanBeKnown()
    {
        var tracker = new PartyCooldownTracker();
        Assert.False(tracker.Get(1, Oblation, 0).IsKnown);
        Assert.False(tracker.Get(1, Oblation, 60).IsKnown);
        Assert.Equal(new PartyCooldownStatus(2, 2, 0, true), tracker.Get(1, Oblation, 120));
    }

    [Fact]
    public void UnknownChargedUseDoesNotInventARemainingCharge()
    {
        var tracker = new PartyCooldownTracker();
        tracker.Observe(1, Oblation, 10, 1);
        Assert.False(tracker.Get(1, Oblation, 11).IsKnown);
        Assert.False(tracker.Get(1, Oblation, 11).IsReady);
        Assert.True(tracker.Get(1, Oblation, 130).IsReady);
    }

    [Fact]
    public void EqualChargeCountsDoNotMakeDifferentRechargeClocksKnown()
    {
        var tracker = new PartyCooldownTracker();
        _ = tracker.Get(1, Oblation, 0);
        tracker.Observe(1, Oblation, 1, 1);
        tracker.Observe(1, Oblation, 50, 2);
        var status = tracker.Get(1, Oblation, 51);
        Assert.Equal(0, status.Charges);
        Assert.False(status.IsKnown);
        Assert.False(status.IsReady);
    }

    [Fact]
    public void ConfirmedFullChargesKeepTheFirstRechargeClockAcrossLaterUses()
    {
        var tracker = new PartyCooldownTracker();
        tracker.ConfirmReady(1, Oblation, 0);
        tracker.Observe(1, Oblation, 1, 1);
        tracker.Observe(1, Oblation, 50, 2);
        Assert.Equal(new PartyCooldownStatus(0, 2, 10, true), tracker.Get(1, Oblation, 51));
    }

    [Fact]
    public void AoeAndDuplicatePacketsDoNotSpendMultipleCharges()
    {
        var tracker = new PartyCooldownTracker();
        tracker.ConfirmReady(1, Oblation, 0);
        tracker.Observe(1, Oblation, 10, 42);
        tracker.Observe(1, Oblation, 10.1, 42);
        tracker.Observe(1, Oblation, 10.2, 42);
        Assert.Equal(1, tracker.Get(1, Oblation, 11).Charges);
        Assert.Equal(59, tracker.Get(1, Oblation, 11).RemainingSeconds);
    }

    [Fact]
    public void ZeroSequenceDuplicatesAreBoundedWithoutDroppingSeparateUses()
    {
        var tracker = new PartyCooldownTracker();
        tracker.ConfirmReady(1, Oblation, 0);
        tracker.Observe(1, Oblation, 10, 0);
        tracker.Observe(1, Oblation, 10.02, 0);
        Assert.Equal(1, tracker.Get(1, Oblation, 11).Charges);
        tracker.Observe(1, Oblation, 12, 0);
        Assert.Equal(0, tracker.Get(1, Oblation, 13).Charges);
    }

    [Fact]
    public void LateDuplicateOfAnEarlierUseCannotMoveTheRechargeClock()
    {
        var tracker = new PartyCooldownTracker();
        tracker.ConfirmReady(1, Oblation, 0);
        tracker.Observe(1, Oblation, 10, 1);
        tracker.Observe(1, Oblation, 12, 2);
        tracker.Observe(1, Oblation, 13, 1);
        Assert.Equal(55, tracker.Get(1, Oblation, 15).RemainingSeconds);
    }

    [Fact]
    public void LateOlderPacketsCannotRestartANewerCooldown()
    {
        var tracker = new PartyCooldownTracker();
        tracker.Observe(1, Rampart, 100, 2);
        tracker.Observe(1, Rampart, 99, 1);
        Assert.Equal(80, tracker.Get(1, Rampart, 110).RemainingSeconds);
    }

    [Fact]
    public void DifferentPlayersAndDifferentGroupsAreIsolated()
    {
        var tracker = new PartyCooldownTracker();
        tracker.ConfirmReady(2, Rampart, 0);
        tracker.ConfirmReady(1, Oblation, 0);
        tracker.Observe(1, Rampart, 10, 1);
        Assert.True(tracker.Get(2, Rampart, 20).IsReady);
        Assert.Equal(2, tracker.Get(1, Oblation, 20).Charges);
        Assert.False(tracker.Get(1, Rampart, 20).IsReady);
    }

    [Fact]
    public void ConfirmedResetClearsCooldownAndPacketDeduplicationState()
    {
        var tracker = new PartyCooldownTracker();
        tracker.Observe(1, Rampart, 10, 1);
        tracker.ConfirmReady(1, Rampart, 20);
        Assert.True(tracker.Get(1, Rampart, 20).IsReady);
        tracker.Observe(1, Rampart, 21, 1);
        Assert.False(tracker.Get(1, Rampart, 22).IsReady);
    }

    [Fact]
    public void ConfirmedResetAlsoRestoresShortCooldowns()
    {
        var tracker = new PartyCooldownTracker();
        var shortCooldown = new PartyCooldownProfile(25751, 7, 25, 1);
        tracker.Observe(1, shortCooldown, 10, 1);
        tracker.ConfirmReady(1, shortCooldown, 12);
        Assert.True(tracker.Get(1, shortCooldown, 12).IsReady);
    }

    [Fact]
    public void MemberLeavingDoesNotChangeOtherMembers()
    {
        var tracker = new PartyCooldownTracker();
        tracker.Observe(1, Rampart, 10, 1);
        tracker.Observe(2, Rampart, 10, 1);
        tracker.RetainMembers(new HashSet<ulong> { 2 });
        Assert.False(tracker.Get(1, Rampart, 20).IsKnown);
        Assert.True(tracker.Get(2, Rampart, 20).IsKnown);
        Assert.Equal(80, tracker.Get(2, Rampart, 20).RemainingSeconds);
    }

    [Fact]
    public void ProfileChangeInvalidatesOldLevelSyncState()
    {
        var tracker = new PartyCooldownTracker();
        var swiftcast = new PartyCooldownProfile(7561, 44, 60, 1);
        tracker.Observe(1, swiftcast, 0, 1);
        Assert.False(tracker.Get(1, swiftcast with { RecastSeconds = 40 }, 10).IsKnown);
    }

    [Theory]
    [InlineData(0, 60, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, -5, 1)]
    [InlineData(1, double.NaN, 1)]
    [InlineData(1, 60, 0)]
    [InlineData(1, 60, 6)]
    public void RejectsInvalidProfiles(byte group, double seconds, int charges)
    {
        var tracker = new PartyCooldownTracker();
        var profile = new PartyCooldownProfile(1, group, seconds, charges);
        tracker.Observe(1, profile, 10, 1);
        Assert.Equal(default, tracker.Get(1, profile, 20));
    }

    [Theory]
    [InlineData(0, 2, 0, 120, 60)]
    [InlineData(0, 2, 30, 120, 30)]
    [InlineData(1, 2, 60, 120, 60)]
    [InlineData(1, 2, 90, 120, 30)]
    [InlineData(2, 2, 120, 120, 0)]
    [InlineData(0, 3, 10, 90, 20)]
    [InlineData(1, 3, 40, 90, 20)]
    [InlineData(0, 1, 5, 40, 35)]
    [InlineData(0, 1, 30, 60, 30)]
    public void NativeRecastShowsTimeToNextChargeRatherThanTimeToFull(int charges, int maximum, double elapsed, double total, double remaining)
    {
        var status = PartyCooldownStatus.FromNative(charges, maximum, elapsed, total);
        Assert.Equal(remaining, status.RemainingSeconds);
        Assert.Equal(charges, status.Charges);
        Assert.True(status.IsKnown);
    }

    [Fact]
    public void RepeatedStatusReadsAllocateNoPerFrameObjects()
    {
        var tracker = new PartyCooldownTracker();
        tracker.Observe(1, Rampart, 10, 1);
        for (var iteration = 0; iteration < 100; iteration++)
            _ = tracker.Get(1, Rampart, 20);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var iteration = 0; iteration < 10_000; iteration++)
            _ = tracker.Get(1, Rampart, 20);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void MockEncountersNeverInventKnownChargesFromPartialHistory(int maximum)
    {
        var random = new Random(8363 + maximum);
        var profile = new PartyCooldownProfile(1, 1, 30, maximum);
        for (var scenario = 0; scenario < 100; scenario++)
        {
            var tracker = new PartyCooldownTracker();
            var actualEnds = new List<double>();
            var initialMissing = random.Next(maximum + 1);
            var firstRecharge = random.NextDouble() * 30;
            for (var charge = 0; charge < initialMissing; charge++)
                actualEnds.Add(firstRecharge + charge * 30);
            _ = tracker.Get(1, profile, 0);
            uint sequence = 0;
            for (var now = 1; now <= 600; now++)
            {
                actualEnds.RemoveAll(end => end <= now);
                if (actualEnds.Count < maximum && random.NextDouble() < .15)
                {
                    actualEnds.Add((actualEnds.Count == 0 ? now : actualEnds[^1]) + 30);
                    tracker.Observe(1, profile, now, ++sequence);
                }
                var status = tracker.Get(1, profile, now);
                Assert.InRange(status.Charges, 0, maximum - actualEnds.Count);
                if (status.IsKnown)
                {
                    Assert.Equal(maximum - actualEnds.Count, status.Charges);
                    Assert.Equal(actualEnds.Count > 0 ? actualEnds[0] - now : 0, status.RemainingSeconds, 3);
                }
            }
        }
    }
}
