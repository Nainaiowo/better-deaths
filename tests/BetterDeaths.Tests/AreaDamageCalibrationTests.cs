namespace BetterDeaths.Tests;

using BetterDeaths.DamageParsing;

public sealed class AreaDamageCalibrationTests
{
    [Theory]
    [InlineData("Deals unaspected damage with a potency of 150 to all nearby enemies. Additional Effect: Stun", 1.0)]
    [InlineData("Deals lightning damage with a potency of 100 to target and all enemies nearby it. Additional Effect: Lightning damage over time Potency: 40", 1.0)]
    [InlineData("Deals damage to target and all enemies nearby it with a potency of 640 for the first enemy, and 40% less for all remaining enemies.", 0.6)]
    [InlineData("Deals damage to target and all enemies nearby it with a potency of 1,400 for the first enemy, and 50% less for all remaining enemies.", 0.5)]
    [InlineData("Deals damage to target and all enemies nearby it with a potency of 600 for the first enemy, and 25% less for all remaining enemies.", 0.75)]
    [InlineData("Deals damage with a potency of 400 to all nearby enemies. Additional Effect: Restores HP Cure Potency: 400", 1.0)]
    [InlineData("Deals damage with a potency of 100 to all nearby enemies. Damage is reduced against secondary targets.", null)]
    [InlineData("Deals damage with a potency of 100 to all nearby enemies. Damage is divided among targets.", null)]
    [InlineData("Deals damage with a potency of 100 to target and all enemies nearby it, with 50% less damage.", null)]
    [InlineData("Deals damage with a potency of 100 for the first enemy, and 150% less for all remaining enemies.", null)]
    [InlineData("Deals damage with a potency of 100 to all nearby enemies. Combo Potency: 200", null)]
    [InlineData("Deals damage with a potency of 100.", null)]
    public void OnlyRecognizedTargetScalingEnablesAreaCalibration(string text, double? expected)
    {
        Assert.Equal(expected, ActionPotencyProfileParser.Parse(text, false).SecondaryTargetMultiplier);
    }

    [Theory]
    [InlineData(1.0, 1000u, 100.0)]
    [InlineData(0.5, 500u, 50.0)]
    [InlineData(0.6, 600u, 60.0)]
    public void TargetSpecificPotencyCalibratesWithoutChangingCapturedDamage(double multiplier, uint secondaryAmount, double secondaryPotency)
    {
        var source = new DamageActorIdentity(0x1001, "Caster", 0, "", true, 24);
        var target = new DamageActorIdentity(0x40000001, "Enemy", 0, "", false, 0);
        var time = new DateTime(2026, 9, 4, 0, 0, 0, DateTimeKind.Utc);
        var module = new DamageParsingModule();
        var parsed = module.Process(new DamageActionPacket(1, time, 1, source, 100, "Area spell",
            [new DamageActionTarget(0, target, [new(0, 3, 0, 0, 0, 0, 1000)]),
             new DamageActionTarget(1, target with { EntityId = 0x40000002 }, [new(0, 3, 0, 0, 0, 0, secondaryAmount)])])
        { DirectPotency = 100, CanCalibratePotency = true, SecondaryTargetPotencyMultiplier = multiplier });
        Assert.Equal(100.0, parsed[0].DirectPotency);
        Assert.Equal(secondaryPotency, parsed[1].DirectPotency);
        Assert.All(parsed, e => Assert.True(e.CanCalibratePotency));
        Assert.Equal(1000u, parsed[0].Amount);
        Assert.Equal(secondaryAmount, parsed[1].Amount);
        module.ObserveStatus(new DamageStatusApplication(target, source, 0x500, "DoT", 0, 100, "Spell",
            time.AddSeconds(1), 30, true, false, false)
        { PeriodicPotency = 20 });
        module.ProcessPeriodicTick(new PeriodicDamageTick(2, time.AddSeconds(3), target, 0, "", 0, 600, null));
        var inputs = Assert.Single(module.FlushPendingPeriodicTicks(time.AddSeconds(3.1))).PeriodicEstimateInputs!;
        Assert.Equal(10.0, inputs.DamagePerPotency);
        Assert.Equal(2, inputs.CalibrationSampleCount);
    }

    [Fact]
    public void MultipleDamageEffectsAndSourceSideDamageAreNotCalibrationSamples()
    {
        var source = new DamageActorIdentity(0x1001, "Caster", 0, "", true, 24);
        var target = new DamageActorIdentity(0x40000001, "Enemy", 0, "", false, 0);
        var parsed = new DirectDamageParser().Parse(new DamageActionPacket(1, DateTime.UtcNow, 1, source, 100, "Spell",
            [new DamageActionTarget(0, target, [new(0, 3, 0, 0, 0, 0, 1000), new(1, 3, 0, 0, 0, 0, 1000)]),
             new DamageActionTarget(1, target, [new(0, 3, 0, 0, 0, 0x80, 1000)])])
        { DirectPotency = 100, CanCalibratePotency = true, SecondaryTargetPotencyMultiplier = 1 });
        Assert.Equal(3, parsed.Count);
        Assert.All(parsed, e => Assert.False(e.CanCalibratePotency));
        Assert.All(parsed, e => Assert.Equal(1000u, e.Amount));
    }
}
