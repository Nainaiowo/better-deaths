using BetterDeaths.DamageParsing;

namespace BetterDeaths.Tests;

public sealed class DamageMeterCombatantPolicyTests
{
    [Fact]
    public void DisplaysNearbyPlayerOutsideTheLocalParty()
    {
        var alliancePlayer = Actor(isPlayer: true);

        Assert.True(DamageMeterCombatantPolicy.ShouldDisplay(alliancePlayer));
    }

    [Fact]
    public void DisplaysPartyFallbackAndLimitBreakSources()
    {
        Assert.True(DamageMeterCombatantPolicy.ShouldDisplay(Actor(isPartyMember: true)));
        Assert.True(DamageMeterCombatantPolicy.ShouldDisplay(Actor(isLimitBreak: true)));
    }

    [Fact]
    public void DoesNotDisplayEnemyCombatants()
    {
        Assert.False(DamageMeterCombatantPolicy.ShouldDisplay(Actor()));
    }

    private static DamageActorIdentity Actor(
        bool isPlayer = false,
        bool isPartyMember = false,
        bool isLimitBreak = false)
    {
        return new DamageActorIdentity(1, "Actor", 0, string.Empty, isPlayer, 0)
        {
            IsPartyMember = isPartyMember,
            IsLimitBreak = isLimitBreak,
        };
    }
}
