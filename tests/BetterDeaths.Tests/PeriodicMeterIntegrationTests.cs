namespace BetterDeaths.Tests;

using System.Text.Json;
using BetterDeaths.DamageParsing;

public sealed class PeriodicMeterIntegrationTests
{
    private static readonly DateTime Start = new(2026, 9, 7, 1, 41, 40, DateTimeKind.Utc);
    private static readonly DamageActorIdentity Player = new(0x10001001, "Player 1", 0, "", true, 24) { Level = 100 };
    private static readonly DamageActorIdentity Enemy = new(0x40001001, "Target", 0, "", false, 0);

    [Theory]
    [InlineData(19u)]
    [InlineData(22u)]
    [InlineData(23u)]
    [InlineData(24u)]
    [InlineData(25u)]
    [InlineData(27u)]
    [InlineData(28u)]
    [InlineData(30u)]
    [InlineData(31u)]
    [InlineData(32u)]
    [InlineData(33u)]
    [InlineData(34u)]
    [InlineData(36u)]
    [InlineData(37u)]
    [InlineData(40u)]
    public async Task CalibratedAmountsReachLiveFinalSavedAndAbilityTotals(uint job)
    {
        var player = Player with { ClassJobId = job, IsPartyMember = false };
        var module = new DamageParsingModule();
        var direct = Assert.Single(module.Process(Packet(player)));
        module.ObserveStatus(Application(player));
        module.ProcessPeriodicTick(new(2, Start.AddSeconds(3), Enemy, 0, "", 0, 600, null));
        var tick = Assert.Single(module.FlushPendingPeriodicTicks(Start.AddSeconds(3), true));
        const double expected = 202.5; // 20 * 10 * (1 + 0.25 * 0.05), cold crit history.
        Assert.Equal(1000u, direct.Amount);
        Assert.Equal(1000, direct.RawMeterAmount);
        Assert.Equal(600u, tick.Amount);
        Assert.True(tick.PeriodicMeterUsesEstimate);
        Assert.Equal(expected, tick.RawMeterAmount, 6);
        Assert.Equal(DamageAttributionQuality.Estimated, tick.AttributionQuality);

        module.GetLiveEncounter();
        module.RefreshLiveEncounter(Start.AddSeconds(4));
        await module.PendingLiveSnapshot!;
        module.RefreshLiveEncounter(Start.AddSeconds(4));
        Check(module.GetLiveEncounter()!);
        var final = module.EndEncounter(Start.AddSeconds(5), "Duty reset")!;
        Check(final);
        Check(JsonSerializer.Deserialize<DamageEncounterSnapshot>(JsonSerializer.Serialize(final with { Events = [] }))!);
        var restoredTick = JsonSerializer.Deserialize<ParsedDamageEvent>(JsonSerializer.Serialize(tick))!;
        Assert.Equal(tick, restoredTick with { SourceStatuses = tick.SourceStatuses, TargetStatuses = tick.TargetStatuses });
        Assert.Equal(600, Assert.Single(final.Diagnostics.PeriodicAllocations).AllocatedDamage);

        static void Check(DamageEncounterSnapshot snapshot)
        {
            Assert.Equal(1600ul, snapshot.TotalDamage);
            Assert.Equal(1202.5, snapshot.ObservedMeterDamage, 6);
            Assert.Equal(1202.5 / snapshot.DurationSeconds, snapshot.DamagePerSecond, 6);
            var source = Assert.Single(snapshot.Sources);
            Assert.Equal(snapshot.ObservedMeterDamage, source.ObservedMeterDamage);
            Assert.Equal(source.ObservedMeterDamage, source.Actions.Sum(action => action.ObservedMeterDamage));
            Assert.Equal(expected, Assert.Single(source.Actions, action => action.ActionId == 50000).ObservedMeterDamage, 6);
        }
    }

