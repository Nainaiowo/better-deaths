namespace BetterDeaths.Tests;

using BetterDeaths.DamageParsing;

public sealed class PeriodicLifecycleTests
{
    private static readonly DateTime Start = new(2026, 9, 5, 4, 0, 0, DateTimeKind.Utc);
    private static readonly DamageActorIdentity Source = new(0x1001, "Caster", 0, "", true, 25);
    private static readonly DamageActorIdentity OtherSource = Source with { EntityId = 0x1002 };
    private static readonly DamageActorIdentity Target = new(0x40000001, "Target", 0, "", false, 0);
    private static readonly uint[] ThunderStatuses = [0xA1, 0xA2, 0xA3, 0x4BA, 0xF1F, 0xF20];

    public static IEnumerable<object[]> ThunderReplacements =>
        from original in ThunderStatuses
        from replacement in ThunderStatuses
        where original != replacement
        select new object[] { original, replacement };

    public static TheoryData<uint, int> TickLimits => new()
    {
        { 0xA1, 8 }, { 0xA2, 6 }, { 0xA3, 9 }, { 0x4BA, 7 },
        { 0xF1F, 10 }, { 0xF20, 8 }, { 0xF2B, 5 }, { 0x759, 10 },
    };

    [Theory]
    [MemberData(nameof(ThunderReplacements))]
    public void ConfirmedThunderVariantReplacesOnlyItsCastersPreviousVariant(uint original, uint replacement)
    {
        var tracker = new PeriodicDamageTracker();
        tracker.Observe(Application(original, 0));
        tracker.Observe(Application(original, 0) with { Source = OtherSource });
        tracker.Observe(Application(replacement, 2));
        var ticks = Tick(tracker, 3);
        Assert.Equal(replacement, Assert.Single(ticks, tick => tick.Source.EntityId == Source.EntityId).StatusId);
        Assert.Equal(original, Assert.Single(ticks, tick => tick.Source.EntityId == OtherSource.EntityId).StatusId);
        Assert.Equal(600.0, ticks.Sum(tick => tick.EffectiveMeterAmount));
    }

    [Fact]
    public void ThunderReplacementDoesNotAffectAnotherTargetOrUnrelatedDot()
    {
        var tracker = new PeriodicDamageTracker();
        var otherTarget = Target with { EntityId = 0x40000002 };
        tracker.Observe(Application(0xF20, 0));
        tracker.Observe(Application(0xF20, 0) with { Target = otherTarget });
        tracker.Observe(Application(0x500, 0));
        tracker.Observe(Application(0xF1F, 2));
        Assert.Equal(new uint[] { 0x500, 0xF1F }, Tick(tracker, 3).Select(tick => tick.StatusId).Order());
        Assert.Equal(0xF20u, Assert.Single(Tick(tracker, 3, otherTarget)).StatusId);
    }

    [Fact]
    public void PendingVariantDoesNotReplaceUntilConfirmationAndOldVariantCannotReturn()
    {
        var tracker = new PeriodicDamageTracker();
        tracker.Observe(Application(0xF20, 0));
        tracker.Observe(Application(0xF1F, 2) with { ActionId = 36986, DurationSeconds = 0 });
        Assert.Equal(0xF20u, Assert.Single(Tick(tracker, 3)).StatusId);
        tracker.Observe(Application(0xF1F, 4));
        Assert.Equal(0xF1Fu, Assert.Single(Tick(tracker, 6)).StatusId);
        tracker.Retire(Target.EntityId, 0xF1F, Source.EntityId, Start.AddSeconds(7));
        Assert.Equal(DamageAttributionQuality.Unattributed, Assert.Single(Tick(tracker, 9)).AttributionQuality);
    }

