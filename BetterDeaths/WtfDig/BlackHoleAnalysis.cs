using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace BetterDeaths.WtfDig;

internal enum BlackHoleRoleType
{
    Dps,
    Support,
    Accretion,
}

internal enum NothingLevel
{
    None,
    Unbecoming,
    Meanest,
}

internal sealed record BlackHoleRole(int? Order, BlackHoleRoleType Type, bool Accretion, string Label);
internal sealed record BlackHolePlayerInfo(int ActorId, string Name, WtfDigJobInfo Job, BlackHoleRole Role);
internal sealed record BlackHoleBeamHit(int ActorId, long Amount, bool Lethal);
internal sealed record BlackHoleBeam(
    int Instance,
    Vector2 Origin,
    double FacingRadians,
    int? TetherHolder,
    IReadOnlyList<BlackHoleBeamHit> Hits,
    IReadOnlySet<int> ContactActors);
internal sealed record BlackHolePlayerState(
    int ActorId,
    NothingLevel Level,
    bool Crust,
    bool CleansedThisTether,
    Vector2? Position,
    int HitsThisTether,
    bool TetherThisTether,
    bool ClippedThisTether,
    int SoakCount,
    bool LethalThisTether,
    bool Dead,
    bool DiedThisTether);
internal sealed record BlackHoleTether(
    int Set,
    int Tether,
    string Label,
    double Time,
    IReadOnlyList<BlackHoleBeam> Beams,
    IReadOnlyList<BlackHolePlayerState> States,
    Vector2? BigKefka,
    Vector2? Chaos,
    Vector2? Exdeath);
internal sealed record BlackHoleAnalysis(
    FflogsFight Fight,
    Vector2 Center,
    IReadOnlyList<BlackHolePlayerInfo> Players,
    IReadOnlyList<BlackHoleTether> Tethers);

internal sealed class BlackHoleAnalyzer(IWtfDigEventSource client)
{
    internal const uint BlackHoleCastId = 47867;
    internal const uint NothingnessId = 47868;
    internal const float ArenaRadius = 20.0f;
    internal const float BigKefkaRadius = 25.0f;
    internal const float ChaosRadius = 6.0f;
    internal const float ExdeathRadius = 3.8f;

    private const uint Accretion = 1001604;
    private const uint FirstInLine = 1003004;
    private const uint SecondInLine = 1003005;
    private const uint ThirdInLine = 1003006;
    private const uint PrimordialCrust = 1005454;
    private const uint Unbecoming = 1005452;
    private const uint MeanestExistence = 1005453;
    private const string DebuffFilter = "ability.id in (1001604,1003004,1003005,1003006,1005454,1005452,1005453)";

