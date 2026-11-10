using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BetterDeaths.WtfDig;

internal enum P4RealFake { Real, Fake }
internal enum P4Timer { Short, Long }
internal enum P4DebuffKind { Lightning, Water, Gaze, Accel }
internal enum P4WoundColor { White, Black }
internal enum P4WoundField { Swap, Keep }
internal enum P4AntilightTier { Safe, Mitigated, Lethal }

internal sealed class P4WoundInfo
{
    public required int ActorId { get; init; }
    public required string Name { get; init; }
    public required WtfDigJobInfo Job { get; init; }
    public P4WoundColor? Wound { get; set; }
    public P4WoundField? Field { get; set; }
    public P4WoundColor? FinalWound { get; set; }
    public bool WrongSide { get; set; }
    public P4AntilightTier? Antilight { get; init; }
    public bool CaughtMiddle { get; init; }
    public bool Died { get; init; }
    public bool FloodResolved { get; init; }
    public bool? Success { get; set; }

    public string ResultLabel => !FloodResolved ? "Not resolved" : Success switch
    {
        true => "Correct",
        false when CaughtMiddle => "Caught in middle",
        false => "Wrong side",
        _ => "No result captured",
    };
}

internal sealed record P4DebuffTarget(
    int ActorId,
    string Name,
    WtfDigJobInfo Job,
    P4DebuffKind Kind,
    P4Timer? Timer,
    double DurationMs);

internal sealed record P4Round(
    int Index,
    double Time,
    P4RealFake? Ice,
    P4RealFake? Lightning,
    P4RealFake? NeoExdeath,
    P4Timer? MarkTimer,
    string? ChaosAbility,
    P4RealFake? Chaos,
    IReadOnlyList<P4DebuffTarget> Water,
    IReadOnlyList<P4DebuffTarget> LightningMarks,
    IReadOnlyList<P4DebuffTarget> Gaze,
    IReadOnlyList<P4DebuffTarget> ShortAccel,
    IReadOnlyList<P4DebuffTarget> LongAccel,
    IReadOnlyList<P4WoundInfo> Wounds);

internal sealed record P4ResolutionPlayer(
    int ActorId,
    string Name,
    WtfDigJobInfo Job,
    string Call,
    bool Danger,
    bool IsDebuff);

internal sealed class P4ResolutionElement
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public string? Icon { get; init; }
    public P4RealFake? RealFake { get; init; }
    public bool HasRealFake { get; init; }
    public string? Call { get; init; }
    public P4RealFake? CastLightning { get; init; }
    public P4RealFake? CastIce { get; init; }
    public P4RealFake? Lightning { get; init; }
    public P4RealFake? Ice { get; init; }
    public bool HasManaResult { get; init; }
    public IReadOnlyList<P4ResolutionPlayer> Players { get; init; } = [];
    public double ResolveTime { get; init; }
}

internal sealed record P4Resolution(
    IReadOnlyList<P4ResolutionElement> Block1,
    IReadOnlyList<P4ResolutionElement> Block2,
    bool Block1Reached,
    bool Block2Reached);

internal sealed record P4DeathMarker(
    string Stage,
    int? Round,
    string? Substep,
    int? Index,
    string Kind,
    int Count,
    double AtSeconds);

internal sealed record P4Analysis(
    FflogsFight Fight,
    double? KefkaSaysTime,
    IReadOnlyList<P4Round> Rounds,
    P4RealFake? FloodOfNaught,
    P4Resolution? Resolution,
    IReadOnlyList<P4DeathMarker> Markers);

internal sealed class P4Analyzer(IWtfDigEventSource client)
{
    internal const uint KefkaSaysId = 49884;
    private const uint MysteryMagicId = 47764;
    private const uint GrandCrossId = 47892;
    private const uint InfernoId = 47904;
    private const uint TsunamiId = 47905;
    private const uint FloodOfNaughtId = 50067;
    private const uint ManaChargeId = 47780;
    private const uint ManaReleaseId = 47781;
    private const uint RealFakeDebuffId = 1002056;
    private const uint WhiteAntilightId = 50068;
    private const uint BlackAntilightId = 50069;
    private const uint EdgeOfDeathId = 50070;
    private const long AntilightMinimumHit = 600;
    private const uint WhiteWound1Id = 1004887;
    private const uint BlackWound1Id = 1004888;
    private const uint WhiteWound2Id = 1005541;
    private const uint BlackWound2Id = 1005542;
    private const uint CursedShriekId = 1005543;
    private const uint ForkedLightningId = 1005544;
    private const uint CompressedWaterId = 1005545;
    private const uint AccelerationBombId = 1005546;

    private static readonly IReadOnlyDictionary<uint, (string Element, P4RealFake Value)> KefkaMarkers =
        new Dictionary<uint, (string, P4RealFake)>
        {
            [675] = ("ice", P4RealFake.Fake),
            [676] = ("ice", P4RealFake.Real),
            [677] = ("lightning", P4RealFake.Fake),
            [678] = ("lightning", P4RealFake.Real),
        };

