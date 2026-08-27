namespace BetterDeaths.DamageParsing;

internal static class ReactiveDamageStatusPolicy
{
    public static bool IsKnown(uint statusId)
    {
        return statusId is
            0x0059 or // Vengeance
            0x00C5 or // Blaze Spikes
            0x00C6 or // Ice Spikes
            0x0156 or // Repelling Spray
            0x01DD or // Mantle of the Whorl
            0x01DE or // Veil of the Whorl
            0x06B8 or // Ice Spikes (player)
            0x06BC or // Veil of the Whorl (player)
            0x08D7 or // Shock Spikes
            0x0E2F or // Schiltron
            0x0EF8;   // Damnation
    }
}