    internal async Task<BlackHoleAnalysis> AnalyzeAsync(
        FflogsReportSummary report,
        FflogsFight fight,
        CancellationToken cancellationToken)
    {
        var center = WtfDigAnalysisHelpers.DefaultCenter;
        var ttl = FflogsClient.EventsCacheTtl(report, fight);
        var players = WtfDigAnalysisHelpers.FightPlayers(report, fight);
        var playerById = players.ToDictionary(player => player.Id);
        var anchorEvents = await client.FetchAllEventsAsync(
            new FflogsEventQuery(report.Code, fight.Id, fight.StartTime, fight.EndTime, FflogsEventDataType.Casts,
                FflogsHostilityType.Enemies, AbilityId: BlackHoleCastId, CacheTtl: ttl), cancellationToken)
            .ConfigureAwait(false);
        var anchor = anchorEvents.FirstOrDefault(entry => entry.Type == "cast") ?? anchorEvents.FirstOrDefault();
        if (anchor is null)
        {
            return new BlackHoleAnalysis(fight, center, [], []);
        }

        var mechanicEnd = Math.Min(fight.EndTime, anchor.Timestamp + 122_000);
        var assignmentStart = anchor.Timestamp - 70_000;
        var resolutionStart = anchor.Timestamp - 2_000;
        var debuffsTask = client.FetchAllEventsAsync(
            new FflogsEventQuery(report.Code, fight.Id, assignmentStart, mechanicEnd, FflogsEventDataType.Debuffs,
                IncludeResources: true, FilterExpression: DebuffFilter, CacheTtl: ttl), cancellationToken);
        var damageTask = client.FetchAllEventsAsync(
            new FflogsEventQuery(report.Code, fight.Id, resolutionStart, mechanicEnd, FflogsEventDataType.DamageTaken,
                AbilityId: NothingnessId, IncludeResources: true, CacheTtl: ttl), cancellationToken);
        var firingsTask = client.FetchAllEventsAsync(
            new FflogsEventQuery(report.Code, fight.Id, resolutionStart, mechanicEnd, FflogsEventDataType.Casts,
                FflogsHostilityType.Enemies, AbilityId: NothingnessId, IncludeResources: true, CacheTtl: ttl), cancellationToken);
        var friendlyCastsTask = client.FetchAllEventsAsync(
            new FflogsEventQuery(report.Code, fight.Id, resolutionStart, mechanicEnd, FflogsEventDataType.Casts,
                IncludeResources: true, CacheTtl: ttl), cancellationToken);
        var tetherEventsTask = client.FetchAllEventsAsync(
            new FflogsEventQuery(report.Code, fight.Id, resolutionStart, mechanicEnd,
                IncludeResources: true, FilterExpression: client is LocalPullEventSource ? "type='tether'" : "type='tether' or type='summon'",
                CacheTtl: ttl), cancellationToken);
        var bossCastsTask = client.FetchAllEventsAsync(
            new FflogsEventQuery(report.Code, fight.Id, anchor.Timestamp - 8_000, mechanicEnd, FflogsEventDataType.Casts,
                FflogsHostilityType.Enemies, true,
                FilterExpression: "source.name in ('Kefka', 'Chaos', 'Exdeath')", CacheTtl: ttl), cancellationToken);
        var deathsTask = client.FetchAllEventsAsync(
            new FflogsEventQuery(report.Code, fight.Id, resolutionStart, mechanicEnd, FflogsEventDataType.Deaths,
                CacheTtl: ttl), cancellationToken);
        await Task.WhenAll(debuffsTask, damageTask, firingsTask, friendlyCastsTask, tetherEventsTask, bossCastsTask, deathsTask)
            .ConfigureAwait(false);
        var debuffs = (await debuffsTask.ConfigureAwait(false)).OrderBy(entry => entry.Timestamp).ToArray();
        var damage = await damageTask.ConfigureAwait(false);
        var firings = await firingsTask.ConfigureAwait(false);
        var friendlyCasts = await friendlyCastsTask.ConfigureAwait(false);
        var tetherEvents = await tetherEventsTask.ConfigureAwait(false);
        var bossCasts = await bossCastsTask.ConfigureAwait(false);
        var deaths = await deathsTask.ConfigureAwait(false);

        HashSet<int> BossIdsNamed(string name) => report.Actors
            .Where(actor => actor.SubType == "Boss" && actor.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
            .Select(actor => actor.Id)
            .ToHashSet();
        var markerEvidence = client is LocalPullEventSource local ? local.BlackHoleMarkers :
            BlackHoleMarkerEvidence.FromReport(BossIdsNamed("kefka"), bossCasts, tetherEvents, center);
        IReadOnlyList<BossPositionSample> BossSamples(HashSet<int> ids) => bossCasts
            .Where(entry => entry.SourceResources is not null && entry.SourceID is { } actorId && ids.Contains(actorId))
            .Select(entry => new BossPositionSample(
                entry.Timestamp,
                WtfDigAnalysisHelpers.RawToArena(entry.SourceResources!.X, entry.SourceResources.Y, center)))
            .OrderBy(sample => sample.Time)
            .ToArray();
        var chaosSamples = BossSamples(BossIdsNamed("chaos"));
        var exdeathSamples = BossSamples(BossIdsNamed("exdeath"));

        var holderChanges = tetherEvents
            .Where(entry => entry.Type == "tether" && entry.SourceID.HasValue &&
                entry.TargetID is { } id && playerById.ContainsKey(id))
            .OrderBy(entry => entry.Timestamp)
            .ToLookup(entry => new BlackHoleActor(entry.SourceID!.Value, entry.SourceInstance));
        bool Has(int actorId, uint statusId) => debuffs.Any(entry =>
            entry.Type == "applydebuff" && entry.TargetID == actorId && entry.AbilityGameID == statusId);
        var playerInfos = players.Select(player =>
        {
            var accretion = Has(player.Id, Accretion);
            var order = Has(player.Id, FirstInLine) ? 1 : Has(player.Id, SecondInLine) ? 2 : Has(player.Id, ThirdInLine) ? 3 : (int?)null;
            var job = WtfDigAnalysisHelpers.JobInfo(player.SubType);
            var type = accretion ? BlackHoleRoleType.Accretion : job.Role == "dps" ? BlackHoleRoleType.Dps : BlackHoleRoleType.Support;
            var roleName = FormatRoleType(type);
            var label = order is { } value ? $"{Ordinal(value)} {roleName}" : roleName;
            return new BlackHolePlayerInfo(player.Id, player.Name, job, new BlackHoleRole(order, type, accretion, label));
        }).ToArray();

        var positionSamples = WtfDigAnalysisHelpers.BuildSampleMap(damage, friendlyCasts);
        var targetSamples = WtfDigAnalysisHelpers.BuildSampleMap(tetherEvents.Concat(debuffs), []);
        Vector2? PositionAt(int actorId, double time) => WtfDigAnalysisHelpers.PositionAt(positionSamples, actorId, time, center)
            ?? WtfDigAnalysisHelpers.PositionAt(targetSamples, actorId, time, center);
        var deadAt = WtfDigAnalysisHelpers.MakeDeadAt(positionSamples, deaths);
        var fires = new List<BlackHoleFire>();
        foreach (var firing in firings.Where(entry => entry.Type == "cast" && entry.AbilityGameID == NothingnessId)
                     .OrderBy(entry => entry.Timestamp))
        {
            if (!fires.Any(fire => SameSource(fire.Event, firing) &&
                    Math.Abs(fire.Time - firing.Timestamp) <= 1500))
            {
                fires.Add(new BlackHoleFire(firing, []));
            }
        }

        foreach (var hit in damage.Where(entry => !entry.IsEstimated && entry.Type == "calculateddamage" && entry.AbilityGameID == NothingnessId)
                     .OrderBy(entry => entry.Timestamp))
        {
            var fire = fires
                .Where(candidate => SameSource(candidate.Event, hit) && Math.Abs(candidate.Time - hit.Timestamp) <= 2500)
                .MinBy(candidate => Math.Abs(candidate.Time - hit.Timestamp));
            if (fire is null)
            {
                fire = new BlackHoleFire(hit, []);
                fires.Add(fire);
            }

            fire.Hits.Add(hit);
        }

        if (fires.Count == 0)
        {
            return new BlackHoleAnalysis(fight, center, playerInfos, []);
        }

        var rounds = new List<BlackHoleRound>();
        var setNumber = 1;
        var tetherNumber = 0;
        foreach (var fire in fires.OrderBy(entry => entry.Time))
        {
            var last = rounds.LastOrDefault();
            if (last is null)
            {
                tetherNumber = 1;
                rounds.Add(new BlackHoleRound(fire.Time, 1, 1, [fire]));
                continue;
            }

            var gap = fire.Time - last.Fires[^1].Time;
            if (gap <= 2500)
            {
                last.Fires.Add(fire);
                continue;
            }

            if (gap > 12_000)
            {
                setNumber++;
                tetherNumber = 1;
            }
            else
            {
                tetherNumber++;
            }

            rounds.Add(new BlackHoleRound(fire.Time, setNumber, tetherNumber, [fire]));
        }

        var bigKefkaBySet = new Dictionary<int, Vector2?>();
        var previousSetEnd = anchor.Timestamp - 8_000;
        foreach (var set in rounds.GroupBy(r => r.Set))
        {
            var first = set.First();
            var holes = first.Fires.Select(f => f.Event).Where(e => e.SourceID.HasValue)
                .Select(e => new BlackHoleActor(e.SourceID!.Value, e.SourceInstance));
            bigKefkaBySet[set.Key] = markerEvidence.AtFirstAppearance(holes, previousSetEnd, first.Time);
            previousSetEnd = set.Last().Fires.Max(f => f.Time);
        }

        var soakedSoFar = new Dictionary<int, int>();
        var previousDeathTime = double.NegativeInfinity;
        var tethers = new List<BlackHoleTether>();
        foreach (var round in rounds)
        {
            // The first two contacts apply different statuses. Either can identify a hit without HP damage.
            var contactStatuses = debuffs.Where(entry => entry.Type == "applydebuff" &&
                entry.AbilityGameID is Unbecoming or MeanestExistence &&
                entry.TargetID is { } id && playerById.ContainsKey(id) &&
                round.Fires.Any(fire => fire.InStatusWindow(entry))).ToArray();
            var contactsByFire = round.Fires.ToDictionary(fire => fire,
                fire => fire.Hits.Where(hit => hit.TargetID is { } id && playerById.ContainsKey(id))
                    .Select(hit => hit.TargetID!.Value).ToHashSet());
            foreach (var status in contactStatuses)
            {
                var matches = round.Fires.Where(fire => fire.InStatusWindow(status) && SameSource(fire.Event, status)).ToArray();
                if (matches.Length == 1)
                    contactsByFire[matches[0]].Add(status.TargetID!.Value);
            }

            var beams = round.Fires.Select(fire =>
            {
                var sourceResources = fire.Event.SourceResources ??
                    fire.Hits.FirstOrDefault(entry => entry.SourceResources is not null)?.SourceResources;
                var origin = sourceResources is null
                    ? Vector2.Zero
                    : WtfDigAnalysisHelpers.RawToArena(sourceResources.X, sourceResources.Y, center);
                var beamHits = fire.Hits
                    .Where(entry => entry.TargetID is { } actorId && playerById.ContainsKey(actorId))
                    .Select(entry => new BlackHoleBeamHit(entry.TargetID!.Value, entry.Amount ?? 0, (entry.Amount ?? 0) >= 1_000_000))
                    .ToArray();
                var instance = fire.Event.SourceInstance ?? fire.Event.SourceID ?? 0;
                var holder = fire.Event.SourceID is { } sourceId
                    ? holderChanges[new BlackHoleActor(sourceId, fire.Event.SourceInstance)]
                        .LastOrDefault(change => change.Timestamp <= fire.Time + 300)?.TargetID
                    : null;
                var target = holder is { } holderId ? PositionAt(holderId, round.Time) : null;
                if (target is null && sourceResources?.HasFacing != true)
                {
                    target = beamHits
                        .Select(hit => PositionAt(hit.ActorId, round.Time))
                        .Where(position => position is not null)
                        .Select(position => position!.Value)
                        .OrderByDescending(position => Vector2.Distance(position, origin))
                        .Select(position => (Vector2?)position)
                        .FirstOrDefault();
                }

                var facingRadians = target is { } targetPosition
                    ? Math.Atan2(targetPosition.X - origin.X, targetPosition.Y - origin.Y)
                    : sourceResources?.HasFacing == true
                        ? WtfDigAnalysisHelpers.FacingToRadians(sourceResources.Facing)
                        : 0;
                return new BlackHoleBeam(instance, origin, facingRadians, holder, beamHits, contactsByFire[fire]);
            }).ToArray();

            var snapshotTime = round.Time;
            var stateTime = round.Time + 1500;
            var deathTime = round.Time + 3000;
            var holders = beams.Where(beam => beam.TetherHolder is not null).Select(beam => beam.TetherHolder!.Value).ToHashSet();
            var states = players.Select(player =>
            {
                // A status with a missing/ambiguous orb still confirms contact for the round.
                var distinctLevels = contactStatuses.Where(entry => entry.TargetID == player.Id)
                    .Select(entry => entry.AbilityGameID).Distinct().Count();
                var hitsThisTether = Math.Max(distinctLevels, beams.Count(beam => beam.ContactActors.Contains(player.Id)));
                var clipped = beams.Any(beam => beam.TetherHolder is { } holder && holder != player.Id &&
                    beam.ContactActors.Contains(player.Id));
                var lethal = beams.Any(beam => beam.Hits.Any(hit => hit.ActorId == player.Id && hit.Lethal));
                var soakCount = soakedSoFar.GetValueOrDefault(player.Id) + hitsThisTether;
                soakedSoFar[player.Id] = soakCount;
                var meanest = ActiveAt(debuffs, MeanestExistence, player.Id, stateTime);
                var unbecoming = ActiveAt(debuffs, Unbecoming, player.Id, stateTime);
                var crustBefore = ActiveAt(debuffs, PrimordialCrust, player.Id, round.Time - 500, true);
                var crust = ActiveAt(debuffs, PrimordialCrust, player.Id, stateTime, true);
                var dead = deadAt(player.Id, deathTime);
                return new BlackHolePlayerState(
                    player.Id,
                    meanest ? NothingLevel.Meanest : unbecoming ? NothingLevel.Unbecoming : NothingLevel.None,
                    crust,
                    crustBefore && !crust,
                    PositionAt(player.Id, snapshotTime),
                    hitsThisTether,
                    holders.Contains(player.Id),
                    clipped,
                    soakCount,
                    lethal,
                    dead,
                    dead && !deadAt(player.Id, previousDeathTime));
            }).ToArray();
            previousDeathTime = deathTime;
            tethers.Add(new BlackHoleTether(
                round.Set,
                round.Tether,
                $"{Ordinal(round.Set)} Set, Tether {round.Tether}",
                Math.Round((round.Time - fight.StartTime) / 1000),
                beams,
                states,
                bigKefkaBySet[round.Set],
                BossPositionAt(chaosSamples, round.Time),
                BossPositionAt(exdeathSamples, round.Time)));
        }

        return new BlackHoleAnalysis(fight, center, playerInfos, tethers);
    }

    internal static int ExpectedSoaks(int set, int tether) => set == 1
        ? tether == 1 ? 1 : 2
        : set == 4
            ? tether == 1 ? 2 : 1
            : 3;

    internal static int RoleSortKey(BlackHoleRole role, bool supportFirst) =>
        (role.Order ?? 9) * 10 + role.Type switch
        {
            BlackHoleRoleType.Accretion => 2,
            BlackHoleRoleType.Support => supportFirst ? 0 : 1,
            _ => supportFirst ? 1 : 0,
        };

    private static bool ActiveAt(
        IEnumerable<FflogsEvent> events,
        uint statusId,
        int actorId,
        double time,
        bool startsActive = false)
    {
        var active = startsActive;
        foreach (var entry in events)
        {
            if (entry.Timestamp > time)
            {
                break;
            }

            if (entry.TargetID != actorId || entry.AbilityGameID != statusId)
            {
                continue;
            }

            if (entry.Type is "applydebuff" or "refreshdebuff")
            {
                active = true;
            }
            else if (entry.Type == "removedebuff")
            {
                active = false;
            }
        }

        return active;
    }

    private static Vector2? BossPositionAt(IEnumerable<BossPositionSample> samples, double time) => samples
        .OrderBy(sample => Math.Abs(sample.Time - time))
        .Select(sample => (Vector2?)sample.Position)
        .FirstOrDefault();

    private static bool SameSource(FflogsEvent left, FflogsEvent right)
    {
        if (left.SourceID is { } leftId && right.SourceID is { } rightId && leftId != rightId)
        {
            return false;
        }

        if (left.SourceInstance is { } leftInstance && right.SourceInstance is { } rightInstance)
        {
            return leftInstance == rightInstance;
        }

        return left.SourceID is not null && left.SourceID == right.SourceID;
    }

    private static string FormatRoleType(BlackHoleRoleType type) => type switch
    {
        BlackHoleRoleType.Dps => "DPS",
        BlackHoleRoleType.Support => "Support",
        _ => "Accretion",
    };
    private static string Ordinal(int value) => value switch
    {
        1 => "1st",
        2 => "2nd",
        3 => "3rd",
        _ => $"{value}th",
    };

    private sealed record BossPositionSample(double Time, Vector2 Position);
    private sealed record BlackHoleFire(FflogsEvent Event, List<FflogsEvent> Hits)
    {
        internal double Time => Event.Timestamp;
        internal bool InStatusWindow(FflogsEvent status) => status.Timestamp >= Time - 300 && status.Timestamp <= Time + 1500;
    }
    private sealed record BlackHoleRound(double Time, int Set, int Tether, List<BlackHoleFire> Fires);
}
