namespace BetterDeaths;

internal static class ContentCapturePolicy
{
    public static bool SupportsPreDutyCalibration(uint contentType, uint intendedUse) =>
        contentType is 4 or 5 or 28 or 30 or 37 ||
        contentType == 0 && intendedUse is 4 or 57 or 58;

    public static bool IsDungeon(uint contentType, uint intendedUse)
    {
        // Only regular and Deep Dungeons are blocked; explicit duty categories override territory flags.
        return contentType is 2 or 21 ||
            (contentType == 0 && intendedUse is 3 or 31);
    }
}
