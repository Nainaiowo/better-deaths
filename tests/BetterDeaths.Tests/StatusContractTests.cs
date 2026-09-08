using System.Text.Json;
using BetterDeaths.DamageParsing;

namespace BetterDeaths.Tests;

public class StatusContractTests
{
    private static readonly DateTime Start = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DamageActorIdentity Player = new(0x1001, "Player", 0, "", true, 23);
    private static readonly DamageActorIdentity A = new(0x1002, "Provider A", 0, "", true, 33);
    private static readonly DamageActorIdentity B = A with { EntityId = 0x1003, Name = "Provider B" };

    [Theory]
    [InlineData(0xF2Fu)]
    [InlineData(0xF31u)]
    [InlineData(0xB94u)]
    [InlineData(0x71Du)]
    [InlineData(0x71Eu)]
    [InlineData(0x839u)]
    public void UnknownStrengthNeverBecomesRoleOrMaximumStrength(uint statusId)
    {
        foreach (var job in Enumerable.Range(1, 42))
        {
            var snapshot = new DamageStatusSnapshot(statusId, A, 3, 15) { HasParameter = false };
            Assert.True(RaidBuffPolicy.HasUnknownStrength(snapshot));
            Assert.Equal(0, Assert.Single(RaidBuffPolicy.GetEffects(snapshot, false, Player with { ClassJobId = (uint)job })).Amount);
            var knownZero = snapshot with { AppliedParameter = 0 };
            Assert.False(RaidBuffPolicy.HasUnknownStrength(knownZero));
            Assert.Equal(0, Assert.Single(RaidBuffPolicy.GetEffects(knownZero, false, Player)).Amount);
        }
    }

    [Theory]
    [InlineData(0xF2Fu)]
    [InlineData(0xF31u)]
    [InlineData(0xB94u)]
    [InlineData(0x71Eu)]
    public void AppliedStrengthSurvivesLandingAndObservationWithoutConflatingStacks(uint status)
    {
        var tracker = new RaidBuffTracker(true);
        tracker.Observe(Buff(0, status) with { ActionId = 100, DurationSeconds = 0,
            ObservationKind = DamageStatusObservationKind.Announcement, AppliedParameter = 4, ApplicationSequence = 10 });
        tracker.Observe(Buff(1, status) with { Parameter = 3, HasParameter = false, ApplicationSequence = 10 });
        Snapshot(tracker, 2, new DamageStatusSnapshot(status, A, 3, 13) { HasParameter = false, StatusSlot = 4 });
        var active = Assert.Single(Active(tracker, 3));
        Assert.Equal(3, active.Parameter);
        Assert.Equal((byte)4, active.AppliedParameter);
        Assert.Equal(.04, Assert.Single(RaidBuffPolicy.GetEffects(active, false, Player)).Amount);
        Assert.Equal(active, JsonSerializer.Deserialize<DamageStatusSnapshot>(JsonSerializer.Serialize(active)));
    }

    [Fact]
    public void ReplacementWithoutStrengthDoesNotBorrowPreviousGenerationsValue()
    {
        var tracker = new RaidBuffTracker(true);
        tracker.Observe(Buff(0) with { AppliedParameter = 6, ApplicationSequence = 1 });
        tracker.Observe(Buff(1) with { ApplicationSequence = 2 });
        Assert.True(RaidBuffPolicy.HasUnknownStrength(Assert.Single(Active(tracker, 2))));
    }

    [Theory]
    [InlineData(0xF2Fu)]
    [InlineData(0xF31u)]
    [InlineData(0xB94u)]
    [InlineData(0x71Eu)]
    public void RemovalBetweenAnnouncementAndLandingDoesNotEraseTheNewApplicationsStrength(uint status)
    {
        var tracker = new RaidBuffTracker(true);
        tracker.Observe(Buff(0, status) with { AppliedParameter = 6, ApplicationSequence = 1 });
        tracker.Observe(Buff(1, status) with { ActionId = 100, DurationSeconds = 0, AppliedParameter = 3,
            ApplicationSequence = 2, ObservationKind = DamageStatusObservationKind.Announcement });
        tracker.Observe(Buff(2, status) with { IsRemoval = true });
        tracker.Observe(Buff(3, status) with { ApplicationSequence = 2 });
        Assert.Equal((byte)3, Assert.Single(Active(tracker, 4)).AppliedParameter);
    }

