namespace BetterDeaths;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

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
    Rectangle,
}

internal readonly record struct ReplayArenaInfo(
    float CenterX,
    float CenterZ,
    float HalfWidth,
    float HalfHeight,
    ReplayArenaShape Shape)
{
    public ReplayArenaInfo(float centerX, float centerZ, float radius, ReplayArenaShape shape)
        : this(centerX, centerZ, radius, radius, shape)
    {
    }

    public float Radius => MathF.Max(HalfWidth, HalfHeight);
}

internal interface IReplayEncounterModule
{
    string Name { get; }

    bool AppliesTo(uint territoryId);

    bool TryGetReplayArena(
        IReadOnlyList<ReplayPositionSnapshot> positions,
        IReadOnlyList<ReplayPositionSnapshot> actorStates,
        DateTime selectedAtUtc,
        out ReplayArenaInfo arena);

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
}

internal static class ReplayEncounterModules
{
    private const uint DmuP1FireSpreadMarkerId = 127;
    private const uint DmuP1FireStackMarkerId = 128;
    private const uint DmuP1MysteryMagicFireLieMarkerId = 673;
    private const uint DmuP1MysteryMagicFireTruthMarkerId = 674;
    private const uint DmuP1MysteryMagicIceLieMarkerId = 675;
    private const uint DmuP1MysteryMagicIceTruthMarkerId = 676;
    private const uint DmuP1MysteryMagicThunderLieMarkerId = 677;
    private const uint DmuP1MysteryMagicThunderTruthMarkerId = 678;
    private const uint DmuP4RealityTellStatusId = 2056;
    private const uint DmuP3EntropyStatusId = 1600;
    private const uint DmuP3DynamicFluidStatusId = 1601;
    private const uint DmuP3HeadwindStatusId = 1602;
    private const uint DmuP3TailwindStatusId = 1603;
    private const uint DmuP3AccretionStatusId = 1604;
    private const uint DmuP3EpicHeroStatusId = 4192;
    private const uint DmuP3FatedHeroStatusId = 4194;
    private const uint DmuP3UnbecomingStatusId = 5452;
    private const uint DmuP3MeanestExistenceStatusId = 5453;
    private const uint DmuP3PrimordialCrustStatusId = 5454;
    private const uint DmuP4CursedShriekStatusId = 5543;
    private const uint DmuP4ForkedLightningStatusId = 5544;
    private const uint DmuP4CompressedWaterStatusId = 5545;
    private const uint DmuP4AccelerationBombStatusId = 5546;
    private const uint DmuP4EntropyStatusId = 5547;
    private const uint DmuP4DynamicFluidStatusId = 5548;
    private static readonly IReplayEncounterModule FallbackModule = new GenericReplayEncounterModule();
    private static readonly IReadOnlyList<IReplayEncounterModule> Modules =
    [
        new CrownReplayEncounterModule(),
        new ArcadiaReplayEncounterModule(),
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

    public static bool IsDancingMadUltimate(uint territoryId)
    {
        return territoryId == DmuReplayEncounterModule.TerritoryDancingMadUltimate;
    }

    public static bool IsDmuP4RealityTellMarker(uint markerId)
    {
        return markerId == DmuP4RealityTellStatusId;
    }

    public static bool IsDmuP4AssignmentMarker(uint markerId)
    {
        return markerId is DmuP4CursedShriekStatusId or
            DmuP4ForkedLightningStatusId or
            DmuP4CompressedWaterStatusId or
            DmuP4AccelerationBombStatusId or
            DmuP4EntropyStatusId or
            DmuP4DynamicFluidStatusId;
    }

    public static bool TryResolveDmuP1FireMarkerInfo(
        ReplayMarkerSnapshot marker,
        IReadOnlyList<ReplayMarkerSnapshot> markers,
        ReplayMarkerInfo baseInfo,
        out ReplayMarkerInfo resolvedInfo)
    {
        resolvedInfo = baseInfo;
        if (!IsDmuP1FireMarker(marker.MarkerId) ||
            !TryFindNearestDmuP1FireTell(marker, markers, out var tell) ||
            !TryGetDmuP1FireTell(tell.MarkerId, out var isTruth))
        {
            return false;
        }

        var displayedSpread = marker.MarkerId == DmuP1FireSpreadMarkerId;
        var resolvedSpread = isTruth ? displayedSpread : !displayedSpread;
        var displayedText = displayedSpread ? "spread" : "stack";
        var resolvedText = resolvedSpread ? "spread" : "stack";
        resolvedInfo = baseInfo with
        {
            ShortLabel = resolvedSpread ? "Spread" : "Stack",
            Description = isTruth
                ? $"Fire {resolvedText}"
                : $"Fire {resolvedText} (fake {displayedText})",
            Shape = resolvedSpread ? ReplayMechanicShape.Spread : ReplayMechanicShape.Stack,
            Radius = resolvedSpread ? 5.0f : 6.0f,
            DurationSeconds = 5.8f,
        };
        return true;
    }

    public static bool IsDmuP1FireMarker(uint markerId)
    {
        return markerId is DmuP1FireSpreadMarkerId or DmuP1FireStackMarkerId;
    }

    public static bool IsDmuP1MysteryMagicMarker(uint markerId)
    {
        return markerId is DmuP1MysteryMagicFireLieMarkerId or
            DmuP1MysteryMagicFireTruthMarkerId or
            DmuP1MysteryMagicIceLieMarkerId or
            DmuP1MysteryMagicIceTruthMarkerId or
            DmuP1MysteryMagicThunderLieMarkerId or
            DmuP1MysteryMagicThunderTruthMarkerId;
    }

    private static bool TryFindNearestDmuP1FireTell(
        ReplayMarkerSnapshot marker,
        IReadOnlyList<ReplayMarkerSnapshot> markers,
        out ReplayMarkerSnapshot tell)
    {
        tell = markers
            .Where(candidate => candidate.ActorKind == ReplayActorKind.Enemy &&
                TryGetDmuP1FireTell(candidate.MarkerId, out _) &&
                candidate.SeenAtUtc >= marker.SeenAtUtc.AddSeconds(-8.0) &&
                candidate.SeenAtUtc <= marker.SeenAtUtc.AddSeconds(8.0))
            .OrderBy(candidate => Math.Abs((candidate.SeenAtUtc - marker.SeenAtUtc).TotalSeconds))
            .FirstOrDefault()!;
        return tell is not null;
    }

    private static bool TryGetDmuP1FireTell(uint markerId, out bool isTruth)
    {
        switch (markerId)
        {
            case DmuP1MysteryMagicFireLieMarkerId:
                isTruth = false;
                return true;
            case DmuP1MysteryMagicFireTruthMarkerId:
                isTruth = true;
                return true;
            default:
                isTruth = false;
                return false;
        }
    }

    public static bool TryGetDmuP4RealityTell(uint rawMarkerId, out string label, out bool isReal)
    {
        switch (rawMarkerId)
        {
            case 1119:
            case 1121:
                label = "Fake";
                isReal = false;
                return true;
            case 1120:
            case 1122:
                label = "Real";
                isReal = true;
                return true;
            default:
                label = string.Empty;
                isReal = false;
                return false;
        }
    }

    public static bool TryGetDmuP4StatusResolution(uint statusId, bool isReal, out string resolution)
    {
        resolution = statusId switch
        {
            DmuP4CompressedWaterStatusId => isReal ? "Stack" : "Spread",
            DmuP4ForkedLightningStatusId => isReal ? "Spread" : "Stack",
            DmuP4CursedShriekStatusId => isReal ? "Look away" : "Look toward",
            DmuP4AccelerationBombStatusId => isReal ? "Stop" : "Move",
            DmuP4DynamicFluidStatusId => isReal ? "Donut" : "Point-blank",
            DmuP4EntropyStatusId => isReal ? "Point-blank" : "Donut",
            _ => string.Empty,
        };
        return !string.IsNullOrEmpty(resolution);
    }

    private static bool GenericTryGetMarkerInfo(uint markerId, out ReplayMarkerInfo info)
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

    private static bool ContainsEnemyActor(
        IReadOnlyList<ReplayPositionSnapshot> positions,
        IReadOnlyList<string> actorNames)
    {
        return positions.Any(position =>
            position.ActorKind == ReplayActorKind.Enemy &&
            ActorNameMatches(position.ActorName, actorNames));
    }

    private static bool ActorNameMatches(string actorName, IReadOnlyList<string> actorNames)
    {
        return actorNames.Any(candidate =>
            string.Equals(actorName, candidate, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class GenericReplayEncounterModule : IReplayEncounterModule
    {
        public string Name => "Universal";

        public bool AppliesTo(uint territoryId) => true;

        public bool TryGetReplayArena(
            IReadOnlyList<ReplayPositionSnapshot> positions,
            IReadOnlyList<ReplayPositionSnapshot> actorStates,
            DateTime selectedAtUtc,
            out ReplayArenaInfo arena)
        {
            arena = default;
            return false;
        }

        public bool IsReplayOverheadStatus(uint statusId) => false;

        public bool TryGetMarkerInfo(uint markerId, out ReplayMarkerInfo info)
        {
            return GenericTryGetMarkerInfo(markerId, out info);
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

    }

    private sealed class CrownReplayEncounterModule : IReplayEncounterModule
    {
        private const uint TerritoryTheCrown = 1325;
        private static readonly ReplayArenaInfo Arena = new(100.0f, 100.0f, 20.0f, ReplayArenaShape.Square);

        public string Name => "The Crown";

        public bool AppliesTo(uint territoryId) => territoryId == TerritoryTheCrown;

        public bool TryGetReplayArena(
            IReadOnlyList<ReplayPositionSnapshot> positions,
            IReadOnlyList<ReplayPositionSnapshot> actorStates,
            DateTime selectedAtUtc,
            out ReplayArenaInfo arena)
        {
            arena = Arena;
            return true;
        }

        public bool IsReplayOverheadStatus(uint statusId) => false;

        public bool TryGetMarkerInfo(uint markerId, out ReplayMarkerInfo info)
        {
            return GenericTryGetMarkerInfo(markerId, out info);
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
    }

    private sealed class ArcadiaReplayEncounterModule : IReplayEncounterModule
    {
        private const uint TerritoryArcadia = 1327;
        private static readonly ReplayArenaInfo P1Arena = new(100.0f, 100.0f, 20.0f, 15.0f, ReplayArenaShape.Rectangle);
        private static readonly ReplayArenaInfo P2Arena = new(100.0f, 100.0f, 20.0f, ReplayArenaShape.Circle);
        private static readonly string[] P1EnemyNames = ["Blood Vessel"];
        private static readonly string[] P2EnemyNames = ["Lindschrat", "Luzzelwurm", "Mana Sphere", "Understudy"];

        public string Name => "Arcadia";

        public bool AppliesTo(uint territoryId) => territoryId == TerritoryArcadia;

        public bool TryGetReplayArena(
            IReadOnlyList<ReplayPositionSnapshot> positions,
            IReadOnlyList<ReplayPositionSnapshot> actorStates,
            DateTime selectedAtUtc,
            out ReplayArenaInfo arena)
        {
            if (TryGetFirstEnemySeenAt(positions, P2EnemyNames, out var firstP2SeenAtUtc))
            {
                if (selectedAtUtc >= firstP2SeenAtUtc.AddSeconds(-1.0))
                {
                    arena = P2Arena;
                    return true;
                }

                arena = P1Arena;
                return true;
            }

            if (ContainsEnemyActor(actorStates, P2EnemyNames) ||
                ContainsEnemyActorNear(positions, selectedAtUtc, P2EnemyNames))
            {
                arena = P2Arena;
                return true;
            }

            if (ContainsEnemyActor(actorStates, P1EnemyNames) ||
                ContainsEnemyActorNear(positions, selectedAtUtc, P1EnemyNames))
            {
                arena = P1Arena;
                return true;
            }

            arena = ContainsEnemyActor(positions, P2EnemyNames)
                ? P2Arena
                : P1Arena;
            return true;
        }

        public bool IsReplayOverheadStatus(uint statusId) => false;

        public bool TryGetMarkerInfo(uint markerId, out ReplayMarkerInfo info)
        {
            return GenericTryGetMarkerInfo(markerId, out info);
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

        private static bool ContainsEnemyActorNear(
            IReadOnlyList<ReplayPositionSnapshot> positions,
            DateTime selectedAtUtc,
            IReadOnlyList<string> actorNames)
        {
            var startAtUtc = selectedAtUtc.AddSeconds(-2.0);
            var endAtUtc = selectedAtUtc.AddSeconds(2.0);
            return positions.Any(position =>
                position.ActorKind == ReplayActorKind.Enemy &&
                position.SeenAtUtc >= startAtUtc &&
                position.SeenAtUtc <= endAtUtc &&
                ActorNameMatches(position.ActorName, actorNames));
        }

        private static bool TryGetFirstEnemySeenAt(
            IReadOnlyList<ReplayPositionSnapshot> positions,
            IReadOnlyList<string> actorNames,
            out DateTime seenAtUtc)
        {
            seenAtUtc = positions
                .Where(position => position.ActorKind == ReplayActorKind.Enemy &&
                    ActorNameMatches(position.ActorName, actorNames))
                .Select(position => position.SeenAtUtc)
                .OrderBy(seenAt => seenAt)
                .FirstOrDefault();
            return seenAtUtc != default;
        }
    }

    private sealed class DmuReplayEncounterModule : IReplayEncounterModule
    {
        public const uint TerritoryDancingMadUltimate = 1363;
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
        private static readonly ConditionalWeakTable<IReadOnlyList<ReplayMarkerSnapshot>, ForsakenReplayCache> ForsakenReplayCaches = new();
        private static readonly object ForsakenReplayCacheLock = new();

        public string Name => "Dancing Mad Ultimate";

        public bool AppliesTo(uint territoryId) => territoryId == TerritoryDancingMadUltimate;

        public bool TryGetReplayArena(
            IReadOnlyList<ReplayPositionSnapshot> positions,
            IReadOnlyList<ReplayPositionSnapshot> actorStates,
            DateTime selectedAtUtc,
            out ReplayArenaInfo arena)
        {
            arena = Arena;
            return true;
        }

        public bool IsReplayOverheadStatus(uint statusId)
        {
            return statusId is 3004 or 3005 or 3006 or
                DmuP3EntropyStatusId or
                DmuP3DynamicFluidStatusId or
                DmuP3HeadwindStatusId or
                DmuP3TailwindStatusId or
                DmuP3AccretionStatusId or
                DmuP3EpicHeroStatusId or
                DmuP3FatedHeroStatusId or
                DmuP3UnbecomingStatusId or
                DmuP3MeanestExistenceStatusId or
                DmuP3PrimordialCrustStatusId or
                5084 or 5085 or 5086 or
                DmuP4CursedShriekStatusId or
                DmuP4ForkedLightningStatusId or
                DmuP4CompressedWaterStatusId or
                DmuP4AccelerationBombStatusId or
                DmuP4EntropyStatusId or
                DmuP4DynamicFluidStatusId;
        }

        public bool TryGetMarkerInfo(uint markerId, out ReplayMarkerInfo info)
        {
            info = markerId switch
            {
                DmuP1FireSpreadMarkerId => new ReplayMarkerInfo("Spread", "Fire spread", ReplayMechanicShape.Spread, Radius: 5.0f, DurationSeconds: 5.8f),
                DmuP1FireStackMarkerId => new ReplayMarkerInfo("Stack", "Fire stack", ReplayMechanicShape.Stack, Radius: 6.0f, DurationSeconds: 5.8f),
                DmuP1MysteryMagicFireLieMarkerId => new ReplayMarkerInfo("Fake Fire", "Mystery Magic fire lie", DurationSeconds: 5.8f),
                DmuP1MysteryMagicFireTruthMarkerId => new ReplayMarkerInfo("Real Fire", "Mystery Magic fire truth", DurationSeconds: 5.8f),
                DmuP1MysteryMagicIceLieMarkerId => new ReplayMarkerInfo("Fake Ice", "Mystery Magic ice lie"),
                DmuP1MysteryMagicIceTruthMarkerId => new ReplayMarkerInfo("Real Ice", "Mystery Magic ice truth"),
                DmuP1MysteryMagicThunderLieMarkerId => new ReplayMarkerInfo("Fake Thunder", "Mystery Magic thunder lie"),
                DmuP1MysteryMagicThunderTruthMarkerId => new ReplayMarkerInfo("Real Thunder", "Mystery Magic thunder truth"),
                161 => new ReplayMarkerInfo("Stack", "Final stack", ReplayMechanicShape.Stack, Radius: 5.0f, DurationSeconds: 10.0f),
                715 => new ReplayMarkerInfo("Stack", "Forsaken stack", ReplayMechanicShape.Stack, Radius: 5.0f),
                716 => new ReplayMarkerInfo("Spread", "Forsaken spread", ReplayMechanicShape.Spread, Radius: 5.0f),
                717 => new ReplayMarkerInfo("Cone", "Forsaken cone", ReplayMechanicShape.Cone, Radius: 18.0f, Length: 18.0f, AngleDegrees: 60.0f, ConeBaitsClosestPlayer: true),
                DmuP3EntropyStatusId => new ReplayMarkerInfo("Entropy", "Entropy", ReplayMechanicShape.Circle, Radius: 5.0f),
                DmuP3DynamicFluidStatusId => new ReplayMarkerInfo("Fluid", "Dynamic Fluid", ReplayMechanicShape.Donut, Radius: 10.0f, Width: 4.0f),
                DmuP3HeadwindStatusId => new ReplayMarkerInfo("Headwind", "Headwind"),
                DmuP3TailwindStatusId => new ReplayMarkerInfo("Tailwind", "Tailwind"),
                DmuP3AccretionStatusId => new ReplayMarkerInfo("Accretion", "Accretion"),
                DmuP3EpicHeroStatusId => new ReplayMarkerInfo("Epic", "Epic Hero"),
                DmuP3FatedHeroStatusId => new ReplayMarkerInfo("Fated", "Fated Hero"),
                DmuP3UnbecomingStatusId => new ReplayMarkerInfo("Unbecoming", "Unbecoming"),
                DmuP3MeanestExistenceStatusId => new ReplayMarkerInfo("Meanest", "Meanest Existence"),
                DmuP3PrimordialCrustStatusId => new ReplayMarkerInfo("Crust", "Primordial Crust"),
                3004 => new ReplayMarkerInfo("1st", "First in line"),
                3005 => new ReplayMarkerInfo("2nd", "Second in line"),
                3006 => new ReplayMarkerInfo("3rd", "Third in line"),
                5084 => new ReplayMarkerInfo("Stack", "Head stack", ReplayMechanicShape.Stack, Radius: 5.0f),
                5085 => new ReplayMarkerInfo("Circle", "Circle", ReplayMechanicShape.Circle, Radius: 5.0f),
                5086 => new ReplayMarkerInfo("Fan", "Fan", ReplayMechanicShape.Cone, Radius: 18.0f, Length: 18.0f, AngleDegrees: 70.0f, ConeBaitsClosestPlayer: true),
                DmuP4RealityTellStatusId => new ReplayMarkerInfo("Tell", "P4 real/fake tell"),
                DmuP4CursedShriekStatusId => new ReplayMarkerInfo("Shriek", "Cursed Shriek"),
                DmuP4ForkedLightningStatusId => new ReplayMarkerInfo("Lightning", "Forked Lightning"),
                DmuP4CompressedWaterStatusId => new ReplayMarkerInfo("Water", "Compressed Water"),
                DmuP4AccelerationBombStatusId => new ReplayMarkerInfo("Bomb", "Acceleration Bomb"),
                DmuP4EntropyStatusId => new ReplayMarkerInfo("Entropy", "Entropy"),
                DmuP4DynamicFluidStatusId => new ReplayMarkerInfo("Fluid", "Dynamic Fluid"),
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

            var cache = GetForsakenReplayCache(markers, positions);
            var markerGroup = cache.ActorGroups.TryGetValue(marker.ActorKey, out var group)
                ? group
                : ReplayMarkerResolveGroup.Unknown;
            var activeGroup = GetActiveForsakenResolveGroup(cache, selectedAtUtc);
            return markerGroup == ReplayMarkerResolveGroup.Unknown ||
                activeGroup == ReplayMarkerResolveGroup.Unknown ||
                markerGroup == activeGroup;
        }

        private static ReplayMarkerResolveGroup GetActiveForsakenResolveGroup(ForsakenReplayCache cache, DateTime selectedAtUtc)
        {
            if (cache.RelevantMarkers.Count == 0 ||
                cache.ActorGroups.Count == 0)
            {
                return ReplayMarkerResolveGroup.Unknown;
            }

            var sequenceIndex = 0;
            foreach (var batch in cache.UpdateBatches)
            {
                if (batch[0].SeenAtUtc > selectedAtUtc)
                {
                    break;
                }

                var activeGroup = ForsakenTowerResolveSequence[Math.Clamp(sequenceIndex, 0, ForsakenTowerResolveSequence.Length - 1)];
                if (batch.Any(marker => cache.ActorGroups.TryGetValue(marker.ActorKey, out var markerGroup) && markerGroup == activeGroup))
                {
                    sequenceIndex = Math.Min(sequenceIndex + 1, ForsakenTowerResolveSequence.Length - 1);
                }
            }

            return ForsakenTowerResolveSequence[sequenceIndex];
        }

        private static ForsakenReplayCache GetForsakenReplayCache(
            IReadOnlyList<ReplayMarkerSnapshot> markers,
            IReadOnlyList<ReplayPositionSnapshot> positions)
        {
            lock (ForsakenReplayCacheLock)
            {
                if (ForsakenReplayCaches.TryGetValue(markers, out var cached) &&
                    cached.Matches(markers, positions))
                {
                    return cached;
                }

                ForsakenReplayCaches.Remove(markers);
                var cache = BuildForsakenReplayCache(markers, positions);
                ForsakenReplayCaches.Add(markers, cache);
                return cache;
            }
        }

        private static ForsakenReplayCache BuildForsakenReplayCache(
            IReadOnlyList<ReplayMarkerSnapshot> markers,
            IReadOnlyList<ReplayPositionSnapshot> positions)
        {
            var relevantMarkers = GetForsakenTowerMarkers(markers);
            var initialBatchEnd = relevantMarkers.Count == 0
                ? DateTime.MinValue
                : relevantMarkers[0].SeenAtUtc.AddSeconds(3.0);
            var updateBatches = relevantMarkers.Count == 0
                ? []
                : BuildForsakenMarkerUpdateBatches(relevantMarkers, initialBatchEnd);

            return new ForsakenReplayCache(
                markers,
                positions,
                CreateMarkerListSignature(markers),
                CreatePositionListSignature(positions),
                relevantMarkers,
                updateBatches,
                BuildForsakenActorGroups(markers, positions));
        }

        private static ReplayListSignature CreateMarkerListSignature(IReadOnlyList<ReplayMarkerSnapshot> markers)
        {
            return markers.Count == 0
                ? new ReplayListSignature(0, 0L, 0L)
                : new ReplayListSignature(markers.Count, markers[0].SeenAtUtc.Ticks, markers[^1].SeenAtUtc.Ticks);
        }

        private static ReplayListSignature CreatePositionListSignature(IReadOnlyList<ReplayPositionSnapshot> positions)
        {
            return positions.Count == 0
                ? new ReplayListSignature(0, 0L, 0L)
                : new ReplayListSignature(positions.Count, positions[0].SeenAtUtc.Ticks, positions[^1].SeenAtUtc.Ticks);
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

        private readonly record struct ReplayListSignature(int Count, long FirstSeenAtTicks, long LastSeenAtTicks);

        private sealed record ForsakenReplayCache(
            IReadOnlyList<ReplayMarkerSnapshot> Markers,
            IReadOnlyList<ReplayPositionSnapshot> Positions,
            ReplayListSignature MarkerSignature,
            ReplayListSignature PositionSignature,
            IReadOnlyList<ReplayMarkerSnapshot> RelevantMarkers,
            IReadOnlyList<IReadOnlyList<ReplayMarkerSnapshot>> UpdateBatches,
            IReadOnlyDictionary<string, ReplayMarkerResolveGroup> ActorGroups)
        {
            public bool Matches(
                IReadOnlyList<ReplayMarkerSnapshot> markers,
                IReadOnlyList<ReplayPositionSnapshot> positions)
            {
                return ReferenceEquals(Markers, markers) &&
                    ReferenceEquals(Positions, positions) &&
                    MarkerSignature == CreateMarkerListSignature(markers) &&
                    PositionSignature == CreatePositionListSignature(positions);
            }
        }
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
