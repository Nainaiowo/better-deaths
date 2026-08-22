using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace BetterDeaths.WtfDig;

internal enum ArrowStartRole
{
    Sleep,
    Confused,
}

internal enum ArrowStrategy
{
    MerryGoRound,
    Filipino,
    Freaky,
}

internal sealed record ArrowDrop(
    int ActorId,
    string Name,
    WtfDigJobInfo Job,
    uint StatusId,
    int DirectionIndex,
    int Wave,
    double Time,
    Vector2? Position,
    double? Facing);

internal sealed record ArrowWave(int Index, double Time, IReadOnlyList<ArrowDrop> Arrows);

internal sealed record ArrowStart(
    int ActorId,
    string Name,
    WtfDigJobInfo Job,
    ArrowStartRole Role,
    double Time,
    Vector2? Position);

internal sealed record ArrowsAnalysis(
    FflogsFight Fight,
    Vector2 Center,
    IReadOnlyList<ArrowWave> Waves,
    IReadOnlyList<ArrowStart> Starts);

internal sealed class ArrowsAnalyzer(IWtfDigEventSource client)
{
    internal const uint TeleTrouncingCastGameId = 47801;
    internal const float ArrowAoeRadius = 2.0f;

    private static readonly HashSet<uint> ArrowStatusIds =
    [
        1004876, 1004877, 1004878, 1004879, 1005079, 1005080, 1005081, 1005082,
    ];

    private static readonly IReadOnlyDictionary<uint, int> DirectionByStatus = new Dictionary<uint, int>
    {
        [1004876] = 0,
        [1005079] = 0,
        [1004878] = 1,
        [1005081] = 1,
        [1004877] = 2,
        [1005080] = 2,
        [1004879] = 3,
        [1005082] = 3,
    };

    internal async Task<ArrowsAnalysis> AnalyzeAsync(
        FflogsReportSummary report,
        FflogsFight fight,
        CancellationToken cancellationToken)
    {
        var center = WtfDigAnalysisHelpers.DefaultCenter;
        var ttl = FflogsClient.EventsCacheTtl(report, fight);
        var players = WtfDigAnalysisHelpers.FightPlayers(report, fight);
        var playerById = players.ToDictionary(player => player.Id);
        var telegraphCasts = await client.FetchAllEventsAsync(
            new FflogsEventQuery(
                report.Code,
                fight.Id,
                fight.StartTime,
                fight.EndTime,
                FflogsEventDataType.Casts,
                FflogsHostilityType.Enemies,
                AbilityId: TeleTrouncingCastGameId,
                CacheTtl: ttl),
            cancellationToken).ConfigureAwait(false);
        var telegraph = telegraphCasts.FirstOrDefault(entry => entry.Type == "cast");
        if (telegraph is null)
        {
            return new ArrowsAnalysis(fight, center, [], []);
        }

        var start = telegraph.Timestamp - 2_000;
        var end = Math.Min(fight.EndTime, telegraph.Timestamp + 27_000);
        var debuffsTask = client.FetchAllEventsAsync(
            new FflogsEventQuery(report.Code, fight.Id, start, end, FflogsEventDataType.Debuffs, CacheTtl: ttl),
            cancellationToken);
        var damageTask = client.FetchAllEventsAsync(
            new FflogsEventQuery(report.Code, fight.Id, start, end, FflogsEventDataType.DamageTaken,
                IncludeResources: true, CacheTtl: ttl), cancellationToken);
        var friendlyCastsTask = client.FetchAllEventsAsync(
            new FflogsEventQuery(report.Code, fight.Id, start, end, FflogsEventDataType.Casts,
                IncludeResources: true, CacheTtl: ttl), cancellationToken);
        var willCastsTask = client.FetchAllEventsAsync(
            new FflogsEventQuery(report.Code, fight.Id, fight.StartTime, fight.EndTime, FflogsEventDataType.Casts,
                FflogsHostilityType.Enemies, true,
                FilterExpression: "ability.id in (47798, 47797)", CacheTtl: ttl), cancellationToken);
        await Task.WhenAll(debuffsTask, damageTask, friendlyCastsTask, willCastsTask).ConfigureAwait(false);
        var debuffs = await debuffsTask.ConfigureAwait(false);
        var damage = await damageTask.ConfigureAwait(false);
        var friendlyCasts = await friendlyCastsTask.ConfigureAwait(false);
        var willCasts = await willCastsTask.ConfigureAwait(false);

        var samples = WtfDigAnalysisHelpers.BuildSampleMap(damage, friendlyCasts);
        foreach (var cast in willCasts)
        {
            WtfDigAnalysisHelpers.AddSample(samples, cast.TargetID, cast.Timestamp, cast.TargetResources);
        }

        var removals = debuffs
            .Where(entry =>
                entry.Type == "removedebuff" &&
                entry.AbilityGameID is { } statusId &&
                ArrowStatusIds.Contains(statusId) &&
                entry.TargetID is { } actorId &&
                playerById.ContainsKey(actorId))
            .OrderBy(entry => entry.Timestamp)
            .ToArray();
        var clusters = GroupByGap(removals, 1500);
        var waves = clusters.Select((cluster, index) =>
        {
            var time = WtfDigAnalysisHelpers.Median(cluster.Select(entry => entry.Timestamp));
            var arrows = cluster.Select(entry =>
            {
                var player = playerById[entry.TargetID!.Value];
                var resources = WtfDigAnalysisHelpers.SampleAt(samples, player.Id, entry.Timestamp);
                var statusId = entry.AbilityGameID!.Value;
                return new ArrowDrop(
                    player.Id,
                    player.Name,
                    WtfDigAnalysisHelpers.JobInfo(player.SubType),
                    statusId,
                    DirectionByStatus.GetValueOrDefault(statusId),
                    index + 1,
                    (entry.Timestamp - fight.StartTime) / 1000,
                    resources is null
                        ? null
                        : WtfDigAnalysisHelpers.RawToArena(resources.X, resources.Y, center),
                    resources?.Facing);
            }).ToArray();
            return new ArrowWave(index, Math.Round((time - fight.StartTime) / 1000), arrows);
        }).ToArray();

        var starts = new List<ArrowStart>();
        var seen = new HashSet<int>();
        foreach (var entry in debuffs)
        {
            if (entry.Type != "applydebuff" || entry.TargetID is not { } actorId || !playerById.TryGetValue(actorId, out var player))
            {
                continue;
            }

            var role = entry.AbilityGameID switch
            {
                1004894 => ArrowStartRole.Sleep,
                1001283 => ArrowStartRole.Confused,
                _ => (ArrowStartRole?)null,
            };
            if (role is null || !seen.Add(actorId))
            {
                continue;
            }

            var resources = WtfDigAnalysisHelpers.SampleAt(samples, actorId, entry.Timestamp);
            starts.Add(new ArrowStart(
                actorId,
                player.Name,
                WtfDigAnalysisHelpers.JobInfo(player.SubType),
                role.Value,
                (entry.Timestamp - fight.StartTime) / 1000,
                resources is null ? null : WtfDigAnalysisHelpers.RawToArena(resources.X, resources.Y, center)));
        }

        return new ArrowsAnalysis(fight, center, waves, starts);
    }

