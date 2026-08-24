using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BetterDeaths;

internal static class ForsakenCleavePosePolicy
{
    internal const float CastPredictionGraceSeconds = 0.6f;
    private const float MaximumPoseDistanceSeconds = 1.5f;

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
