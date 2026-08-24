using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BetterDeaths.WtfDig;

internal enum WtfDigLocalDataQuality
{
    Exact,
    Estimated,
    Unavailable,
}

internal sealed record WtfDigLocalAnalyzerAvailability(
    WtfDigLocalDataQuality Quality,
    string Summary);

internal sealed class LocalPullEventSource : IWtfDigEventSource
{
    private const uint RealFakeStatusId = 2056;
    private const uint PathOfLightActionId = 47806;

    private static readonly IReadOnlyDictionary<uint, string> CanonicalAbilityNames =
        new Dictionary<uint, string>
        {
            [47801] = "Tele-Trouncing",
            [47804] = "Forsaken",
            [47806] = "The Path of Light",
            [47808] = "Spelldriver",
            [47809] = "Spellscatter",
            [47810] = "Spellwave",
            [47826] = "Future's End",
            [47827] = "Past's End",
            [47830] = "Future's End",
            [47831] = "Past's End",
            [47832] = "Future's End",
            [47833] = "Past's End",
            [47836] = "All Things Ending",
            [47837] = "All Things Ending",
            [47843] = "Ultima Blaster",
            [47844] = "Ultima Blaster",
            [47867] = "Black Hole",
            [47868] = "Nothingness",
            [47892] = "Grand Cross",
            [47904] = "Inferno",
            [47905] = "Tsunami",
            [49884] = "Kefka Says",
            [50067] = "Flood of Naught",
            [50068] = "White Antilight",
            [50069] = "Black Antilight",
            [50070] = "Edge of Death",
        };

    private static readonly IReadOnlyDictionary<uint, string> JobNames = new Dictionary<uint, string>
    {
        [19] = "Paladin",
        [20] = "Monk",
        [21] = "Warrior",
        [22] = "Dragoon",
        [23] = "Bard",
        [24] = "WhiteMage",
        [25] = "BlackMage",
        [27] = "Summoner",
        [28] = "Scholar",
        [30] = "Ninja",
        [31] = "Machinist",
        [32] = "DarkKnight",
        [33] = "Astrologian",
        [34] = "Samurai",
        [35] = "RedMage",
        [37] = "Gunbreaker",
        [38] = "Dancer",
        [39] = "Reaper",
        [40] = "Sage",
        [41] = "Viper",
        [42] = "Pictomancer",
    };

    private readonly IReadOnlyList<LocalEvent> events;
    private readonly IReadOnlySet<int> friendlyActorIds;
    private readonly IReadOnlyDictionary<int, string> actorNames;

    private LocalPullEventSource(
        FflogsReportSummary report,
        FflogsFight fight,
        IReadOnlyList<LocalEvent> events,
        IReadOnlyDictionary<string, WtfDigLocalAnalyzerAvailability> analyzerAvailability)
    {
        Report = report;
        Fight = fight;
        this.events = events;
        friendlyActorIds = report.Actors
            .Where(actor => actor.Type is "Player" or "Pet")
            .Select(actor => actor.Id)
            .ToHashSet();
        actorNames = report.Actors.ToDictionary(actor => actor.Id, actor => actor.Name);
        AnalyzerAvailability = analyzerAvailability;
    }

    internal FflogsReportSummary Report { get; }

    internal FflogsFight Fight { get; }

    internal IReadOnlyDictionary<string, WtfDigLocalAnalyzerAvailability> AnalyzerAvailability { get; }

    internal static LocalPullEventSource Create(PullDeathSnapshot pull)
    {
        var builder = new Builder(pull);
        return builder.Build();
    }

    public Task<IReadOnlyList<FflogsEvent>> FetchAllEventsAsync(
        FflogsEventQuery query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var filter = string.IsNullOrWhiteSpace(query.FilterExpression)
            ? null
            : ParseFilterExpression(query.FilterExpression);
        var matching = events
            .Where(entry => entry.Event.Timestamp >= query.StartTime && entry.Event.Timestamp <= query.EndTime)
            .Where(entry => MatchesDataType(entry.Event, query.DataType, query.HostilityType))
            .Where(entry => query.AbilityId is null || entry.Event.AbilityGameID == query.AbilityId)
            .Where(entry => filter is null || filter(entry.Event))
            .OrderBy(entry => entry.Event.Timestamp)
            .Select(entry => entry.Event)
            .ToArray();
        return Task.FromResult<IReadOnlyList<FflogsEvent>>(matching);
    }

    private bool MatchesDataType(
        FflogsEvent entry,
        FflogsEventDataType? dataType,
        FflogsHostilityType hostilityType)
    {
        var wantFriendly = hostilityType == FflogsHostilityType.Friendlies;
        var sourceIsFriendly = entry.SourceID is { } sourceId && friendlyActorIds.Contains(sourceId);
        var targetIsFriendly = entry.TargetID is { } targetId && friendlyActorIds.Contains(targetId);
        return dataType switch
        {
            FflogsEventDataType.Casts => IsCastEvent(entry.Type) && sourceIsFriendly == wantFriendly,
            FflogsEventDataType.DamageTaken => IsDamageEvent(entry.Type) && targetIsFriendly == wantFriendly,
            FflogsEventDataType.Debuffs => IsDebuffEvent(entry.Type) && targetIsFriendly == wantFriendly,
            FflogsEventDataType.Buffs => IsBuffEvent(entry.Type) && targetIsFriendly == wantFriendly,
            FflogsEventDataType.Deaths => string.Equals(entry.Type, "death", StringComparison.Ordinal) && targetIsFriendly == wantFriendly,
            FflogsEventDataType.CombatantInfo => false,
            null when IsDamageEvent(entry.Type) || string.Equals(entry.Type, "death", StringComparison.Ordinal) =>
                targetIsFriendly == wantFriendly,
            null => true,
            _ => false,
        };
    }

    private Func<FflogsEvent, bool> ParseFilterExpression(string expression)
    {
        var clauses = Regex.Split(expression, @"\s+or\s+", RegexOptions.IgnoreCase)
            .Select(ParseFilterClause)
            .ToArray();
        return entry => clauses.Any(clause => clause(entry));
    }

    private Func<FflogsEvent, bool> ParseFilterClause(string rawClause)
    {
        var clause = rawClause.Trim();
        var match = Regex.Match(clause, "^type\\s*=\\s*'([^']*)'$", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var type = match.Groups[1].Value;
            return entry => string.Equals(entry.Type, type, StringComparison.Ordinal);
        }

        match = Regex.Match(
            clause,
            @"^ability\.id\s*(?:=\s*(\d+)|in\s*\(([^)]*)\))$",
            RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var values = match.Groups[1].Success
                ? new[] { match.Groups[1].Value }
                : match.Groups[2].Value.Split(',');
            var abilityIds = values
                .Select(value => uint.Parse(value.Trim(), CultureInfo.InvariantCulture))
                .ToHashSet();
            return entry => entry.AbilityGameID is { } abilityId && abilityIds.Contains(abilityId);
        }

        match = Regex.Match(
            clause,
            @"^source\.name\s*(?:=\s*'([^']*)'|in\s*\(([^)]*)\))$",
            RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var values = match.Groups[1].Success
                ? new[] { match.Groups[1].Value }
                : match.Groups[2].Value.Split(',');
            var names = values
                .Select(value => value.Trim().Trim('\''))
                .ToHashSet(StringComparer.Ordinal);
            return entry => entry.SourceID is { } sourceId &&
                actorNames.TryGetValue(sourceId, out var name) &&
                names.Contains(name);
        }

        throw new InvalidOperationException($"Local pull query cannot evaluate filter clause: {clause}");
    }

    private static bool IsCastEvent(string type) => type is "begincast" or "cast";