    internal static IReadOnlyList<Vector2> ExpectedSlots(ArrowStrategy strategy)
    {
        if (strategy == ArrowStrategy.Filipino)
        {
            var coordinates = new[] { -12.0f, -6.0f, 6.0f, 12.0f };
            return coordinates.SelectMany(x => coordinates.Select(y => new Vector2(x, y))).ToArray();
        }

        var squareCoordinates = new[] { -12.0f, -6.0f, 0.0f, 6.0f, 12.0f };
        var slots = squareCoordinates
            .SelectMany(x => squareCoordinates.Select(y => new Vector2(x, y)))
            .Where(position => Math.Max(Math.Abs(position.X), Math.Abs(position.Y)) == 12.0f)
            .ToArray();
        if (strategy != ArrowStrategy.Freaky)
        {
            return slots;
        }

        const float inset = 10.7f;
        return slots.Select(position =>
        {
            if (position.X == 0 && Math.Abs(position.Y) == 12)
            {
                return new Vector2(0, Math.Sign(position.Y) * inset);
            }

            return position.Y == 0 && Math.Abs(position.X) == 12
                ? new Vector2(Math.Sign(position.X) * inset, 0)
                : position;
        }).ToArray();
    }

    internal static float? Error(Vector2? position, IReadOnlyList<Vector2> slots) => position is not { } point
        ? null
        : slots.Select(slot => Vector2.Distance(point, slot)).DefaultIfEmpty(float.PositiveInfinity).Min();

    private static IReadOnlyList<List<FflogsEvent>> GroupByGap(IEnumerable<FflogsEvent> entries, double gapMs)
    {
        var groups = new List<List<FflogsEvent>>();
        foreach (var entry in entries)
        {
            var last = groups.LastOrDefault();
            if (last is not null && entry.Timestamp - last[^1].Timestamp <= gapMs)
            {
                last.Add(entry);
            }
            else
            {
                groups.Add([entry]);
            }
        }

        return groups;
    }
}
