using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace BetterDeaths.WtfDig;

internal enum LimitCutRotation
{
    Cw,
    Ccw,
}

internal sealed record LimitCutBlasterCast(double Time, Vector2 Position, double Angle);
internal sealed record LimitCutKefkaInfo(
    double StartAngle,
    string StartName,
    LimitCutRotation Rotation,
    IReadOnlyList<LimitCutBlasterCast> Casts);
internal sealed record LimitCutTargetGap(int Number, double Angle, Vector2 Position);
internal sealed record LimitCutPlayer(
    int ActorId,
    string Name,
    WtfDigJobInfo Job,
    int? Number,
    bool Dead,
    Vector2? Position,
    double? Angle,
    double? ExpectedAngle,
    Vector2? ExpectedPosition,
    double? AngleError);
internal sealed record LimitCutAnalysis(
    FflogsFight Fight,
    Vector2 Center,
    LimitCutKefkaInfo? Kefka,
    LimitCutRotation? PlayerRotation,
    double? PlayerStartAngle,
    IReadOnlyList<LimitCutBlasterCast> FinalBlasters,
    IReadOnlyList<Vector2> BlasterSpots,
    IReadOnlyList<LimitCutTargetGap> Gaps,
    double? FinalBlastTime,
    IReadOnlyList<LimitCutPlayer> Players,
    double WallRadius);

internal sealed class LimitCutAnalyzer(IWtfDigEventSource client)
{
    internal const uint RotatingBlasterId = 47843;
    internal const uint FinalBlasterId = 47844;

    private static readonly IReadOnlyDictionary<uint, int> NumberByMarker = new Dictionary<uint, int>
    {
        [336] = 1,
        [337] = 2,
        [338] = 3,
        [339] = 4,
        [437] = 5,
        [438] = 6,
        [439] = 7,
        [440] = 8,
    };

    private static readonly IReadOnlyDictionary<int, string> SpotNames = new Dictionary<int, string>
    {
        [0] = "N (A)",
        [45] = "NE (2)",
        [90] = "E (B)",
        [135] = "SE (3)",
        [180] = "S (C)",
        [225] = "SW (4)",
        [270] = "W (D)",
        [315] = "NW (1)",
    };

