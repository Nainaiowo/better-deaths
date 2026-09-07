namespace BetterDeaths.Tests;

using BetterDeaths.DamageParsing;

public sealed class ComboCalibrationTests
{
    [Theory]
    [InlineData(19u, 170, 330)]
    [InlineData(21u, 150, 300)]
    [InlineData(22u, 140, 300)]
    [InlineData(22u, 130, 340)]
    [InlineData(22u, 160, 460)]
    [InlineData(32u, 170, 320)]
    [InlineData(37u, 160, 300)]
    public void PacketSelectsBaseOrComboWithoutChangingDirectDamage(uint job, int basePotency, int comboPotency)
    {
        var profile = ActionPotencyProfileParser.Parse(
            $"Delivers an attack with a potency of {basePotency}. Combo Action: Previous skill Combo Potency: {comboPotency}", false);
        Assert.Equal(basePotency, profile.DirectPotency);
        Assert.Equal(comboPotency, profile.ComboPotency);
        var source = new DamageActorIdentity(0x1001, "Player", 0, "", true, job);
        var enemy = new DamageActorIdentity(0x40000001, "Enemy", 0, "", false, 0);
        foreach (var comboByte in new byte[] { 0, 100 })
        {
            var packet = new DamageActionPacket(1, DateTime.UtcNow, 1, source, 100, "Combo skill",
                [new(0, enemy, [new(0, 3, 0x60, 3, 0, 0, 54321) { Param2 = comboByte }])])
            {
                DirectPotency = profile.DirectPotency,
                ComboPotency = profile.ComboPotency,
                CanCalibratePotency = true,
            };
            var hit = Assert.Single(new DirectDamageParser().Parse(packet));
            Assert.Equal(comboByte == 0 ? basePotency : comboPotency, hit.DirectPotency);
            Assert.Equal(54321u, hit.Amount);
            Assert.Equal(54321, hit.RawMeterAmount);
            Assert.True(hit.Critical);
            Assert.True(hit.DirectHit);
            Assert.True(hit.CanCalibratePotency);
        }
    }

    [Theory]
    [InlineData("Delivers an attack with a potency of 100. Combo Potency: 400 Rear Combo Potency: 500")]
    [InlineData("Delivers an attack with a potency of 100. Combo Potency: 400 Flank Potency: 500")]
    [InlineData("Delivers an attack with a potency of 100. Combo Potency: 400 Potency increases during a buff.")]
    [InlineData("Delivers an attack with a potency of 100. Combo Potency: 400.5")]
    [InlineData("Delivers an attack with a potency of 100. Combo Potency: 4,00")]
    [InlineData("Delivers an attack with a potency of 100. Combo Potency: ?")]
    public void AmbiguousOrMalformedComboPotencyIsNotCalibrated(string description)
    {
        var profile = ActionPotencyProfileParser.Parse(description, false);
        Assert.Null(profile.DirectPotency);
        Assert.Null(profile.ComboPotency);
    }

    [Fact]
    public void ComboAfterAdditionalEffectIsStillRecognized()
    {
        var profile = ActionPotencyProfileParser.Parse(
            "Deals damage with a potency of 200. Additional Effect: Grants a buff Combo Potency: 400", false);
        Assert.Equal(200, profile.DirectPotency);
        Assert.Equal(400, profile.ComboPotency);
    }
}
