namespace BetterDeaths;

internal static class ContentCapturePolicy
{
    public static bool SupportsOverworldMeter(bool hasTerritory, bool hasDuty, uint contentType, uint intendedUse) =>
        hasTerritory && !hasDuty && contentType == 0 && !SupportsPreDutyCalibration(contentType, intendedUse);

    public static bool SupportsPreDutyCalibration(uint contentType, uint intendedUse) =>
        contentType is 2 or 4 or 5 or 21 or 28 or 30 or 37 ||
        contentType == 0 && intendedUse is 3 or 4 or 31 or 57 or 58;
}