    internal async Task<LimitCutAnalysis> AnalyzeAsync(
        FflogsReportSummary report,
        FflogsFight fight,
        CancellationToken cancellationToken)
    {
        var center = WtfDigAnalysisHelpers.DefaultCenter;
        var ttl = FflogsClient.EventsCacheTtl(report, fight);
        var players = WtfDigAnalysisHelpers.FightPlayers(report, fight);
        var empty = Empty(fight, center);
        var anchorEvents = await client.FetchAllEventsAsync(
            new FflogsEventQuery(
                report.Code,
                fight.Id,
                fight.StartTime,
                fight.EndTime,
                FflogsEventDataType.Casts,
                FflogsHostilityType.Enemies,
                AbilityId: RotatingBlasterId,
                CacheTtl: ttl),
            cancellationToken).ConfigureAwait(false);
        var anchor = anchorEvents.FirstOrDefault(entry => entry.Type == "cast");
        if (anchor is null)
        {
            return empty;
        }

        var blasterEvents = await client.FetchAllEventsAsync(
            new FflogsEventQuery(
                report.Code,
                fight.Id,
                anchor.Timestamp - 2_000,
                Math.Min(fight.EndTime, anchor.Timestamp + 35_000),
                FflogsEventDataType.Casts,
                FflogsHostilityType.Enemies,
                true,
                FilterExpression: $"ability.id in ({RotatingBlasterId}, {FinalBlasterId})",
                CacheTtl: ttl),
            cancellationToken).ConfigureAwait(false);
        var rotating = ToCasts(blasterEvents.Where(entry => entry.AbilityGameID == RotatingBlasterId), center);
        var finalBlasters = ToCasts(blasterEvents.Where(entry => entry.AbilityGameID == FinalBlasterId), center);
        if (rotating.Count == 0)
        {
            return empty;
        }

        var wallRadius = WtfDigAnalysisHelpers.Median(rotating
            .Concat(finalBlasters)
            .Select(cast => (double)cast.Position.Length())
            .Where(radius => radius > 8));
        if (wallRadius <= 0)
        {
            wallRadius = 20;
        }

        var startTime = rotating[0].Time;
        var differences = rotating.Skip(1).Select((cast, index) => cast.Time - rotating[index].Time).ToArray();
        var step = differences.Length > 0 ? WtfDigAnalysisHelpers.Median(differences) : 2000;
        // Timing indices can skip, so retain the original cast radius for filtering.
        var valid = rotating
            .Select(cast => new IndexedAngle(step > 0 ? (int)Math.Round((cast.Time - startTime) / step) : 0, cast.Angle, cast.Position.Length()))
            .Where(cast => cast.Radius > 8)
            .ToArray();
        if (valid.Length == 0)
        {
            return empty;
        }

        var (startAngle, rotation) = FitStartRotation(valid);
        var kefka = new LimitCutKefkaInfo(
            startAngle,
            SpotNames.GetValueOrDefault((int)startAngle, $"{startAngle:0.#} degrees"),
            rotation,
            rotating);
        var playerRotation = rotation == LimitCutRotation.Cw ? LimitCutRotation.Ccw : LimitCutRotation.Cw;
        var direction = playerRotation == LimitCutRotation.Cw ? 1 : -1;
        var playerStartAngle = Normalize(startAngle + 180 + direction * 22.5);
        var blasterSpots = Enumerable.Range(0, 8).Select(index => PositionAtAngle(index * 45, wallRadius)).ToArray();
        var gaps = Enumerable.Range(0, 8).Select(index =>
        {
            var angle = Normalize(playerStartAngle + index * 45 * direction);
            return new LimitCutTargetGap(index + 1, angle, PositionAtAngle(angle, wallRadius));
        }).ToArray();
        var finalBlastMs = finalBlasters.Count > 0
            ? WtfDigAnalysisHelpers.Median(finalBlasters.Select(cast => cast.Time))
            : (double?)null;
        var snapshotMs = finalBlasters.FirstOrDefault()?.Time;
        IReadOnlyList<LimitCutPlayer> cuts = [];
        if (snapshotMs is { } snapshot)
        {
            var windowStart = snapshot - 9_000;
            var windowEnd = snapshot + 3_000;
            var damageTask = client.FetchAllEventsAsync(
                new FflogsEventQuery(report.Code, fight.Id, windowStart, windowEnd, FflogsEventDataType.DamageTaken,
                    IncludeResources: true, CacheTtl: ttl), cancellationToken);
            var friendlyCastsTask = client.FetchAllEventsAsync(
                new FflogsEventQuery(report.Code, fight.Id, windowStart, windowEnd, FflogsEventDataType.Casts,
                    IncludeResources: true, CacheTtl: ttl), cancellationToken);
            var markersTask = client.FetchAllEventsAsync(
                new FflogsEventQuery(report.Code, fight.Id, rotating[0].Time - 2_000, windowEnd,
                    FilterExpression: "type='headmarker'", CacheTtl: ttl), cancellationToken);
            var deathsTask = client.FetchAllEventsAsync(
                new FflogsEventQuery(report.Code, fight.Id, rotating[0].Time - 2_000, windowEnd,
                    FflogsEventDataType.Deaths, CacheTtl: ttl), cancellationToken);
            await Task.WhenAll(damageTask, friendlyCastsTask, markersTask, deathsTask).ConfigureAwait(false);
            var damage = await damageTask.ConfigureAwait(false);
            var friendlyCasts = await friendlyCastsTask.ConfigureAwait(false);
            var markers = await markersTask.ConfigureAwait(false);
            var deaths = await deathsTask.ConfigureAwait(false);
            var numberByActor = new Dictionary<int, int>();
            foreach (var marker in markers)
            {
                if (marker.MarkerID is { } markerId &&
                    marker.SourceID is { } actorId &&
                    NumberByMarker.TryGetValue(markerId, out var number))
                {
                    numberByActor[actorId] = number;
                }
            }

            var samples = WtfDigAnalysisHelpers.BuildSampleMap(damage, friendlyCasts);
            var deadAt = WtfDigAnalysisHelpers.MakeDeadAt(samples, deaths);
            cuts = players.Select(player =>
                {
                    var number = numberByActor.TryGetValue(player.Id, out var value) ? value : (int?)null;
                    var position = WtfDigAnalysisHelpers.PositionAt(samples, player.Id, snapshot, center);
                    var expectedAngle = number is { } assigned
                        ? Normalize(playerStartAngle + (assigned - 1) * 45 * direction)
                        : (double?)null;
                    var angle = position is { } actual ? AngleOf(actual) : (double?)null;
                    return new LimitCutPlayer(
                        player.Id,
                        player.Name,
                        WtfDigAnalysisHelpers.JobInfo(player.SubType),
                        number,
                        deadAt(player.Id, snapshot),
                        position,
                        angle,
                        expectedAngle,
                        expectedAngle is { } target ? PositionAtAngle(target, wallRadius) : null,
                        angle is { } stood && expectedAngle is { } expected
                            ? Math.Abs(AngleDifference(stood, expected))
                            : null);
                })
                .OrderBy(player => player.Number ?? 99)
                .ToArray();
        }

        return new LimitCutAnalysis(
            fight,
            center,
            kefka,
            playerRotation,
            playerStartAngle,
            finalBlasters,
            blasterSpots,
            gaps,
            finalBlastMs is { } blast ? Math.Round((blast - fight.StartTime) / 1000) : null,
            cuts,
            wallRadius);
    }