    [Theory]
    [InlineData(19u)]
    [InlineData(24u)]
    [InlineData(40u)]
    public void PendingBuffCannotAffectAnEarlierDotConfirmation(uint job)
    {
        var module = new DamageParsingModule();
        var player = Player with { ClassJobId = job };
        module.Process(Packet(player));
        var dot = Application(player) with { ActionId = 16532, DurationSeconds = 0, CriticalRateLowByte = 6 };
        module.ObserveStatus(dot);
        var buff = new DamageStatusApplication(player, Player with { EntityId = 0x10001002 }, 0x312,
            "Critical buff", 0, 3557, "Buff action", Start.AddSeconds(1.5), 0, false, false, false);
        module.ObserveStatus(buff);
        module.ObserveStatus(dot with
        {
            SeenAtUtc = Start.AddSeconds(2), ActionId = 0, DurationSeconds = 30,
            BaseDamageLowByte = null, CriticalRateLowByte = null,
        });
        module.ObserveStatus(buff with { SeenAtUtc = Start.AddSeconds(2.1), ActionId = 0, DurationSeconds = 20 });
        module.ProcessPeriodicTick(new(2, Start.AddSeconds(3), Enemy, 0, "", 0, 600, null));
        var tick = Assert.Single(module.FlushPendingPeriodicTicks(Start.AddSeconds(3), true));
        Assert.Equal(1.4, tick.PeriodicCompatibilityEstimate!.Inputs!.CriticalMultiplier, 6);
        Assert.Equal(0.006, tick.PeriodicCompatibilityEstimate.Inputs.CriticalRate, 6);
        Assert.Equal(202.986, tick.RawMeterAmount, 6);
    }

    [Theory]
    [InlineData(0.9f, 1.65)]
    [InlineData(1.0f, 1.55)]
    [InlineData(20.0f, 1.55)]
    public void CriticalBuffDeductionUsesTheConfirmedExpiryBoundary(float remaining, double multiplier)
    {
        var tracker = new PeriodicDamageTracker();
        tracker.ObserveDirectDamage(new DirectDamageParser().Parse(Packet(Player)));
        tracker.Observe(Application(Player) with
        {
            CriticalRateLowByte = 45,
            SourceStatuses = [new(0x312, Player, 0, remaining)],
            SourceBaseRates = new(0.3, 0),
        });
        // Warm the crit history before a replacement application.
        for (var i = 0; i < 20; i++) tracker.ObserveDirectDamage(new DirectDamageParser().Parse(Packet(Player) with
        {
            Targets = [new(0, Enemy, [new(0, 3, i < 6 ? (byte)0x20 : (byte)0, 0, 0, 0, 1000)])],
        }));
        tracker.Observe(Application(Player) with
        {
            SeenAtUtc = Start.AddSeconds(2), CriticalRateLowByte = 45,
            SourceStatuses = [new(0x312, Player, 0, remaining)],
        });
        var tick = Assert.Single(tracker.Process(new(2, Start.AddSeconds(3), Enemy, 0, "", 0, 600, null)));
        Assert.Equal(multiplier, tick.PeriodicCompatibilityEstimate!.Inputs!.CriticalMultiplier, 6);
    }

    [Fact]
    public void MissingCalibrationAndSourceSpecificTicksKeepObservedAmounts()
    {
        var tracker = new PeriodicDamageTracker();
        tracker.Observe(Application(Player));
        var cold = Assert.Single(tracker.Process(new(1, Start.AddSeconds(3), Enemy, 0, "", 0, 600, null)));
        Assert.False(cold.PeriodicMeterUsesEstimate);
        Assert.Equal(600, cold.RawMeterAmount);
        tracker.ObserveDirectDamage(new DirectDamageParser().Parse(Packet(Player)));
        var ground = Assert.Single(tracker.Process(new(2, Start.AddSeconds(6), Enemy, 0x74A, "Ground tick", 0, 700, Player)));
        Assert.False(ground.PeriodicMeterUsesEstimate);
        Assert.Equal(700, ground.RawMeterAmount);
    }