    [Fact]
    public void MultipleInFlightSequencesRetainTheirOwnApplicationEvidence()
    {
        var tracker = new RaidBuffTracker(true);
        tracker.Observe(Buff(0) with { ActionId = 100, DurationSeconds = 0, AppliedParameter = 3,
            ApplicationSequence = 1, ObservationKind = DamageStatusObservationKind.Announcement });
        tracker.Observe(Buff(1) with { ActionId = 100, DurationSeconds = 0, AppliedParameter = 6,
            ApplicationSequence = 2, ObservationKind = DamageStatusObservationKind.Announcement });
        tracker.Observe(Buff(2) with { ApplicationSequence = 1 });
        Assert.Equal((byte)3, Assert.Single(Active(tracker, 2.5)).AppliedParameter);
        tracker.Observe(Buff(3) with { ApplicationSequence = 2 });
        Assert.Equal((byte)6, Assert.Single(Active(tracker, 4)).AppliedParameter);
    }

    [Theory]
    [InlineData(0xF2Fu)]
    [InlineData(0xF31u)]
    [InlineData(0x71Eu)]
    public void SameSlotReplacementRetiresOnlyThePreviousOccupant(uint status)
    {
        var tracker = new RaidBuffTracker(true);
        tracker.Observe(Buff(0, status) with { AppliedParameter = 6, StatusSlot = 4 });
        tracker.Observe(Buff(1, status) with { Source = B, AppliedParameter = 3, StatusSlot = 4 });
        Assert.Equal(A.EntityId, Assert.Single(Active(tracker, .5)).Source.EntityId);
        Assert.Equal(B.EntityId, Assert.Single(Active(tracker, 2)).Source.EntityId);
    }

    [Fact]
    public void DifferentSlotsAllowConcurrentProviders()
    {
        var tracker = new RaidBuffTracker();
        tracker.Observe(Buff(0) with { StatusSlot = 4, AppliedParameter = 6 });
        tracker.Observe(Buff(1) with { Source = B, StatusSlot = 5, AppliedParameter = 3 });
        Assert.Equal(2, Active(tracker, 2).Count);
    }

    [Fact]
    public void IrrelevantStatusCanAuthoritativelyReplaceARelevantSlot()
    {
        var tracker = new RaidBuffTracker();
        tracker.Observe(Buff(0) with { StatusSlot = 4, AppliedParameter = 6 });
        tracker.Observe(Buff(1, 65000) with { StatusSlot = 4 });
        Assert.Empty(Active(tracker, 2));
    }

    [Fact]
    public void LateOldSlotObservationCannotReviveAnOverwrittenCard()
    {
        var tracker = new RaidBuffTracker();
        tracker.Observe(Buff(0) with { StatusSlot = 4, AppliedParameter = 6 });
        tracker.Observe(Buff(2) with { Source = B, StatusSlot = 4, AppliedParameter = 3 });
        tracker.Observe(Buff(1) with { StatusSlot = 4, ObservationKind = DamageStatusObservationKind.Observation });
        Assert.Equal(B.EntityId, Assert.Single(Active(tracker, 3)).Source.EntityId);
        Assert.Equal(A.EntityId, Assert.Single(Active(tracker, 1.5)).Source.EntityId);
    }