    internal static double AngleOf(Vector2 position) => Normalize(Math.Atan2(position.X, -position.Y) * 180 / Math.PI);
    internal static Vector2 PositionAtAngle(double degrees, double radius)
    {
        var radians = degrees * Math.PI / 180;
        return new Vector2((float)(radius * Math.Sin(radians)), (float)(-radius * Math.Cos(radians)));
    }

    private static IReadOnlyList<LimitCutBlasterCast> ToCasts(IEnumerable<FflogsEvent> events, Vector2 center) => events
        .Where(entry => entry.Type == "cast" && entry.SourceResources is not null)
        .Select(entry =>
        {
            var position = WtfDigAnalysisHelpers.RawToArena(entry.SourceResources!.X, entry.SourceResources.Y, center);
            return new LimitCutBlasterCast(entry.Timestamp, position, AngleOf(position));
        })
        .OrderBy(cast => cast.Time)
        .ToArray();

    private static (double StartAngle, LimitCutRotation Rotation) FitStartRotation(IReadOnlyList<IndexedAngle> valid)
    {
        Fit? best = null;
        foreach (var direction in new[] { 1, -1 })
        {
            var counts = new Dictionary<double, int>();
            foreach (var entry in valid)
            {
                var start = Snap45(Normalize(entry.Angle - entry.Index * 45 * direction));
                counts[start] = counts.GetValueOrDefault(start) + 1;
            }

            var mode = counts.OrderByDescending(entry => entry.Value).First();
            if (best is null || mode.Value > best.Agreement)
            {
                best = new Fit(direction, mode.Key, mode.Value);
            }
        }

        return (best!.StartAngle, best.Direction == 1 ? LimitCutRotation.Cw : LimitCutRotation.Ccw);
    }

    private static LimitCutAnalysis Empty(FflogsFight fight, Vector2 center) =>
        new(fight, center, null, null, null, [], [], [], null, [], 20);
    private static double Normalize(double angle) => ((angle % 360) + 360) % 360;
    private static double Snap45(double angle) => Normalize(Math.Round(angle / 45) * 45);
    private static double AngleDifference(double first, double second)
    {
        var difference = Normalize(first - second);
        return difference > 180 ? difference - 360 : difference;
    }

    private sealed record IndexedAngle(int Index, double Angle, double Radius = 20);
    private sealed record Fit(int Direction, double StartAngle, int Agreement);
}
