namespace BetterDeaths.DamageParsing;

public sealed class ActionPotencyProfileParserTests
{
    [Theory]
    [InlineData("fr-FR")]
    [InlineData("tr-TR")]
    public void EnglishPotencyParsingIsIndependentOfClientCulture(string culture)
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new(culture);
            Assert.Equal(new ActionPotencyProfile(1100, null), ActionPotencyProfileParser.Parse(
                "DELIVERS AN ATTACK WITH A POTENCY OF 1,100.", true));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("Deals unaspected damage over time. Potency: 90 Duration: 30s Additional Effect: Restores HP to targets under Kardion Cure Potency: 170", null, 90)]
    [InlineData("Deals physical damage over time with a potency of 200. Duration: 5s Executing Phantom Flurry again deals physical damage with a potency of 600.", null, 200)]
    [InlineData("Deals unaspected damage with a potency of 1,100. Additional Effect: Restores HP Cure Potency: 400", 1100, null)]
    [InlineData("Delivers an attack with a potency of 100. Additional Effect: Venom Potency: 15 Duration: 45s", 100, 15)]
    [InlineData("Creates a patch of salted earth at your feet, dealing unaspected damage with a potency of 50 to any enemies who enter. Duration: 15s", null, 50)]
    [InlineData("Deals wind damage with a potency of 520. Additional Effect: Creates a windstorm centered around the target, dealing damage to any enemies who enter Potency: 30 Duration: 15s", 520, 30)]
    [InlineData("Delivers an attack with a potency of 260. Opo-opo's Fury Potency: 460 Additional Effect: Changes form", null, null)]
    [InlineData("Delivers an attack with a potency of 270. 330 when executed from a target's flank.", null, null)]
    [InlineData("Deals unaspected damage with a potency of 400. Divine Might Potency: 500 Requiescat Potency: 700", null, null)]
    [InlineData("Deals water damage with a potency of 500. Additional Effect: Potency increases to 1,000 during rain", null, null)]
    [InlineData("Deals damage with a potency of 200. Additional Effect: Grants a buff Combo Potency: 400", null, null)]
    [InlineData("Restores target's HP. Cure Potency: 1,000", null, null)]
    [InlineData("Deals damage over time. Potency: ? Duration: 30s Additional Effect: Healing Cure Potency: 170", null, null)]
    [InlineData("Deals damage with a potency of 1,10.", null, null)]
    [InlineData("Deals damage with a potency of 1.5.", null, null)]
    public void SeparatesDamageFromHealingAndConditionalEffects(string text, int? direct, int? periodic)
    {
        Assert.Equal(new ActionPotencyProfile(direct, periodic), ActionPotencyProfileParser.Parse(text, true));
    }

    [Fact]
    public void ReadsDirectAndPeriodicPotenciesFromAStatusAction()
    {
        var profile = ActionPotencyProfileParser.Parse(
            "Deals wind damage with a potency of 100. Additional Effect: Wind damage over time Potency: 25 Duration: 45s",
            appliesPeriodicDamage: true);

        Assert.Equal(100.0, profile.DirectPotency);
        Assert.Equal(25.0, profile.PeriodicPotency);
    }

    [Fact]
    public void ReadsPeriodicOnlyActionsWithoutInventingDirectPotency()
    {
        var profile = ActionPotencyProfileParser.Parse(
            "Deals unaspected damage over time. Potency: 50 Duration: 30s",
            appliesPeriodicDamage: true);

        Assert.Null(profile.DirectPotency);
        Assert.Equal(50.0, profile.PeriodicPotency);
    }

    [Fact]
    public void RejectsVariableDirectPotencyForCalibration()
    {
        var profile = ActionPotencyProfileParser.Parse(
            "Delivers an attack with a potency of 100. Combo Potency: 400",
            appliesPeriodicDamage: false);

        Assert.Null(profile.DirectPotency);
        Assert.Null(profile.PeriodicPotency);
    }

    [Fact]
    public void LeavesMacroSubstitutedPotencyUnknown()
    {
        var profile = ActionPotencyProfileParser.Parse(
            "Delivers an attack with a potency of .",
            appliesPeriodicDamage: false);

        Assert.Null(profile.DirectPotency);
    }
}
