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
    IReadOnlyList<BlackHoleBeamHit> Hits);
internal sealed record BlackHolePlayerState(
    int ActorId,
    NothingLevel Level,
    bool Crust,
    bool CleansedThisTether,
    Vector2? Position,
    int HitsThisTether,
    bool TetherThisTether,
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
                FilterExpression: DebuffFilter, CacheTtl: ttl), cancellationToken);
        var damageTask = client.FetchAllEventsAsync(
            new FflogsEventQuery(report.Code, fight.Id, resolutionStart, mechanicEnd, FflogsEventDataType.DamageTaken,
                AbilityId: NothingnessId, IncludeResources: true, CacheTtl: ttl), cancellationToken);
        var friendlyCastsTask = client.FetchAllEventsAsync(
            new FflogsEventQuery(report.Code, fight.Id, resolutionStart, mechanicEnd, FflogsEventDataType.Casts,
                IncludeResources: true, CacheTtl: ttl), cancellationToken);
        var tetherEventsTask = client.FetchAllEventsAsync(
            new FflogsEventQuery(report.Code, fight.Id, resolutionStart, mechanicEnd,
                FilterExpression: "type='tether'", CacheTtl: ttl), cancellationToken);
        var bossCastsTask = client.FetchAllEventsAsync(
            new FflogsEventQuery(report.Code, fight.Id, anchor.Timestamp - 8_000, mechanicEnd, FflogsEventDataType.Casts,
                FflogsHostilityType.Enemies, true,
                FilterExpression: "source.name in ('Kefka', 'Chaos', 'Exdeath')", CacheTtl: ttl), cancellationToken);
        var deathsTask = client.FetchAllEventsAsync(
            new FflogsEventQuery(report.Code, fight.Id, resolutionStart, mechanicEnd, FflogsEventDataType.Deaths,
                CacheTtl: ttl), cancellationToken);
        await Task.WhenAll(debuffsTask, damageTask, friendlyCastsTask, tetherEventsTask, bossCastsTask, deathsTask)
            .ConfigureAwait(false);
        var debuffs = (await debuffsTask.ConfigureAwait(false)).OrderBy(entry => entry.Timestamp).ToArray();
        var damage = await damageTask.ConfigureAwait(false);
        var friendlyCasts = await friendlyCastsTask.ConfigureAwait(false);
        var tetherEvents = await tetherEventsTask.ConfigureAwait(false);
        var bossCasts = await bossCastsTask.ConfigureAwait(false);
        var deaths = await deathsTask.ConfigureAwait(false);

        HashSet<int> BossIdsNamed(string name) => report.Actors
            .Where(actor => actor.SubType == "Boss" && actor.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
            .Select(actor => actor.Id)
            .ToHashSet();
        var bossKefkaIds = BossIdsNamed("kefka");
        var centerKefka = bossCasts
            .Where(entry =>
                entry.Type == "cast" &&
                entry.SourceResources is not null &&
                entry.SourceID is { } actorId &&
                bossKefkaIds.Contains(actorId))
            .Select(entry => new BossFacingSample(
                entry.Timestamp,
                entry.SourceResources!.Facing,
                Math.Sqrt(
                    Math.Pow(entry.SourceResources.X / 100 - center.X, 2) +
                    Math.Pow(entry.SourceResources.Y / 100 - center.Y, 2))))
            .Where(sample => sample.Distance <= 3)
            .OrderBy(sample => sample.Time)
            .ToArray();
        IReadOnlyList<BossPositionSample> BossSamples(HashSet<int> ids) => bossCasts
            .Where(entry => entry.SourceResources is not null && entry.SourceID is { } actorId && ids.Contains(actorId))
            .Select(entry => new BossPositionSample(
                entry.Timestamp,
                WtfDigAnalysisHelpers.RawToArena(entry.SourceResources!.X, entry.SourceResources.Y, center)))
            .OrderBy(sample => sample.Time)
            .ToArray();
        var chaosSamples = BossSamples(BossIdsNamed("chaos"));
        var exdeathSamples = BossSamples(BossIdsNamed("exdeath"));

        var tetherByInstance = new Dictionary<int, List<TetherHolderSample>>();
        foreach (var entry in tetherEvents.Where(entry => entry.TargetID is not null))
        {
            var instance = entry.SourceInstance ?? 0;
            if (!tetherByInstance.TryGetValue(instance, out var samples))
            {
                samples = [];
                tetherByInstance[instance] = samples;
            }

            samples.Add(new TetherHolderSample(entry.Timestamp, entry.TargetID!.Value));
        }

        foreach (var samples in tetherByInstance.Values)
        {
            samples.Sort((left, right) => left.Time.CompareTo(right.Time));
        }

        int? TetherHolderAt(int instance, double fireTime) => tetherByInstance.TryGetValue(instance, out var samples)
            ? samples.Where(sample => sample.Time <= fireTime + 300).Select(sample => (int?)sample.ActorId).LastOrDefault()
            : null;
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
        Vector2? PositionAt(int actorId, double time) => WtfDigAnalysisHelpers.PositionAt(positionSamples, actorId, time, center);
        var deadAt = WtfDigAnalysisHelpers.MakeDeadAt(positionSamples, deaths);
        var hits = damage
            .Where(entry => entry.Type == "calculateddamage" && entry.AbilityGameID == NothingnessId)
            .OrderBy(entry => entry.Timestamp)
            .ToArray();
        if (hits.Length == 0)
        {
            return new BlackHoleAnalysis(fight, center, playerInfos, []);
        }

        var rounds = new List<BlackHoleRound>();
        var setNumber = 1;
        var tetherNumber = 0;
        foreach (var hit in hits)
        {
            var last = rounds.LastOrDefault();
            if (last is null)
            {
                tetherNumber = 1;
                rounds.Add(new BlackHoleRound(hit.Timestamp, 1, 1, [hit]));
                continue;
            }

            var gap = hit.Timestamp - last.Events[^1].Timestamp;
            if (gap <= 2500)
            {
                last.Events.Add(hit);
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

            rounds.Add(new BlackHoleRound(hit.Timestamp, setNumber, tetherNumber, [hit]));
        }

        var setStarts = rounds.GroupBy(round => round.Set).ToDictionary(group => group.Key, group => group.First().Time);
        var setNumbers = setStarts.Keys.OrderBy(value => value).ToArray();
        var bigKefkaBySet = new Dictionary<int, Vector2>();
        for (var index = 0; index < setNumbers.Length; index++)
        {
            var set = setNumbers[index];
            var low = index == 0 ? anchor.Timestamp - 8_000 : setStarts[setNumbers[index - 1]];
            var high = setStarts[set];
            var facing = centerKefka.LastOrDefault(sample => sample.Time >= low && sample.Time < high);
            if (facing is null)
            {
                continue;
            }

            var heading = WtfDigAnalysisHelpers.FacingToRadians(facing.Facing);
            var vector = new Vector2((float)Math.Sin(heading), (float)Math.Cos(heading));
            bigKefkaBySet[set] = -vector * BigKefkaRadius;
        }

        var soakedSoFar = new Dictionary<int, int>();
        var previousDeathTime = double.NegativeInfinity;
        var tethers = new List<BlackHoleTether>();
        foreach (var round in rounds)
        {
            var beams = round.Events.GroupBy(entry => entry.SourceInstance ?? 0).Select(group =>
            {
                var entries = group.ToArray();
                var withResources = entries.FirstOrDefault(entry => entry.SourceResources is not null) ?? entries[0];
                var sourceResources = withResources.SourceResources;
                var origin = sourceResources is null
                    ? Vector2.Zero
                    : WtfDigAnalysisHelpers.RawToArena(sourceResources.X, sourceResources.Y, center);
                var beamHits = entries
                    .Where(entry => entry.TargetID is { } actorId && playerById.ContainsKey(actorId))
                    .Select(entry => new BlackHoleBeamHit(entry.TargetID!.Value, entry.Amount ?? 0, (entry.Amount ?? 0) >= 1_000_000))
                    .ToArray();
                var fireTime = entries[entries.Length / 2].Timestamp;
                var holder = TetherHolderAt(group.Key, fireTime) ?? (beamHits.Length == 1 ? beamHits[0].ActorId : null);
                var target = holder is { } holderId ? PositionAt(holderId, round.Time) : null;
                if (target is null)
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
                    : sourceResources is not null
                        ? WtfDigAnalysisHelpers.FacingToRadians(sourceResources.Facing)
                        : 0;
                return new BlackHoleBeam(group.Key, origin, facingRadians, holder, beamHits);
            }).ToArray();

            var snapshotTime = round.Time;
            var stateTime = round.Time + 1500;
            var deathTime = round.Time + 3000;
            var holders = beams.Where(beam => beam.TetherHolder is not null).Select(beam => beam.TetherHolder!.Value).ToHashSet();
            var states = players.Select(player =>
            {
                var hitsThisTether = beams.Sum(beam => beam.Hits.Count(hit => hit.ActorId == player.Id));
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
                bigKefkaBySet.GetValueOrDefault(round.Set),
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

    private sealed record BossFacingSample(double Time, double Facing, double Distance);
    private sealed record BossPositionSample(double Time, Vector2 Position);
    private sealed record TetherHolderSample(double Time, int ActorId);
    private sealed record BlackHoleRound(double Time, int Set, int Tether, List<FflogsEvent> Events);
}
