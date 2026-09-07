namespace BetterDeaths.Tests;

using System.Text.Json;
using BetterDeaths.DamageParsing;

public sealed class PeriodicWorkflowContextTests
{
    private static readonly DateTime Start = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DamageActorIdentity Player = new(0x10001001, "Player 1", 0, "", true, 24) { Level = 100 };
    private static readonly DamageActorIdentity Target = new(0x40001001, "Target", 0, "", false, 0);

    [Theory]
    [InlineData("level", 1, 20.0)]
    [InlineData("job", 1, 20.0)]
    [InlineData("attributes", 1, 20.0)]
    [InlineData("unknown", 2, 15.0)]
    public void ContextChangeAffectsNewApplicationsWithoutRewritingFrozenOnes(string change, int samples, double calibration)
    {
        var module = new DamageParsingModule();
        var rates = new DamageBaseRateSnapshot(0.3, 0.4);
        module.Process(Heal(Player, 1, 1000, rates));
        module.ObserveStatus(Dot(Player, Target, 2));
        var original = Tick(module, Target, 5).PeriodicCompatibilityEstimate!;
        Assert.True(original.UsedHealingCalibration);
        Assert.Equal(10, original.Inputs!.DamagePerPotency);

        var changed = change switch
        {
            "level" => Player with { Level = 90 },
            "job" => Player with { ClassJobId = 28 },
            "unknown" => Player with { Level = 0, ClassJobId = 0 },
            _ => Player,
        };
        var newRates = change == "attributes" ? new(0.3, 0.5) : change == "unknown" ? null : rates;
        module.Process(Heal(changed, 6, 2000, newRates));
        var nextTarget = Target with { EntityId = Target.EntityId + 1 };
        module.ObserveStatus(Dot(changed, nextTarget, 7));
        var oldTick = Tick(module, Target, 8);
        var newTick = Tick(module, nextTarget, 10);
        Assert.Equal(original, oldTick.PeriodicCompatibilityEstimate);
        Assert.Equal(samples, newTick.PeriodicCompatibilityEstimate!.Inputs!.CriticalSampleCount);
        Assert.Equal(calibration, newTick.PeriodicCompatibilityEstimate.Inputs.DamagePerPotency);
        Assert.True(newTick.PeriodicCompatibilityEstimate.UsedHealingCalibration);
        Assert.Equal(calibration * 20 * 1.0125, newTick.RawMeterAmount, 6);

        var final = module.EndEncounter(Start.AddSeconds(11), "Test")!;
        var saved = JsonSerializer.Deserialize<DamageEncounterSnapshot>(JsonSerializer.Serialize(final with { Events = [] }))!;
        Assert.Equal(final.ObservedMeterDamage, saved.ObservedMeterDamage);
        Assert.Equal(final.ObservedMeterDamage, saved.Sources.Sum(s => s.Actions.Sum(a => a.ObservedMeterDamage)), 6);
    }

    [Fact]
    public void TwentyFourNearbyPlayersHaveIndependentCalibrationAndAttribution()
    {
        var module = new DamageParsingModule();
        for (var i = 0; i < 24; i++)
        {
            var player = Player with { EntityId = Player.EntityId + (uint)i, Name = $"Player {i + 1}", IsPartyMember = false };
            module.Process(new(i + 1, Start, (uint)i + 1, player, 50000, "Synthetic calibration hit",
                [new(0, Target, [new(0, 3, 0, 0, 0, 0, (uint)(1000 + i * 100))])])
            { CanCalibratePotency = true, DirectPotency = 100, HasSourceStatusSnapshot = true });
            module.ObserveStatus(Dot(player, Target, 1));
        }
        var ticks = module.ProcessPeriodicTick(new(25, Start.AddSeconds(3), Target, 0, "", 0, 24000, null)).ToList();
        ticks.AddRange(module.FlushPendingPeriodicTicks(Start.AddSeconds(3), true));
        Assert.Equal(24, ticks.Count);
        Assert.Equal(24000, ticks.Sum(t => (double)t.Amount));
        for (var i = 0; i < 24; i++)
        {
            var tick = Assert.Single(ticks, t => t.Source.EntityId == Player.EntityId + i);
            Assert.Equal(1, tick.PeriodicCompatibilityEstimate!.Inputs!.CriticalSampleCount);
            Assert.Equal((10 + i) * 20 * 1.0125, tick.RawMeterAmount, 6);
        }
        var snapshot = module.GetCurrentEncounter()!;
        Assert.Equal(24, snapshot.Sources.Count);
        Assert.Equal(snapshot.ObservedMeterDamage, snapshot.Sources.Sum(s => s.ObservedMeterDamage), 6);
    }

    private static DamageActionPacket Heal(DamageActorIdentity player, int sequence, uint amount, DamageBaseRateSnapshot? rates) =>
        new(sequence, Start.AddSeconds(sequence), (uint)sequence, player, player.ClassJobId == 28 ? 190u : 120u, "Heal",
            [new(0, player, [new(0, 4, 0, 0, 0, 0, amount)]) { HasTargetStatusSnapshot = true }])
        { HealingPotency = 100, ActionCategoryId = 2, HasSourceStatusSnapshot = true, SourceBaseRates = rates };

    private static DamageStatusApplication Dot(DamageActorIdentity player, DamageActorIdentity target, int seconds) =>
        new(target, player, 50000, "Synthetic DoT", 0, 0, "", Start.AddSeconds(seconds), 30, true, false, false)
        { PeriodicPotency = 20, CriticalRateLowByte = 0, HasSourceStatusSnapshot = true, ActionCategoryId = 2, DamageType = 5 };

    private static ParsedDamageEvent Tick(DamageParsingModule module, DamageActorIdentity target, int seconds)
    {
        var events = module.ProcessPeriodicTick(new(seconds, Start.AddSeconds(seconds), target, 0, "", 0, 1000, null)).ToList();
        events.AddRange(module.FlushPendingPeriodicTicks(Start.AddSeconds(seconds), true));
        return Assert.Single(events);
    }
}