    [Theory]
    [InlineData(2216u, 0.02)]
    [InlineData(786u, 0.10)]
    [InlineData(2125u, 0.20)]
    [InlineData(1825u, 0.20)]
    [InlineData(851u, 1.0)]
    [InlineData(86u, 1.0)]
    [InlineData(1177u, 0.0)] // Restricted to direct actions, not periodic applications.
    [InlineData(116u, 1.0)]
    public void AllPeriodicCriticalBuffDefinitionsHaveExplicitCoverage(uint status, double expected)
    {
        Assert.Equal(expected, PeriodicCalibrationPolicy.CriticalBuffRate([new(status, Player, 0, 30)], []), 6);
    }

    [Fact]
    public void TargetCriticalBuffAndSourceBuffAreCountedSeparately()
    {
        Assert.Equal(0.12, PeriodicCalibrationPolicy.CriticalBuffRate(
            [new(2216, Player, 0, 30)], [new(0x4C5, Player, 0, 30)]), 6);
    }

    [Fact]
    public void LegacySavedEventIsNotReinterpretedFromItsDiagnosticEstimate()
    {
        var tracker = new PeriodicDamageTracker();
        tracker.ObserveDirectDamage(new DirectDamageParser().Parse(Packet(Player)));
        tracker.Observe(Application(Player));
        var tick = Assert.Single(tracker.Process(new(1, Start.AddSeconds(3), Enemy, 0, "", 0, 600, null)));
        var old = tick with { MeterAmount = 600, PeriodicMeterUsesEstimate = false };
        var restored = JsonSerializer.Deserialize<ParsedDamageEvent>(JsonSerializer.Serialize(old))!;
        Assert.Equal(600, restored.RawMeterAmount);
        Assert.False(restored.PeriodicMeterUsesEstimate);
    }

    [Fact]
    public void ZeroRawShareCannotDiscardACalibratedTick()
    {
        var module = new DamageParsingModule();
        module.Process(Packet(Player));
        module.ObserveStatus(Application(Player));
        module.ObserveStatus(Application(Player) with { StatusId = 0x4B0 });
        module.ProcessPeriodicTick(new(2, Start.AddSeconds(3), Enemy, 0, "", 0, 1, null));
        var ticks = module.FlushPendingPeriodicTicks(Start.AddSeconds(3), true);
        Assert.Equal(2, ticks.Count);
        Assert.Contains(ticks, tick => tick.Amount == 0 && tick.RawMeterAmount == 202.5);
        Assert.Equal(1.0, ticks.Sum(tick => (double)tick.Amount));
        Assert.Equal(1405, module.GetCurrentEncounter()!.ObservedMeterDamage);
        Assert.Equal(2, module.GetCurrentEncounter()!.Diagnostics.PeriodicAllocations.Count);
    }

    [Fact]
    public void CalibratedZeroIsNotReplacedWithObservedDamage()
    {
        var tracker = new PeriodicDamageTracker();
        tracker.ObserveDirectDamage(new DirectDamageParser().Parse(Packet(Player)));
        tracker.Observe(Application(Player) with { PeriodicPotency = 10, BaseDamageLowByte = 0 });
        var tick = Assert.Single(tracker.Process(new(2, Start.AddSeconds(3), Enemy, 0, "", 0, 600, null)));
        Assert.True(tick.PeriodicMeterUsesEstimate);
        Assert.Equal(600u, tick.Amount);
        Assert.Equal(0, tick.RawMeterAmount);
    }

    private static DamageActionPacket Packet(DamageActorIdentity player) => new(1, Start, 1, player, 100, "Direct hit",
        [new(0, Enemy, [new(0, 3, 0, 0, 0, 0, 1000)])])
    { DirectPotency = 100, CanCalibratePotency = true, HasSourceStatusSnapshot = true };

    private static DamageStatusApplication Application(DamageActorIdentity player) => new(Enemy, player, 50000, "Synthetic DoT", 0, 0,
        "", Start.AddSeconds(1), 30, true, false, false)
    { PeriodicPotency = 20, BaseDamageLowByte = 200, CriticalRateLowByte = 0 };
}
