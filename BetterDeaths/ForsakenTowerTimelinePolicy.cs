namespace BetterDeaths;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

internal static class ForsakenTowerTimelinePolicy
{
    internal const string PathOfLightRawEventKind = "dmu-p2-path-of-light";

    internal static bool IsPathOfLightMechanic(ReplayMechanicSnapshot mechanic)
    {
        return string.Equals(mechanic.RawEventKind, PathOfLightRawEventKind, StringComparison.Ordinal) &&
            mechanic.Shape == ReplayMechanicShape.Tower;
    }

    internal static bool IsActiveAt(ReplayMechanicSnapshot mechanic, DateTime selectedAtUtc)
    {
        var endAtUtc = mechanic.SeenAtUtc.AddSeconds(Math.Max(0.05f, mechanic.DurationSeconds));
        return mechanic.SeenAtUtc <= selectedAtUtc && selectedAtUtc < endAtUtc;
    }

    internal static bool TryGetTowerIndex(string sourceKey, out int towerIndex)
    {
        var prefix = PathOfLightRawEventKind + ":";
        towerIndex = 0;
        if (!sourceKey.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var indexEnd = sourceKey.IndexOf(':', prefix.Length);
        return indexEnd > prefix.Length &&
            int.TryParse(sourceKey[prefix.Length..indexEnd], CultureInfo.InvariantCulture, out towerIndex) &&
            towerIndex is >= 1 and <= 8;
    }

    internal static IReadOnlyList<ReplayMechanicSnapshot> NormalizeTimeline(
        IReadOnlyList<ReplayMechanicSnapshot> mechanics)
    {
        var passthroughMechanics = new List<ReplayMechanicSnapshot>();
        var pathOfLightTowers = new List<ReplayMechanicSnapshot>();
        foreach (var mechanic in mechanics)
        {
            if (IsPathOfLightMechanic(mechanic))
            {
                pathOfLightTowers.Add(mechanic);
            }
            else
            {
                passthroughMechanics.Add(mechanic);
            }
        }

        if (pathOfLightTowers.Count == 0)
        {
            return passthroughMechanics;
        }

        pathOfLightTowers = ExtendTowersToHandoff(pathOfLightTowers, mechanics).ToList();

        var adjustedTowers = new List<ReplayMechanicSnapshot>(pathOfLightTowers.Count);
        var activeTowers = new List<(ReplayMechanicSnapshot Mechanic, DateTime EndAtUtc)>();
        var storedTowers = new List<ReplayMechanicSnapshot>();

        foreach (var tower in pathOfLightTowers
            .OrderBy(mechanic => mechanic.SeenAtUtc)
            .ThenBy(mechanic => mechanic.SourceKey, StringComparer.Ordinal))
        {
            ReleaseStoredTowers(tower.SeenAtUtc);
            if (activeTowers.Count >= 2)
            {
                storedTowers.Add(tower);
                continue;
            }

            AddActiveTower(tower, tower.SeenAtUtc);
        }

        ReleaseStoredTowers(DateTime.MaxValue);
        passthroughMechanics.AddRange(adjustedTowers);
        return passthroughMechanics;

        void AddActiveTower(ReplayMechanicSnapshot tower, DateTime displayStartAtUtc)
        {
            var adjustedTower = AdjustTowerStart(tower, displayStartAtUtc);
            if (adjustedTower is null)
            {
                return;
            }

            adjustedTowers.Add(adjustedTower);
            activeTowers.Add((adjustedTower, adjustedTower.SeenAtUtc.AddSeconds(Math.Max(0.05f, adjustedTower.DurationSeconds))));
        }

        void ReleaseStoredTowers(DateTime selectedAtUtc)
        {
            while (activeTowers.Any(entry => entry.EndAtUtc <= selectedAtUtc))
            {
                var releaseAtUtc = activeTowers
                    .Where(entry => entry.EndAtUtc <= selectedAtUtc)
                    .Select(entry => entry.EndAtUtc)
                    .DefaultIfEmpty(DateTime.MinValue)
                    .Max();
                activeTowers.RemoveAll(entry => entry.EndAtUtc <= selectedAtUtc);
                if (activeTowers.Count != 0 || storedTowers.Count == 0)
                {
                    continue;
                }

                var releaseBatch = storedTowers
                    .OrderBy(mechanic => mechanic.SeenAtUtc)
                    .ThenBy(mechanic => mechanic.SourceKey, StringComparer.Ordinal)
                    .ToList();
                storedTowers.Clear();
                foreach (var storedTower in releaseBatch)
                {
                    AddActiveTower(storedTower, releaseAtUtc);
                }
            }
        }
    }

    private static ReplayMechanicSnapshot? AdjustTowerStart(
        ReplayMechanicSnapshot tower,
        DateTime displayStartAtUtc)
    {
        if (displayStartAtUtc <= tower.SeenAtUtc)
        {
            return tower;
        }

        var originalEndAtUtc = tower.SeenAtUtc.AddSeconds(Math.Max(0.05f, tower.DurationSeconds));
        if (displayStartAtUtc >= originalEndAtUtc)
        {
            return null;
        }

        var startDelaySeconds = (float)(displayStartAtUtc - tower.SeenAtUtc).TotalSeconds;
        return tower with
        {
            SeenAtUtc = displayStartAtUtc,
            PullElapsedSeconds = tower.PullElapsedSeconds + startDelaySeconds,
            DurationSeconds = Math.Max(0.05f, (float)(originalEndAtUtc - displayStartAtUtc).TotalSeconds),
        };
    }

    private static IReadOnlyList<ReplayMechanicSnapshot> ExtendTowersToHandoff(
        IReadOnlyList<ReplayMechanicSnapshot> towers,
        IReadOnlyList<ReplayMechanicSnapshot> mechanics)
    {
        var activations = mechanics
            .Where(mechanic => string.Equals(
                mechanic.RawEventKind,
                ReplayEncounterModules.DmuP2PathOfLightActivationRawEventKind,
                StringComparison.Ordinal))
            .Select(mechanic => TryGetActivationTowerIndex(mechanic.SourceKey, out var towerIndex)
                ? (Mechanic: mechanic, TowerIndex: towerIndex)
                : (Mechanic: (ReplayMechanicSnapshot?)null, TowerIndex: 0))
            .Where(entry => entry.Mechanic is not null)
            .Select(entry => (Mechanic: entry.Mechanic!, entry.TowerIndex))
            .ToArray();
        var resolveMechanics = mechanics
            .Where(mechanic =>
                mechanic.RawEventId is 47808 or 47809 or 47810 &&
                (string.Equals(
                        mechanic.RawEventKind,
                        ReplayEncounterModules.DmuP2ForsakenTargetRawEventKind,
                        StringComparison.Ordinal) ||
                    string.Equals(mechanic.RawEventKind, "dmu-p2-spelldriver", StringComparison.Ordinal) ||
                    string.Equals(mechanic.RawEventKind, "dmu-p2-spellscatter", StringComparison.Ordinal) ||
                    string.Equals(mechanic.RawEventKind, "dmu-p2-spellwave", StringComparison.Ordinal)))
            .ToArray();
        var resolveTimes = BuildTimestampBatches(resolveMechanics
            .Select(mechanic => mechanic.SeenAtUtc), TimeSpan.FromSeconds(0.25));
        if (resolveTimes.Length == 0)
        {
            return towers;
        }

        var fallbackResolveTimes = new Dictionary<(DateTime SeenAtUtc, string SourceKey), DateTime>();
        var towerBatches = BuildMechanicBatches(towers, TimeSpan.FromSeconds(1.0));
        for (var index = 0; index < Math.Min(towerBatches.Count, resolveTimes.Length); index++)
        {
            foreach (var tower in towerBatches[index])
            {
                fallbackResolveTimes[(tower.SeenAtUtc, tower.SourceKey)] = resolveTimes[index];
            }
        }

        return towers.Select(tower =>
        {
            if (!TryGetTowerIndex(tower.SourceKey, out var towerIndex))
            {
                return tower;
            }

            var currentEndAtUtc = tower.SeenAtUtc.AddSeconds(Math.Max(0.05f, tower.DurationSeconds));
            var activation = activations
                .Where(entry => entry.TowerIndex == towerIndex)
                .OrderBy(entry => Math.Abs((entry.Mechanic.SeenAtUtc - currentEndAtUtc).TotalSeconds))
                .FirstOrDefault();
            var resolveAtUtc = activation.Mechanic is not null &&
                Math.Abs((activation.Mechanic.SeenAtUtc - currentEndAtUtc).TotalSeconds) <= 1.0
                    ? resolveTimes.FirstOrDefault(candidate =>
                        candidate >= activation.Mechanic.SeenAtUtc &&
                        candidate - activation.Mechanic.SeenAtUtc <= TimeSpan.FromSeconds(2.0))
                    : default;
            if (resolveAtUtc == default &&
                !fallbackResolveTimes.TryGetValue((tower.SeenAtUtc, tower.SourceKey), out resolveAtUtc))
            {
                return tower;
            }

            var handoffAtUtc = ReplayEncounterModules.GetDmuP2ForsakenHandoffAtUtc(resolveAtUtc);
            return handoffAtUtc <= currentEndAtUtc
                ? tower
                : tower with
                {
                    DurationSeconds = Math.Max(0.05f, (float)(handoffAtUtc - tower.SeenAtUtc).TotalSeconds),
                };
        }).ToList();
    }

    private static DateTime[] BuildTimestampBatches(
        IEnumerable<DateTime> timestamps,
        TimeSpan maxGap)
    {
        var batches = new List<List<DateTime>>();
        foreach (var timestamp in timestamps.OrderBy(value => value))
        {
            if (batches.Count == 0 || timestamp - batches[^1][^1] > maxGap)
            {
                batches.Add([timestamp]);
            }
            else
            {
                batches[^1].Add(timestamp);
            }
        }

        return batches.Select(batch => batch.Max()).ToArray();
    }

    private static IReadOnlyList<IReadOnlyList<ReplayMechanicSnapshot>> BuildMechanicBatches(
        IEnumerable<ReplayMechanicSnapshot> mechanics,
        TimeSpan maxGap)
    {
        var batches = new List<IReadOnlyList<ReplayMechanicSnapshot>>();
        var current = new List<ReplayMechanicSnapshot>();
        foreach (var mechanic in mechanics
            .OrderBy(entry => entry.SeenAtUtc)
            .ThenBy(entry => entry.SourceKey, StringComparer.Ordinal))
        {
            if (current.Count > 0 && mechanic.SeenAtUtc - current[^1].SeenAtUtc > maxGap)
            {
                batches.Add(current);
                current = [];
            }

            current.Add(mechanic);
        }

        if (current.Count > 0)
        {
            batches.Add(current);
        }

        return batches;
    }

    private static bool TryGetActivationTowerIndex(string sourceKey, out int towerIndex)
    {
        var prefix = ReplayEncounterModules.DmuP2PathOfLightActivationRawEventKind + ":";
        towerIndex = 0;
        if (!sourceKey.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var indexEnd = sourceKey.IndexOf(':', prefix.Length);
        return indexEnd > prefix.Length &&
            int.TryParse(sourceKey[prefix.Length..indexEnd], CultureInfo.InvariantCulture, out towerIndex) &&
            towerIndex is >= 1 and <= 8;
    }
}
