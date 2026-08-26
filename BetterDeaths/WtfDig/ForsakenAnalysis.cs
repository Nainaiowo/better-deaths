using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace BetterDeaths.WtfDig;

internal enum ForsakenEffect
{
    Raidwide,
    Stack,
    Spread,
    Cone,
    Clone,
    Cleave,
    Other,
}

internal enum ForsakenAssignment
{
    Stack,
    Spread,
    Cone,
}

internal sealed record ForsakenTakenEffect(ForsakenEffect Effect, long Damage, string? Label = null);

internal sealed class ForsakenPlayerSnapshot
{
    public required int ActorId { get; init; }
    public required string Name { get; init; }
    public required WtfDigJobInfo Job { get; init; }
    public Vector2? Position { get; init; }
    public double? Facing { get; init; }
    public long? Hp { get; init; }
    public long? MaxHp { get; init; }
    public ForsakenAssignment? Assignment { get; init; }
    public ForsakenAssignment? ReassignedTo { get; set; }
    public int? TroubleStacks { get; init; }
    public IReadOnlyList<ForsakenTakenEffect> Taken { get; init; } = [];
    public bool DoubleHit { get; init; }
    public bool Died { get; init; }
    public bool DiedThisSet { get; set; }
    public bool SoakedTower { get; init; }
    public Vector2? SoakPosition { get; init; }
}

internal sealed record ForsakenConeAoe(Vector2 Origin, double FacingRadians, int? BaitId);
internal sealed record ForsakenCircleAoe(ForsakenEffect Effect, Vector2 Position);
internal sealed record ForsakenActorPosition(int ActorId, Vector2 Position, WtfDigJobInfo Job);
internal sealed record ForsakenCloneDrop(
    Vector2 Position,
    int ActorId,
    WtfDigJobInfo Job,
    double SnapshotSeconds,
    double ResolveSeconds);
internal sealed record ForsakenCleaveBait(int ActorId, WtfDigJobInfo Job, Vector2 Position);
internal sealed record ForsakenCleave(
    Vector2 Origin,
    double FacingRadians,
    bool Frontal,
    bool Boss,
    ForsakenCleaveBait? Bait,
    double SnapshotSeconds,
    double ResolveSeconds);

internal sealed class ForsakenResolution
{
    public int Index { get; init; }
    public int SetNumber { get; init; }
    public bool IsRaidwide => SetNumber == 0;
    public string Parity => IsRaidwide ? string.Empty : SetNumber % 2 == 1 ? "odd" : "even";
    public string Label => IsRaidwide ? "Forsaken (raidwide)" : $"Tower set {SetNumber}";
    public double ResolveTime { get; init; }
    public double ResolveTimeExact { get; init; }
    public double SnapshotTime { get; init; }
    public IReadOnlyList<ForsakenPlayerSnapshot> Players { get; init; } = [];
    public IReadOnlyList<Vector2> Candidates { get; init; } = [];
    public IReadOnlyList<Vector2> ActiveTowers { get; set; } = [];
    public (int First, int Second)? Slots { get; set; }
    public bool SlotsFromModel { get; set; }
    public bool Mismatch { get; set; }
    public IReadOnlyList<ForsakenConeAoe> Cones { get; set; } = [];
    public IReadOnlyList<ForsakenCircleAoe> CircleAoes { get; set; } = [];
    public IReadOnlyList<ForsakenCloneDrop> Clones { get; set; } = [];
    public IReadOnlyList<ForsakenCleave> Cleaves { get; set; } = [];
    public IReadOnlyList<ForsakenActorPosition> CleaveSnapshot { get; set; } = [];
    public IReadOnlyList<ForsakenActorPosition> CloneSnapshot { get; set; } = [];
    public IReadOnlyDictionary<ForsakenEffect, int> Counts { get; init; } = new Dictionary<ForsakenEffect, int>();
}

internal sealed record ForsakenRotation(string Direction, (int First, int Second) FirstSlots, int Matches);

internal sealed record ForsakenAnalysis(
    FflogsFight Fight,
    IReadOnlyList<ForsakenResolution> Resolutions,
    Vector2 Center,
    ForsakenRotation? Rotation);

internal sealed class ForsakenAnalyzer(IWtfDigEventSource client)
{
    internal const uint ForsakenCastGameId = 47804;
    internal const float TowerRadius = 8.0f;
    internal const float TowerAoeRadius = 4.0f;
    internal const float CloneAoeRadius = 5.0f;
    internal const float SoakAoeRadius = 5.0f;
    internal const double ConeHalfRadians = Math.PI / 4;