    [Theory]
    [InlineData(-0.02, 0xF20u)]
    [InlineData(0.0, 0xF1Fu)]
    [InlineData(0.02, 0xF1Fu)]
    public void ReplacementBoundaryChoosesExactlyOneConfirmedSnapshot(double offset, uint expected)
    {
        var tracker = new PeriodicDamageTracker();
        tracker.Observe(Application(0xF20, 0));
        tracker.Observe(Application(0xF1F, 2) with { ActionId = 36986, DurationSeconds = 0 });
        tracker.Observe(Application(0xF1F, 3));
        Assert.Equal(expected, Assert.Single(Tick(tracker, 3 + offset)).StatusId);
    }

    [Fact]
    public void DelayedOlderVariantCannotRetireNewerConfirmedVariant()
    {
        var tracker = new PeriodicDamageTracker();
        tracker.Observe(Application(0xF1F, 4));
        tracker.Observe(Application(0xF20, 2));
        Assert.Equal(0xF20u, Assert.Single(Tick(tracker, 3)).StatusId);
        Assert.Equal(0xF1Fu, Assert.Single(Tick(tracker, 6)).StatusId);
    }

    [Fact]
    public void EqualConfirmationTimestampsPreferMostRecentlyAppliedVariant()
    {
        var tracker = new PeriodicDamageTracker();
        tracker.Observe(Application(0xF20, 0));
        tracker.Observe(Application(0xF1F, 0));
        Assert.Equal(0xF1Fu, Assert.Single(Tick(tracker, 0)).StatusId);
    }

    [Fact]
    public void TickBeforeRefreshConfirmationUsesPreviousApplicationsSnapshot()
    {
        var tracker = new PeriodicDamageTracker();
        tracker.Observe(Application(0xF1F, 0) with
        {
            SourceStatuses = [new DamageStatusSnapshot(0x7D, Source, 0, 20)],
            HasSourceStatusSnapshot = true,
        });
        tracker.Observe(Application(0xF1F, 2) with
        {
            ActionId = 36986,
            DurationSeconds = 0,
            HasSourceStatusSnapshot = true,
        });
        tracker.Observe(Application(0xF1F, 3));
        var oldTick = Assert.Single(Tick(tracker, 2.98));
        Assert.Equal(0x7Du, Assert.Single(oldTick.SourceStatuses).StatusId);
        Assert.Empty(Assert.Single(Tick(tracker, 6)).SourceStatuses);
    }

    [Fact]
    public void DelayedSameVariantHistoryCannotOverlapAThirdConfirmedVariant()
    {
        var tracker = new PeriodicDamageTracker();
        tracker.Observe(Application(0xF1F, 4));
        tracker.Observe(Application(0xF20, 3));
        tracker.Observe(Application(0xF1F, 1));
        Assert.Equal(0xF1Fu, Assert.Single(Tick(tracker, 2)).StatusId);
        Assert.Equal(0xF20u, Assert.Single(Tick(tracker, 3.5)).StatusId);
        Assert.Equal(0xF1Fu, Assert.Single(Tick(tracker, 5)).StatusId);
    }

    [Theory]
    [MemberData(nameof(TickLimits))]
    public void ExhaustedApplicationCannotTakeAnotherDotsDamage(uint statusId, int ticks)
    {
        var tracker = new PeriodicDamageTracker();
        tracker.Observe(Application(statusId, 0, ticks * 3));
        tracker.Observe(Application(0x500, 0, 120) with { Source = OtherSource });
        for (var index = 0; index < ticks; index++)
        {
            Assert.Equal(2, Tick(tracker, index * 3 + 0.01).Count);
        }
        Assert.Equal(0x500u, Assert.Single(Tick(tracker, ticks * 3 + 0.01)).StatusId);
    }

    [Fact]
    public void DuplicateAcknowledgementDoesNotResetTickBudget()
    {
        var tracker = new PeriodicDamageTracker();
        tracker.Observe(Application(0xF2B, 0, 15));
        tracker.Observe(Application(0x500, 0, 120) with { Source = OtherSource });
        Assert.Equal(2, Tick(tracker, 0.01).Count);
        tracker.Observe(Application(0xF2B, 0.1, 14.9f));
        for (var index = 1; index < 5; index++)
        {
            Assert.Equal(2, Tick(tracker, index * 3 + 0.01).Count);
        }
        Assert.Equal(0x500u, Assert.Single(Tick(tracker, 15.01)).StatusId);
    }

