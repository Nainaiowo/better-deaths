namespace BetterDeaths.DamageParsing;

internal static class PeriodicDamageRefreshPolicy
{
    internal const uint IronJawsActionId = 0x0DE8;

    public static bool IsExplicitResnapshot(uint actionId)
    {
        return actionId == IronJawsActionId;
    }
}
