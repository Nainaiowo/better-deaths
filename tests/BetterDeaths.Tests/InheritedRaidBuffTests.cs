using BetterDeaths.DamageParsing;

namespace BetterDeaths.Tests;

public class InheritedRaidBuffTests
{
    private static readonly DateTime Start = new(2026, 9, 9, 13, 0, 0, DateTimeKind.Utc);
    private static readonly DamageActorIdentity Owner = new(100, "Owner", 0, "", true, 31);
    private static readonly DamageActorIdentity Provider = new(200, "Provider", 0, "", true, 23);
    private static readonly DamageActorIdentity Pet = new(300, "Pet", Owner.EntityId, "Owner", false, 31) { IsPet = true };

    public static IEnumerable<object[]> VariableBuffs => new uint[] { 0x75A, 0x75D, 0xF2F, 0xF31, 0xB94, 0x71D, 0x71E, 0x839 }
        .SelectMany(status => new[] { new object[] { status, false }, new object[] { status, true } });

    [Theory]
    [MemberData(nameof(VariableBuffs))]
    public void ObservedPetBuffRetainsConfirmedApplicationStrength(uint status, bool confirmedOnly)
    {
        var tracker = new RaidBuffTracker(confirmedOnly);
        Land(tracker, Owner, 0, status, 4, 10);
        Observe(tracker, 2, status, 12);
        var active = Active(tracker, 3);
        Assert.Equal((byte)4, active.AppliedParameter);
        Assert.Equal(10u, active.ApplicationSequence);
        Assert.Equal(Provider.EntityId, active.Source.EntityId);
        Assert.Equal(11f, active.RemainingTime);
        Observe(tracker, 4, status, 10);
        Assert.Equal((byte)4, Active(tracker, 5).AppliedParameter);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnerRemovalAndHistoryPruningDoNotErasePetsLearnedStrength(bool expiry)
    {
        var tracker = new RaidBuffTracker();
        Land(tracker, Owner, 0, strength: 4, duration: 5);
        Observe(tracker, 1, remaining: 30);
        if (!expiry)
            tracker.Observe(Buff(Owner, 2) with { IsRemoval = true });
        Observe(tracker, 20, remaining: 11);
        Assert.Equal((byte)4, Active(tracker, 21).AppliedParameter);
        Assert.Empty(AllActive(tracker, 32));
    }

    [Fact]
    public void PendingAndLandedOwnerReplacementsDoNotChangeExistingPetApplication()
    {
        var tracker = new RaidBuffTracker();
        Land(tracker, Owner, 0, strength: 2, sequence: 10);
        Observe(tracker, 1);
        tracker.Observe(Buff(Owner, 2) with { ObservationKind = DamageStatusObservationKind.Announcement,
            AppliedParameter = 6, ApplicationSequence = 11, ActionId = 100, DurationSeconds = 0 });
        Observe(tracker, 3);
        Assert.Equal((byte)2, Active(tracker, 3).AppliedParameter);
        Land(tracker, Owner, 4, strength: 6, sequence: 11);
        Observe(tracker, 5);
        Assert.Equal((byte)2, Active(tracker, 5).AppliedParameter);
        Assert.Equal(10u, Active(tracker, 5).ApplicationSequence);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PetReplacementBindsToItsNewSequence(bool landing)
    {
        var tracker = new RaidBuffTracker();
        Land(tracker, Owner, 0, strength: 2, sequence: 10);
        Observe(tracker, 1);
        Land(tracker, Owner, 2, strength: 6, sequence: 11);
        if (landing)
            tracker.Observe(Buff(Pet, 3) with { ApplicationSequence = 11 });
        else
            Observe(tracker, 3, sequence: 11);
        Observe(tracker, 4);
        Assert.Equal((byte)6, Active(tracker, 4).AppliedParameter);
        Assert.Equal(11u, Active(tracker, 4).ApplicationSequence);
        Assert.Equal((byte)2, Active(tracker, 1.5).AppliedParameter);
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("removal")]
    [InlineData("slot")]
    [InlineData("expiry")]
    public void EndedPetApplicationCannotLeakIntoTheNext(string ending)
    {
        var tracker = new RaidBuffTracker();
        Land(tracker, Owner, 0, strength: 2);
        Observe(tracker, 1, remaining: ending == "expiry" ? 1 : 15);
        if (ending == "absent")
            tracker.ObserveSnapshots(Buff(Pet, 2) with { Source = Pet, HasSourceStatusSnapshot = true });
        else if (ending == "removal")
            tracker.Observe(Buff(Pet, 2) with { IsRemoval = true });
        else if (ending == "slot")
            tracker.Observe(Buff(Pet, 2, 65000) with { StatusSlot = 4 });
        Land(tracker, Owner, 3, strength: 6, sequence: 11);
        Observe(tracker, 4);
        Assert.Equal((byte)6, Active(tracker, 4).AppliedParameter);
        Assert.Equal(11u, Active(tracker, 4).ApplicationSequence);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(3, false)]
    [InlineData(0, true)]
    [InlineData(3, true)]
    public void ExplicitPetStrengthOverridesInheritedStrength(int strength, bool appliedParameter)
    {
        var tracker = new RaidBuffTracker();
        Land(tracker, Owner, 0, strength: 6);
        Observe(tracker, 1);
        Observe(tracker, 2, strength: (byte)strength, appliedParameter: appliedParameter);
        Observe(tracker, 3);
        var active = Active(tracker, 3);
        Assert.False(RaidBuffPolicy.HasUnknownStrength(active));
        Assert.Equal(strength / 100.0, Assert.Single(RaidBuffPolicy.GetEffects(active, false, Pet)).Amount);
    }

    [Fact]
    public void ExplicitPetLandingStrengthIsNotOverwrittenByOwnerAnnouncement()
    {
        var tracker = new RaidBuffTracker();
        Land(tracker, Owner, 0, strength: 6);
        tracker.Observe(Buff(Pet, 1) with { Parameter = 0, HasParameter = true, ApplicationSequence = 10 });
        Assert.Equal(0, Assert.Single(RaidBuffPolicy.GetEffects(Active(tracker, 2), false, Pet)).Amount);
    }

    [Fact]
    public void MissingStrengthRemainsUnknownInsteadOfSwitchingToAnotherOwnerApplication()
    {
        var tracker = new RaidBuffTracker();
        tracker.Observe(Buff(Owner, 0) with { ApplicationSequence = 10 });
        Observe(tracker, 1);
        Land(tracker, Owner, 2, strength: 6, sequence: 11);
        Observe(tracker, 3);
        var active = Active(tracker, 3);
        Assert.True(RaidBuffPolicy.HasUnknownStrength(active));
        Assert.Equal(10u, active.ApplicationSequence);
    }

    [Fact]
    public void LaterConfirmedStrengthCanCompleteTheSameSequencedApplication()
    {
        var tracker = new RaidBuffTracker();
        tracker.Observe(Buff(Owner, 0) with { ApplicationSequence = 10 });
        Observe(tracker, 1);
        Assert.True(RaidBuffPolicy.HasUnknownStrength(Active(tracker, 1)));
        Land(tracker, Owner, 2, strength: 4, sequence: 10);
        Observe(tracker, 3);
        Assert.Equal((byte)4, Active(tracker, 3).AppliedParameter);
        Assert.Equal(10u, Active(tracker, 3).ApplicationSequence);
    }

    [Fact]
    public void UnsequencedUnknownPetBuffDoesNotGuessFromALaterOwnerLanding()
    {
        var tracker = new RaidBuffTracker();
        Observe(tracker, 0);
        Land(tracker, Owner, 1, strength: 6);
        Observe(tracker, 2);
        Assert.True(RaidBuffPolicy.HasUnknownStrength(Active(tracker, 2)));
    }

    [Fact]
    public void EnrichmentPreservesPetSlotAndExplicitZeroWhileClearDropsAssociation()
    {
        var tracker = new RaidBuffTracker();
        Land(tracker, Owner, 0, strength: 6);
        Observe(tracker, 1);
        tracker.Observe(Buff(Pet, 2) with { ObservationKind = DamageStatusObservationKind.Observation,
            Parameter = 0, HasParameter = true, StatusSlot = null, ApplicationSequence = 0 });
        var active = Active(tracker, 2);
        Assert.Equal((byte)4, active.StatusSlot);
        Assert.Equal(10u, active.ApplicationSequence);
        Assert.Equal(0, Assert.Single(RaidBuffPolicy.GetEffects(active, false, Pet)).Amount);
        tracker.Clear();
        Observe(tracker, 3);
        Assert.True(RaidBuffPolicy.HasUnknownStrength(Active(tracker, 3)));
    }

    [Fact]
    public void TemporarilyMissingProviderIdentityDoesNotEraseLearnedPetStrength()
    {
        var tracker = new RaidBuffTracker();
        Land(tracker, Owner, 0, strength: 6);
        Observe(tracker, 1);
        Observe(tracker, 2, provider: Provider with { EntityId = 0 });
        Assert.Equal((byte)6, Active(tracker, 2).AppliedParameter);
        Assert.Equal(Provider.EntityId, Active(tracker, 2).Source.EntityId);
    }

    [Fact]
    public void InheritingAppliedStrengthDoesNotReplacePetsCapturedStackCount()
    {
        var tracker = new RaidBuffTracker();
        tracker.Observe(Buff(Owner, 0) with { AppliedParameter = 6, Parameter = 4, HasParameter = true });
        tracker.ObserveSnapshots(Buff(Pet, 1) with { Source = Pet, HasSourceStatusSnapshot = true,
            SourceStatuses = [new(0xB94, Provider, 2, 10) { HasParameter = false }] });
        var active = Active(tracker, 1);
        Assert.Equal((byte)6, active.AppliedParameter);
        Assert.Equal((ushort)2, active.Parameter);
        Assert.False(active.HasParameter);
    }

    [Fact]
    public void EnrichedHitTransfersOnlyBuffCreditWithoutChangingPersonalDamage()
    {
        var tracker = new RaidBuffTracker();
        Land(tracker, Owner, 0, strength: 6);
        var hit = new ParsedDamageEvent("hit", 1, Start.AddSeconds(1), 1, Pet,
            new(400, "Enemy", 0, "", false, 0), 16504, "Arm Punch", 0, 0,
            DamageEventOutcome.Damage, 10600, 0, false, false, false, false, 3, 0, 0, 0, 0)
        {
            AttributedSource = Owner, HasSourceStatusSnapshot = true, HasTargetStatusSnapshot = true,
            SourceStatuses = [new(0xB94, Provider, 0, 12) { HasParameter = false, StatusSlot = 3 }],
        };
        tracker.ObserveSnapshots(hit);
        var enriched = tracker.ApplyFallback(hit);
        Assert.Equal(hit, enriched with { SourceStatuses = hit.SourceStatuses });
        Assert.Equal(12, enriched.SourceStatuses[0].RemainingTime);
        Assert.Equal((byte)3, enriched.SourceStatuses[0].StatusSlot);
        var result = RaidDamageCalculator.CalculateBoth([enriched], [], true);
        var credit = Assert.Single(result.Diagnostics.RawCredits);
        Assert.Equal(600, credit.TotalCredit, 6);
        Assert.Equal(Provider, credit.Provider);
        Assert.Equal(Owner, credit.Recipient);
        Assert.Empty(result.Diagnostics.MissingStrength);
    }

    [Theory]
    [InlineData("announcement")]
    [InlineData("provider")]
    [InlineData("owner")]
    [InlineData("sequence")]
    [InlineData("future")]
    [InlineData("expired")]
    [InlineData("not-pet")]
    public void UnrelatedOrUnconfirmedOwnerEvidenceDoesNotSupplyStrength(string mismatch)
    {
        var tracker = new RaidBuffTracker();
        if (mismatch == "announcement")
            tracker.Observe(Buff(Owner, 0) with { AppliedParameter = 6, ObservationKind = DamageStatusObservationKind.Announcement });
        else
            Land(tracker, mismatch == "owner" ? Owner with { EntityId = 101 } : Owner,
                mismatch == "future" ? 3 : 0, strength: 6, duration: mismatch == "expired" ? 1 : 15);
        Observe(tracker, 2, provider: mismatch == "provider" ? Provider with { EntityId = 201 } : null,
            sequence: mismatch == "sequence" ? 11u : null,
            pet: mismatch == "not-pet" ? Pet with { IsPet = false } : null);
        Assert.True(RaidBuffPolicy.HasUnknownStrength(Active(tracker, 2)));
    }

    [Fact]
    public void AbsentPetBuffIsNeverCopiedFromTheOwner()
    {
        var tracker = new RaidBuffTracker();
        Land(tracker, Owner, 0, strength: 6);
        tracker.ObserveSnapshots(Buff(Pet, 1) with { Source = Pet, HasSourceStatusSnapshot = true });
        Assert.Empty(AllActive(tracker, 2));
    }

    [Fact]
    public void LatePetObservationUsesHistoricalApplicationRatherThanFutureOwnerState()
    {
        var tracker = new RaidBuffTracker();
        Land(tracker, Owner, 0, strength: 2);
        Observe(tracker, 1);
        Land(tracker, Owner, 3, strength: 6, sequence: 11);
        Observe(tracker, 4, sequence: 11);
        Observe(tracker, 2);
        Assert.Equal((byte)2, Active(tracker, 2.5).AppliedParameter);
        Assert.Equal((byte)6, Active(tracker, 4.5).AppliedParameter);
    }

    private static DamageStatusApplication Buff(DamageActorIdentity target, double second, uint status = 0xB94) =>
        new(target, Provider, status, "Buff", 0, 0, "", Start.AddSeconds(second), 15, false, false, false)
        { HasParameter = false, StatusSlot = 4, ObservationKind = DamageStatusObservationKind.Landing };

    private static void Land(RaidBuffTracker tracker, DamageActorIdentity target, double second, uint status = 0xB94,
        byte strength = 4, uint sequence = 10, float duration = 15)
    {
        tracker.Observe(Buff(target, second, status) with { DurationSeconds = 0, ActionId = 100,
            ObservationKind = DamageStatusObservationKind.Announcement, AppliedParameter = strength, ApplicationSequence = sequence });
        tracker.Observe(Buff(target, second, status) with { ApplicationSequence = sequence, DurationSeconds = duration });
    }

    private static void Observe(RaidBuffTracker tracker, double second, uint status = 0xB94, float remaining = 10,
        uint? sequence = null, byte? strength = null, bool appliedParameter = true, DamageActorIdentity? provider = null,
        DamageActorIdentity? pet = null) => tracker.ObserveSnapshots(Buff(Pet, second) with
        {
            Source = pet ?? Pet, HasSourceStatusSnapshot = true,
            SourceStatuses = [new(status, provider ?? Provider, appliedParameter ? (ushort)0 : strength.GetValueOrDefault(), remaining)
                { HasParameter = !appliedParameter && strength.HasValue, AppliedParameter = appliedParameter ? strength : null,
                    StatusSlot = 4, ApplicationSequence = sequence }],
        });

    private static IReadOnlyList<DamageStatusSnapshot> AllActive(RaidBuffTracker tracker, double second) =>
        tracker.ApplyConfirmed(Buff(Pet, second) with { Source = Pet }).SourceStatuses;

    private static DamageStatusSnapshot Active(RaidBuffTracker tracker, double second) => Assert.Single(AllActive(tracker, second));
}
