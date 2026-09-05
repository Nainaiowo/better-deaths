namespace BetterDeaths.Tests;

using BetterDeaths.DamageParsing;
using System.Text.Json;

public sealed class PeriodicCalibrationHistoryTests
{
    private static readonly DateTime Start = new(2026, 9, 4, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DamageActorIdentity Source = new(0x1001, "Caster", 0, "", true, 25) { Level = 100 };
    private static readonly DamageActorIdentity Target = new(0x40000001, "Enemy", 0, "", false, 0);

    [Fact]
    public void EncounterBoundaryKeepsCalibrationButClearsDamageAndApplications()
    {
        var module = new DamageParsingModule();
        module.Process(Packet(1, 0));
        module.ObserveStatus(Application(0.1));
        module.EndEncounter(Start.AddSeconds(4), "Combat ended");
        module.Process(Packet(2, 10) with { DirectPotency = null, CanCalibratePotency = false });
        var unknown = Tick(module, 11);
        Assert.Equal(DamageAttributionQuality.Unattributed, unknown.AttributionQuality);
        module.ObserveStatus(Application(12));
        var known = Tick(module, 15);
        Assert.Equal(10, known.PeriodicEstimateInputs!.DamagePerPotency);
        Assert.Equal(1, known.PeriodicEstimateInputs.CalibrationSampleCount);
        Assert.Equal(2, known.PeriodicEstimateInputs.CriticalSampleCount);
        Assert.Equal(2200ul, module.GetCurrentEncounter()!.TotalDamage);
    }

    [Fact]
    public void IdlePruningDropsStagedDamageWithoutDroppingCalibration()
    {
        var module = new DamageParsingModule();
        module.Process(Packet(1, 0), allowAutomaticEncounterStart: false);
        module.FlushPendingPeriodicTicks(Start.AddSeconds(3));
        module.SetCombatActive(true, Start.AddSeconds(4));
        module.ObserveStatus(Application(4.1));
        var tick = Tick(module, 7);
        Assert.Equal(10, tick.PeriodicEstimateInputs!.DamagePerPotency);
        Assert.Equal(600ul, module.GetCurrentEncounter()!.TotalDamage);
    }

    [Fact]
    public void ExplicitContextResetStartsFreshWithoutChangingCompletedEncounter()
    {
        var module = new DamageParsingModule();
        module.Process(Packet(1, 0));
        var completed = module.EndEncounter(Start.AddSeconds(4), "Left territory");
        module.ResetCalibration();
        module.ObserveStatus(Application(10));
        Assert.Null(Tick(module, 13).PeriodicEstimateInputs);
        Assert.Equal(1000ul, completed!.TotalDamage);
        Assert.Same(completed, module.LastEncounter);
    }

    [Theory]
    [InlineData(24u, (byte)100)]
    [InlineData(25u, (byte)90)]
    public void ChangedJobOrLevelDoesNotReuseCalibration(uint job, byte level)
    {
        var module = new DamageParsingModule();
        module.Process(Packet(1, 0));
        module.EndEncounter(Start.AddSeconds(4), "Combat ended");
        module.ObserveStatus(Application(10) with { Source = Source with { ClassJobId = job, Level = level } });
        Assert.Null(Tick(module, 13).PeriodicEstimateInputs);
    }

    [Fact]
    public void ChangedKnownAttributesDiscardOldSamplesAndUseNewHit()
    {
        var module = new DamageParsingModule();
        module.Process(Packet(1, 0) with { SourceBaseRates = new(0.2, 0.1) });
        module.EndEncounter(Start.AddSeconds(4), "Combat ended");
        module.Process(Packet(2, 10, 2000) with { SourceBaseRates = new(0.3, 0.2) });
        module.ObserveStatus(Application(11));
        var inputs = Tick(module, 14).PeriodicEstimateInputs!;
        Assert.Equal(20, inputs.DamagePerPotency);
        Assert.Equal(1, inputs.CalibrationSampleCount);
        Assert.True(inputs.UsedKnownAttributes);
        Assert.Equal(new DamageBaseRateSnapshot(0.3, 0.2), inputs.CalibrationBaseRates);
    }

    [Fact]
    public void UnknownActorMetadataDoesNotEraseKnownContext()
    {
        var tracker = new PeriodicDamageTracker();
        tracker.ObserveDirectDamage(new DirectDamageParser().Parse(Packet(1, 0)));
        tracker.Clear(preserveCalibration: true);
        tracker.Observe(Application(10) with { Source = Source with { ClassJobId = 0, Level = 0 } });
        var tick = Assert.Single(tracker.Process(new PeriodicDamageTick(100, Start.AddSeconds(13), Target, 0, "", 0, 600, null)));
        Assert.Equal(10, tick.PeriodicEstimateInputs!.DamagePerPotency);
    }

    [Fact]
    public void RetainedRatesPreventShortBurstFromSelectingAnotherCritByteRange()
    {
        var module = new DamageParsingModule();
        for (var i = 0; i < 100; i++) module.Process(Packet(i + 1, i, critical: i < 26, direct: i < 30));
        module.EndEncounter(Start.AddSeconds(101), "Combat ended");
        for (var i = 0; i < 12; i++) module.Process(Packet(i + 101, i + 110, critical: i < 6));
        module.ObserveStatus(Application(123) with { CriticalRateLowByte = 6 });
        var inputs = Tick(module, 126).PeriodicEstimateInputs!;
        Assert.Equal(0.261, inputs.CriticalRate, 6);
        Assert.Equal(112, inputs.CriticalSampleCount);
        Assert.Equal(112, inputs.DirectHitSampleCount);
        Assert.False(inputs.UsedKnownAttributes);
        Assert.Equal(30.0 / 112, inputs.CalibrationBaseRates!.DirectHit, 6);
        var restored = JsonSerializer.Deserialize<PeriodicDamageEstimateInputs>(JsonSerializer.Serialize(inputs));
        Assert.Equal(inputs, restored);
    }

    [Fact]
    public void ActorsDoNotShareCalibrationAndFullClearRemovesIt()
    {
        var tracker = new PeriodicDamageTracker();
        tracker.ObserveDirectDamage(new DirectDamageParser().Parse(Packet(1, 0)));
        tracker.Observe(Application(1) with { Source = Source with { EntityId = 0x1002 } });
        Assert.Null(Assert.Single(tracker.Process(new PeriodicDamageTick(10, Start.AddSeconds(4), Target, 0, "", 0, 600, null)))
            .PeriodicEstimateInputs);
        tracker.Clear();
        tracker.Observe(Application(10));
        Assert.Null(Assert.Single(tracker.Process(new PeriodicDamageTick(11, Start.AddSeconds(13), Target, 0, "", 0, 600, null)))
            .PeriodicEstimateInputs);
    }

    private static DamageActionPacket Packet(long sequence, double seconds, uint amount = 1000,
        bool critical = false, bool direct = false) => new(sequence, Start.AddSeconds(seconds), (uint)sequence,
        Source, 100, "Spell", [new DamageActionTarget(0, Target,
            [new(0, 3, (byte)((critical ? 0x20 : 0) | (direct ? 0x40 : 0)), 0, 0, 0, amount)])])
        { DirectPotency = 100, CanCalibratePotency = true, HasSourceStatusSnapshot = true };

    private static DamageStatusApplication Application(double seconds) => new(Target, Source, 0x500, "DoT", 0,
        100, "Spell", Start.AddSeconds(seconds), 30, true, false, false)
    { PeriodicPotency = 20, HasSourceStatusSnapshot = true };

    private static ParsedDamageEvent Tick(DamageParsingModule module, double seconds)
    {
        module.ProcessPeriodicTick(new PeriodicDamageTick(10000, Start.AddSeconds(seconds), Target, 0, "", 0, 600, null));
        return Assert.Single(module.FlushPendingPeriodicTicks(Start.AddSeconds(seconds + 0.1)));
    }
}
