namespace BetterDeaths;

using System;
using System.Collections.Generic;
using System.Linq;

internal enum ReplayMarkerResolveGroup
{
    Unknown,
    GroupA,
    GroupB,
}

internal enum ForsakenMarkerKind
{
    Unknown,
    Stack,
    Spread,
    Cone,
}

internal readonly record struct ReplayMarkerInfo(
    string ShortLabel,
    string Description,
    ReplayMechanicShape? Shape = null,
    float Radius = 0.0f,
    float Length = 0.0f,
    float Width = 0.0f,
    float AngleDegrees = 0.0f,
    float DurationSeconds = 8.0f,
    bool AnchorsToActor = true,
    bool ConeBaitsClosestPlayer = false);

internal enum ReplayArenaShape
{
    Circle,
    Square,
}

internal readonly record struct ReplayArenaInfo(
    float CenterX,
    float CenterZ,
    float Radius,
    ReplayArenaShape Shape);

internal interface IReplayEncounterModule
{
    string Name { get; }

    bool AppliesTo(uint territoryId);

    bool TryGetReplayArena(out ReplayArenaInfo arena);

    bool IsReplayOverheadStatus(uint statusId);

    bool TryGetMarkerInfo(uint markerId, out ReplayMarkerInfo info);

    bool ShouldCreateReplayMarkerMechanic(
        ReplayMarkerSnapshot marker,
        IReadOnlyList<ReplayMarkerSnapshot> markers);

    bool ShouldDisplayReplayMarker(
        ReplayMarkerSnapshot marker,
        IReadOnlyList<ReplayMarkerSnapshot> markers,
        IReadOnlyList<ReplayPositionSnapshot> positions,
        DateTime selectedAtUtc);

    IReadOnlyList<ReplayMechanicSnapshot> GetReplayMechanics(PartyDeathRecord death);
}

internal static class ReplayEncounterModules
{
    private static readonly IReplayEncounterModule FallbackModule = new GenericReplayEncounterModule();
    private static readonly IReadOnlyList<IReplayEncounterModule> Modules =
    [
        new DmuReplayEncounterModule(),
    ];

    public static IReplayEncounterModule Get(uint territoryId)
    {
        return Modules.FirstOrDefault(module => module.AppliesTo(territoryId)) ?? FallbackModule;
    }

    public static bool IsReplayOverheadStatus(uint territoryId, uint statusId)
    {
        return Get(territoryId).IsReplayOverheadStatus(statusId);
    }

    private sealed class GenericReplayEncounterModule : IReplayEncounterModule
    {
        public string Name => "Universal";

        public bool AppliesTo(uint territoryId) => true;

        public bool TryGetReplayArena(out ReplayArenaInfo arena)
        {
            arena = default;
            return false;
        }

        public bool IsReplayOverheadStatus(uint statusId) => false;

        public bool TryGetMarkerInfo(uint markerId, out ReplayMarkerInfo info)
        {
            if (TryGetNumber(markerId, out var number))
            {
                info = new ReplayMarkerInfo(number.ToString(), $"Number {number}");
                return true;
            }

            info = markerId == 0
                ? new ReplayMarkerInfo("?", "Unknown marker")
                : new ReplayMarkerInfo($"#{markerId}", "Unknown marker");
            return true;
        }

        public bool ShouldCreateReplayMarkerMechanic(
            ReplayMarkerSnapshot marker,
            IReadOnlyList<ReplayMarkerSnapshot> markers)
        {
            return TryGetMarkerInfo(marker.MarkerId, out var info) &&
                info.Shape is not null;
        }

        public bool ShouldDisplayReplayMarker(
            ReplayMarkerSnapshot marker,
            IReadOnlyList<ReplayMarkerSnapshot> markers,
            IReadOnlyList<ReplayPositionSnapshot> positions,
            DateTime selectedAtUtc) => true;

        public IReadOnlyList<ReplayMechanicSnapshot> GetReplayMechanics(PartyDeathRecord death)
        {
            return death.ReplayMechanics;
        }
    }