    private const int TakenWindowMs = 3800;
    private const int DeathAfterMs = 5000;
    private const string CloneCleaveFilter =
        "ability.id in (47826, 47827, 47830, 47831, 47832, 47833, 47836, 47837)";

    private static readonly IReadOnlyDictionary<string, ForsakenEffect> CoreEffects =
        new Dictionary<string, ForsakenEffect>(StringComparer.OrdinalIgnoreCase)
        {
            ["forsaken"] = ForsakenEffect.Raidwide,
            ["spelldriver"] = ForsakenEffect.Stack,
            ["spellscatter"] = ForsakenEffect.Spread,
            ["spellwave"] = ForsakenEffect.Cone,
        };

    private static readonly IReadOnlyDictionary<string, ForsakenEffect> DamageEffects =
        new Dictionary<string, ForsakenEffect>(StringComparer.OrdinalIgnoreCase)
        {
            ["forsaken"] = ForsakenEffect.Raidwide,
            ["spelldriver"] = ForsakenEffect.Stack,
            ["spellscatter"] = ForsakenEffect.Spread,
            ["spellwave"] = ForsakenEffect.Cone,
            ["future's end"] = ForsakenEffect.Clone,
            ["past's end"] = ForsakenEffect.Clone,
            ["all things ending"] = ForsakenEffect.Cleave,
        };

    private static readonly IReadOnlyDictionary<uint, ForsakenAssignment> AssignmentStatuses =
        new Dictionary<uint, ForsakenAssignment>
        {
            [1005084] = ForsakenAssignment.Stack,
            [1005085] = ForsakenAssignment.Spread,
            [1005086] = ForsakenAssignment.Cone,
        };