    [Fact]
    public void PendingRefreshPreservesExhaustedHistoryAndConfirmationResetsBudget()
    {
        var tracker = new PeriodicDamageTracker();
        tracker.Observe(Application(0xF1F, 0));
        tracker.Observe(Application(0x500, 0, 120) with { Source = OtherSource });
        for (var index = 0; index < 10; index++)
        {
            Assert.Equal(2, Tick(tracker, index * 3 + 0.01).Count);
        }
        tracker.Observe(Application(0xF1F, 28) with { ActionId = 36986, DurationSeconds = 0 });
        Assert.Equal(0x500u, Assert.Single(Tick(tracker, 30.01)).StatusId);
        tracker.Observe(Application(0xF1F, 31));
        Assert.Equal(2, Tick(tracker, 33.01).Count);
    }

    [Fact]
    public void LateTickIsStillAllowedWhenBudgetHasNotBeenUsed()
    {
        var tracker = new PeriodicDamageTracker();
        tracker.Observe(Application(0xF2B, 0, 15));
        tracker.Observe(Application(0x500, 0, 120) with { Source = OtherSource });
        Assert.Equal(2, Tick(tracker, 17).Count);
        Assert.Equal(0x500u, Assert.Single(Tick(tracker, 20)).StatusId);
    }

    [Fact]
    public void SoleCandidateStillReceivesUnambiguousObservedDamage()
    {
        var tracker = new PeriodicDamageTracker();
        tracker.Observe(Application(0xF2B, 0, 15));
        for (var index = 0; index <= 5; index++)
        {
            var tick = Assert.Single(Tick(tracker, index * 3 + 0.01));
            Assert.Equal(0xF2Bu, tick.StatusId);
            Assert.Equal(600u, tick.Amount);
        }
    }

    [Fact]
    public void UnknownEffectsAndGroundTicksAreNotSubjectToTargetDotBudgets()
    {
        Assert.Null(PeriodicDamageRefreshPolicy.GetMaximumTicks(0x500));
        var tracker = new PeriodicDamageTracker();
        tracker.Observe(Application(0x500, 0, 120));
        tracker.Observe(Application(0x501, 0, 120) with { Source = OtherSource });
        tracker.Observe(Application(0x1F5, 0, 120) with { Target = Source });
        for (var index = 0; index < 15; index++)
        {
            Assert.Equal(2, Tick(tracker, index * 3 + 0.01).Count);
            var ground = new PeriodicDamageTick(index, Start.AddSeconds(index * 3 + 0.01),
                Target, 0x1F5, "Ground", 0, 600, Source);
            Assert.Equal(600u, Assert.Single(tracker.Process(ground)).Amount);
        }
    }

    [Fact]
    public void ExhaustedCandidatesLeaveUnattributedDamageInsteadOfDroppingPacket()
    {
        var tracker = new PeriodicDamageTracker();
        tracker.Observe(Application(0xF2B, 0, 15));
        tracker.Observe(Application(0xF2B, 0, 15) with { Source = OtherSource });
        for (var index = 0; index < 5; index++)
        {
            Assert.Equal(2, Tick(tracker, index * 3 + 0.01).Count);
        }
        var residual = Assert.Single(Tick(tracker, 15.01));
        Assert.Equal(DamageAttributionQuality.Unattributed, residual.AttributionQuality);
        Assert.Equal(600u, residual.Amount);
    }

    private static DamageStatusApplication Application(uint statusId, double seconds, float duration = 30) =>
        new(Target, Source, statusId, "DoT", 0, 0, "", Start.AddSeconds(seconds), duration, true, false, false);

    private static IReadOnlyList<ParsedDamageEvent> Tick(PeriodicDamageTracker tracker, double seconds,
        DamageActorIdentity? target = null) =>
        tracker.Process(new PeriodicDamageTick((long)(seconds * 1000), Start.AddSeconds(seconds),
            target ?? Target, 0, "", 0, 600, null));
}
