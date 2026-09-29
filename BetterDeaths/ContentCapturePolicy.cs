namespace BetterDeaths;

internal static class ContentCapturePolicy
{
    public static bool RequiresExplicitDeathNotice(
        bool highEndDuty, uint contentType, uint contentUiCategory, uint intendedUse)
    {
        // Older Extreme and Savage duties leave Raid Finder but retain their difficulty category.
        var supportsInferredDeaths = highEndDuty || contentType == 28 ||
            contentType == 4 && contentUiCategory is 19 or 20 or 22 or 33 or 39 or 47 ||
            contentType == 5 && contentUiCategory is 12 or 13 or 17 or 35 or 41 or 49 ||
            contentType is 0 or 30 && intendedUse == 58;
        return !supportsInferredDeaths;
    }

    public static bool SupportsOverworldMeter(bool hasTerritory, bool hasDuty, uint contentType, uint intendedUse) =>
        hasTerritory && !hasDuty && contentType == 0 && !SupportsPreDutyCalibration(contentType, intendedUse);

    public static bool SupportsPreDutyCalibration(uint contentType, uint intendedUse) =>
        contentType is 2 or 4 or 5 or 21 or 28 or 30 or 37 ||
        contentType == 0 && intendedUse is 3 or 4 or 31 or 57 or 58;
}