    internal async Task<ForsakenAnalysis> AnalyzeAsync(
        FflogsReportSummary report,
        FflogsFight fight,
        CancellationToken cancellationToken)
    {
        var center = WtfDigAnalysisHelpers.DefaultCenter;
        var slots = TowerSlots();
        var abilityNames = report.Abilities.ToDictionary(ability => ability.GameID, ability => ability.Name);
        string NameOf(uint? id) => id is { } value && abilityNames.TryGetValue(value, out var name)
            ? name.ToLowerInvariant()
            : string.Empty;
        var ttl = FflogsClient.EventsCacheTtl(report, fight);
        var players = WtfDigAnalysisHelpers.FightPlayers(report, fight);

        var anchorCasts = await client.FetchAllEventsAsync(
            new FflogsEventQuery(
                report.Code,
                fight.Id,
                fight.StartTime,
                fight.EndTime,
                FflogsEventDataType.Casts,
                FflogsHostilityType.Enemies,
                AbilityId: ForsakenCastGameId,
                CacheTtl: ttl),
            cancellationToken).ConfigureAwait(false);
        var anchor = anchorCasts.FirstOrDefault(entry => entry.Type == "cast");
        if (anchor is null)
        {
            return new ForsakenAnalysis(fight, [], center, null);
        }

        var windowStart = anchor.Timestamp - 4_000;
        var windowEnd = Math.Min(fight.EndTime, anchor.Timestamp + 130_000);
        var enemyCastsTask = client.FetchAllEventsAsync(
            new FflogsEventQuery(report.Code, fight.Id, windowStart, windowEnd, FflogsEventDataType.Casts,
                FflogsHostilityType.Enemies, true, FilterExpression: CloneCleaveFilter, CacheTtl: ttl), cancellationToken);
        var damageTask = client.FetchAllEventsAsync(
            new FflogsEventQuery(report.Code, fight.Id, windowStart, windowEnd, FflogsEventDataType.DamageTaken,
                FflogsHostilityType.Friendlies, true, CacheTtl: ttl), cancellationToken);
        var debuffsTask = client.FetchAllEventsAsync(
            new FflogsEventQuery(report.Code, fight.Id, windowStart, windowEnd, FflogsEventDataType.Debuffs,
                CacheTtl: ttl), cancellationToken);
        var deathsTask = client.FetchAllEventsAsync(
            new FflogsEventQuery(report.Code, fight.Id, windowStart, windowEnd, FflogsEventDataType.Deaths,
                CacheTtl: ttl), cancellationToken);
        var friendlyCastsTask = client.FetchAllEventsAsync(
            new FflogsEventQuery(report.Code, fight.Id, windowStart, windowEnd, FflogsEventDataType.Casts,
                FflogsHostilityType.Friendlies, true, CacheTtl: ttl), cancellationToken);
        await Task.WhenAll(enemyCastsTask, damageTask, debuffsTask, deathsTask, friendlyCastsTask).ConfigureAwait(false);

        var enemyCasts = await enemyCastsTask.ConfigureAwait(false);
        var damage = await damageTask.ConfigureAwait(false);
        var debuffs = (await debuffsTask.ConfigureAwait(false)).OrderBy(entry => entry.Timestamp).ToArray();
        var deaths = await deathsTask.ConfigureAwait(false);
        var friendlyCasts = await friendlyCastsTask.ConfigureAwait(false);

        var applies = new List<AssignmentChange>();
        var removes = new List<(int ActorId, double Time)>();
        foreach (var entry in debuffs)
        {
            if (entry.AbilityGameID is not { } statusId ||
                entry.TargetID is not { } actorId ||
                !AssignmentStatuses.TryGetValue(statusId, out var assignment))
            {
                continue;
            }

            if (entry.Type == "applydebuff")
            {
                applies.Add(new AssignmentChange(actorId, assignment, entry.Timestamp));
            }
            else if (entry.Type == "removedebuff")
            {
                removes.Add((actorId, entry.Timestamp));
            }
        }

        ForsakenAssignment? AssignmentAt(int actorId, double time) => applies
            .Where(entry => entry.ActorId == actorId && entry.Time <= time)
            .Select(entry => (ForsakenAssignment?)entry.Assignment)
            .LastOrDefault();
        ForsakenAssignment? ReassignIn(int actorId, double start, double end) => applies
            .Where(entry => entry.ActorId == actorId && entry.Time > start && entry.Time <= end)
            .Select(entry => (ForsakenAssignment?)entry.Assignment)
            .LastOrDefault();
        bool RemovedIn(int actorId, double start, double end) =>
            removes.Any(entry => entry.ActorId == actorId && entry.Time > start && entry.Time <= end);

        var mechanicDamage = damage
            .Where(entry => entry.Type == "damage" && CoreEffects.ContainsKey(NameOf(entry.AbilityGameID)))
            .OrderBy(entry => entry.Timestamp)
            .ToArray();
        var takenCalculations = damage
            .Where(entry => entry.Type == "calculateddamage" && !IsAuto(NameOf(entry.AbilityGameID)))
            .ToArray();
        var realDamage = damage
            .Where(entry => entry.Type == "damage" && !IsAuto(NameOf(entry.AbilityGameID)))
            .ToArray();
        var clusters = GroupByGap(mechanicDamage, 3_000);
        var samples = WtfDigAnalysisHelpers.BuildSampleMap(damage, friendlyCasts);
        var deadAt = WtfDigAnalysisHelpers.MakeDeadAt(samples, deaths);

        var setNumber = 0;
        var allResolutions = new List<ForsakenResolution>();
        foreach (var (cluster, index) in clusters.Select((value, index) => (value, index)))
        {
            var resolveMs = WtfDigAnalysisHelpers.Median(cluster.Select(entry => entry.Timestamp));
            var raidwide = cluster.All(entry => CoreEffects[NameOf(entry.AbilityGameID)] == ForsakenEffect.Raidwide);
            if (!raidwide)
            {
                setNumber++;
            }

            var counts = Enum.GetValues<ForsakenEffect>().ToDictionary(effect => effect, _ => 0);
            var slotSoakers = new Dictionary<int, int>();
            bool InWindow(FflogsEvent entry) => Math.Abs(entry.Timestamp - resolveMs) <= TakenWindowMs;
            var takenEvents = takenCalculations.Where(InWindow).ToArray();
            var damageEvents = realDamage.Where(InWindow).ToArray();
            var snapshotTimes = damage
                .Where(entry =>
                    entry.Type == "calculateddamage" &&
                    CoreEffects.ContainsKey(NameOf(entry.AbilityGameID)) &&
                    InWindow(entry))
                .Select(entry => entry.Timestamp)
                .ToArray();
            var snapshotMs = snapshotTimes.Length > 0
                ? WtfDigAnalysisHelpers.Median(snapshotTimes)
                : resolveMs;

            var snapshots = new List<ForsakenPlayerSnapshot>();
            foreach (var player in players)
            {
                var taken = new List<ForsakenTakenEffect>();
                var soakedTower = false;
                Vector2? soakPosition = null;
                foreach (var entry in takenEvents.Where(entry => entry.TargetID == player.Id))
                {
                    var displayName = entry.AbilityGameID is { } id && abilityNames.TryGetValue(id, out var found)
                        ? found
                        : string.Empty;
                    if (displayName == "The Path of Light")
                    {
                        soakedTower = true;
                        if (entry.TargetResources is { } resources)
                        {
                            soakPosition = WtfDigAnalysisHelpers.RawToArena(resources.X, resources.Y, center);
                        }

                        continue;
                    }

                    var landed = damageEvents.FirstOrDefault(candidate =>
                        candidate.TargetID == player.Id &&
                        candidate.AbilityGameID == entry.AbilityGameID &&
                        Math.Abs(candidate.Timestamp - entry.Timestamp) <= 1500) ?? entry;
                    var amount = landed.UnmitigatedAmount ?? landed.Amount ?? 0;
                    if (amount <= 0)
                    {
                        continue;
                    }

                    taken.Add(DamageEffects.TryGetValue(displayName, out var effect)
                        ? new ForsakenTakenEffect(effect, amount)
                        : new ForsakenTakenEffect(
                            ForsakenEffect.Other,
                            amount,
                            displayName == "The River of Light" ? "Tower" : displayName));
                }

                foreach (var hit in taken)
                {
                    counts[hit.Effect]++;
                }

                var resourcesAtSnapshot = WtfDigAnalysisHelpers.SampleAt(samples, player.Id, snapshotMs);
                Vector2? position = resourcesAtSnapshot is null
                    ? null
                    : WtfDigAnalysisHelpers.RawToArena(resourcesAtSnapshot.X, resourcesAtSnapshot.Y, center);
                var towerPosition = soakPosition ?? position;
                if (towerPosition is { } towerPoint && soakedTower)
                {
                    var nearest = slots
                        .Select((slot, slotIndex) => (slotIndex, Distance: Vector2.Distance(towerPoint, slot)))
                        .OrderBy(candidate => candidate.Distance)
                        .First();
                    if (nearest.Distance <= TowerAoeRadius)
                    {
                        slotSoakers[nearest.slotIndex] = slotSoakers.GetValueOrDefault(nearest.slotIndex) + 1;
                    }
                }

                snapshots.Add(new ForsakenPlayerSnapshot
                {
                    ActorId = player.Id,
                    Name = player.Name,
                    Job = WtfDigAnalysisHelpers.JobInfo(player.SubType),
                    Position = position,
                    Facing = resourcesAtSnapshot?.Facing,
                    Hp = resourcesAtSnapshot?.HitPoints,
                    MaxHp = resourcesAtSnapshot?.MaxHitPoints,
                    Assignment = AssignmentAt(player.Id, raidwide ? resolveMs + 1500 : resolveMs - 2500),
                    ReassignedTo = raidwide ? null : ReassignIn(player.Id, resolveMs - 2000, resolveMs + 3000),
                    TroubleStacks = TroubleStacksAt(debuffs, player.Id, resolveMs + 200),
                    Taken = taken,
                    DoubleHit = taken.Count(hit => IsCoreDamage(hit.Effect)) >= 2,
                    Died = deadAt(player.Id, resolveMs + DeathAfterMs),
                    SoakedTower = soakedTower,
                    SoakPosition = soakPosition,
                });
            }

            var detected = BestSlotPair(slotSoakers);
            allResolutions.Add(new ForsakenResolution
            {
                Index = index,
                SetNumber = raidwide ? 0 : setNumber,
                ResolveTime = Math.Round((resolveMs - fight.StartTime) / 1000),
                ResolveTimeExact = (resolveMs - fight.StartTime) / 1000,
                SnapshotTime = (snapshotMs - fight.StartTime) / 1000,
                Players = snapshots,
                Candidates = slots,
                Slots = detected,
                ActiveTowers = detected is { } pair ? [slots[pair.First], slots[pair.Second]] : [],
                Counts = counts,
            });
        }

        var rotation = FitRotation(allResolutions.Where(result => result.SetNumber > 0).ToArray());
        if (rotation is not null)
        {
            var direction = rotation.Direction == "cw" ? 1 : -1;
            foreach (var resolution in allResolutions.Where(result => result.SetNumber > 0))
            {
                var first = Mod8(rotation.FirstSlots.First + (resolution.SetNumber - 1) * direction);
                var pair = (first, Mod8(first + 2));
                resolution.Slots = pair;
                resolution.SlotsFromModel = true;
                resolution.ActiveTowers = [slots[pair.first], slots[pair.Item2]];
            }
        }

        var resolutions = allResolutions.Where(result => result.SetNumber > 0).ToArray();
        var previousEnd = double.NegativeInfinity;
        foreach (var resolution in resolutions)
        {
            var end = fight.StartTime + resolution.ResolveTime * 1000 + DeathAfterMs;
            foreach (var player in resolution.Players)
            {
                player.DiedThisSet = deadAt(player.ActorId, end) && !deadAt(player.ActorId, previousEnd);
            }

            previousEnd = end;
        }

        bool SoakedAt(ForsakenPlayerSnapshot player, Vector2 tower) =>
            player.SoakedTower &&
            (player.SoakPosition ?? player.Position) is { } at &&
            Vector2.Distance(at, tower) <= TowerAoeRadius;

        foreach (var resolution in resolutions.Where(result => result.ActiveTowers.Count > 0))
        {
            resolution.Mismatch = resolution.ActiveTowers.Any(tower =>
                resolution.Players.Count(player => SoakedAt(player, tower)) != 2);
            var absoluteMs = fight.StartTime + resolution.ResolveTime * 1000;
            foreach (var player in resolution.Players)
            {
                if (player.ReassignedTo is not null || player.Assignment is null || !player.SoakedTower)
                {
                    continue;
                }

                if (!RemovedIn(player.ActorId, absoluteMs - 2000, absoluteMs + 3000))
                {
                    player.ReassignedTo = player.Assignment;
                }
            }
        }

        foreach (var resolution in resolutions)
        {
            var absoluteMs = fight.StartTime + resolution.ResolveTime * 1000;
            var coneVictimIds = ConeVictimIdsAt(damage, absoluteMs, abilityNames);
            var coneVictims = resolution.Players
                .Where(player => player.Position is not null && coneVictimIds.Contains(player.ActorId))
                .ToArray();
            var suppressed = new HashSet<int>();
            foreach (var tower in resolution.ActiveTowers)
            {
                var soakers = resolution.Players.Where(player => SoakedAt(player, tower)).ToArray();
                if (soakers.Length <= 2)
                {
                    continue;
                }

                var activated = soakers
                    .Where(player =>
                        player.Assignment is not null &&
                        player.Assignment != ForsakenAssignment.Cone &&
                        player.Taken.Any(hit => hit.Effect == AssignmentEffect(player.Assignment.Value)))
                    .Select(player => player.ActorId)
                    .ToHashSet();
                var coneSoakers = soakers
                    .Where(player => player.Assignment == ForsakenAssignment.Cone)
                    .OrderBy(player => coneVictimIds.Contains(player.ActorId) ? 1 : 0)
                    .ThenBy(player => coneVictims
                        .Where(victim => victim.ActorId != player.ActorId)
                        .Select(victim => player.Position is { } position
                            ? Vector2.Distance(position, victim.Position!.Value)
                            : float.PositiveInfinity)
                        .DefaultIfEmpty(float.PositiveInfinity)
                        .Min())
                    .ToArray();
                foreach (var player in coneSoakers)
                {
                    if (activated.Count >= 2)
                    {
                        break;
                    }

                    activated.Add(player.ActorId);
                }

                foreach (var player in soakers.Where(player => !activated.Contains(player.ActorId)))
                {
                    suppressed.Add(player.ActorId);
                }
            }

            resolution.Cones = resolution.Players
                .Where(player =>
                    player.Position is not null &&
                    player.Assignment == ForsakenAssignment.Cone &&
                    player.SoakedTower &&
                    (player.Hp ?? 0) > 0 &&
                    !suppressed.Contains(player.ActorId))
                .Select(player =>
                {
                    var bait = coneVictims
                        .Where(victim => victim.ActorId != player.ActorId)
                        .OrderBy(victim => Vector2.Distance(player.Position!.Value, victim.Position!.Value))
                        .FirstOrDefault();
                    var facing = bait is null
                        ? 0
                        : Math.Atan2(
                            bait.Position!.Value.X - player.Position!.Value.X,
                            bait.Position.Value.Y - player.Position.Value.Y);
                    return new ForsakenConeAoe(player.Position!.Value, facing, bait?.ActorId);
                })
                .ToArray();
            resolution.CircleAoes = resolution.Players
                .Where(player =>
                    player.Position is not null &&
                    player.Assignment is ForsakenAssignment.Stack or ForsakenAssignment.Spread &&
                    player.SoakedTower &&
                    (player.Hp ?? 0) > 0 &&
                    !suppressed.Contains(player.ActorId))
                .Select(player => new ForsakenCircleAoe(AssignmentEffect(player.Assignment!.Value), player.Position!.Value))
                .ToArray();
        }

        AddCloneAndCleaveData(report, fight, center, abilityNames, players, samples, deadAt, enemyCasts, damage, resolutions);
        return new ForsakenAnalysis(fight, resolutions, center, rotation);
    }

