namespace BetterDeaths.DamageParsing;

internal static class PeriodicDamageRefreshPolicy
{
    internal const uint IronJawsActionId = 0x0DE8;

    public static bool IsExplicitResnapshot(uint actionId)
    {
        return actionId == IronJawsActionId;
    }

    public static uint? GetExclusiveFamily(uint statusId) => statusId switch
    {
        // A caster can maintain only one Thunder variant on a given target.
        0xA1 or 0xA2 or 0xA3 or 0x4BA or 0xF1F or 0xF20 => 0xA1,
        _ => null,
    };

    public static int? GetMaximumTicks(uint statusId) => statusId switch
    {
        0xA1 => 8,   // Thunder: 24s
        0xA2 => 6,   // Thunder II: 18s
        0xA3 => 9,   // Thunder III: 27s
        0x4BA => 7,  // Thunder IV: 21s
        0xF1F => 10, // High Thunder: 30s
        0xF20 => 8,  // High Thunder II: 24s
        0xF2B => 5,  // Baneful Impaction: 15s
        0x759 => 10, // Combust III: 30s
        _ => null,
    };
}