    private static readonly IReadOnlyDictionary<string, string> ResolutionCategories =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["White Antilight"] = "flood",
            ["Black Antilight"] = "flood",
            ["Death Surge"] = "flood",
            ["Edge of Death"] = "flood",
            ["Death Bolt"] = "marks",
            ["Death Wave"] = "marks",
            ["Death Shriek"] = "gaze",
            ["Death Bomb"] = "accel",
            ["Inferno"] = "inferno",
            ["Tsunami"] = "tsunami",
        };

    internal async Task<P4Analysis> AnalyzeAsync(
        FflogsReportSummary report,
        FflogsFight fight,
        CancellationToken cancellationToken)
    {
        var ttl = FflogsClient.EventsCacheTtl(report, fight);
        var players = WtfDigAnalysisHelpers.FightPlayers(report, fight);
        var playerById = players.ToDictionary(player => player.Id);
        var anchorEvents = await client.FetchAllEventsAsync(
            new FflogsEventQuery(report.Code, fight.Id, fight.StartTime, fight.EndTime, FflogsEventDataType.Casts,
                FflogsHostilityType.Enemies, AbilityId: KefkaSaysId, CacheTtl: ttl), cancellationToken)
            .ConfigureAwait(false);
        var says = anchorEvents.FirstOrDefault(entry => entry.Type == "cast");
        if (says is null)
        {
            return Empty(fight);
        }

        var start = says.Timestamp - 3_000;
        var end = says.Timestamp + 110_000;
        var deathEnd = Math.Min(fight.EndTime, says.Timestamp + 200_000);
        var woundKindById = new Dictionary<uint, string>
        {
            [WhiteWound1Id] = "white",
            [BlackWound1Id] = "black",
            [WhiteWound2Id] = "white",
            [BlackWound2Id] = "black",
        };
        foreach (var ability in report.Abilities)
        {
            var kind = ability.Name switch
            {
                "White Wound" => "white",
                "Black Wound" => "black",
                "Allagan Field" => "swap",
                "Beyond Death" => "keep",
                _ => null,
            };
            if (kind is not null)
            {
                woundKindById[ability.GameID] = kind;
            }
        }

        var categoryById = report.Abilities
            .Where(ability => ResolutionCategories.ContainsKey(ability.Name))
            .ToDictionary(ability => ability.GameID, ability => ResolutionCategories[ability.Name]);
        var enemyIds = new[]
        {
            MysteryMagicId, GrandCrossId, InfernoId, TsunamiId, FloodOfNaughtId,
            ManaChargeId, ManaReleaseId, RealFakeDebuffId,
        };
        var friendlyIds = new[]
            {
                CursedShriekId, ForkedLightningId, CompressedWaterId, AccelerationBombId,
                WhiteAntilightId, BlackAntilightId, EdgeOfDeathId,
            }
            .Concat(woundKindById.Keys)
            .ToArray();
        var bossTask = client.FetchAllEventsAsync(
            new FflogsEventQuery(report.Code, fight.Id, start, end,
                FilterExpression: $"type='headmarker' or ability.id in ({string.Join(", ", enemyIds)})", CacheTtl: ttl),
            cancellationToken);
        var friendlyTask = client.FetchAllEventsAsync(
            new FflogsEventQuery(report.Code, fight.Id, start, deathEnd,
                FilterExpression: $"type='death' or ability.id in ({string.Join(", ", friendlyIds)})", CacheTtl: ttl),
            cancellationToken);
        await Task.WhenAll(bossTask, friendlyTask).ConfigureAwait(false);
        var boss = await bossTask.ConfigureAwait(false);
        var friendly = await friendlyTask.ConfigureAwait(false);
        var bossCasts = boss.Where(entry => entry.Type == "cast").ToArray();
        var headmarkers = boss.Where(entry => entry.Type == "headmarker").ToArray();
        var realFakeEvents = boss.Where(entry => entry.AbilityGameID == RealFakeDebuffId).ToArray();
        var debuffs = friendly.Where(entry => entry.Type == "applydebuff" ||
            entry.Type is "refreshdebuff" or "applydebuffstack" && entry.AbilityGameID is { } id &&
            woundKindById.GetValueOrDefault(id) is "white" or "black").ToArray();
        var antilightEvents = friendly
            .Where(entry => entry.Type == "damage" && !entry.IsEstimated &&
                entry.AbilityGameID is WhiteAntilightId or BlackAntilightId)
            .ToArray();
        var deaths = friendly.Where(entry => entry.Type == "death").ToArray();
        FflogsEvent[] Casts(uint id) => bossCasts.Where(entry => entry.AbilityGameID == id).OrderBy(entry => entry.Timestamp).ToArray();
        var mysteryMagics = Casts(MysteryMagicId);
        var grandCrosses = Casts(GrandCrossId);
        var infernos = Casts(InfernoId);
        var tsunamis = Casts(TsunamiId);

        var markerCutoff = says.Timestamp + 50_000;
        var markerGroups = GroupByGap(
            headmarkers.Where(entry =>
                entry.MarkerID is { } markerId &&
                KefkaMarkers.ContainsKey(markerId) &&
                entry.Timestamp < markerCutoff),
            900);
        var kefkaCalls = markerGroups.Select(group =>
        {
            P4RealFake? ice = null;
            P4RealFake? lightning = null;
            foreach (var entry in group)
            {
                var marker = KefkaMarkers[entry.MarkerID!.Value];
                if (marker.Element == "ice")
                {
                    ice = marker.Value;
                }
                else
                {
                    lightning = marker.Value;
                }
            }

            return new KefkaCall(group[0].Timestamp, ice, lightning);
        }).ToArray();

        var tells = realFakeEvents.Where(entry => entry.Type is "applydebuff" or "refreshdebuff" or "statussnapshot")
            .OrderBy(entry => entry.Timestamp).ToArray();
        P4RealFake? RealFakeForCast(FflogsEvent? cast, IReadOnlyList<FflogsEvent> sameBossCasts, bool chaos)
        {
            if (cast is null)
                return null;
            // Status 2056 encodes Chaos as 1119/1120 and Neo Exdeath as 1121/1122 (fake/real).
            bool MatchesRole(long? value) => chaos ? value is 1119 or 1120 : value is 1121 or 1122;
            var roleActors = tells.Where(entry => entry.Timestamp <= cast.Timestamp && MatchesRole(entry.ExtraInfo) && entry.TargetID is not null)
                .Select(entry => entry.TargetID!.Value).ToHashSet();
            var previous = sameBossCasts.Where(entry => entry.Timestamp < cast.Timestamp)
                .Select(entry => entry.Timestamp).DefaultIfEmpty(start).Max();
            var before = tells.Where(entry => entry.Timestamp > previous && entry.Timestamp <= cast.Timestamp &&
                entry.TargetID is { } actor && (roleActors.Contains(actor) || actor == cast.SourceID)).ToArray();
            var exact = before.Where(entry => cast.SourceID is not null && entry.TargetID == cast.SourceID).ToArray();
            // Local casts can come from helper actors. The encoded tell identifies the mechanic's boss role.
            var pick = exact.LastOrDefault() ?? (before.Select(entry => entry.TargetID).Distinct().Count() == 1 ? before.LastOrDefault() : null);
            return pick is not null && MatchesRole(pick.ExtraInfo) ? RealFakeFromExtra(pick.ExtraInfo) : null;
        }

        var neoExdeathRealFake = grandCrosses.Select(cast => RealFakeForCast(cast, grandCrosses, false)).ToArray();
        var chaosCasts = infernos.Concat(tsunamis).OrderBy(entry => entry.Timestamp).ToArray();
        var infernoRealFake = RealFakeForCast(infernos.FirstOrDefault(), chaosCasts, true);
        var tsunamiRealFake = RealFakeForCast(tsunamis.FirstOrDefault(), chaosCasts, true);
        var chaosCastList = infernos.Select(cast => new ChaosCast(cast.Timestamp, "Inferno", cast))
            .Concat(tsunamis.Select(cast => new ChaosCast(cast.Timestamp, "Tsunami", cast)))
            .OrderBy(cast => cast.Time)
            .ToArray();
        var chaosByRound = grandCrosses.Select((grandCross, index) =>
        {
            var next = index + 1 < grandCrosses.Length ? grandCrosses[index + 1].Timestamp : double.PositiveInfinity;
            var cast = chaosCastList.FirstOrDefault(candidate => candidate.Time >= grandCross.Timestamp && candidate.Time < next);
            return cast is null ? null : new ChaosRound(cast.Ability, RealFakeForCast(cast.Event, chaosCasts, true));
        }).ToArray();
        var floodSignal = bossCasts.Concat(friendly.Where(entry => entry.Type == "cast" || entry.Type == "damage" && !entry.IsEstimated))
            .Where(entry => entry.AbilityGameID is FloodOfNaughtId or WhiteAntilightId or BlackAntilightId)
            .OrderBy(entry => entry.Timestamp).FirstOrDefault();
        var floodRealFake = RealFakeForCast(floodSignal, grandCrosses, false);

        var antilightByPlayer = new Dictionary<int, AntilightRecord>();
        AntilightRecord AntilightFor(int actorId)
        {
            if (!antilightByPlayer.TryGetValue(actorId, out var record))
                antilightByPlayer[actorId] = record = new();
            return record;
        }
        foreach (var entry in antilightEvents.Where(entry => entry.TargetID is not null))
        {
            var record = AntilightFor(entry.TargetID!.Value);
            var amount = entry.Amount ?? 0;
            if (entry.AbilityGameID == WhiteAntilightId)
            {
                record.White = Math.Max(record.White, amount);
                record.HitWhite |= amount > AntilightMinimumHit;
            }
            else if (entry.AbilityGameID == BlackAntilightId)
            {
                record.Black = Math.Max(record.Black, amount);
                record.HitBlack |= amount > AntilightMinimumHit;
            }
        }

        var floodDeaths = new HashSet<int>();
        var middleDeaths = new HashSet<int>();
        var confirmedFloodTimes = boss.Concat(friendly)
            .Where(entry => !entry.IsEstimated && (entry.Type is "cast" or "damage") && IsFloodResolutionAction(entry.AbilityGameID))
            .Select(entry => entry.Timestamp).ToList();
        foreach (var death in deaths)
        {
            if (death.TargetID is not { } actorId || death.KillingAbilityGameID is not { } abilityId)
            {
                continue;
            }

            if (abilityId == EdgeOfDeathId)
                middleDeaths.Add(actorId);
            if (abilityId == WhiteAntilightId)
                AntilightFor(actorId).HitWhite = true;
            if (abilityId == BlackAntilightId)
                AntilightFor(actorId).HitBlack = true;
            if (abilityId is WhiteAntilightId or BlackAntilightId or EdgeOfDeathId || categoryById.GetValueOrDefault(abilityId) == "flood")
            {
                floodDeaths.Add(actorId);
                confirmedFloodTimes.Add(death.Timestamp);
            }
        }
        bool IsWound(FflogsEvent entry) => entry.AbilityGameID is { } id && woundKindById.GetValueOrDefault(id) is "white" or "black";
        var woundTransitions = debuffs.Where(entry => entry.TargetID is not null && IsWound(entry))
            .GroupBy(entry => entry.TargetID).SelectMany(group =>
            {
                var first = group.OrderBy(entry => entry.Timestamp).First();
                return group.Where(entry => entry.Timestamp > first.Timestamp && entry.AbilityGameID != first.AbilityGameID &&
                    debuffs.Any(field => field.TargetID == entry.TargetID && field.Timestamp <= entry.Timestamp &&
                        field.AbilityGameID is { } id && woundKindById.GetValueOrDefault(id) is "swap" or "keep"));
            }).Select(entry => entry.Timestamp);
        var floodResolvedAt = confirmedFloodTimes.Count > 0 ? confirmedFloodTimes.Min() :
            woundTransitions.DefaultIfEmpty(double.PositiveInfinity).Min();
        var floodResolved = double.IsFinite(floodResolvedAt) || bossCasts.Any(entry => !entry.IsEstimated &&
            entry.AbilityGameID is ManaChargeId or ManaReleaseId);
        var woundRemovals = friendly.Where(entry => entry.Type == "removedebuff" && IsWound(entry)).ToArray();

        var setTimes = grandCrosses.Select(cast => cast.Timestamp).ToArray();
        int SetOf(double time) => NearestIndex(setTimes, time);
        var setMarkResolve = setTimes.Select((setTime, index) =>
        {
            var mark = debuffs.FirstOrDefault(entry =>
                entry.AbilityGameID is ForkedLightningId or CompressedWaterId && SetOf(entry.Timestamp) == index);
            return mark is null ? setTime : mark.Timestamp + (mark.Duration ?? 0);
        }).ToArray();
        var resolves = setTimes.Select((_, index) => index)
            .Where(index => debuffs.Any(entry =>
                entry.AbilityGameID is ForkedLightningId or CompressedWaterId && SetOf(entry.Timestamp) == index))
            .Select(index => setMarkResolve[index])
            .ToArray();
        var shortResolve = resolves.Length > 0 ? resolves.Min() : 0;
        var longResolve = resolves.Length > 0 ? resolves.Max() : 0;
        P4Timer TimerOf(double resolveTime) => Math.Abs(resolveTime - shortResolve) <= Math.Abs(resolveTime - longResolve)
            ? P4Timer.Short
            : P4Timer.Long;
        P4DebuffTarget? ToTarget(FflogsEvent entry, P4DebuffKind kind, P4Timer? timer)
        {
            if (entry.TargetID is not { } actorId || !playerById.TryGetValue(actorId, out var player))
            {
                return null;
            }

            return new P4DebuffTarget(
                player.Id, player.Name, WtfDigAnalysisHelpers.JobInfo(player.SubType), kind, timer, entry.Duration ?? 0);
        }

        var rounds = grandCrosses.Select((grandCross, index) =>
        {
            var setDebuffs = debuffs.Where(entry => SetOf(entry.Timestamp) == index).OrderBy(entry => entry.Timestamp).ToArray();
            var water = new List<P4DebuffTarget>();
            var lightning = new List<P4DebuffTarget>();
            var gaze = new List<P4DebuffTarget>();
            var shortAccel = new List<P4DebuffTarget>();
            var longAccel = new List<P4DebuffTarget>();
            var woundByPlayer = new Dictionary<int, P4WoundInfo>();
            var initialWoundByPlayer = new Dictionary<int, FflogsEvent>();
            P4WoundInfo? WoundInfo(int actorId)
            {
                if (!playerById.TryGetValue(actorId, out var player))
                {
                    return null;
                }

                if (woundByPlayer.TryGetValue(actorId, out var existing))
                {
                    return existing;
                }

                antilightByPlayer.TryGetValue(actorId, out var antilight);
                var maximum = antilight is null ? 0 : Math.Max(antilight.White, antilight.Black);
                var created = new P4WoundInfo
                {
                    ActorId = player.Id,
                    Name = player.Name,
                    Job = WtfDigAnalysisHelpers.JobInfo(player.SubType),
                    Antilight = maximum > 0 ? AntilightTier(maximum) : null,
                    CaughtMiddle = middleDeaths.Contains(actorId) || antilight is { HitWhite: true, HitBlack: true },
                    Died = floodDeaths.Contains(actorId),
                    FloodResolved = floodResolved,
                };
                woundByPlayer[actorId] = created;
                return created;
            }

            foreach (var entry in setDebuffs)
            {
                var resolveTime = entry.Timestamp + (entry.Duration ?? 0);
                if (entry.AbilityGameID == CompressedWaterId)
                {
                    AddIfNotNull(water, ToTarget(entry, P4DebuffKind.Water, TimerOf(setMarkResolve[index])));
                }
                else if (entry.AbilityGameID == ForkedLightningId)
                {
                    AddIfNotNull(lightning, ToTarget(entry, P4DebuffKind.Lightning, TimerOf(setMarkResolve[index])));
                }
                else if (entry.AbilityGameID == CursedShriekId)
                {
                    AddIfNotNull(gaze, ToTarget(entry, P4DebuffKind.Gaze, null));
                }
                else if (entry.AbilityGameID == AccelerationBombId)
                {
                    var timer = TimerOf(resolveTime);
                    AddIfNotNull(timer == P4Timer.Short ? shortAccel : longAccel, ToTarget(entry, P4DebuffKind.Accel, timer));
                }
                else if (entry.AbilityGameID is { } statusId && entry.TargetID is { } actorId && woundKindById.TryGetValue(statusId, out var kind))
                {
                    var wound = WoundInfo(actorId);
                    if (wound is null)
                    {
                        continue;
                    }

                    switch (kind)
                    {
                        case "white":
                        case "black":
                            var color = kind == "white" ? P4WoundColor.White : P4WoundColor.Black;
                            if (entry.Timestamp >= floodResolvedAt)
                                wound.FinalWound = color;
                            else
                            {
                                wound.Wound = color;
                                initialWoundByPlayer[actorId] = entry;
                            }
                            break;
                        case "swap":
                            wound.Field = P4WoundField.Swap;
                            break;
                        case "keep":
                            wound.Field = P4WoundField.Keep;
                            break;
                    }
                }
            }

            var wounds = woundByPlayer.Values.OrderBy(RoleRank).ThenBy(wound => wound.Job.Abbreviation).ToArray();
            foreach (var wound in wounds)
            {
                if (floodResolved && wound.FinalWound is null && initialWoundByPlayer.TryGetValue(wound.ActorId, out var initial))
                {
                    var removal = woundRemovals.Where(entry => entry.TargetID == wound.ActorId &&
                            entry.AbilityGameID == initial.AbilityGameID && entry.Timestamp >= initial.Timestamp)
                        .OrderBy(entry => entry.Timestamp).FirstOrDefault();
                    var expired = initial.Duration is > 0 && initial.Timestamp + initial.Duration <= floodResolvedAt;
                    // An unchanged wound observed until after Flood is evidence too; a death clearing it is not.
                    if (!expired && removal is not null && removal.Timestamp >= floodResolvedAt &&
                        !deaths.Any(entry => entry.TargetID == wound.ActorId && entry.Timestamp >= initial.Timestamp && entry.Timestamp <= removal.Timestamp))
                        wound.FinalWound = wound.Wound;
                }
                P4WoundColor? expected = wound.Field switch
                {
                    P4WoundField.Swap when wound.Wound is not null =>
                        wound.Wound == P4WoundColor.White ? P4WoundColor.Black : P4WoundColor.White,
                    P4WoundField.Keep => wound.Wound,
                    _ => null,
                };
                antilightByPlayer.TryGetValue(wound.ActorId, out var antilight);
                var hitWhite = antilight?.HitWhite == true;
                var hitBlack = antilight?.HitBlack == true;
                P4WoundColor? hitSide = hitWhite == hitBlack ? null : hitWhite ? P4WoundColor.White : P4WoundColor.Black;
                // A lethal hit can prevent the wound reapplication. The confirmed hit still records the actual side.
                wound.FinalWound = hitSide ?? wound.FinalWound;
                wound.WrongSide = wound.FinalWound is not null && expected is not null && wound.FinalWound != expected;
                wound.Success = !floodResolved ? null : wound.CaughtMiddle ? false :
                    wound.FinalWound is not null && expected is not null ? !wound.WrongSide : null;
            }

            var hasMarks = water.Count > 0 || lightning.Count > 0;
            return new P4Round(
                index + 1,
                (grandCross.Timestamp - fight.StartTime) / 1000,
                index < kefkaCalls.Length ? kefkaCalls[index].Ice : null,
                index < kefkaCalls.Length ? kefkaCalls[index].Lightning : null,
                index < neoExdeathRealFake.Length ? neoExdeathRealFake[index] : null,
                hasMarks ? TimerOf(setMarkResolve[index]) : null,
                index < chaosByRound.Length ? chaosByRound[index]?.Ability : null,
                index < chaosByRound.Length ? chaosByRound[index]?.Value : null,
                water, lightning, gaze, shortAccel, longAccel, wounds);
        }).ToArray();

        var manaMarks = headmarkers
            .Where(entry =>
                entry.MarkerID is { } markerId &&
                KefkaMarkers.ContainsKey(markerId) &&
                entry.Timestamp >= markerCutoff)
            .OrderBy(entry => entry.Timestamp)
            .ToArray();
        P4RealFake? MarkerValue(FflogsEvent? entry) => entry?.MarkerID is { } markerId
            ? KefkaMarkers[markerId].Value
            : null;
        var lightningMarks = manaMarks.Where(entry => KefkaMarkers[entry.MarkerID!.Value].Element == "lightning").ToArray();
        var iceMarks = manaMarks.Where(entry => KefkaMarkers[entry.MarkerID!.Value].Element == "ice").ToArray();
        var chargeLightning = MarkerValue(lightningMarks.ElementAtOrDefault(0));
        var releaseLightning = MarkerValue(lightningMarks.ElementAtOrDefault(1));
        var chargeIce = MarkerValue(iceMarks.ElementAtOrDefault(0));
        var releaseIce = MarkerValue(iceMarks.ElementAtOrDefault(1));
        var finalLightning = Combine(chargeLightning, releaseLightning);
        var finalIce = Combine(chargeIce, releaseIce);

        P4ResolutionPlayer MakePlayer(FflogsActor player, string call, bool danger, bool isDebuff = false) =>
            new(player.Id, player.Name, WtfDigAnalysisHelpers.JobInfo(player.SubType), call, danger, isDebuff);
        P4ResolutionPlayer FromDebuff(P4DebuffTarget target, string call, bool danger) =>
            new(target.ActorId, target.Name, target.Job, call, danger, true);
        IReadOnlyList<P4ResolutionPlayer> MarksPlayers(P4Round? round)
        {
            if (round?.NeoExdeath is null)
            {
                return [];
            }

            var spreaders = round.NeoExdeath == P4RealFake.Fake ? round.Water : round.LightningMarks;
            var spreaderIds = spreaders.Select(target => target.ActorId).ToHashSet();
            var spreadRows = spreaders.Select(target => FromDebuff(target, "spread", true))
                .OrderBy(RoleRank).ThenBy(player => player.Job.Abbreviation);
            var stackRows = players.Where(player => !spreaderIds.Contains(player.Id))
                .Select(player => MakePlayer(player, "stack", false))
                .OrderBy(RoleRank).ThenBy(player => player.Job.Abbreviation);
            return spreadRows.Concat(stackRows).ToArray();
        }
        IReadOnlyList<P4ResolutionPlayer> GazePlayers(P4Round? round)
        {
            if (round is null)
            {
                return [];
            }

            var away = round.NeoExdeath == P4RealFake.Real;
            var call = round.NeoExdeath is null ? "?" : away ? "look away" : "look at";
            var holders = round.Gaze.Select(target => target.ActorId).ToHashSet();
            return round.Gaze.Select(target => FromDebuff(target, call, !away))
                .Concat(players.Where(player => !holders.Contains(player.Id)).Select(player => MakePlayer(player, call, !away)))
                .OrderBy(player => player.IsDebuff ? 0 : 1).ThenBy(RoleRank).ThenBy(player => player.Job.Abbreviation).ToArray();
        }
        IReadOnlyList<P4ResolutionPlayer> AccelPlayers(P4Timer timer) => rounds.SelectMany(round =>
            {
                var still = round.NeoExdeath == P4RealFake.Real;
                var call = round.NeoExdeath is null ? "?" : still ? "stillness" : "motion";
                var targets = timer == P4Timer.Short ? round.ShortAccel : round.LongAccel;
                return targets.Select(target => FromDebuff(target, call, !still));
            })
            .OrderBy(RoleRank).ThenBy(player => player.Job.Abbreviation).ToArray();

        var round1 = rounds.ElementAtOrDefault(0);
        var round2 = rounds.ElementAtOrDefault(1);
        var shortMarkRound = rounds.FirstOrDefault(round => round.MarkTimer == P4Timer.Short);
        var longMarkRound = rounds.FirstOrDefault(round => round.MarkTimer == P4Timer.Long);
        var infernoCall = infernoRealFake is null ? null : infernoRealFake == P4RealFake.Real ? "Move" : "Stay";
        var tsunamiCall = tsunamiRealFake is null ? null : tsunamiRealFake == P4RealFake.Fake ? "Move" : "Stay";
        var manaChargeCast = bossCasts.Any(entry => entry.AbilityGameID == ManaChargeId);
        var manaReleaseCast = bossCasts.Any(entry => entry.AbilityGameID == ManaReleaseId);
        var manaReleaseTime = bossCasts.FirstOrDefault(entry => entry.AbilityGameID == ManaReleaseId)?.Timestamp ?? longResolve + 14_000;
        var tsunamiTime = manaReleaseTime;
        var shortGazeTime = shortResolve + 8_000;
        var longGazeTime = longResolve + 8_000;
        var infernoTime = shortResolve + 16_000;
        var resolution = new P4Resolution(
            [
                Element("short-accel", "Short Accel Bomb", "accel", AccelPlayers(P4Timer.Short), shortResolve),
                Element("short-marks", "Short Water/Lightning", "water", MarksPlayers(shortMarkRound), shortResolve),
                Element("mana-lightning", "Mana Charge (Lightning)", null, [], manaReleaseTime, chargeLightning, true),
                Element("short-gaze", "Short Gazes", "gaze", GazePlayers(round1), shortGazeTime),
                Element("inferno", "Inferno", null, [], infernoTime, infernoRealFake, true, infernoCall),
            ],
            [
                Element("long-accel", "Long Accel Bomb", "accel", AccelPlayers(P4Timer.Long), longResolve),
                Element("mana-ice", "Mana Charge (Ice)", null, [], manaReleaseTime, chargeIce, true),
                Element("long-marks", "Long Water/Lightning", "water", MarksPlayers(longMarkRound), longResolve),
                Element("long-gaze", "Long Gazes", "gaze", GazePlayers(round2), longGazeTime),
                Element("tsunami", "Tsunami", null, [], tsunamiTime, tsunamiRealFake, true, tsunamiCall),
                new P4ResolutionElement
                {
                    Key = "mana-release", Title = "Mana Release", ResolveTime = manaReleaseTime,
                    CastLightning = releaseLightning, CastIce = releaseIce,
                    Lightning = finalLightning, Ice = finalIce, HasManaResult = true,
                },
            ],
            manaChargeCast,
            manaReleaseCast);

        var markers = BuildMarkers(fight, says.Timestamp, mysteryMagics, grandCrosses, infernos, tsunamis,
            Casts(FloodOfNaughtId), deaths, shortResolve, longResolve, shortGazeTime, longGazeTime,
            infernoTime, manaReleaseTime, resolution);
        return new P4Analysis(
            fight,
            (says.Timestamp - fight.StartTime) / 1000,
            rounds,
            floodRealFake,
            resolution,
            markers);
    }

    private static IReadOnlyList<P4DeathMarker> BuildMarkers(
        FflogsFight fight,
        double saysTime,
        IReadOnlyList<FflogsEvent> mysteryMagic,
        IReadOnlyList<FflogsEvent> grandCross,
        IReadOnlyList<FflogsEvent> infernos,
        IReadOnlyList<FflogsEvent> tsunamis,
        IReadOnlyList<FflogsEvent> floods,
        IReadOnlyList<FflogsEvent> deaths,
        double shortResolve,
        double longResolve,
        double shortGaze,
        double longGaze,
        double inferno,
        double manaRelease,
        P4Resolution resolution)
    {
        double[] AfterSays(IEnumerable<FflogsEvent> events) => events.Select(entry => entry.Timestamp)
            .Where(time => time >= saysTime).OrderBy(time => time).ToArray();
        var mysteryTimes = AfterSays(mysteryMagic);
        var crossTimes = AfterSays(grandCross);
        var chaosTimes = AfterSays(infernos).Concat(AfterSays(tsunamis)).OrderBy(time => time).ToArray();
        var floodThird = AfterSays(floods).FirstOrDefault();
        if (floodThird <= 0 && crossTimes.Length > 2)
        {
            floodThird = crossTimes[2] + 12_000;
        }

        var thirdTimes = new double?[]
        {
            chaosTimes.Length > 0 ? chaosTimes[0] : null,
            chaosTimes.Length > 1 ? chaosTimes[1] : null,
            floodThird > 0 ? floodThird : null,
        };
        var subCasts = new List<SubCast>();
        for (var index = 0; index < 3; index++)
        {
            if (index < mysteryTimes.Length) subCasts.Add(new SubCast(index + 1, "mm", mysteryTimes[index]));
            if (index < crossTimes.Length) subCasts.Add(new SubCast(index + 1, "gc", crossTimes[index]));
            if (thirdTimes[index] is { } thirdTime) subCasts.Add(new SubCast(index + 1, "third", thirdTime));
        }
        subCasts.Sort((left, right) => left.Time.CompareTo(right.Time));
        var mechanics = new[]
        {
            new ResolutionTime(shortResolve, "short"), new(shortGaze, "short"), new(inferno, "short"),
            new ResolutionTime(longResolve, "long"), new(longGaze, "long"), new(manaRelease, "long"),
        };
        int DividerIndex(IReadOnlyList<P4ResolutionElement> elements, double time)
        {
            var index = elements.Count;
            for (var current = elements.Count - 1; current >= 0; current--)
            {
                if (elements[current].ResolveTime > time) index = current;
                else break;
            }
            return index;
        }
        P4DeathMarker Classify(double time, string kind, int count, double atSeconds)
        {
            if (time < shortResolve && subCasts.Count > 0)
            {
                var sub = subCasts.LastOrDefault(candidate => candidate.Time <= time) ?? subCasts[0];
                return new P4DeathMarker("round", sub.Round, sub.Substep, null, kind, count, atSeconds);
            }

            var nearest = mechanics.OrderBy(mechanic => Math.Abs(mechanic.Time - time)).First();
            var elements = nearest.Stage == "short" ? resolution.Block1 : resolution.Block2;
            return new P4DeathMarker(nearest.Stage, null, null, DividerIndex(elements, time), kind, count, atSeconds);
        }

        var deathTimes = deaths.Where(entry => entry.TargetID is not null && entry.Timestamp >= saysTime)
            .Select(entry => entry.Timestamp).OrderBy(time => time).ToArray();
        if (deathTimes.Length == 0)
        {
            return [];
        }

        var markers = new List<P4DeathMarker>();
        var last = deathTimes[^1];
        var wipeCluster = fight.Kill == true ? [] : deathTimes.Where(time => last - time <= 6000).ToArray();
        if (wipeCluster.Length > 0)
        {
            markers.Add(Classify(wipeCluster.Min(), "wipe", wipeCluster.Length, (fight.EndTime - fight.StartTime) / 1000));
        }

        var earlier = deathTimes.Where(time => fight.Kill == true || last - time > 6000).ToArray();
        var groups = new List<List<double>>();
        foreach (var time in earlier)
        {
            var group = groups.LastOrDefault();
            if (group is not null && time - group[^1] <= 2000) group.Add(time);
            else groups.Add([time]);
        }
        markers.AddRange(groups.Select(group => Classify(group[0], "death", group.Count, (group[0] - fight.StartTime) / 1000)));
        return markers.OrderBy(marker => marker.AtSeconds).ToArray();
    }

    private static P4ResolutionElement Element(
        string key,
        string title,
        string? icon,
        IReadOnlyList<P4ResolutionPlayer> players,
        double resolveTime,
        P4RealFake? realFake = null,
        bool hasRealFake = false,
        string? call = null) => new()
        {
            Key = key,
            Title = title,
            Icon = icon,
            Players = players,
            ResolveTime = resolveTime,
            RealFake = realFake,
            HasRealFake = hasRealFake,
            Call = call,
        };

    private static P4RealFake? RealFakeFromExtra(long? extraInfo) => extraInfo is null
        ? null
        : extraInfo % 2 == 0 ? P4RealFake.Real : P4RealFake.Fake;
    private static P4RealFake? Combine(P4RealFake? first, P4RealFake? second) => first is null || second is null
        ? null
        : first == second ? P4RealFake.Real : P4RealFake.Fake;
    private static P4AntilightTier AntilightTier(long amount) => amount < 10_000
        ? P4AntilightTier.Safe
        : amount < 120_000 ? P4AntilightTier.Mitigated : P4AntilightTier.Lethal;
    private static int RoleRank(P4WoundInfo value) => RoleRank(value.Job);
    private static int RoleRank(P4ResolutionPlayer value) => RoleRank(value.Job);
    private static int RoleRank(WtfDigJobInfo job) => job.Role switch { "tank" => 0, "healer" => 1, _ => 2 };
    private static int NearestIndex(IReadOnlyList<double> values, double time)
    {
        var bestIndex = 0;
        var best = double.PositiveInfinity;
        for (var index = 0; index < values.Count; index++)
        {
            var distance = Math.Abs(values[index] - time);
            if (distance < best)
            {
                best = distance;
                bestIndex = index;
            }
        }
        return bestIndex;
    }
    private static IReadOnlyList<List<FflogsEvent>> GroupByGap(IEnumerable<FflogsEvent> entries, double gap)
    {
        var groups = new List<List<FflogsEvent>>();
        foreach (var entry in entries.OrderBy(value => value.Timestamp))
        {
            var last = groups.LastOrDefault();
            if (last is not null && entry.Timestamp - last[^1].Timestamp <= gap) last.Add(entry);
            else groups.Add([entry]);
        }
        return groups;
    }
    private static void AddIfNotNull<T>(ICollection<T> target, T? value) where T : class
    {
        if (value is not null) target.Add(value);
    }
    private static P4Analysis Empty(FflogsFight fight) => new(fight, null, [], null, null, []);

    internal static bool IsFloodResolutionAction(uint? id) => id is FloodOfNaughtId or WhiteAntilightId or BlackAntilightId or EdgeOfDeathId;

    private sealed record KefkaCall(double Time, P4RealFake? Ice, P4RealFake? Lightning);
    private sealed record ChaosCast(double Time, string Ability, FflogsEvent Event);
    private sealed record ChaosRound(string Ability, P4RealFake? Value);
    private sealed class AntilightRecord
    {
        public long White { get; set; }
        public long Black { get; set; }
        public bool HitWhite { get; set; }
        public bool HitBlack { get; set; }
    }
    private sealed record SubCast(int Round, string Substep, double Time);
    private sealed record ResolutionTime(double Time, string Stage);
}