    private static bool IsDamageEvent(string type) => type is "damage" or "calculateddamage";

    private static bool IsDebuffEvent(string type) => type is
        "applydebuff" or "removedebuff" or "applydebuffstack" or "removedebuffstack" or "refreshdebuff";

    private static bool IsBuffEvent(string type) => type is
        "applybuff" or "removebuff" or "applybuffstack" or "removebuffstack" or "refreshbuff";

    private sealed class Builder
    {
        private readonly PullDeathSnapshot pull;
        private readonly IReadOnlyList<ReplayDebuffSnapshot> replayDebuffs;
        private readonly ActorIndex actors = new();
        private readonly List<LocalEvent> output = [];
        private readonly Dictionary<int, ReplayPositionSnapshot[]> positionTracks = [];
        private readonly Dictionary<int, ReplayMechanicSnapshot[]> mechanicPositionTracks = [];
        private readonly List<KnownDamage> knownDamage = [];

        internal Builder(PullDeathSnapshot pull)
        {
            this.pull = pull;
            replayDebuffs = ReplayDebuffSourceCorrectionPolicy.NormalizeForAnalysis(pull.ReplayDebuffs);
        }

        internal LocalPullEventSource Build()
        {
            IndexActors();
            BuildPositionTracks();
            AddPositionSamples();
            AddDebuffs();
            AddMarkers();
            AddKnownDamageAndDeaths();
            AddMechanics();
            AddAnalyzerEvents();
            AddMissingAnalyzerAnchors();

            var durationMs = Math.Max(
                pull.PullElapsedSeconds * 1000.0,
                output.Select(entry => entry.Event.Timestamp).DefaultIfEmpty(0).Max());
            var fight = new FflogsFight
            {
                Id = LocalFightId(pull),
                Name = string.IsNullOrWhiteSpace(pull.TerritoryName) ? "Dancing Mad" : pull.TerritoryName,
                EncounterID = WtfDigAnalyzerCatalog.DancingMadEncounterId,
                Kill = pull.Reason.Contains("complete", StringComparison.OrdinalIgnoreCase) ? true : null,
                StartTime = 0,
                EndTime = durationMs,
                FriendlyPlayers = actors.FriendlyIds,
            };
            var startedAtUtc = pull.CapturedAtUtc.AddMilliseconds(-durationMs);
            var reportStart = new DateTimeOffset(DateTime.SpecifyKind(startedAtUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
            var report = new FflogsReportSummary
            {
                Code = $"local:{pull.PullNumber}:{pull.CapturedAtUtc.Ticks}",
                Title = $"Better Deaths Pull {pull.PullNumber}",
                StartTime = reportStart,
                EndTime = reportStart + durationMs,
                Zone = new FflogsZone { Id = unchecked((int)pull.TerritoryId), Name = pull.TerritoryName },
                Fights = [fight],
                Actors = actors.BuildActors(),
                Abilities = BuildAbilities(),
            };
            return new LocalPullEventSource(
                report,
                fight,
                output.OrderBy(entry => entry.Event.Timestamp).ToArray(),
                BuildAnalyzerAvailability());
        }

        private IReadOnlyDictionary<string, WtfDigLocalAnalyzerAvailability> BuildAnalyzerAvailability()
        {
            return WtfDigAnalyzerCatalog.All.ToDictionary(
                analyzer => analyzer.Key,
                analyzer => HasExactAnalyzerAnchor(analyzer.AnchorAbilityId)
                    ? new WtfDigLocalAnalyzerAvailability(
                        WtfDigLocalDataQuality.Exact,
                        "The mechanic timing was recorded directly in this pull.")
                    : HasAnalyzerFallbackEvidence(analyzer.Key)
                        ? new WtfDigLocalAnalyzerAvailability(
                            WtfDigLocalDataQuality.Estimated,
                            "This pull is usable, but its mechanic timing is estimated from nearby fight data.")
                        : new WtfDigLocalAnalyzerAvailability(
                            WtfDigLocalDataQuality.Unavailable,
                            "This pull does not contain enough recorded data for this mechanic."),
                StringComparer.Ordinal);
        }

        private bool HasExactAnalyzerAnchor(uint abilityId)
        {
            return pull.ReplayAnalyzerEvents.Any(entry =>
                    entry.AbilityId == abilityId &&
                    string.Equals(entry.EventType, "cast", StringComparison.OrdinalIgnoreCase)) ||
                pull.ReplayMechanics.Any(mechanic =>
                    mechanic.RawEventId == abilityId &&
                    !IsPositionOnlyObject(mechanic));
        }

        private bool HasAnalyzerFallbackEvidence(string analyzerKey)
        {
            return analyzerKey switch
            {
                "arrows" => replayDebuffs.Any(change => change.Status.Id is
                    4876 or 4877 or 4878 or 4879 or 5079 or 5080 or 5081 or 5082),
                "forsaken" => pull.ReplayMechanics.Any(mechanic => mechanic.RawEventId is
                    47806 or 47808 or 47809 or 47810 or 47836 or 47837),
                "kefka-lc" => false,
                "black-hole" => pull.ReplayMechanics.Any(mechanic => mechanic.RawEventId == BlackHoleAnalyzer.NothingnessId) ||
                    replayDebuffs.Any(change => change.Status.Id is 5452 or 5453 or 5454),
                "kefka-says" => pull.ReplayMechanics.Any(mechanic => mechanic.RawEventId == 47892),
                _ => false,
            };
        }

        private void IndexActors()
        {
            foreach (var position in pull.ReplayPositions
                .OrderBy(position => position.ActorKind)
                .ThenBy(position => position.PartyIndex)
                .ThenBy(position => position.SeenAtUtc))
            {
                actors.GetPositionActor(position);
            }

            foreach (var debuff in replayDebuffs)
            {
                actors.GetPlayer(debuff.MemberKey, debuff.MemberName, debuff.PartyIndex, debuff.ClassJobId, debuff.ClassJobName);
            }

            foreach (var death in pull.Deaths)
            {
                actors.GetPlayer(death.MemberKey, death.MemberName, death.PartyIndex, death.ClassJobId, death.ClassJobName);
            }

            foreach (var marker in pull.ReplayMarkers)
            {
                actors.GetMarkerActor(marker);
            }

            foreach (var mechanic in pull.ReplayMechanics)
            {
                actors.GetMechanicSource(mechanic);
            }

            foreach (var analyzerEvent in pull.ReplayAnalyzerEvents)
            {
                actors.GetCombatSource(analyzerEvent.SourceEntityId, analyzerEvent.SourceName);
            }
        }

        private void BuildPositionTracks()
        {
            foreach (var group in pull.ReplayPositions.GroupBy(actors.GetPositionActor))
            {
                positionTracks[group.Key] = group.OrderBy(position => position.PullElapsedSeconds).ToArray();
            }

            foreach (var group in pull.ReplayMechanics
                .Where(IsPositionOnlyObject)
                .GroupBy(actors.GetMechanicSource))
            {
                mechanicPositionTracks[group.Key] = group
                    .OrderBy(position => position.PullElapsedSeconds)
                    .ToArray();
            }
        }

        private void AddPositionSamples()
        {
            foreach (var position in pull.ReplayPositions)
            {
                var actorId = actors.GetPositionActor(position);
                output.Add(new LocalEvent(
                    new FflogsEvent
                    {
                        Timestamp = ToMilliseconds(position.PullElapsedSeconds),
                        Type = "cast",
                        SourceID = actorId,
                        SourceResources = ToResources(position),
                    },
                    FflogsEventDataType.Casts,
                    position.ActorKind == ReplayActorKind.Player
                        ? FflogsHostilityType.Friendlies
                        : FflogsHostilityType.Enemies));
            }
        }

        private void AddDebuffs()
        {
            var active = new HashSet<(int ActorId, uint StatusId, uint SourceId)>();
            foreach (var change in replayDebuffs)
            {
                var actorId = actors.GetPlayer(
                    change.MemberKey,
                    change.MemberName,
                    change.PartyIndex,
                    change.ClassJobId,
                    change.ClassJobName);
                var statusId = ToFflogsStatusId(change.Status.Id);
                var key = (actorId, statusId, change.Status.SourceId);
                string type;
                if (change.Active)
                {
                    type = active.Add(key) ? "applydebuff" : "refreshdebuff";
                }
                else
                {
                    if (!active.Remove(key))
                    {
                        continue;
                    }

                    type = "removedebuff";
                }

                output.Add(new LocalEvent(
                    new FflogsEvent
                    {
                        Timestamp = ToMilliseconds(change.PullElapsedSeconds),
                        Type = type,
                        SourceID = actors.FindByEntity(change.Status.SourceId),
                        TargetID = actorId,
                        AbilityGameID = statusId,
                        Stack = change.Status.StackCount,
                        Duration = change.Status.RemainingTime > 0 ? change.Status.RemainingTime * 1000.0 : 0,
                        TargetResources = ResourceAt(actorId, change.PullElapsedSeconds),
                    },
                    FflogsEventDataType.Debuffs,
                    FflogsHostilityType.Friendlies));
            }
        }

        private void AddMarkers()
        {
            var lastRealityTell = new Dictionary<int, (uint Param, double Time)>();
            foreach (var marker in pull.ReplayMarkers.OrderBy(marker => marker.PullElapsedSeconds))
            {
                var actorId = actors.GetMarkerActor(marker);
                var timestamp = ToMilliseconds(marker.PullElapsedSeconds);
                if (marker.ActorKind == ReplayActorKind.Enemy && marker.MarkerId == RealFakeStatusId)
                {
                    if (lastRealityTell.TryGetValue(actorId, out var previous) &&
                        previous.Param == marker.RawMarkerId &&
                        timestamp - previous.Time < 10_000)
                    {
                        continue;
                    }

                    lastRealityTell[actorId] = (marker.RawMarkerId, timestamp);
                    output.Add(new LocalEvent(
                        new FflogsEvent
                        {
                            Timestamp = timestamp,
                            Type = "applydebuff",
                            TargetID = actorId,
                            AbilityGameID = ToFflogsStatusId(RealFakeStatusId),
                            ExtraInfo = marker.RawMarkerId,
                            TargetResources = ResourceAt(actorId, marker.PullElapsedSeconds),
                        },
                        null,
                        FflogsHostilityType.Enemies));
                    continue;
                }

                output.Add(new LocalEvent(
                    new FflogsEvent
                    {
                        Timestamp = timestamp,
                        Type = "headmarker",
                        SourceID = actorId,
                        TargetID = actorId,
                        MarkerID = marker.MarkerId,
                        SourceResources = ResourceAt(actorId, marker.PullElapsedSeconds),
                        TargetResources = ResourceAt(actorId, marker.PullElapsedSeconds),
                    },
                    null,
                    marker.ActorKind == ReplayActorKind.Player
                        ? FflogsHostilityType.Friendlies
                        : FflogsHostilityType.Enemies));
            }
        }

        private void AddKnownDamageAndDeaths()
        {
            var seenEvents = new HashSet<string>(StringComparer.Ordinal);
            foreach (var death in pull.Deaths)
            {
                var targetId = actors.GetPlayer(
                    death.MemberKey,
                    death.MemberName,
                    death.PartyIndex,
                    death.ClassJobId,
                    death.ClassJobName);
                foreach (var combatEvent in death.RecentEvents.Where(entry => entry.Kind == DeathEventKind.Damage))
                {
                    var eventKey = !string.IsNullOrWhiteSpace(combatEvent.EventIdentity)
                        ? combatEvent.EventIdentity
                        : $"{combatEvent.MemberKey}:{combatEvent.ActionId}:{combatEvent.SeenAtUtc.Ticks}:{combatEvent.Amount}";
                    if (!seenEvents.Add(eventKey))
                    {
                        continue;
                    }

                    var sourceId = actors.GetCombatSource(combatEvent.SourceEntityId, combatEvent.SourceName);
                    var timestamp = ToMilliseconds(combatEvent.PullElapsedSeconds);
                    var targetResources = ResourceAt(targetId, combatEvent.PullElapsedSeconds) ?? new FflogsResources
                    {
                        HitPoints = combatEvent.ResultSeenAtUtc is not null ? combatEvent.ResultCurrentHp : combatEvent.CurrentHp,
                        MaxHitPoints = combatEvent.ResultSeenAtUtc is not null ? combatEvent.ResultMaxHp : combatEvent.MaxHp,
                        Absorb = combatEvent.ResultSeenAtUtc is not null ? combatEvent.ResultShieldHp : combatEvent.ShieldHp,
                    };
                    knownDamage.Add(new KnownDamage(combatEvent.ActionId, targetId, timestamp, combatEvent.Amount));
                    AddDamagePair(
                        timestamp,
                        sourceId,
                        targetId,
                        combatEvent.ActionId,
                        combatEvent.Amount,
                        ResourceAt(sourceId, combatEvent.PullElapsedSeconds),
                        targetResources,
                        sourceId);
                }

                output.Add(new LocalEvent(
                    new FflogsEvent
                    {
                        Timestamp = ToMilliseconds(death.PullElapsedSeconds),
                        Type = "death",
                        TargetID = targetId,
                        KillingAbilityGameID = death.LikelyCause?.ActionId,
                        TargetResources = ResourceAt(targetId, death.PullElapsedSeconds),
                    },
                    FflogsEventDataType.Deaths,
                    FflogsHostilityType.Friendlies));
            }
        }

        private void AddMechanics()
        {
            var mechanics = pull.ReplayMechanics
                .Where(mechanic => mechanic.RawEventId != 0)
                .OrderBy(mechanic => mechanic.PullElapsedSeconds)
                .ToArray();
            var explicitCasts = mechanics.Where(IsCastSnapshot).ToArray();
            var castKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var mechanic in mechanics)
            {
                if (IsPositionOnlyObject(mechanic))
                {
                    continue;
                }

                if (IsForsakenCloneDrop(mechanic))
                {
                    AddForsakenCloneDrop(mechanic, castKeys);
                    continue;
                }

                if (IsForsakenTargetEvidence(mechanic))
                {
                    AddMechanicDamage(mechanic);
                    continue;
                }

                if (mechanic.Shape == ReplayMechanicShape.Tether)
                {
                    AddTether(mechanic);
                    continue;
                }

                if (IsCastSnapshot(mechanic))
                {
                    if (IsSupersededForsakenCleaveCast(mechanic, mechanics))
                    {
                        continue;
                    }

                    AddCast(mechanic, castKeys, predicted: true);
                    continue;
                }

                if (IsForsakenCleaveResolve(mechanic))
                {
                    AddForsakenCleaveCast(mechanic, explicitCasts, castKeys);
                    AddMechanicDamage(mechanic);
                    continue;
                }

                var sourceId = actors.GetMechanicSource(mechanic);
                var hasNearbyCast = explicitCasts.Any(cast =>
                    cast.RawEventId == mechanic.RawEventId &&
                    actors.GetMechanicSource(cast) == sourceId &&
                    cast.PullElapsedSeconds <= mechanic.PullElapsedSeconds + 1 &&
                    mechanic.PullElapsedSeconds - cast.PullElapsedSeconds <= 20);
                if (!hasNearbyCast)
                {
                    AddCast(mechanic, castKeys, predicted: false);
                }

                AddMechanicDamage(mechanic);
            }
        }

        private void AddForsakenCloneDrop(ReplayMechanicSnapshot mechanic, ISet<string> castKeys)
        {
            var sourceId = actors.GetMechanicSource(mechanic);
            var targetId = actors.FindPlayerInSourceKey(mechanic.SourceKey);
            if (targetId is null)
            {
                return;
            }

            var timestamp = ToMilliseconds(mechanic.PullElapsedSeconds);
            var roundedTime = (long)Math.Round(timestamp / 100.0);
            if (castKeys.Add($"{sourceId}:{mechanic.RawEventId}:{targetId.Value}:{roundedTime}"))
            {
                output.Add(new LocalEvent(
                    new FflogsEvent
                    {
                        Timestamp = timestamp,
                        Type = "cast",
                        SourceID = sourceId,
                        TargetID = targetId.Value,
                        AbilityGameID = mechanic.RawEventId,
                        SourceInstance = sourceId,
                        SourceResources = ResourceAt(sourceId, mechanic.PullElapsedSeconds),
                        TargetResources = ToResources(mechanic.X, mechanic.Z, mechanic.Rotation),
                    },
                    FflogsEventDataType.Casts,
                    FflogsHostilityType.Enemies));
            }

            var known = knownDamage
                .Where(entry => entry.ActionId == mechanic.RawEventId && entry.TargetId == targetId.Value)
                .OrderBy(entry => Math.Abs(entry.Timestamp - timestamp))
                .FirstOrDefault();
            if (known is not null && Math.Abs(known.Timestamp - timestamp) <= 1_500)
            {
                return;
            }

            AddDamagePair(
                timestamp,
                sourceId,
                targetId.Value,
                mechanic.RawEventId,
                mechanic.RawState,
                ResourceAt(sourceId, mechanic.PullElapsedSeconds),
                ToResources(mechanic.X, mechanic.Z, mechanic.Rotation),
                sourceId);
        }

        private bool IsSupersededForsakenCleaveCast(
            ReplayMechanicSnapshot cast,
            IReadOnlyList<ReplayMechanicSnapshot> mechanics)
        {
            if (cast.RawEventId is not (47836 or 47837))
            {
                return false;
            }

            var sourceId = actors.GetMechanicSource(cast);
            return mechanics.Any(candidate =>
                IsForsakenCleaveResolve(candidate) &&
                candidate.RawEventId == cast.RawEventId &&
                actors.GetMechanicSource(candidate) == sourceId &&
                candidate.PullElapsedSeconds >= cast.PullElapsedSeconds &&
                candidate.PullElapsedSeconds - cast.PullElapsedSeconds <= 7.0f);
        }

        private void AddForsakenCleaveCast(
            ReplayMechanicSnapshot mechanic,
            IReadOnlyList<ReplayMechanicSnapshot> explicitCasts,
            ISet<string> castKeys)
        {
            var sourceId = actors.GetMechanicSource(mechanic);
            var matchingCast = explicitCasts
                .Where(candidate =>
                    candidate.RawEventId == mechanic.RawEventId &&
                    actors.GetMechanicSource(candidate) == sourceId &&
                    candidate.PullElapsedSeconds <= mechanic.PullElapsedSeconds &&
                    mechanic.PullElapsedSeconds - candidate.PullElapsedSeconds <= 7.0f)
                .OrderBy(candidate => candidate.PullElapsedSeconds)
                .FirstOrDefault();
            if (matchingCast is not null)
            {
                output.Add(new LocalEvent(
                    new FflogsEvent
                    {
                        Timestamp = ToMilliseconds(matchingCast.PullElapsedSeconds),
                        Type = "begincast",
                        SourceID = sourceId,
                        AbilityGameID = mechanic.RawEventId,
                        SourceInstance = sourceId,
                        SourceResources = ResourceAt(sourceId, matchingCast.PullElapsedSeconds) ?? MechanicSourceResources(matchingCast),
                    },
                    FflogsEventDataType.Casts,
                    FflogsHostilityType.Enemies));
            }

            var timestamp = ToMilliseconds(mechanic.PullElapsedSeconds);
            var roundedTime = (long)Math.Round(timestamp / 100.0);
            if (!castKeys.Add($"{sourceId}:{mechanic.RawEventId}:{roundedTime}"))
            {
                return;
            }

            output.Add(new LocalEvent(
                new FflogsEvent
                {
                    Timestamp = timestamp,
                    Type = "cast",
                    SourceID = sourceId,
                    AbilityGameID = mechanic.RawEventId,
                    SourceInstance = sourceId,
                    SourceResources = MechanicSourceResources(mechanic),
                },
                FflogsEventDataType.Casts,
                FflogsHostilityType.Enemies));
        }

        private void AddMissingAnalyzerAnchors()
        {
            AddMissingAnchor(
                ArrowsAnalyzer.TeleTrouncingCastGameId,
                replayDebuffs
                    .Where(change => change.Active && change.Status.Id is 4876 or 4877 or 4878 or 4879 or 5079 or 5080 or 5081 or 5082)
                    .Select(change => (float?)(change.PullElapsedSeconds - 2.0f))
                    .OrderBy(value => value)
                    .FirstOrDefault(),
                "Kefka");
            AddMissingAnchor(
                ForsakenAnalyzer.ForsakenCastGameId,
                pull.ReplayMechanics
                    .Where(mechanic => mechanic.RawEventId is 47806 or 47808 or 47809 or 47810)
                    .Select(mechanic => (float?)(mechanic.PullElapsedSeconds - 6.0f))
                    .OrderBy(value => value)
                    .FirstOrDefault(),
                "Kefka");
            AddMissingAnchor(
                BlackHoleAnalyzer.BlackHoleCastId,
                pull.ReplayMechanics
                    .Where(mechanic => mechanic.RawEventId == BlackHoleAnalyzer.NothingnessId)
                    .Select(mechanic => (float?)(mechanic.PullElapsedSeconds - 6.0f))
                    .OrderBy(value => value)
                    .FirstOrDefault(),
                "Kefka");
            AddMissingAnchor(
                P4Analyzer.KefkaSaysId,
                pull.ReplayMechanics
                    .Where(mechanic => mechanic.RawEventId == 47892)
                    .Select(mechanic => (float?)(mechanic.PullElapsedSeconds - 3.0f))
                    .OrderBy(value => value)
                    .FirstOrDefault(),
                "Kefka");
        }

        private void AddAnalyzerEvents()
        {
            foreach (var analyzerEvent in pull.ReplayAnalyzerEvents.OrderBy(entry => entry.PullElapsedSeconds))
            {
                var sourceId = actors.GetCombatSource(analyzerEvent.SourceEntityId, analyzerEvent.SourceName);
                var timestamp = ToMilliseconds(analyzerEvent.PullElapsedSeconds);
                if (output.Any(entry =>
                    IsCastEvent(entry.Event.Type) &&
                    entry.Event.SourceID == sourceId &&
                    entry.Event.AbilityGameID == analyzerEvent.AbilityId &&
                    Math.Abs(entry.Event.Timestamp - timestamp) <= 100))
                {
                    continue;
                }

                output.Add(new LocalEvent(
                    new FflogsEvent
                    {
                        Timestamp = timestamp,
                        Type = string.IsNullOrWhiteSpace(analyzerEvent.EventType) ? "cast" : analyzerEvent.EventType,
                        SourceID = sourceId,
                        SourceInstance = sourceId,
                        AbilityGameID = analyzerEvent.AbilityId,
                        SourceResources = analyzerEvent.HasSourcePosition
                            ? ToResources(analyzerEvent.X, analyzerEvent.Z, analyzerEvent.Rotation)
                            : ResourceAt(sourceId, analyzerEvent.PullElapsedSeconds),
                    },
                    FflogsEventDataType.Casts,
                    FflogsHostilityType.Enemies));
            }
        }

        private void AddMissingAnchor(uint actionId, float? elapsedSeconds, string sourceName)
        {
            if (elapsedSeconds is not { } seconds ||
                output.Any(entry => entry.Event.Type == "cast" && entry.Event.AbilityGameID == actionId))
            {
                return;
            }

            seconds = Math.Max(0, seconds);
            var sourceId = actors.GetCombatSource(0, sourceName);
            output.Add(new LocalEvent(
                new FflogsEvent
                {
                    Timestamp = ToMilliseconds(seconds),
                    Type = "cast",
                    SourceID = sourceId,
                    SourceInstance = sourceId,
                    AbilityGameID = actionId,
                    SourceResources = ResourceAt(sourceId, seconds),
                },
                FflogsEventDataType.Casts,
                FflogsHostilityType.Enemies));
        }

        private void AddCast(ReplayMechanicSnapshot mechanic, ISet<string> castKeys, bool predicted)
        {
            var sourceId = actors.GetMechanicSource(mechanic);
            var eventSeconds = predicted
                ? ForsakenCleavePosePolicy.PredictedResultElapsedSeconds(mechanic)
                : mechanic.PullElapsedSeconds;
            var timestamp = ToMilliseconds(eventSeconds);
            var roundedTime = (long)Math.Round(timestamp / 100.0);
            var key = $"{sourceId}:{mechanic.RawEventId}:{roundedTime}";
            if (!castKeys.Add(key))
            {
                return;
            }

            output.Add(new LocalEvent(
                new FflogsEvent
                {
                    Timestamp = timestamp,
                    Type = "cast",
                    SourceID = sourceId,
                    AbilityGameID = mechanic.RawEventId,
                    SourceInstance = sourceId,
                    SourceResources = ResourceAt(sourceId, eventSeconds) ?? MechanicSourceResources(mechanic),
                },
                FflogsEventDataType.Casts,
                FflogsHostilityType.Enemies));
        }

        private void AddMechanicDamage(ReplayMechanicSnapshot mechanic)
        {
            if (string.Equals(mechanic.RawEventKind, "action-sheet-action", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (IsForsakenTargetEvidence(mechanic))
            {
                var targetId = actors.FindPlayerInSourceKey(mechanic.SourceKey);
                if (targetId is null)
                {
                    return;
                }

                var evidenceTimestamp = ToMilliseconds(mechanic.PullElapsedSeconds);
                var known = knownDamage
                    .Where(entry => entry.ActionId == mechanic.RawEventId && entry.TargetId == targetId.Value)
                    .OrderBy(entry => Math.Abs(entry.Timestamp - evidenceTimestamp))
                    .FirstOrDefault();
                if (known is not null && Math.Abs(known.Timestamp - evidenceTimestamp) <= 1_500)
                {
                    return;
                }

                var evidenceSourceId = actors.GetMechanicSource(mechanic);
                AddDamagePair(
                    evidenceTimestamp,
                    evidenceSourceId,
                    targetId.Value,
                    mechanic.RawEventId,
                    mechanic.RawState,
                    ResourceAt(evidenceSourceId, mechanic.PullElapsedSeconds) ?? MechanicSourceResources(mechanic),
                    ResourceAt(targetId.Value, mechanic.PullElapsedSeconds) ?? ToResources(mechanic.X, mechanic.Z, mechanic.Rotation),
                    evidenceSourceId);
                return;
            }

            var sourceId = actors.GetMechanicSource(mechanic);
            var eventSeconds = mechanic.RawEventId == PathOfLightActionId &&
                string.Equals(mechanic.RawEventKind, "dmu-p2-path-of-light", StringComparison.OrdinalIgnoreCase)
                ? mechanic.PullElapsedSeconds + mechanic.DurationSeconds
                : mechanic.PullElapsedSeconds;
            var timestamp = ToMilliseconds(eventSeconds);
            if (HasExactForsakenTargetEvidence(mechanic, eventSeconds))
            {
                return;
            }

            if (string.Equals(mechanic.RawEventKind, "black-hole-blast", StringComparison.OrdinalIgnoreCase))
            {
                var targetId = actors.FindPlayerInSourceKey(mechanic.SourceKey);
                if (targetId is not null)
                {
                    AddDamagePair(
                        timestamp,
                        sourceId,
                        targetId.Value,
                        mechanic.RawEventId,
                        mechanic.RawState,
                        BlackHoleSourceResources(mechanic, sourceId),
                        ResourceAt(targetId.Value, eventSeconds),
                        sourceId);
                }

                return;
            }

            if (mechanic.RawEventId == PathOfLightActionId &&
                !string.Equals(mechanic.RawEventKind, "dmu-p2-path-of-light", StringComparison.OrdinalIgnoreCase) &&
                pull.ReplayMechanics.Any(candidate =>
                    string.Equals(candidate.RawEventKind, "dmu-p2-path-of-light", StringComparison.OrdinalIgnoreCase) &&
                    Math.Abs(candidate.PullElapsedSeconds + candidate.DurationSeconds - mechanic.PullElapsedSeconds) <= 2.0f))
            {
                return;
            }

            output.Add(new LocalEvent(
                new FflogsEvent
                {
                    Timestamp = timestamp,
                    Type = "damage",
                    SourceID = sourceId,
                    AbilityGameID = mechanic.RawEventId,
                    SourceInstance = sourceId,
                    SourceResources = ResourceAt(sourceId, eventSeconds) ?? MechanicSourceResources(mechanic),
                },
                FflogsEventDataType.DamageTaken,
                FflogsHostilityType.Friendlies));

            if (mechanic.RawEventId == BlackHoleAnalyzer.NothingnessId &&
                pull.ReplayMechanics.Any(candidate =>
                    string.Equals(candidate.RawEventKind, "black-hole-blast", StringComparison.OrdinalIgnoreCase) &&
                    Math.Abs(candidate.PullElapsedSeconds - mechanic.PullElapsedSeconds) <= 0.25f))
            {
                return;
            }

            foreach (var targetId in AffectedPlayers(mechanic, eventSeconds))
            {
                var known = knownDamage
                    .Where(entry => entry.ActionId == mechanic.RawEventId && entry.TargetId == targetId)
                    .OrderBy(entry => Math.Abs(entry.Timestamp - timestamp))
                    .FirstOrDefault();
                if (known is not null && Math.Abs(known.Timestamp - timestamp) <= 1_500)
                {
                    continue;
                }

                var amount = EstimateHpLoss(targetId, eventSeconds);
                AddDamagePair(
                    timestamp,
                    sourceId,
                    targetId,
                    mechanic.RawEventId,
                    Math.Max(1, amount),
                    ResourceAt(sourceId, eventSeconds) ?? MechanicSourceResources(mechanic),
                    ResourceAt(targetId, eventSeconds),
                    sourceId);
            }
        }

        private bool HasExactForsakenTargetEvidence(ReplayMechanicSnapshot mechanic, float eventSeconds)
        {
            if (mechanic.RawEventId is not (47806 or 47808 or 47809 or 47810))
            {
                return false;
            }

            return pull.ReplayMechanics.Any(candidate =>
                IsForsakenTargetEvidence(candidate) &&
                candidate.RawEventId == mechanic.RawEventId &&
                Math.Abs(candidate.PullElapsedSeconds - eventSeconds) <= 1.0f);
        }

        private void AddTether(ReplayMechanicSnapshot mechanic)
        {
            if (!actors.TryResolveTether(mechanic.SourceKey, out var sourceId, out var targetId))
            {
                return;
            }

            var direction = Direction(mechanic.Rotation);
            var source = new Vector2(mechanic.X, mechanic.Z) - direction * mechanic.Length * 0.5f;
            var target = new Vector2(mechanic.X, mechanic.Z) + direction * mechanic.Length * 0.5f;
            output.Add(new LocalEvent(
                new FflogsEvent
                {
                    Timestamp = ToMilliseconds(mechanic.PullElapsedSeconds),
                    Type = "tether",
                    SourceID = sourceId,
                    TargetID = targetId,
                    SourceInstance = sourceId,
                    AbilityGameID = mechanic.RawEventId,
                    SourceResources = ResourceAt(sourceId, mechanic.PullElapsedSeconds) ?? ToResources(source.X, source.Y, mechanic.Rotation),
                    TargetResources = ResourceAt(targetId, mechanic.PullElapsedSeconds) ?? ToResources(target.X, target.Y, 0),
                },
                null,
                FflogsHostilityType.Enemies));
        }

        private void AddDamagePair(
            double timestamp,
            int sourceId,
            int targetId,
            uint actionId,
            long amount,
            FflogsResources? sourceResources,
            FflogsResources? targetResources,
            int sourceInstance)
        {
            foreach (var type in new[] { "calculateddamage", "damage" })
            {
                output.Add(new LocalEvent(
                    new FflogsEvent
                    {
                        Timestamp = timestamp,
                        Type = type,
                        SourceID = sourceId,
                        TargetID = targetId,
                        AbilityGameID = actionId,
                        Amount = amount,
                        UnmitigatedAmount = amount,
                        SourceInstance = sourceInstance,
                        SourceResources = sourceResources,
                        TargetResources = targetResources,
                    },
                    FflogsEventDataType.DamageTaken,
                    FflogsHostilityType.Friendlies));
            }
        }

        private IReadOnlyList<int> AffectedPlayers(ReplayMechanicSnapshot mechanic, float eventSeconds)
        {
            var result = new List<int>();
            foreach (var actorId in actors.FriendlyIds)
            {
                var snapshot = PositionAt(actorId, eventSeconds);
                if (snapshot is null || snapshot.IsDead)
                {
                    continue;
                }

                var point = new Vector2(snapshot.X, snapshot.Z);
                if (Contains(mechanic, point))
                {
                    result.Add(actorId);
                }
            }

            return result;
        }

        private static bool Contains(ReplayMechanicSnapshot mechanic, Vector2 point)
        {
            var center = new Vector2(mechanic.X, mechanic.Z);
            var offset = point - center;
            var radius = Math.Max(0.5f, mechanic.Radius);
            switch (mechanic.Shape)
            {
                case ReplayMechanicShape.Circle:
                case ReplayMechanicShape.Tower:
                case ReplayMechanicShape.Stack:
                case ReplayMechanicShape.Spread:
                    return offset.Length() <= radius + 0.75f;
                case ReplayMechanicShape.Donut:
                    var distance = offset.Length();
                    return distance <= radius + 0.75f && distance >= Math.Max(0, mechanic.Width - 0.75f);
                case ReplayMechanicShape.Cone:
                    var length = Math.Max(radius, mechanic.Length);
                    if (offset.Length() > length + 0.75f)
                    {
                        return false;
                    }

                    var forward = Direction(mechanic.Rotation);
                    var normalized = offset.LengthSquared() <= 0.001f ? forward : Vector2.Normalize(offset);
                    var dot = Math.Clamp(Vector2.Dot(forward, normalized), -1.0f, 1.0f);
                    var angle = MathF.Acos(dot) * 180.0f / MathF.PI;
                    return angle <= mechanic.AngleDegrees * 0.5f + 2.0f;
                case ReplayMechanicShape.Line:
                    var direction = Direction(mechanic.Rotation);
                    var side = new Vector2(direction.Y, -direction.X);
                    return Math.Abs(Vector2.Dot(offset, direction)) <= mechanic.Length * 0.5f + 0.75f &&
                        Math.Abs(Vector2.Dot(offset, side)) <= mechanic.Width * 0.5f + 0.75f;
                default:
                    return false;
            }
        }

        private long EstimateHpLoss(int actorId, float eventSeconds)
        {
            if (!positionTracks.TryGetValue(actorId, out var track) || track.Length == 0)
            {
                return 0;
            }

            var before = track.LastOrDefault(position => position.PullElapsedSeconds <= eventSeconds - 0.02f);
            var after = track.FirstOrDefault(position => position.PullElapsedSeconds >= eventSeconds);
            if (before is null || after is null || after.PullElapsedSeconds - before.PullElapsedSeconds > 1.5f)
            {
                return 0;
            }

            var beforeTotal = (long)before.CurrentHp + before.ShieldHp;
            var afterTotal = (long)after.CurrentHp + after.ShieldHp;
            return Math.Max(0, beforeTotal - afterTotal);
        }

        private FflogsResources? BlackHoleSourceResources(ReplayMechanicSnapshot mechanic, int sourceId)
        {
            var direct = ResourceAt(sourceId, mechanic.PullElapsedSeconds);
            if (direct is not null)
            {
                return direct;
            }

            var tether = pull.ReplayMechanics
                .Where(candidate => candidate.Shape == ReplayMechanicShape.Tether &&
                    candidate.SourceKey.Contains(FirstHexToken(mechanic.SourceKey) ?? "\0", StringComparison.OrdinalIgnoreCase))
                .OrderBy(candidate => Math.Abs(candidate.PullElapsedSeconds - mechanic.PullElapsedSeconds))
                .FirstOrDefault();
            if (tether is null)
            {
                return MechanicSourceResources(mechanic);
            }

            var direction = Direction(tether.Rotation);
            var source = new Vector2(tether.X, tether.Z) - direction * tether.Length * 0.5f;
            return ToResources(source.X, source.Y, tether.Rotation);
        }

        private FflogsResources? ResourceAt(int? actorId, float elapsedSeconds)
        {
            if (actorId is not { } id)
            {
                return null;
            }

            var snapshot = PositionAt(id, elapsedSeconds);
            if (snapshot is not null)
            {
                return ToResources(snapshot);
            }

            var mechanicPosition = MechanicPositionAt(id, elapsedSeconds);
            return mechanicPosition is null
                ? null
                : ToResources(mechanicPosition.X, mechanicPosition.Z, mechanicPosition.Rotation);
        }

        private ReplayMechanicSnapshot? MechanicPositionAt(int actorId, float elapsedSeconds)
        {
            if (!mechanicPositionTracks.TryGetValue(actorId, out var track) || track.Length == 0)
            {
                return null;
            }

            return NearestAt(track, elapsedSeconds, position => position.PullElapsedSeconds);
        }

        private ReplayPositionSnapshot? PositionAt(int actorId, float elapsedSeconds)
        {
            if (!positionTracks.TryGetValue(actorId, out var track) || track.Length == 0)
            {
                return null;
            }

            var low = 0;
            var high = track.Length - 1;
            while (low <= high)
            {
                var middle = low + ((high - low) / 2);
                if (track[middle].PullElapsedSeconds < elapsedSeconds)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            if (low <= 0)
            {
                return track[0];
            }

            if (low >= track.Length)
            {
                return track[^1];
            }

            return Math.Abs(track[low].PullElapsedSeconds - elapsedSeconds) <
                Math.Abs(track[low - 1].PullElapsedSeconds - elapsedSeconds)
                    ? track[low]
                    : track[low - 1];
        }

        private IReadOnlyList<FflogsAbility> BuildAbilities()
        {
            var abilities = CanonicalAbilityNames.ToDictionary(entry => entry.Key, entry => entry.Value);
            foreach (var mechanic in pull.ReplayMechanics.Where(mechanic => mechanic.RawEventId != 0))
            {
                abilities[mechanic.RawEventId] = AbilityName(mechanic.RawEventId, mechanic.Label);
            }

            foreach (var analyzerEvent in pull.ReplayAnalyzerEvents.Where(entry => entry.AbilityId != 0))
            {
                abilities[analyzerEvent.AbilityId] = AbilityName(analyzerEvent.AbilityId, analyzerEvent.AbilityName);
            }

            foreach (var change in replayDebuffs)
            {
                abilities[ToFflogsStatusId(change.Status.Id)] = change.Status.Name;
            }

            abilities[ToFflogsStatusId(RealFakeStatusId)] = "Real/Fake";
            foreach (var combatEvent in pull.Deaths.SelectMany(death => death.RecentEvents))
            {
                if (combatEvent.ActionId != 0)
                {
                    abilities[combatEvent.ActionId] = AbilityName(combatEvent.ActionId, combatEvent.ActionName);
                }
            }

            return abilities
                .OrderBy(entry => entry.Key)
                .Select(entry => new FflogsAbility { GameID = entry.Key, Name = entry.Value })
                .ToArray();
        }

        private static FflogsResources MechanicSourceResources(ReplayMechanicSnapshot mechanic)
        {
            var point = new Vector2(mechanic.X, mechanic.Z);
            if (mechanic.Shape == ReplayMechanicShape.Line && mechanic.Length > 0)
            {
                point -= Direction(mechanic.Rotation) * mechanic.Length * 0.5f;
            }

            return ToResources(point.X, point.Y, mechanic.Rotation);
        }

        private static int LocalFightId(PullDeathSnapshot pull)
        {
            var value = pull.PullNumber > 0 ? pull.PullNumber : pull.CapturedAtUtc.Ticks;
            return (int)(Math.Abs(value % (int.MaxValue - 1)) + 1);
        }
    }

    private sealed class ActorIndex
    {
        private readonly Dictionary<string, ActorSeed> byKey = new(StringComparer.Ordinal);
        private readonly Dictionary<uint, int> byEntity = [];
        private readonly Dictionary<int, ActorSeed> byId = [];
        private readonly Dictionary<string, List<int>> byName = new(StringComparer.OrdinalIgnoreCase);
        private int nextId = 1;

        internal IReadOnlyList<int> FriendlyIds => byId.Values
            .Where(actor => actor.Friendly)
            .OrderBy(actor => actor.PartyIndex)
            .ThenBy(actor => actor.Id)
            .Select(actor => actor.Id)
            .ToArray();

        internal int GetPositionActor(ReplayPositionSnapshot position)
        {
            return position.ActorKind == ReplayActorKind.Player
                ? GetPlayer(
                    PlayerKey(position.ActorKey),
                    position.ActorName,
                    position.PartyIndex,
                    position.ClassJobId,
                    position.ClassJobName,
                    position.EntityId)
                : GetOrAdd(
                    string.IsNullOrWhiteSpace(position.ActorKey) ? $"enemy:{position.EntityId:X8}" : position.ActorKey,
                    position.ActorName,
                    false,
                    position.PartyIndex,
                    0,
                    string.Empty,
                    position.EntityId);
        }

        internal int GetPlayer(
            string memberKey,
            string name,
            int partyIndex,
            uint classJobId,
            string classJobName,
            uint entityId = 0)
        {
            return GetOrAdd(
                $"player:{PlayerKey(memberKey)}",
                name,
                true,
                partyIndex,
                classJobId,
                classJobName,
                entityId);
        }

        internal int GetMarkerActor(ReplayMarkerSnapshot marker)
        {
            return marker.ActorKind == ReplayActorKind.Player
                ? GetPlayer(
                    PlayerKey(marker.ActorKey),
                    marker.ActorName,
                    marker.PartyIndex,
                    marker.ClassJobId,
                    marker.ClassJobName,
                    marker.EntityId)
                : GetOrAdd(
                    string.IsNullOrWhiteSpace(marker.ActorKey) ? $"enemy:{marker.EntityId:X8}" : marker.ActorKey,
                    marker.ActorName,
                    false,
                    marker.PartyIndex,
                    0,
                    string.Empty,
                    marker.EntityId);
        }

        internal int GetMechanicSource(ReplayMechanicSnapshot mechanic)
        {
            if (IsForsakenTargetEvidence(mechanic))
            {
                var evidenceSource = HexTokens(mechanic.SourceKey).FirstOrDefault();
                if (evidenceSource != 0)
                {
                    return GetCombatSource(evidenceSource, mechanic.SourceName);
                }
            }

            var entity = MechanicSourceEntity(mechanic.SourceKey);
            if (entity != 0)
            {
                if (byEntity.TryGetValue(entity, out var existing))
                {
                    return existing;
                }

                return GetOrAdd(
                    $"enemy:{entity:X8}",
                    SourceName(mechanic.SourceName),
                    false,
                    2000,
                    0,
                    string.Empty,
                    entity);
            }

            if (byEntity.TryGetValue(mechanic.RawState, out var rawStateActor) && !byId[rawStateActor].Friendly)
            {
                return rawStateActor;
            }

            var name = SourceName(mechanic.SourceName);
            if (byName.TryGetValue(name, out var named))
            {
                var enemy = named.FirstOrDefault(id => !byId[id].Friendly);
                if (enemy != 0)
                {
                    return enemy;
                }
            }

            return GetOrAdd(
                $"mechanic:{name}",
                name,
                false,
                2000,
                0,
                string.Empty,
                0);
        }

        internal int GetCombatSource(uint entityId, string name)
        {
            var cleanName = SourceName(name);
            if (entityId != 0)
            {
                if (byEntity.TryGetValue(entityId, out var existing))
                {
                    return existing;
                }

                return GetOrAdd(
                    $"enemy:{entityId:X8}",
                    cleanName,
                    false,
                    2000,
                    0,
                    string.Empty,
                    entityId);
            }

            if (byName.TryGetValue(cleanName, out var named))
            {
                var enemy = named.FirstOrDefault(id => !byId[id].Friendly);
                if (enemy != 0)
                {
                    return enemy;
                }
            }

            return GetOrAdd(
                entityId == 0 ? $"source:{cleanName}" : $"enemy:{entityId:X8}",
                cleanName,
                false,
                2000,
                0,
                string.Empty,
                entityId);
        }

        internal int? FindByEntity(uint entityId) => byEntity.GetValueOrDefault(entityId) is var id && id != 0 ? id : null;

        internal int? FindPlayerInSourceKey(string sourceKey)
        {
            return byId.Values
                .Where(actor => actor.Friendly)
                .Where(actor => sourceKey.Contains($":{actor.OriginalKey}:", StringComparison.Ordinal) ||
                    sourceKey.EndsWith($":{actor.OriginalKey}", StringComparison.Ordinal))
                .Select(actor => (int?)actor.Id)
                .FirstOrDefault();
        }

        internal bool TryResolveTether(string sourceKey, out int sourceId, out int targetId)
        {
            var entities = HexTokens(sourceKey).ToArray();
            sourceId = entities.Length > 0 && byEntity.TryGetValue(entities[0], out var source) ? source : 0;
            targetId = entities.Length > 1 && byEntity.TryGetValue(entities[1], out var target) ? target : 0;
            return sourceId != 0 && targetId != 0;
        }

        internal IReadOnlyList<FflogsActor> BuildActors() => byId.Values
            .OrderBy(actor => actor.Id)
            .Select(actor => new FflogsActor
            {
                Id = actor.Id,
                Name = actor.Name,
                Type = actor.Friendly ? "Player" : "NPC",
                SubType = actor.Friendly ? JobName(actor.ClassJobId, actor.ClassJobName) : "Boss",
            })
            .ToArray();

        private int GetOrAdd(
            string key,
            string name,
            bool friendly,
            int partyIndex,
            uint classJobId,
            string classJobName,
            uint entityId)
        {
            if (byKey.TryGetValue(key, out var existing))
            {
                existing.Update(name, partyIndex, classJobId, classJobName, entityId);
                if (entityId != 0)
                {
                    byEntity[entityId] = existing.Id;
                }

                return existing.Id;
            }

            var seed = new ActorSeed(nextId++, key.StartsWith("player:", StringComparison.Ordinal) ? key[7..] : key, name, friendly,
                partyIndex, classJobId, classJobName, entityId);
            byKey[key] = seed;
            byId[seed.Id] = seed;
            if (entityId != 0)
            {
                byEntity[entityId] = seed.Id;
            }

            if (!byName.TryGetValue(seed.Name, out var named))
            {
                named = [];
                byName[seed.Name] = named;
            }

            named.Add(seed.Id);
            return seed.Id;
        }

        private static string PlayerKey(string key) => key.StartsWith("player:", StringComparison.Ordinal) ? key[7..] : key;
    }

    private sealed class ActorSeed(
        int id,
        string originalKey,
        string name,
        bool friendly,
        int partyIndex,
        uint classJobId,
        string classJobName,
        uint entityId)
    {
        internal int Id { get; } = id;
        internal string OriginalKey { get; } = originalKey;
        internal string Name { get; private set; } = name;
        internal bool Friendly { get; } = friendly;
        internal int PartyIndex { get; private set; } = partyIndex;
        internal uint ClassJobId { get; private set; } = classJobId;
        internal string ClassJobName { get; private set; } = classJobName;
        internal uint EntityId { get; private set; } = entityId;

        internal void Update(string updatedName, int updatedPartyIndex, uint updatedClassJobId, string updatedClassJobName, uint updatedEntityId)
        {
            if (!string.IsNullOrWhiteSpace(updatedName)) Name = updatedName;
            if (updatedPartyIndex >= 0) PartyIndex = updatedPartyIndex;
            if (updatedClassJobId != 0) ClassJobId = updatedClassJobId;
            if (!string.IsNullOrWhiteSpace(updatedClassJobName)) ClassJobName = updatedClassJobName;
            if (updatedEntityId != 0) EntityId = updatedEntityId;
        }
    }

    private sealed record LocalEvent(
        FflogsEvent Event,
        FflogsEventDataType? DataType,
        FflogsHostilityType Hostility);

    private sealed record KnownDamage(uint ActionId, int TargetId, double Timestamp, long Amount);

    private static bool IsCastSnapshot(ReplayMechanicSnapshot mechanic) =>
        mechanic.RawEventKind.Contains("cast", StringComparison.OrdinalIgnoreCase) ||
        mechanic.RawEventKind.Contains("predicted", StringComparison.OrdinalIgnoreCase);

    private static bool IsPositionOnlyObject(ReplayMechanicSnapshot mechanic) =>
        string.Equals(mechanic.RawEventKind, "object", StringComparison.OrdinalIgnoreCase);

    private static bool IsForsakenTargetEvidence(ReplayMechanicSnapshot mechanic) =>
        string.Equals(
            mechanic.RawEventKind,
            ReplayEncounterModules.DmuP2PathOfLightActivationRawEventKind,
            StringComparison.Ordinal) ||
        string.Equals(
            mechanic.RawEventKind,
            ReplayEncounterModules.DmuP2ForsakenTargetRawEventKind,
            StringComparison.Ordinal);

    private static bool IsForsakenCloneDrop(ReplayMechanicSnapshot mechanic) =>
        ReplayEncounterModules.IsDmuP2ForsakenCloneDropAction(mechanic.RawEventId) &&
        string.Equals(
            mechanic.RawEventKind,
            ReplayEncounterModules.DmuP2ForsakenCloneDropRawEventKind,
            StringComparison.Ordinal);

    private static bool IsForsakenCleaveResolve(ReplayMechanicSnapshot mechanic) =>
        mechanic.RawEventId is 47836 or 47837 &&
        string.Equals(mechanic.RawEventKind, "dmu-p2-all-things-ending", StringComparison.Ordinal);

    private static uint ToFflogsStatusId(uint statusId) => statusId >= 1_000_000 ? statusId : statusId + 1_000_000;

    private static double ToMilliseconds(float seconds) => Math.Max(0, seconds) * 1000.0;

    private static string AbilityName(uint actionId, string fallback) =>
        CanonicalAbilityNames.TryGetValue(actionId, out var name) ? name : fallback;

    private static string JobName(uint classJobId, string fallback)
    {
        if (JobNames.TryGetValue(classJobId, out var name))
        {
            return name;
        }

        return string.IsNullOrWhiteSpace(fallback) ? "Unknown" : fallback.Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    private static string SourceName(string name)
    {
        var separator = name.IndexOf(" -> ", StringComparison.Ordinal);
        return separator < 0 ? name : name[..separator];
    }

    private static Vector2 Direction(float rotation) => new(MathF.Sin(rotation), MathF.Cos(rotation));

    private static FflogsResources ToResources(ReplayPositionSnapshot position) => new()
    {
        HitPoints = position.CurrentHp,
        MaxHitPoints = position.MaxHp,
        Absorb = position.ShieldHp,
        X = position.X * 100.0,
        Y = position.Z * 100.0,
        Facing = ToRawFacing(position.Rotation),
    };

    private static FflogsResources ToResources(float x, float z, float rotation) => new()
    {
        X = x * 100.0,
        Y = z * 100.0,
        Facing = ToRawFacing(rotation),
    };

    private static double ToRawFacing(float rotation) => -rotation * 100.0 - 150.0 * Math.PI;

    private static IEnumerable<uint> HexTokens(string value)
    {
        foreach (var token in value.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length == 8 && uint.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var entityId))
            {
                yield return entityId;
            }
        }
    }

    private static uint MechanicSourceEntity(string sourceKey)
    {
        var tokens = sourceKey.Split(':', StringSplitOptions.RemoveEmptyEntries);
        var sourceIndex = tokens.Length > 2 && string.Equals(tokens[1], "cast", StringComparison.OrdinalIgnoreCase)
            ? 2
            : 1;
        return sourceIndex < tokens.Length &&
            tokens[sourceIndex].Length == 8 &&
            uint.TryParse(tokens[sourceIndex], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var entityId)
                ? entityId
                : 0;
    }

    private static string? FirstHexToken(string value) => value.Split(':', StringSplitOptions.RemoveEmptyEntries)
        .FirstOrDefault(token => token.Length == 8 && uint.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _));

    private static T NearestAt<T>(IReadOnlyList<T> track, float elapsedSeconds, Func<T, float> time)
    {
        var low = 0;
        var high = track.Count - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (time(track[middle]) < elapsedSeconds)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        if (low <= 0)
        {
            return track[0];
        }

        if (low >= track.Count)
        {
            return track[^1];
        }

        return Math.Abs(time(track[low]) - elapsedSeconds) < Math.Abs(time(track[low - 1]) - elapsedSeconds)
            ? track[low]
            : track[low - 1];
    }
}
