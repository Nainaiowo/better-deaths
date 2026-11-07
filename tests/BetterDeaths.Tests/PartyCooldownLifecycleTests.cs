using BetterDeaths.PartyCooldowns;

namespace BetterDeaths.Tests;

public sealed class PartyCooldownLifecycleTests
{
    [Fact]
    public void DutyStartResetsEarlierWorldUseButKeepsObservedPreparationUses()
    {
        var lifecycle = new PartyCooldownLifecycle();
        var tracker = new PartyCooldownTracker();
        var rampart = new PartyCooldownProfile(7531, 47, 90, 1);
        var reprisal = new PartyCooldownProfile(7535, 45, 60, 1);
        tracker.Observe(1, rampart, 10, 1);
        lifecycle.Observe(1, rampart.Group, 100);
        lifecycle.BeginDuty(200);
        // These queued observations are resolved before applying the duty-entry event.
        tracker.Observe(1, reprisal, 20, 2);
        lifecycle.Observe(1, reprisal.Group, 200);
        foreach (var profile in new[] { rampart, reprisal })
            if (lifecycle.CanConfirmReadyAtEntry(1, profile.Group, 200))
                tracker.ConfirmReady(1, profile, 21);
        lifecycle.EndEntry();
        Assert.True(tracker.Get(1, rampart, 21).IsReady);
        Assert.False(tracker.Get(1, reprisal, 21).IsReady);
        Assert.Equal(59, tracker.Get(1, reprisal, 21).RemainingSeconds);
    }

    [Fact]
    public void OrdinaryZoneChangeCannotAuthorizeAReset()
    {
        var lifecycle = new PartyCooldownLifecycle();
        lifecycle.Observe(1, 47, 100);
        Assert.False(lifecycle.CanConfirmReadyAtEntry(1, 47, 200));
        Assert.False(lifecycle.HasPendingEntry);
    }

    [Fact]
    public void DelayedEntryForDifferentTerritoryCannotResetCurrentTimers()
    {
        var lifecycle = new PartyCooldownLifecycle();
        lifecycle.BeginDuty(100);
        Assert.False(lifecycle.CanConfirmReadyAtEntry(1, 47, 200));
        lifecycle.EndEntry();
        Assert.False(lifecycle.CanConfirmReadyAtEntry(1, 47, 100));
    }

    [Fact]
    public void PreparationUseOnlyProtectsItsOwnPlayerAndSharedGroup()
    {
        var lifecycle = new PartyCooldownLifecycle();
        lifecycle.BeginDuty(100);
        lifecycle.Observe(1, 7, 100);
        Assert.False(lifecycle.CanConfirmReadyAtEntry(1, 7, 100));
        Assert.True(lifecycle.CanConfirmReadyAtEntry(1, 47, 100));
        Assert.True(lifecycle.CanConfirmReadyAtEntry(2, 7, 100));
    }

    [Fact]
    public void LeavingPartyAndExplicitResetRemoveObsoleteObservationContext()
    {
        var lifecycle = new PartyCooldownLifecycle();
        lifecycle.Observe(1, 7, 100);
        lifecycle.Observe(2, 7, 100);
        lifecycle.RetainMembers(new HashSet<ulong> { 2 });
        lifecycle.BeginDuty(100);
        Assert.True(lifecycle.CanConfirmReadyAtEntry(1, 7, 100));
        Assert.False(lifecycle.CanConfirmReadyAtEntry(2, 7, 100));
        lifecycle.Clear();
        Assert.False(lifecycle.HasPendingEntry);
    }

    [Fact]
    public void ReenteringTheSameDutyDoesNotProtectThePreviousInstancesUses()
    {
        var lifecycle = new PartyCooldownLifecycle();
        lifecycle.BeginDuty(100);
        lifecycle.Observe(1, 47, 100);
        lifecycle.EndEntry();
        lifecycle.EnterTerritory(200);
        lifecycle.EnterTerritory(100);
        lifecycle.BeginDuty(100);
        Assert.True(lifecycle.CanConfirmReadyAtEntry(1, 47, 100));
        lifecycle.Observe(1, 45, 100);
        Assert.False(lifecycle.CanConfirmReadyAtEntry(1, 45, 100));
    }

    [Fact]
    public void DelayedObservationFromOldZoneCannotProtectTheCurrentDuty()
    {
        var lifecycle = new PartyCooldownLifecycle();
        lifecycle.EnterTerritory(200);
        lifecycle.BeginDuty(100);
        lifecycle.Observe(1, 47, 200);
        Assert.True(lifecycle.CanConfirmReadyAtEntry(1, 47, 100));
    }
}
