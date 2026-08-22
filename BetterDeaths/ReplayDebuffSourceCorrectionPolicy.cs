using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterDeaths;

internal static class ReplayDebuffSourceCorrectionPolicy
{
    internal const float CorrectionWindowSeconds = 0.75f;

    internal static bool IsSupportedStatus(uint statusId) =>
        statusId is 4876 or 4877 or 4878 or 4879 or 5079 or 5080 or 5081 or 5082;

    internal static IReadOnlyList<ReplayDebuffSnapshot> NormalizeForAnalysis(
        IEnumerable<ReplayDebuffSnapshot> changes)
    {
        var ordered = changes
            .OrderBy(change => change.PullElapsedSeconds)
            .ToArray();
        var knownSourceApplications = ordered
            .Where(change => change.Active && change.Status.SourceId != 0)
            .GroupBy(change => (change.MemberKey, change.Status.Id))
            .ToDictionary(
                group => group.Key,
                group => group.Select(change => change.PullElapsedSeconds).ToArray());

        return ordered
            .Where(change => change.Status.SourceId != 0 ||
                !IsSupportedStatus(change.Status.Id) ||
                !HasKnownSourceApplicationNear(change, knownSourceApplications))
            .ToArray();
    }

    internal static bool IsWithinCorrectionWindow(DateTime left, DateTime right) =>
        Math.Abs((right - left).TotalSeconds) <= CorrectionWindowSeconds;

    private static bool HasKnownSourceApplicationNear(
        ReplayDebuffSnapshot change,
        IReadOnlyDictionary<(string MemberKey, uint StatusId), float[]> knownSourceApplications)
    {
        return knownSourceApplications.TryGetValue((change.MemberKey, change.Status.Id), out var applications) &&
            applications.Any(time => Math.Abs(time - change.PullElapsedSeconds) <= CorrectionWindowSeconds);
    }
}