    private sealed class DmuReplayEncounterModule : IReplayEncounterModule
    {
        private const uint TerritoryDancingMadUltimate = 1363;
        private static readonly ReplayArenaInfo Arena = new(100.0f, 100.0f, 20.0f, ReplayArenaShape.Circle);
        private static readonly ReplayMarkerResolveGroup[] ForsakenTowerResolveSequence =
        [
            ReplayMarkerResolveGroup.GroupA,
            ReplayMarkerResolveGroup.GroupA,
            ReplayMarkerResolveGroup.GroupA,
            ReplayMarkerResolveGroup.GroupB,
            ReplayMarkerResolveGroup.GroupB,
            ReplayMarkerResolveGroup.GroupB,
            ReplayMarkerResolveGroup.GroupB,
            ReplayMarkerResolveGroup.GroupA,
        ];

        public string Name => "Dancing Mad Ultimate";

        public bool AppliesTo(uint territoryId) => territoryId == TerritoryDancingMadUltimate;

        public bool TryGetReplayArena(out ReplayArenaInfo arena)
        {
            arena = Arena;
            return true;
        }

        public bool IsReplayOverheadStatus(uint statusId)
        {
            return statusId is 3004 or 3005 or 3006 or
                5084 or 5085 or 5086;
        }

        public bool TryGetMarkerInfo(uint markerId, out ReplayMarkerInfo info)
        {
            info = markerId switch
            {
                127 => new ReplayMarkerInfo("Spread", "Fire spread", ReplayMechanicShape.Spread, Radius: 5.0f),
                128 => new ReplayMarkerInfo("Stack", "Fire stack", ReplayMechanicShape.Stack, Radius: 5.0f),
                161 => new ReplayMarkerInfo("Stack", "Final stack", ReplayMechanicShape.Stack, Radius: 5.0f, DurationSeconds: 10.0f),
                715 => new ReplayMarkerInfo("Stack", "Forsaken stack", ReplayMechanicShape.Stack, Radius: 5.0f),
                716 => new ReplayMarkerInfo("Spread", "Forsaken spread", ReplayMechanicShape.Spread, Radius: 5.0f),
                717 => new ReplayMarkerInfo("Cone", "Forsaken cone", ReplayMechanicShape.Cone, Radius: 18.0f, Length: 18.0f, AngleDegrees: 60.0f, ConeBaitsClosestPlayer: true),
                3004 => new ReplayMarkerInfo("1st", "First in line"),
                3005 => new ReplayMarkerInfo("2nd", "Second in line"),
                3006 => new ReplayMarkerInfo("3rd", "Third in line"),
                5084 => new ReplayMarkerInfo("Stack", "Head stack", ReplayMechanicShape.Stack, Radius: 5.0f),
                5085 => new ReplayMarkerInfo("Circle", "Circle", ReplayMechanicShape.Circle, Radius: 5.0f),
                5086 => new ReplayMarkerInfo("Fan", "Fan", ReplayMechanicShape.Cone, Radius: 18.0f, Length: 18.0f, AngleDegrees: 70.0f, ConeBaitsClosestPlayer: true),
                _ => default,
            };
            return !string.IsNullOrEmpty(info.ShortLabel) ||
                !string.IsNullOrEmpty(info.Description) ||
                FallbackModule.TryGetMarkerInfo(markerId, out info);
        }

        public bool ShouldCreateReplayMarkerMechanic(
            ReplayMarkerSnapshot marker,
            IReadOnlyList<ReplayMarkerSnapshot> markers)
        {
            if (!IsForsakenTowerMarker(marker.MarkerId))
            {
                return true;
            }

            var relevantMarkers = GetForsakenTowerMarkers(markers);
            if (relevantMarkers.Count == 0)
            {
                return true;
            }

            var initialBatchEnd = relevantMarkers[0].SeenAtUtc.AddSeconds(3.0);
            return marker.SeenAtUtc > initialBatchEnd;
        }

        public IReadOnlyList<ReplayMechanicSnapshot> GetReplayMechanics(PartyDeathRecord death)
        {
            return death.ReplayMechanics;
        }

        public bool ShouldDisplayReplayMarker(
            ReplayMarkerSnapshot marker,
            IReadOnlyList<ReplayMarkerSnapshot> markers,
            IReadOnlyList<ReplayPositionSnapshot> positions,
            DateTime selectedAtUtc)
        {
            if (!IsForsakenTowerMarker(marker.MarkerId))
            {
                return true;
            }

            var markerGroup = GetForsakenResolveGroup(marker, markers, positions);
            var activeGroup = GetActiveForsakenResolveGroup(markers, positions, selectedAtUtc);
            return markerGroup == ReplayMarkerResolveGroup.Unknown ||
                activeGroup == ReplayMarkerResolveGroup.Unknown ||
                markerGroup == activeGroup;
        }

