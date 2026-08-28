namespace BetterDeaths.DamageParsing;

public sealed class ActionPotencyProfileParserTests
{
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
