namespace BetterDeaths.DamageParsing;

internal static class DamageMeterCombatantPolicy
{
    public static bool ShouldDisplay(DamageActorIdentity source)
    {
        return source.IsPlayer || source.IsPartyMember || source.IsLimitBreak;
    }
}