        private static ReplayMarkerResolveGroup GetActiveForsakenResolveGroup(
            IReadOnlyList<ReplayMarkerSnapshot> markers,
            IReadOnlyList<ReplayPositionSnapshot> positions,
            DateTime selectedAtUtc)
        {
            var relevantMarkers = GetForsakenTowerMarkers(markers);
            if (relevantMarkers.Count == 0)
            {
                return ReplayMarkerResolveGroup.Unknown;
            }

            var initialBatchEnd = relevantMarkers[0].SeenAtUtc.AddSeconds(3.0);
            var updateBatches = BuildForsakenMarkerUpdateBatches(relevantMarkers, initialBatchEnd);
            var actorGroups = BuildForsakenActorGroups(markers, positions);
            if (actorGroups.Count == 0)
            {
                return ReplayMarkerResolveGroup.Unknown;
            }

            var sequenceIndex = 0;
            foreach (var batch in updateBatches.Where(batch => batch[0].SeenAtUtc <= selectedAtUtc))
            {
                var activeGroup = ForsakenTowerResolveSequence[Math.Clamp(sequenceIndex, 0, ForsakenTowerResolveSequence.Length - 1)];
                if (batch.Any(marker => actorGroups.TryGetValue(marker.ActorKey, out var markerGroup) && markerGroup == activeGroup))
                {
                    sequenceIndex = Math.Min(sequenceIndex + 1, ForsakenTowerResolveSequence.Length - 1);
                }
            }

            return ForsakenTowerResolveSequence[sequenceIndex];
        }

        private static ReplayMarkerResolveGroup GetForsakenResolveGroup(
            ReplayMarkerSnapshot marker,
            IReadOnlyList<ReplayMarkerSnapshot> markers,
            IReadOnlyList<ReplayPositionSnapshot> positions)
        {
            var actorGroups = BuildForsakenActorGroups(markers, positions);
            return actorGroups.TryGetValue(marker.ActorKey, out var group)
                ? group
                : ReplayMarkerResolveGroup.Unknown;
        }

        private static IReadOnlyList<IReadOnlyList<ReplayMarkerSnapshot>> BuildForsakenMarkerUpdateBatches(
            IReadOnlyList<ReplayMarkerSnapshot> relevantMarkers,
            DateTime initialBatchEnd)
        {
            var batches = new List<List<ReplayMarkerSnapshot>>();
            foreach (var marker in relevantMarkers.Where(marker => marker.SeenAtUtc > initialBatchEnd))
            {
                if (batches.Count == 0 ||
                    marker.SeenAtUtc - batches[^1][^1].SeenAtUtc > TimeSpan.FromSeconds(1.5))
                {
                    batches.Add([marker]);
                    continue;
                }

                batches[^1].Add(marker);
            }

            return batches;
        }

        private static Dictionary<string, ReplayMarkerResolveGroup> BuildForsakenActorGroups(
            IReadOnlyList<ReplayMarkerSnapshot> markers,
            IReadOnlyList<ReplayPositionSnapshot> positions)
        {
            // Forsaken pairings are fixed by the opening marker assignment. Later marker changes
            // advance the active resolve group, but they must not re-pair players mid-replay.
            var initialMarkerKinds = GetInitialForsakenMarkerKinds(markers);
            var pairs = BuildForsakenPairs(markers, positions, initialMarkerKinds);
            var actorGroups = new Dictionary<string, ReplayMarkerResolveGroup>(StringComparer.Ordinal);
            foreach (var pair in pairs)
            {
                var group = GetForsakenPairGroup(pair, initialMarkerKinds);

                foreach (var actorKey in pair)
                {
                    actorGroups[actorKey] = group;
                }
            }

            return actorGroups;
        }

