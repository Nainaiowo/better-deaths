namespace BetterDeaths.Tests;

using System.Reflection;
using BetterDeaths.DamageParsing;

public sealed class HealingCalibrationTests
{
    private static readonly DateTime Start = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DamageActorIdentity Source = new(0x1001, "Healer", 0, "", true, 24) { Level = 100 };
    private static readonly DamageActorIdentity Ally = new(0x1002, "Ally", 0, "", true, 19) { Level = 100 };
    private static readonly DamageActorIdentity Enemy = new(0x40000001, "Enemy", 0, "", false, 0);

    [Fact]
    public void HealFlagUsesParamOneAndPreservesFullAmountIncludingOverheal()
    {
        var tracker = new PeriodicDamageTracker();
        var packet = Heal(1, 1500) with
        {
            Targets = [new(0, Ally, [new(0, 4, 0x40, 0x20, 1, 0x40, 9464)])
            { HasTargetStatusSnapshot = true, TargetHp = new(10000, 0, 10000) }],
        };
        tracker.ObserveActionCalibration(packet, []);
        var estimate = Estimate(tracker);
        Assert.Equal(500, estimate.Inputs!.DamagePerPotency, 9); // 75,000 / 1.5 / 100
        Assert.Equal(1, estimate.Inputs.CriticalSampleCount);
        Assert.True(estimate.UsedHealingCalibration);
        Assert.False(estimate.UsedUnitCalibration);
        Assert.False(estimate.Inputs.UsedKnownAttributes);
        Assert.Equal(0, estimate.DirectHit.Samples);
    }

    [Fact]
    public void DamageAndHealingShareOnlyDiagnosticCritHistory()
    {
        var tracker = new PeriodicDamageTracker();
        for (var i = 1; i <= 10; i++) tracker.ObserveActionCalibration(Heal(i, 1000, critical: false), []);
        var hit = Damage(11, 1000, critical: true);
        tracker.ObserveActionCalibration(hit, new DirectDamageParser().Parse(hit));
        var estimate = Estimate(tracker);
        Assert.Equal(11, estimate.Inputs!.CriticalSampleCount);
        Assert.Equal(1.0 / 11, estimate.Inputs.CalibrationBaseRates!.Critical, 9);
        Assert.Equal(1000 / (1.35 + 1.0 / 11) / 100, estimate.Inputs.DamagePerPotency, 9);
        Assert.False(estimate.UsedHealingCalibration);
        Assert.Equal(1, estimate.DirectHit.Samples);
    }

    [Fact]
    public void DuplicateHealingAndMixedPacketsAreObservedOnceInEffectOrder()
    {
        var module = new DamageParsingModule();
        for (var i = 1; i <= 10; i++)
        {
            var heal = Heal(i, 1000);
            Assert.Empty(module.Process(heal));
            Assert.Empty(module.Process(heal));
        }
        var mixed = Damage(11, 1000, true) with
        {
            HealingPotency = 100,
            Targets = [new(0, Enemy, [new(0, 4, 0, 0, 0, 0, 1000), new(1, 3, 0x20, 0, 0, 0, 1000)])
            { HasTargetStatusSnapshot = true }],
        };
        Assert.Single(module.Process(mixed));
        Assert.Empty(module.Process(mixed));
        var estimate = Estimate(Tracker(module));
        Assert.Equal(12, estimate.Inputs!.CriticalSampleCount);
        Assert.Equal(1000 / (1.35 + 1.0 / 12) / 100, estimate.Inputs.DamagePerPotency, 9);
    }

    [Fact]
    public void HealingCannotCreateDamageOrMoveEncounterDamageTimes()
    {
        var module = new DamageParsingModule();
        Assert.Empty(module.Process(Heal(1, 100000)));
        Assert.Null(module.GetCurrentEncounter(Start.AddSeconds(1)));
        var damage = Damage(2, 1000);
        module.Process(damage);
        var before = module.GetCurrentEncounter(Start.AddSeconds(21))!;
        module.Process(Heal(20, 50000, true));
        var after = module.GetCurrentEncounter(Start.AddSeconds(21))!;
        Assert.Equal(before.StartedAtUtc, after.StartedAtUtc);
        Assert.Equal(before.MeterStartedAtUtc, after.MeterStartedAtUtc);
        Assert.Equal(before.MeterSnapshotAtUtc, after.MeterSnapshotAtUtc);
        Assert.Equal(before.TotalDamage, after.TotalDamage);
        Assert.Equal(before.Events, after.Events);
        Assert.Single(after.Sources);
        Assert.Equal(before.Sources[0].CriticalHits, after.Sources[0].CriticalHits);
    }

