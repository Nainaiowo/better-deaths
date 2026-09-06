namespace BetterDeaths;

internal static class ContentCapturePolicy
{
    public static bool IsDungeon(uint contentType, uint intendedUse)
    {
        // Only regular and Deep Dungeons are blocked; explicit duty categories override territory flags.
        return contentType is 2 or 21 ||
            (contentType == 0 && intendedUse is 3 or 31);
    }
}