        private static IReadOnlyList<IReadOnlyList<string>> BuildForsakenPairs(
            IReadOnlyList<ReplayMarkerSnapshot> markers,
            IReadOnlyList<ReplayPositionSnapshot> positions,
            IReadOnlyDictionary<string, ForsakenMarkerKind> initialMarkerKinds)
        {
            var actors = BuildForsakenActors(markers, positions);
            var tanks = actors
                .Where(actor => IsTank(actor.ClassJobName))
                .OrderBy(actor => GetTankSortKey(actor.ClassJobName))
                .ThenBy(actor => actor.PartyIndex)
                .ToList();
            var healers = actors
                .Where(actor => IsHealer(actor.ClassJobName))
                .OrderBy(actor => GetHealerSortKey(actor.ClassJobName))
                .ThenBy(actor => actor.PartyIndex)
                .ToList();
            var dps = actors
                .Where(actor => !IsTank(actor.ClassJobName) && !IsHealer(actor.ClassJobName))
                .OrderBy(actor => GetDpsSortKey(actor.ClassJobName))
                .ThenBy(actor => actor.PartyIndex)
                .ToList();
            var meleeDps = dps
                .Where(actor => IsMeleeDps(actor.ClassJobName))
                .ToList();
            var rangedDps = dps
                .Where(actor => IsRangedDps(actor.ClassJobName))
                .ToList();
            var pairs = new List<IReadOnlyList<string>>();

            AddPair(pairs, tanks.ElementAtOrDefault(0), healers.ElementAtOrDefault(0));
            pairs.AddRange(BuildForsakenDpsPairs(dps, meleeDps, rangedDps, initialMarkerKinds));
            AddPair(pairs, tanks.ElementAtOrDefault(1), healers.ElementAtOrDefault(1));

            return pairs;
        }

        private static IReadOnlyList<IReadOnlyList<string>> BuildForsakenDpsPairs(
            IReadOnlyList<ForsakenActor> dps,
            IReadOnlyList<ForsakenActor> meleeDps,
            IReadOnlyList<ForsakenActor> rangedDps,
            IReadOnlyDictionary<string, ForsakenMarkerKind> initialMarkerKinds)
        {
            var fallbackPairs = CreateForsakenPairLayout(
                dps.ElementAtOrDefault(0),
                dps.ElementAtOrDefault(2),
                dps.ElementAtOrDefault(1),
                dps.ElementAtOrDefault(3));
            if (dps.Count != 4 || fallbackPairs.Count == 0)
            {
                return fallbackPairs;
            }

            var candidates = BuildForsakenDpsPairCandidates(dps);
            var dpsByActorKey = dps.ToDictionary(actor => actor.ActorKey, StringComparer.Ordinal);
            if (candidates.Count == 0)
            {
                return fallbackPairs;
            }

            var hasExpectedRoleSplit = meleeDps.Count == 2 && rangedDps.Count == 2;
            if (hasExpectedRoleSplit)
            {
                var roleCompatibleCandidates = candidates
                    .Where(candidate => candidate.All(pair => IsMixedDpsRolePair(pair, dpsByActorKey)))
                    .ToList();
                if (roleCompatibleCandidates.Count > 0)
                {
                    candidates = roleCompatibleCandidates;
                }
            }

            return candidates
                .OrderByDescending(candidate => ScoreForsakenPairLayout(candidate, initialMarkerKinds))
                .ThenBy(candidate => IsSamePairLayout(candidate, fallbackPairs) ? 0 : 1)
                .First();
        }

        private static IReadOnlyList<IReadOnlyList<string>> CreateForsakenPairLayout(
            ForsakenActor? firstA,
            ForsakenActor? secondA,
            ForsakenActor? firstB,
            ForsakenActor? secondB)
        {
            var pairs = new List<IReadOnlyList<string>>();
            AddPair(pairs, firstA, secondA);
            AddPair(pairs, firstB, secondB);
            return pairs;
        }

        private static IReadOnlyList<IReadOnlyList<IReadOnlyList<string>>> BuildForsakenDpsPairCandidates(
            IReadOnlyList<ForsakenActor> dps)
        {
            return
            [
                CreateForsakenPairLayout(dps[0], dps[2], dps[1], dps[3]),
                CreateForsakenPairLayout(dps[0], dps[1], dps[2], dps[3]),
                CreateForsakenPairLayout(dps[0], dps[3], dps[1], dps[2]),
            ];
        }