    internal static IReadOnlySet<int> ConeVictimIdsAt(
        IEnumerable<FflogsEvent> damage,
        double resolveMs,
        IReadOnlyDictionary<uint, string> abilityNames)
    {
        return damage
            .Where(entry =>
                entry.Type == "calculateddamage" &&
                Math.Abs(entry.Timestamp - resolveMs) <= TakenWindowMs &&
                entry.AbilityGameID is { } abilityId &&
                abilityNames.TryGetValue(abilityId, out var abilityName) &&
                string.Equals(abilityName, "Spellwave", StringComparison.OrdinalIgnoreCase) &&
                entry.TargetID is not null)
            .Select(entry => entry.TargetID!.Value)
            .ToHashSet();
    }

    private static void AddCloneAndCleaveData(
        FflogsReportSummary report,
        FflogsFight fight,
        Vector2 center,
        IReadOnlyDictionary<uint, string> abilityNames,
        IReadOnlyList<FflogsActor> players,
        IReadOnlyDictionary<int, List<WtfDigResourceSample>> samples,
        Func<int, double, bool> deadAt,
        IReadOnlyList<FflogsEvent> enemyCasts,
        IReadOnlyList<FflogsEvent> damage,
        IReadOnlyList<ForsakenResolution> resolutions)
    {
        string NameOf(uint? id) => id is { } value && abilityNames.TryGetValue(value, out var name)
            ? name.ToLowerInvariant()
            : string.Empty;
        var futurePastCasts = enemyCasts
            .Where(entry => entry.Type == "cast" && IsFutureOrPast(NameOf(entry.AbilityGameID)))
            .OrderBy(entry => entry.Timestamp)
            .ToArray();
        bool FrontalAt(double time)
        {
            var frontal = true;
            foreach (var cast in futurePastCasts.Where(entry => entry.Timestamp <= time))
            {
                frontal = NameOf(cast.AbilityGameID).StartsWith("future", StringComparison.Ordinal);
            }

            return frontal;
        }

        var bossIds = report.Actors.Where(actor => actor.SubType == "Boss").Select(actor => actor.Id).ToHashSet();
        var playerIds = players.Select(player => player.Id).ToHashSet();
        var dropCalculations = damage
            .Where(entry =>
                entry.Type == "calculateddamage" &&
                entry.TargetResources is not null &&
                IsFutureOrPast(NameOf(entry.AbilityGameID)))
            .ToArray();
        var dropCasts = futurePastCasts.Where(entry => playerIds.Contains(entry.TargetID ?? -1)).ToArray();
        var appliedDrops = damage
            .Where(entry => entry.Type == "damage" && IsFutureOrPast(NameOf(entry.AbilityGameID)))
            .OrderBy(entry => entry.Timestamp)
            .ToArray();
        string SourceKey(FflogsEvent entry) => $"{entry.SourceID ?? -1}:{entry.SourceInstance ?? 0}";
        Vector2? DropPositionAt(int actorId, double time)
        {
            var hit = dropCalculations.FirstOrDefault(entry =>
                entry.TargetID == actorId && Math.Abs(entry.Timestamp - time) <= TakenWindowMs);
            return hit?.TargetResources is { } resource
                ? WtfDigAnalysisHelpers.RawToArena(resource.X, resource.Y, center)
                : WtfDigAnalysisHelpers.PositionAt(samples, actorId, time, center);
        }

        foreach (var resolution in resolutions)
        {
            var resolveMs = fight.StartTime + resolution.ResolveTime * 1000;
            var setCasts = dropCasts.Where(entry => Math.Abs(entry.Timestamp - resolveMs) <= TakenWindowMs).ToArray();
            if (setCasts.Length == 0)
            {
                continue;
            }

            var snapshotMs = WtfDigAnalysisHelpers.Median(setCasts.Select(entry => entry.Timestamp));
            resolution.Clones = setCasts.Select(cast =>
                {
                    var player = resolution.Players.FirstOrDefault(entry => entry.ActorId == cast.TargetID);
                    var position = player is null ? null : DropPositionAt(player.ActorId, cast.Timestamp);
                    if (player is null || position is null)
                    {
                        return null;
                    }

                    var applied = appliedDrops.FirstOrDefault(entry =>
                        entry.TargetID == cast.TargetID &&
                        entry.Timestamp >= cast.Timestamp &&
                        entry.Timestamp - cast.Timestamp <= 2500);
                    return new ForsakenCloneDrop(
                        position.Value,
                        player.ActorId,
                        player.Job,
                        (cast.Timestamp - fight.StartTime) / 1000,
                        ((applied?.Timestamp ?? cast.Timestamp + 600) - fight.StartTime) / 1000);
                })
                .Where(entry => entry is not null)
                .Cast<ForsakenCloneDrop>()
                .ToArray();
            resolution.CloneSnapshot = resolution.Players
                .Where(player => !deadAt(player.ActorId, snapshotMs))
                .Select(player =>
                {
                    var position = DropPositionAt(player.ActorId, snapshotMs);
                    return position is { } value ? new ForsakenActorPosition(player.ActorId, value, player.Job) : null;
                })
                .Where(entry => entry is not null)
                .Cast<ForsakenActorPosition>()
                .ToArray();
        }

        var beginTimes = enemyCasts
            .Where(entry => entry.Type == "begincast" && NameOf(entry.AbilityGameID) == "all things ending")
            .Select(entry => entry.Timestamp)
            .OrderBy(time => time)
            .ToArray();
        var cleaveCasts = enemyCasts
            .Where(entry =>
                entry.Type == "cast" &&
                NameOf(entry.AbilityGameID) == "all things ending" &&
                entry.SourceResources is not null)
            .OrderBy(entry => entry.Timestamp)
            .ToArray();
        var appliedCleaves = damage
            .Where(entry => entry.Type == "damage" && NameOf(entry.AbilityGameID) == "all things ending")
            .Select(entry => entry.Timestamp)
            .OrderBy(time => time)
            .ToArray();
        var clusters = new List<CleaveCluster>();
        foreach (var cast in cleaveCasts)
        {
            var resources = cast.SourceResources!;
            var source = new CleaveSource(
                WtfDigAnalysisHelpers.RawToArena(resources.X, resources.Y, center),
                WtfDigAnalysisHelpers.FacingToRadians(resources.Facing),
                bossIds.Contains(cast.SourceID ?? -1),
                SourceKey(cast),
                cast.Timestamp);
            var last = clusters.LastOrDefault();
            if (last is not null && cast.Timestamp - last.Time <= 2500)
            {
                last.Sources.Add(source);
            }
            else
            {
                clusters.Add(new CleaveCluster(cast.Timestamp, [source]));
            }
        }

        double SnapshotTimeFor(double castTime) => beginTimes
            .Where(time => time <= castTime && castTime - time <= 7000)
            .DefaultIfEmpty(castTime)
            .Last();
        ForsakenCleaveBait? BaitFor(CleaveSource source)
        {
            var drop = dropCasts
                .Where(entry =>
                    SourceKey(entry) == source.Key &&
                    entry.Timestamp < source.Time &&
                    source.Time - entry.Timestamp <= 15_000)
                .LastOrDefault();
            if (drop?.TargetID is not { } targetId)
            {
                return null;
            }

            var player = players.FirstOrDefault(entry => entry.Id == targetId);
            var position = player is null ? null : DropPositionAt(player.Id, drop.Timestamp);
            return player is not null && position is { } value
                ? new ForsakenCleaveBait(player.Id, WtfDigAnalysisHelpers.JobInfo(player.SubType), value)
                : null;
        }

        foreach (var cluster in clusters)
        {
            var resolution = resolutions.FirstOrDefault(result =>
                result.SetNumber % 2 == 1 &&
                Math.Abs(fight.StartTime + result.ResolveTime * 1000 - cluster.Time) <= 5000);
            if (resolution is null)
            {
                continue;
            }

            var snapshotTime = SnapshotTimeFor(cluster.Time);
            resolution.CleaveSnapshot = resolution.Players
                .Where(player => !deadAt(player.ActorId, snapshotTime))
                .Select(player =>
                {
                    var position = WtfDigAnalysisHelpers.PositionAt(samples, player.ActorId, snapshotTime, center);
                    return position is { } value ? new ForsakenActorPosition(player.ActorId, value, player.Job) : null;
                })
                .Where(entry => entry is not null)
                .Cast<ForsakenActorPosition>()
                .ToArray();
            var applied = appliedCleaves.Where(time => time >= cluster.Time && time - cluster.Time <= 2500).ToArray();
            var resolveMs = applied.Length > 0 ? WtfDigAnalysisHelpers.Median(applied) : cluster.Time + 600;
            resolution.Cleaves = cluster.Sources
                .Select(source => new ForsakenCleave(
                    source.Position,
                    source.FacingRadians,
                    FrontalAt(cluster.Time),
                    source.Boss,
                    BaitFor(source),
                    (source.Time - fight.StartTime) / 1000,
                    (resolveMs - fight.StartTime) / 1000))
                .ToArray();
        }
    }

