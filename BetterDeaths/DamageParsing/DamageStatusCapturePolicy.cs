namespace BetterDeaths.DamageParsing;

internal static class DamageStatusCapturePolicy
{
    public static bool IsRelevant(uint statusId)
    {
        return RaidBuffPolicy.IsRelevantStatus(statusId) ||
            PersonalDamageModifierPolicy.IsRelevantStatus(statusId) ||
            PeriodicDirectHitCompatibility.IsRelevantStatus(statusId) ||
            JobDamageCalibrationPolicy.IsRelevantStatus(statusId);
    }
}