        private static int ScoreForsakenPairLayout(
            IReadOnlyList<IReadOnlyList<string>> pairs,
            IReadOnlyDictionary<string, ForsakenMarkerKind> initialMarkerKinds)
        {
            var groups = pairs
                .Select(pair => GetForsakenPairGroup(pair, initialMarkerKinds))
                .ToList();
            var knownGroupCount = groups.Count(group => group != ReplayMarkerResolveGroup.Unknown);
            var score = knownGroupCount * 10;

            if (groups.Contains(ReplayMarkerResolveGroup.GroupA) &&
                groups.Contains(ReplayMarkerResolveGroup.GroupB))
            {
                score += 100;
            }

            return score;
        }

        private static bool IsSamePairLayout(
            IReadOnlyList<IReadOnlyList<string>> left,
            IReadOnlyList<IReadOnlyList<string>> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (var index = 0; index < left.Count; index++)
            {
                if (left[index].Count != 2 ||
                    right[index].Count != 2 ||
                    !string.Equals(left[index][0], right[index][0], StringComparison.Ordinal) ||
                    !string.Equals(left[index][1], right[index][1], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsMixedDpsRolePair(
            IReadOnlyList<string> pair,
            IReadOnlyDictionary<string, ForsakenActor> dpsByActorKey)
        {
            if (pair.Count < 2 ||
                !dpsByActorKey.TryGetValue(pair[0], out var first) ||
                !dpsByActorKey.TryGetValue(pair[1], out var second))
            {
                return false;
            }

            return IsMeleeDps(first.ClassJobName) && IsRangedDps(second.ClassJobName) ||
                IsRangedDps(first.ClassJobName) && IsMeleeDps(second.ClassJobName);
        }

        private static IReadOnlyList<ForsakenActor> BuildForsakenActors(
            IReadOnlyList<ReplayMarkerSnapshot> markers,
            IReadOnlyList<ReplayPositionSnapshot> positions)
        {
            return markers
                .Where(marker => marker.ActorKind == ReplayActorKind.Player)
                .Select(marker => new ForsakenActor(marker.ActorKey, marker.PartyIndex, marker.ClassJobName))
                .Concat(positions
                    .Where(position => position.ActorKind == ReplayActorKind.Player)
                    .Select(position => new ForsakenActor(position.ActorKey, position.PartyIndex, position.ClassJobName)))
                .GroupBy(actor => actor.ActorKey, StringComparer.Ordinal)
                .Select(group => group
                    .OrderBy(actor => string.IsNullOrWhiteSpace(actor.ClassJobName) ? 1 : 0)
                    .ThenBy(actor => actor.PartyIndex)
                    .First())
                .ToList();
        }

        private static Dictionary<string, ForsakenMarkerKind> GetInitialForsakenMarkerKinds(IReadOnlyList<ReplayMarkerSnapshot> markers)
        {
            var relevantMarkers = GetForsakenTowerMarkers(markers);
            if (relevantMarkers.Count == 0)
            {
                return [];
            }

            var initialBatchEnd = relevantMarkers[0].SeenAtUtc.AddSeconds(3.0);
            var markerKinds = new Dictionary<string, ForsakenMarkerKind>(StringComparer.Ordinal);
            foreach (var marker in relevantMarkers.Where(marker => marker.SeenAtUtc <= initialBatchEnd))
            {
                var markerKind = GetForsakenMarkerKind(marker.MarkerId);
                if (markerKind != ForsakenMarkerKind.Unknown)
                {
                    markerKinds[marker.ActorKey] = markerKind;
                }
            }

            return markerKinds;
        }

        private static ReplayMarkerResolveGroup GetForsakenPairGroup(
            IReadOnlyList<string> pair,
            IReadOnlyDictionary<string, ForsakenMarkerKind> initialMarkerKinds)
        {
            if (pair.Count < 2 ||
                !initialMarkerKinds.TryGetValue(pair[0], out var firstKind) ||
                !initialMarkerKinds.TryGetValue(pair[1], out var secondKind) ||
                firstKind == ForsakenMarkerKind.Unknown ||
                secondKind == ForsakenMarkerKind.Unknown)
            {
                return ReplayMarkerResolveGroup.Unknown;
            }

            return firstKind == secondKind
                ? ReplayMarkerResolveGroup.GroupB
                : ReplayMarkerResolveGroup.GroupA;
        }

        private static void AddPair(List<IReadOnlyList<string>> pairs, ForsakenActor? first, ForsakenActor? second)
        {
            if (first is null || second is null)
            {
                return;
            }

            pairs.Add([first.ActorKey, second.ActorKey]);
        }

        private static bool IsForsakenTowerMarker(uint markerId)
        {
            return markerId is 715 or 716 or 717 or 5084 or 5085 or 5086;
        }

        private static List<ReplayMarkerSnapshot> GetForsakenTowerMarkers(IReadOnlyList<ReplayMarkerSnapshot> markers)
        {
            return markers
                .Where(marker => IsForsakenTowerMarker(marker.MarkerId))
                .OrderBy(marker => marker.SeenAtUtc)
                .ToList();
        }

        private static ForsakenMarkerKind GetForsakenMarkerKind(uint markerId)
        {
            return markerId switch
            {
                715 or 5084 => ForsakenMarkerKind.Stack,
                716 or 5085 => ForsakenMarkerKind.Spread,
                717 or 5086 => ForsakenMarkerKind.Cone,
                _ => ForsakenMarkerKind.Unknown,
            };
        }

        private static bool IsTank(string classJobName)
        {
            return NormalizeJob(classJobName) is "PLD" or "WAR" or "DRK" or "GNB" or "PALADIN" or "WARRIOR" or "DARKKNIGHT" or "GUNBREAKER";
        }

        private static bool IsHealer(string classJobName)
        {
            return NormalizeJob(classJobName) is "WHM" or "SCH" or "AST" or "SGE" or "WHITEMAGE" or "SCHOLAR" or "ASTROLOGIAN" or "SAGE";
        }

        private static bool IsMeleeDps(string classJobName)
        {
            return NormalizeJob(classJobName) is "MNK" or "MONK" or "DRG" or "DRAGOON" or "NIN" or "NINJA" or "SAM" or "SAMURAI" or "RPR" or "REAPER" or "VPR" or "VIPER";
        }

        private static bool IsRangedDps(string classJobName)
        {
            return NormalizeJob(classJobName) is "BRD" or "BARD" or "MCH" or "MACHINIST" or "DNC" or "DANCER" or "BLM" or "BLACKMAGE" or "SMN" or "SUMMONER" or "RDM" or "REDMAGE" or "PCT" or "PICTOMANCER";
        }

        private static int GetTankSortKey(string classJobName)
        {
            return NormalizeJob(classJobName) switch
            {
                "PLD" or "PALADIN" => 0,
                "WAR" or "WARRIOR" => 1,
                "DRK" or "DARKKNIGHT" => 2,
                "GNB" or "GUNBREAKER" => 3,
                _ => 99,
            };
        }

        private static int GetHealerSortKey(string classJobName)
        {
            return NormalizeJob(classJobName) switch
            {
                "WHM" or "WHITEMAGE" => 0,
                "AST" or "ASTROLOGIAN" => 1,
                "SCH" or "SCHOLAR" => 2,
                "SGE" or "SAGE" => 3,
                _ => 99,
            };
        }

        private static int GetDpsSortKey(string classJobName)
        {
            return NormalizeJob(classJobName) switch
            {
                "MNK" or "MONK" or "DRG" or "DRAGOON" or "NIN" or "NINJA" or "SAM" or "SAMURAI" or "RPR" or "REAPER" or "VPR" or "VIPER" => 0,
                "BRD" or "BARD" or "MCH" or "MACHINIST" or "DNC" or "DANCER" => 1,
                "BLM" or "BLACKMAGE" or "SMN" or "SUMMONER" or "RDM" or "REDMAGE" or "PCT" or "PICTOMANCER" => 2,
                _ => 99,
            };
        }

        private static string NormalizeJob(string classJobName)
        {
            return new string(classJobName.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        }

        private sealed record ForsakenActor(string ActorKey, int PartyIndex, string ClassJobName);
    }

    private static bool TryGetNumber(uint markerId, out int number)
    {
        if (markerId is >= 79 and <= 86)
        {
            number = (int)markerId - 78;
            return true;
        }

        number = markerId switch
        {
            336 => 1,
            337 => 2,
            338 => 3,
            339 => 4,
            437 => 5,
            438 => 6,
            439 => 7,
            440 => 8,
            _ => 0,
        };

        return number > 0;
    }
}