    private static int? TroubleStacksAt(IEnumerable<FflogsEvent> events, int actorId, double time)
    {
        int? stacks = null;
        foreach (var entry in events)
        {
            if (entry.Timestamp > time)
            {
                break;
            }

            if (entry.TargetID != actorId || entry.AbilityGameID != 1005083)
            {
                continue;
            }

            if (entry.Type == "removedebuff")
            {
                stacks = 0;
            }
            else if (entry.Stack is { } value)
            {
                stacks = value;
            }
            else if (entry.Type == "applydebuff" && stacks is null)
            {
                stacks = 4;
            }
        }

        return stacks;
    }

    private static IReadOnlyList<List<FflogsEvent>> GroupByGap(IEnumerable<FflogsEvent> entries, double gapMs)
    {
        var groups = new List<List<FflogsEvent>>();
        foreach (var entry in entries.OrderBy(value => value.Timestamp))
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

    private static Vector2[] TowerSlots() => Enumerable.Range(0, 8)
        .Select(index =>
        {
            var radians = index * 45 * Math.PI / 180;
            return new Vector2((float)(TowerRadius * Math.Sin(radians)), (float)(-TowerRadius * Math.Cos(radians)));
        })
        .ToArray();

    private static (int First, int Second)? BestSlotPair(IReadOnlyDictionary<int, int> counts)
    {
        (int First, int Second)? best = null;
        var score = 0;
        for (var index = 0; index < 8; index++)
        {
            var candidate = counts.GetValueOrDefault(index) + counts.GetValueOrDefault(Mod8(index + 2));
            if (candidate > score)
            {
                score = candidate;
                best = (index, Mod8(index + 2));
            }
        }

        return best;
    }

    private static ForsakenRotation? FitRotation(IReadOnlyList<ForsakenResolution> resolutions)
    {
        var sets = resolutions.Where(result => result.Slots is not null).ToArray();
        if (sets.Length < 2)
        {
            return null;
        }

        ForsakenRotation? best = null;
        for (var first = 0; first < 8; first++)
        {
            foreach (var direction in new[] { 1, -1 })
            {
                var matches = 0;
                foreach (var resolution in sets)
                {
                    var start = Mod8(first + (resolution.SetNumber - 1) * direction);
                    if (PairKey((start, Mod8(start + 2))) == PairKey(resolution.Slots!.Value))
                    {
                        matches++;
                    }
                }

                if (best is null || matches > best.Matches)
                {
                    best = new ForsakenRotation(direction == 1 ? "cw" : "ccw", (first, Mod8(first + 2)), matches);
                }
            }
        }

        return best is { Matches: >= 2 } ? best : null;
    }

    private static ForsakenEffect AssignmentEffect(ForsakenAssignment assignment) => assignment switch
    {
        ForsakenAssignment.Stack => ForsakenEffect.Stack,
        ForsakenAssignment.Spread => ForsakenEffect.Spread,
        _ => ForsakenEffect.Cone,
    };

    private static bool IsCoreDamage(ForsakenEffect effect) =>
        effect is ForsakenEffect.Stack or ForsakenEffect.Spread or ForsakenEffect.Cone;
    private static bool IsAuto(string name) => name.Length == 0 || name == "attack" || name.StartsWith("unknown_", StringComparison.Ordinal);
    private static bool IsFutureOrPast(string name) => name is "future's end" or "past's end";
    private static int Mod8(int value) => ((value % 8) + 8) % 8;
    private static string PairKey((int First, int Second) pair) => pair.First < pair.Second
        ? $"{pair.First},{pair.Second}"
        : $"{pair.Second},{pair.First}";

    private sealed record AssignmentChange(int ActorId, ForsakenAssignment Assignment, double Time);
    private sealed record CleaveSource(Vector2 Position, double FacingRadians, bool Boss, string Key, double Time);
    private sealed record CleaveCluster(double Time, List<CleaveSource> Sources);
}
