using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BetterDeaths;

internal static class ForsakenCleavePosePolicy
{
    internal const float CastPredictionGraceSeconds = 0.6f;
    internal const string PredictedRawEventKind = "dmu-p2-forsaken-cleave-predicted";
    private const float MaximumPoseDistanceSeconds = 1.5f;
    private const string ResolvedRawEventKind = "dmu-p2-all-things-ending";

    internal static float PredictedResultElapsedSeconds(ReplayMechanicSnapshot mechanic) =>
        mechanic.PullElapsedSeconds + Math.Max(0, mechanic.DurationSeconds - CastPredictionGraceSeconds);

    internal static ReplayMechanicSnapshot UseCompletionPose(
        ReplayMechanicSnapshot mechanic,
        IReadOnlyList<ReplayPositionSnapshot> positions)
    {
        if (mechanic.RawEventId is not (47836 or 47837) ||
            !IsCastSnapshot(mechanic) ||
            !TryGetSourceEntityId(mechanic, out var sourceEntityId))
        {
            return mechanic;
        }

        var resultAt = PredictedResultElapsedSeconds(mechanic);
        var pose = positions
            .Where(position => position.EntityId == sourceEntityId)
            .OrderBy(position => Math.Abs(position.PullElapsedSeconds - resultAt))
            .FirstOrDefault();
        if (pose is null || Math.Abs(pose.PullElapsedSeconds - resultAt) > MaximumPoseDistanceSeconds)
        {
            return mechanic;
        }

        return mechanic with
        {
            X = pose.X,
            Y = pose.Y,
            Z = pose.Z,
            Rotation = pose.Rotation,
        };
    }

    internal static IReadOnlyList<ReplayMechanicSnapshot> NormalizeTimeline(
        IReadOnlyList<ReplayMechanicSnapshot> mechanics,
        IReadOnlyList<ReplayPositionSnapshot> positions)
    {
        var normalized = mechanics
            .Select(mechanic => UseCompletionPose(mechanic, positions))
            .ToArray();
        var cloneDrops = normalized
            .Where(IsCloneDrop)
            .ToArray();
        if (cloneDrops.Length == 0)
        {
            return normalized;
        }

        var replacements = new Dictionary<string, ReplayMechanicSnapshot>(StringComparer.Ordinal);
        var supersededCastKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cleave in normalized.Where(IsResolvedCleave))
        {
            if (!TryGetSourceEntityId(cleave, out var sourceEntityId) ||
                FindLatestDrop(cloneDrops, sourceEntityId, cleave.SeenAtUtc) is not { } drop)
            {
                continue;
            }

            var matchingCasts = normalized
                .Where(candidate =>
                    IsCastSnapshot(candidate) &&
                    candidate.RawEventId == cleave.RawEventId &&
                    TryGetSourceEntityId(candidate, out var candidateSourceEntityId) &&
                    candidateSourceEntityId == sourceEntityId &&
                    candidate.SeenAtUtc <= cleave.SeenAtUtc &&
                    cleave.SeenAtUtc - candidate.SeenAtUtc <= TimeSpan.FromSeconds(7))
                .OrderBy(candidate => candidate.SeenAtUtc)
                .ToArray();
            var startedAtUtc = matchingCasts.Length > 0
                ? matchingCasts[0].SeenAtUtc
                : cleave.SeenAtUtc.AddSeconds(-Math.Max(0.05f, cleave.DurationSeconds));
            foreach (var cast in matchingCasts)
            {
                supersededCastKeys.Add(cast.SourceKey);
            }

            var durationSeconds = Math.Max(0.05f, (float)(cleave.SeenAtUtc - startedAtUtc).TotalSeconds);
            replacements[cleave.SourceKey] = ApplyDropDirection(cleave, drop) with
            {
                SeenAtUtc = startedAtUtc,
                PullElapsedSeconds = cleave.PullElapsedSeconds - durationSeconds,
                DurationSeconds = durationSeconds,
                SourceKey = $"{PredictedRawEventKind}:{sourceEntityId:X8}:{drop.RawEventId}:{cleave.SeenAtUtc.Ticks}",
                RawEventKind = PredictedRawEventKind,
            };
        }

        foreach (var cast in normalized.Where(candidate => IsCastSnapshot(candidate) && IsCleaveAction(candidate.RawEventId)))
        {
            if (supersededCastKeys.Contains(cast.SourceKey) ||
                !TryGetSourceEntityId(cast, out var sourceEntityId) ||
                FindLatestDrop(cloneDrops, sourceEntityId, cast.SeenAtUtc) is not { } drop)
            {
                continue;
            }

            replacements[cast.SourceKey] = ApplyDropDirection(cast, drop);
        }

        if (replacements.Count == 0)
        {
            return normalized;
        }

        return normalized
            .Where(mechanic => !supersededCastKeys.Contains(mechanic.SourceKey))
            .Select(mechanic => replacements.GetValueOrDefault(mechanic.SourceKey, mechanic))
            .ToArray();
    }

    private static ReplayMechanicSnapshot ApplyDropDirection(
        ReplayMechanicSnapshot cleave,
        ReplayMechanicSnapshot drop)
    {
        var isPast = ReplayEncounterModules.IsDmuP2ForsakenPastEndAction(drop.RawEventId);
        return cleave with
        {
            Rotation = ReplayEncounterModules.GetDmuP2ForsakenCleaveRotation(cleave.Rotation, drop.RawEventId),
            Label = isPast
                ? "All Things Ending (Past's End)"
                : "All Things Ending (Future's End)",
        };
    }

    private static ReplayMechanicSnapshot? FindLatestDrop(
        IReadOnlyList<ReplayMechanicSnapshot> cloneDrops,
        uint sourceEntityId,
        DateTime cleaveSeenAtUtc)
    {
        return cloneDrops
            .Where(candidate =>
                TryGetSourceEntityId(candidate, out var candidateSourceEntityId) &&
                candidateSourceEntityId == sourceEntityId &&
                candidate.SeenAtUtc < cleaveSeenAtUtc &&
                cleaveSeenAtUtc - candidate.SeenAtUtc <= TimeSpan.FromSeconds(15))
            .OrderByDescending(candidate => candidate.SeenAtUtc)
            .FirstOrDefault();
    }

    private static bool IsCloneDrop(ReplayMechanicSnapshot mechanic) =>
        ReplayEncounterModules.IsDmuP2ForsakenCloneDropAction(mechanic.RawEventId) &&
        string.Equals(
            mechanic.RawEventKind,
            ReplayEncounterModules.DmuP2ForsakenCloneDropRawEventKind,
            StringComparison.Ordinal);

    private static bool IsResolvedCleave(ReplayMechanicSnapshot mechanic) =>
        IsCleaveAction(mechanic.RawEventId) &&
        string.Equals(mechanic.RawEventKind, ResolvedRawEventKind, StringComparison.Ordinal);

    private static bool IsCleaveAction(uint actionId) => actionId is 47836 or 47837;

    private static bool IsCastSnapshot(ReplayMechanicSnapshot mechanic) =>
        mechanic.RawEventKind.Contains("cast", StringComparison.OrdinalIgnoreCase) ||
        mechanic.RawEventKind.Contains("predicted", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetSourceEntityId(ReplayMechanicSnapshot mechanic, out uint sourceEntityId)
    {
        foreach (var token in mechanic.SourceKey.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length == 8 &&
                uint.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out sourceEntityId) &&
                sourceEntityId != 0)
            {
                return true;
            }
        }

        sourceEntityId = mechanic.RawState;
        return sourceEntityId != 0;
    }
}