    [Fact]
    public void OldSequencedRemovalCannotRemoveReplacement()
    {
        var tracker = new RaidBuffTracker();
        tracker.Observe(Buff(0) with { ApplicationSequence = 1, AppliedParameter = 6 });
        tracker.Observe(Buff(1) with { ApplicationSequence = 2, AppliedParameter = 3 });
        tracker.Observe(Buff(2) with { ApplicationSequence = 1, IsRemoval = true });
        Assert.Equal((byte)3, Assert.Single(Active(tracker, 3)).AppliedParameter);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StandardFinishPartnerAssociatesWithOriginEvenAfterSelfLanding(bool selfLanded)
    {
        var tracker = new RaidBuffTracker(true);
        tracker.Observe(Buff(0, 0x71D) with { Target = A, DurationSeconds = 0, ActionId = 100,
            ObservationKind = DamageStatusObservationKind.Announcement, AppliedParameter = 2, ApplicationSequence = 7 });
        if (selfLanded)
            tracker.Observe(Buff(.5, 0x71D) with { Target = A, ApplicationSequence = 7 });
        tracker.Observe(Buff(1, 0x839) with { ApplicationSequence = 7 });
        Assert.Equal((byte)2, Assert.Single(Active(tracker, 2)).AppliedParameter);
        tracker.Observe(Buff(3, 0x839) with { ApplicationSequence = 8 });
        Assert.True(RaidBuffPolicy.HasUnknownStrength(Assert.Single(Active(tracker, 4))));
    }

    [Fact]
    public void PetGetsItsOwnConfirmedBuffWithOwnersApplicationStrength()
    {
        var pet = Player with { EntityId = 0x4001, OwnerEntityId = Player.EntityId, IsPlayer = false, IsPet = true };
        var tracker = new RaidBuffTracker(true);
        tracker.Observe(Buff(0) with { DurationSeconds = 0, ActionId = 100,
            ObservationKind = DamageStatusObservationKind.Announcement, AppliedParameter = 3, ApplicationSequence = 4 });
        tracker.Observe(Buff(.5) with { ApplicationSequence = 4 });
        tracker.Observe(Buff(1) with { Target = pet, ApplicationSequence = 4 });
        var active = Assert.Single(tracker.ApplyConfirmed(Buff(2) with { Source = pet }).SourceStatuses);
        Assert.Equal((byte)3, active.AppliedParameter);
        Assert.Equal(A.EntityId, active.Source.EntityId);
    }

    [Fact]
    public void OrdinaryTrackerReconcilesCompleteStatusLists()
    {
        var tracker = new RaidBuffTracker();
        tracker.Observe(Buff(0) with { AppliedParameter = 6 });
        Snapshot(tracker, 1);
        Assert.Empty(Active(tracker, 2));
    }

    [Fact]
    public void SlotReuseCannotRetireADotThatMovedToAnotherSlot()
    {
        var tracker = new PeriodicDamageTracker();
        var target = Player with { EntityId = 0x4001, IsPlayer = false };
        var dot = new DamageStatusApplication(target, A, 50000, "DoT", 0, 0, "", Start, 30, true, false, false)
            { PeriodicPotency = 20, StatusSlot = 4, ObservationKind = DamageStatusObservationKind.Landing };
        tracker.Observe(dot);
        tracker.Observe(dot with { SeenAtUtc = Start.AddSeconds(1), StatusSlot = 5 });
        tracker.Observe(dot with { SeenAtUtc = Start.AddSeconds(2), StatusId = 50001, IsPeriodicDamage = false });
        var tick = Assert.Single(tracker.Process(new(1, Start.AddSeconds(3), target, 0, "", 0, 1000, null)));
        Assert.Equal(50000u, tick.StatusId);
        tracker.Observe(dot with { SeenAtUtc = Start.AddSeconds(4), StatusId = 50001, IsPeriodicDamage = false, StatusSlot = 5 });
        Assert.Equal(0u, Assert.Single(tracker.Process(new(2, Start.AddSeconds(6), target, 0, "", 0, 1000, null))).StatusId);
    }

    private static DamageStatusApplication Buff(double second, uint status = 0xF2F) =>
        new(Player, A, status, "Buff", 0, 0, "", Start.AddSeconds(second), 15, false, false, false)
        { HasParameter = false, ObservationKind = DamageStatusObservationKind.Landing };

    private static IReadOnlyList<DamageStatusSnapshot> Active(RaidBuffTracker tracker, double second) =>
        tracker.ApplyConfirmed(Buff(second) with { Source = Player }).SourceStatuses;

    private static void Snapshot(RaidBuffTracker tracker, double second, params DamageStatusSnapshot[] snapshots) =>
        tracker.ObserveSnapshots(Buff(second) with { Source = Player, HasSourceStatusSnapshot = true, SourceStatuses = snapshots });
}