    [Fact]
    public void BuffedHealingDoesNotBiasObservedCritRate()
    {
        var tracker = new PeriodicDamageTracker();
        tracker.ObserveActionCalibration(Heal(1, 1500, true) with { SourceStatuses = [new(786, Source, 0, 30)] }, []);
        Assert.Equal(0, Estimate(tracker).Inputs!.CriticalSampleCount);
        tracker.ObserveActionCalibration(Heal(2, 1500, true) with { SourceStatuses = [new(786, Source, 0, 0.5f)] }, []);
        Assert.Equal(1, Estimate(tracker, 2).Inputs!.CriticalSampleCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void UnsupportedPotencyAndIncompleteSnapshotsCannotCalibrate(bool unknownPotency, bool unknownStatus)
    {
        var tracker = new PeriodicDamageTracker();
        var packet = Heal(1, 1000) with
        {
            HealingPotency = unknownPotency ? null : 100,
            HasSourceStatusSnapshot = unknownPotency || unknownStatus,
            SourceStatuses = unknownStatus ? [new(42, Source, 0, 30)] : [],
        };
        tracker.ObserveActionCalibration(packet, []);
        var estimate = Estimate(tracker);
        Assert.True(estimate.UsedUnitCalibration);
        Assert.Equal(1, estimate.Inputs!.CriticalSampleCount);
        Assert.NotNull(estimate.Limitation);
    }

    [Fact]
    public void SourceAndExtraHealEntriesDoNotCalibratePrimaryHeal()
    {
        var tracker = new PeriodicDamageTracker();
        var packet = Heal(1, 1000) with
        {
            Targets = [new(0, Ally, [new(0, 4, 0, 0, 0, 0x80, 50000), new(1, 4, 0, 0, 0, 0, 50000)])
            { HasTargetStatusSnapshot = true }],
        };
        tracker.ObserveActionCalibration(packet, []);
        var estimate = Estimate(tracker);
        Assert.True(estimate.UsedUnitCalibration);
        Assert.Equal(2, estimate.Inputs!.CriticalSampleCount);
    }

    [Fact]
    public void HealingCalibrationIsFrozenThenSupersededByDamageOnNewApplication()
    {
        var tracker = new PeriodicDamageTracker();
        tracker.ObserveActionCalibration(Heal(1, 1000), []);
        var frozen = Estimate(tracker);
        var damage = Damage(3, 5000);
        tracker.ObserveActionCalibration(damage, new DirectDamageParser().Parse(damage));
        var existing = Assert.Single(tracker.Process(new(2, Start.AddSeconds(16), Enemy, 0, "DoT", 0, 600, null)));
        Assert.Equal(frozen, existing.PeriodicCompatibilityEstimate);
        var refreshed = Estimate(tracker, 20);
        Assert.False(refreshed.UsedHealingCalibration);
        Assert.Equal(50, refreshed.Inputs!.DamagePerPotency);
        tracker.Clear(preserveCalibration: true);
        Assert.Equal(50, Estimate(tracker, 30).Inputs!.DamagePerPotency);
        tracker.ClearCalibration();
        Assert.True(Estimate(tracker, 40).UsedUnitCalibration);
    }

    [Fact]
    public void PetObservationsDoNotChangeOwnersHistory()
    {
        var tracker = new PeriodicDamageTracker();
        tracker.ObserveActionCalibration(Heal(1, 1000) with
        {
            Source = new(0x40001234, "Pet", Source.EntityId, Source.Name, false, 0) { IsPet = true },
            SourceOwner = Source,
        }, []);
        Assert.Equal(0, Estimate(tracker).Inputs!.CriticalSampleCount);
    }

    [Fact]
    public void RepeatedDamageDoesNotSuppressANewHealEntry()
    {
        var module = new DamageParsingModule();
        var packet = Damage(1, 1000);
        module.Process(packet);
        module.Process(packet with
        {
            HealingPotency = 100,
            Targets = [packet.Targets[0] with { Effects = [.. packet.Targets[0].Effects, new(1, 4, 0, 0, 0, 0, 1000)],
                HasTargetStatusSnapshot = true }],
        });
        Assert.Equal(2, Estimate(Tracker(module)).Inputs!.CriticalSampleCount);
    }

    [Fact]
    public void HealingHistoryAndDedupStorageAreBounded()
    {
        var tracker = new PeriodicDamageTracker();
        tracker.ObserveActionCalibration(Heal(1, 1000), []);
        for (var i = 2; i <= 1001; i++)
            tracker.ObserveActionCalibration(Heal(i, 0), []);
        Assert.True(Estimate(tracker, 1002).UsedUnitCalibration);
        for (var i = 1002; i <= 9000; i++)
            tracker.ObserveActionCalibration(Heal(i, 0), []);
        var ids = (HashSet<string>)typeof(PeriodicDamageTracker).GetField("healingEventIds", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(tracker)!;
        Assert.Equal(8192, ids.Count);
        tracker.ClearCalibration();
        Assert.Empty(ids);
    }

    [Fact]
    public void JobOrLevelChangeResetsHealingAndCritCalibration()
    {
        var tracker = new PeriodicDamageTracker();
        tracker.ObserveActionCalibration(Heal(1, 1000), []);
        tracker.ObserveActionCalibration(Heal(2, 2000) with { Source = Source with { Level = 90 } }, []);
        var snapshot = typeof(PeriodicDamageTracker).GetMethod("GetCompatibilityCalibration", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(tracker, [Source])!;
        Assert.Equal(20.0, snapshot.GetType().GetProperty("DamagePerPotency")!.GetValue(snapshot));
        Assert.Equal(1, snapshot.GetType().GetProperty("CriticalSamples")!.GetValue(snapshot));
    }

    [Fact]
    public void ObservedAllCriticalHistoryIsNotClampedToCharacterStatLimits()
    {
        var tracker = new PeriodicDamageTracker();
        for (var i = 1; i <= 11; i++) tracker.ObserveActionCalibration(Heal(i, 1500, true), []);
        var estimate = Estimate(tracker, 20);
        Assert.Equal(1.0, estimate.Inputs!.CriticalRate);
        Assert.Equal(2.35, estimate.Inputs.CriticalMultiplier);
    }

    private static DamageActionPacket Heal(int sequence, uint amount, bool critical = false) =>
        new(sequence, Start.AddSeconds(sequence), (uint)sequence, Source, 120, "Cure",
            [new(0, Ally, [new(0, 4, 0, critical ? (byte)0x20 : (byte)0, 0, 0, amount)])
            { HasTargetStatusSnapshot = true }])
        {
            HealingPotency = 100,
            ActionCategoryId = 2,
            HasSourceStatusSnapshot = true,
            SourceBaseRates = new(0.40, 0.30),
        };

    private static DamageActionPacket Damage(int sequence, uint amount, bool critical = false) =>
        new(sequence, Start.AddSeconds(sequence), (uint)sequence, Source, 25859, "Glare III",
            [new(0, Enemy, [new(0, 3, critical ? (byte)0x20 : (byte)0, 0, 0, 0, amount)])])
        {
            DirectPotency = 100,
            CanCalibratePotency = true,
            ActionCategoryId = 2,
            HasSourceStatusSnapshot = true,
            SourceBaseRates = new(0.40, 0.30),
        };

    private static PeriodicCompatibilityEstimate Estimate(PeriodicDamageTracker tracker, int seconds = 10)
    {
        tracker.Observe(new(Enemy, Source, 1871, "Dia", 0, 0, "", Start.AddSeconds(seconds), 30, true, false, false)
        { PeriodicPotency = 60, SourceBaseRates = new(0.40, 0.30) });
        return Assert.Single(tracker.Process(new(1, Start.AddSeconds(seconds + 3), Enemy, 0, "DoT", 0, 600, null)))
            .PeriodicCompatibilityEstimate!;
    }

    private static PeriodicDamageTracker Tracker(DamageParsingModule module) =>
        (PeriodicDamageTracker)typeof(DamageParsingModule).GetField("periodicDamageTracker", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(module)!;
}
