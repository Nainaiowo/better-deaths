namespace BetterDeaths.Tests;

using BetterDeaths.DamageParsing;

public sealed class RepeatedHitCalibrationTests
{
    private static readonly DateTime Start = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DamageActorIdentity Player = new(0x1001, "Player", 0, "", true, 24);
    private static readonly DamageActorIdentity Enemy = new(0x40000001, "Enemy", 0, "", false, 0);

    [Theory]
    [InlineData(2, null)]
    [InlineData(3, null)]
    [InlineData(4, 0.5)]
    public void RepeatedTargetSlotsCalibrateEachHitAtFullPotency(int count, double? falloff)
    {
        var module = new DamageParsingModule();
        var packet = Packet(Enumerable.Range(0, count).Select(i => Target(i, Enemy)).ToArray()) with
        { SecondaryTargetPotencyMultiplier = falloff };
        var hits = module.Process(packet);
        Assert.Equal(count, hits.Count);
        Assert.Equal(count, hits.Select(h => h.EventId).Distinct().Count());
        Assert.All(hits, hit =>
        {
            Assert.True(hit.CanCalibratePotency);
            Assert.Equal(100, hit.DirectPotency);
            Assert.Equal(1000u, hit.Amount);
            Assert.Equal(1000, hit.RawMeterAmount);
        });
        Assert.Empty(module.Process(packet));
        module.ObserveStatus(new(Enemy, Player, 50000, "DoT", 0, 0, "", Start.AddSeconds(1), 30, true, false, false)
        { PeriodicPotency = 20 });
        module.ProcessPeriodicTick(new(2, Start.AddSeconds(3), Enemy, 0, "", 0, 600, null));
        var tick = Assert.Single(module.FlushPendingPeriodicTicks(Start.AddSeconds(3), true));
        Assert.Equal(count, tick.PeriodicCompatibilityEstimate!.Inputs!.CalibrationSampleCount);
        Assert.Equal(10, tick.PeriodicCompatibilityEstimate.Inputs.DamagePerPotency);
    }

    [Fact]
    public void UnknownAreaScalingRemainsExcludedEvenIfCallerAllowsCalibration()
    {
        var hits = new DirectDamageParser().Parse(Packet([Target(0, Enemy), Target(1, Enemy with { EntityId = 0x40000002 })]));
        Assert.All(hits, hit => Assert.False(hit.CanCalibratePotency));
        Assert.Equal(2000, hits.Sum(h => h.RawMeterAmount));
    }

    [Fact]
    public void MixedTargetsScaleByIdentityAndNotByHitPosition()
    {
        var hits = new DirectDamageParser().Parse(Packet([
            Target(0, Enemy), Target(1, Enemy), Target(2, Enemy with { EntityId = 0x40000002 }), Target(3, Enemy)]) with
        { SecondaryTargetPotencyMultiplier = 0.5, ActionId = 3555, DirectPotency = 280, Source = Player with { Level = 100 } });
        Assert.Equal(new double?[] { 280, 280, 140, 280 }, hits.Select(h => h.DirectPotency));
        Assert.Equal(new double?[] { 280, 280, 140, 280 }, hits.Select(h => JobDamageCalibrationPolicy.GetCalibrationPotency(h, true)));
        Assert.All(hits, hit => Assert.True(hit.CanCalibratePotency));
    }

    [Fact]
    public void ExplicitExclusionsAmbiguousEffectsAndUnknownTargetsStayExcluded()
    {
        var parser = new DirectDamageParser();
        Assert.All(parser.Parse(Packet([Target(0, Enemy), Target(1, Enemy)]) with { CanCalibratePotency = false }),
            hit => Assert.False(hit.CanCalibratePotency));
        Assert.All(parser.Parse(Packet([Target(0, Enemy) with { Effects = [Effect(0), Effect(1)] }])),
            hit => Assert.False(hit.CanCalibratePotency));
        Assert.All(parser.Parse(Packet([Target(0, Enemy with { EntityId = 0 })])),
            hit => Assert.False(hit.CanCalibratePotency));
    }

    [Theory]
    [MemberData(nameof(PeriodicCalibrationProfileTests.Profiles), MemberType = typeof(PeriodicCalibrationProfileTests))]
    public void EveryMeterPotencyProfileUsesTargetIdentityForRepeatedHits(uint action, double primary, double combo,
        double secondary, double secondaryCombo)
    {
        foreach (var comboByte in new byte[] { 0, 1 })
        {
            var packet = Packet([Target(0, Enemy), Target(1, Enemy)]) with
            {
                ActionId = action, Source = Player with { Level = 100, ClassJobId = 19 },
                Targets = Enumerable.Range(0, 2).Select(index => new DamageActionTarget(index, Enemy,
                    [Effect(0) with { Param2 = comboByte }])).ToArray(),
                ComboPotency = 200, SecondaryTargetPotencyMultiplier = 0.5,
            };
            foreach (var hit in new DirectDamageParser().Parse(packet))
            {
                Assert.True(hit.CanCalibratePotency);
                Assert.Equal(comboByte == 0 ? primary : combo, JobDamageCalibrationPolicy.GetCalibrationPotency(hit, true));
                Assert.Equal(comboByte == 0 ? secondary : secondaryCombo,
                    JobDamageCalibrationPolicy.GetCalibrationPotency(hit with { IsSecondaryTarget = null, TargetIndex = 1 }, true));
                Assert.Equal(comboByte == 0 ? 100 : 200, hit.DirectPotency);
                Assert.Equal(1000, hit.RawMeterAmount);
            }
        }
    }

    private static DamageActionEffect Effect(int index) => new(index, 3, 0, 0, 0, 0, 1000);
    private static DamageActionTarget Target(int index, DamageActorIdentity target) => new(index, target, [Effect(0)]);
    private static DamageActionPacket Packet(DamageActionTarget[] targets) => new(1, Start, 1, Player, 100, "Hit", targets)
    { DirectPotency = 100, CanCalibratePotency = true, HasSourceStatusSnapshot = true };
}
