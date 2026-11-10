using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterDeaths;

public sealed record ReplayDebuffActiveState(
    ReplayDebuffSnapshot Snapshot,
    float? RemainingSeconds);

public static class ReplayDebuffTimeline
{
    private const float TimelineEpsilonSeconds = 0.001f;
    public static IReadOnlyList<ReplayDebuffActiveState> GetActiveStates(
        IReadOnlyList<ReplayDebuffSnapshot> changes,
        float pullElapsedSeconds)
    {
        if (changes.Count == 0)
        {
            return [];
        }

        var latestByStatus = new Dictionary<(string MemberKey, uint StatusId, uint SourceId), ReplayDebuffSnapshot>();
        foreach (var change in changes)
        {
            if (change.PullElapsedSeconds > pullElapsedSeconds + TimelineEpsilonSeconds)
            {
                continue;
            }

            var key = (change.MemberKey, change.Status.Id, change.Status.SourceId);
            if (!latestByStatus.TryGetValue(key, out var existing) ||
                change.PullElapsedSeconds > existing.PullElapsedSeconds ||
                (MathF.Abs(change.PullElapsedSeconds - existing.PullElapsedSeconds) <= TimelineEpsilonSeconds &&
                    change.SeenAtUtc >= existing.SeenAtUtc))
            {
                latestByStatus[key] = change;
            }
        }

        var active = new List<ReplayDebuffActiveState>();
        foreach (var change in latestByStatus.Values)
        {
            if (!change.Active)
            {
                continue;
            }

            float? remainingSeconds = null;
            if (change.Status.RemainingTime > 0.0f)
            {
                remainingSeconds = MathF.Max(
                    0.0f,
                    change.Status.RemainingTime -
                        MathF.Max(0.0f, pullElapsedSeconds - change.PullElapsedSeconds));
                if (remainingSeconds <= 0)
                    continue;
            }

            active.Add(new ReplayDebuffActiveState(change, remainingSeconds));
        }

        return active
            .OrderBy(state => state.Snapshot.PartyIndex)
            .ThenBy(state => state.Snapshot.MemberName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(state => state.Snapshot.Status.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(state => state.Snapshot.Status.Id)
            .ThenBy(state => state.Snapshot.Status.SourceId)
            .ToList();
    }
}
