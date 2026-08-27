namespace BetterDeaths.DamageParsing;

internal static class GroundDamageStatusPolicy
{
    public static bool IsKnown(uint statusId)
    {
        return statusId is
            0x01F5 or // Doton
            0x02ED or // Salted Earth
            0x035D or // Wildfire
            0x04B5 or // Flamethrower
            0x09C6 or // Phantom Flurry
            0x0A92 or // Slipstream
            0x0E3C;   // Apokalypsis
    }
}
