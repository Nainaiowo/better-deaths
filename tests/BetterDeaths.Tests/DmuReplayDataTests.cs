namespace BetterDeaths;

public sealed class DmuReplayDataTests
{
    [Theory]
    [InlineData(0x14, 100.0f, 100.0f)]
    [InlineData(0x15, 113.5f, 100.0f)]
    [InlineData(0x16, 100.0f, 86.5f)]
    [InlineData(0x17, 86.5f, 100.0f)]
    [InlineData(0x18, 100.0f, 113.5f)]
    [InlineData(0x19, 113.5f, 113.5f)]
    [InlineData(0x1A, 113.5f, 86.5f)]
    [InlineData(0x1B, 86.5f, 86.5f)]
    [InlineData(0x1C, 86.5f, 113.5f)]
    public void P5ArenaHoleMapEffectIndexesResolveToExpectedPositions(int index, float expectedX, float expectedZ)
    {
        Assert.True(ReplayEncounterModules.TryGetDmuP5ArenaHolePosition((uint)index, out var x, out var z));
        Assert.Equal(expectedX, x);
        Assert.Equal(expectedZ, z);
    }

    [Theory]
    [InlineData(0x13)]
    [InlineData(0x1D)]
    public void P5ArenaHoleMapEffectRejectsUnknownIndexes(int index)
    {
        Assert.False(ReplayEncounterModules.TryGetDmuP5ArenaHolePosition((uint)index, out _, out _));
    }

    [Fact]
    public void P5ArenaHoleUsesBossModStateAndRadius()
    {
        Assert.Equal(0x00200010u, ReplayEncounterModules.DmuP5ArenaHoleMapEffectState);
        Assert.Equal(8.0f, ReplayEncounterModules.DmuP5ArenaHoleRadius);
    }

    [Theory]
    [InlineData(717)]
    [InlineData(5086)]
    public void P2ForsakenConeUsesFortyYalmNinetyDegreeGeometry(uint markerId)
    {
        var module = ReplayEncounterModules.Get(1363);

        Assert.True(module.TryGetMarkerInfo(markerId, out var info));
        Assert.Equal(ReplayMechanicShape.Cone, info.Shape);
        Assert.Equal(40.0f, info.Radius);
        Assert.Equal(40.0f, info.Length);
        Assert.Equal(90.0f, info.AngleDegrees);
    }

    [Fact]
    public void P2ForsakenStatusUsesSpreadShape()
    {
        var module = ReplayEncounterModules.Get(1363);

        Assert.True(module.TryGetMarkerInfo(5085, out var info));
        Assert.Equal(ReplayMechanicShape.Spread, info.Shape);
        Assert.Equal(5.0f, info.Radius);
    }

    [Fact]
    public void P5ForsakenBondsMarkerUsesSixYalmFivePointOneSecondStack()
    {
        var module = ReplayEncounterModules.Get(1363);

        Assert.True(module.TryGetMarkerInfo(161, out var info));
        Assert.Equal(ReplayMechanicShape.Stack, info.Shape);
        Assert.Equal(6.0f, info.Radius);
        Assert.Equal(5.1f, info.DurationSeconds);
    }

    [Fact]
    public void P2ForsakenInitialAssignmentsCreateMechanics()
    {
        var module = ReplayEncounterModules.Get(1363);
        var marker = CreateMarker(ForsakenStart, "tank-one", "PLD", 0, 715);

        Assert.True(module.ShouldCreateReplayMarkerMechanic(marker, [marker]));
    }

    [Fact]
    public void P2ForsakenKeepsResolvingShapeUntilRecordedHit()
    {
        var module = ReplayEncounterModules.Get(1363);
        var (markers, positions, mechanics) = CreateForsakenScenario();
        var initialStack = markers.Single(marker => marker.ActorKey == "tank-one" && marker.MarkerId == 715);
        var nextCone = markers.Single(marker => marker.ActorKey == "tank-one" && marker.MarkerId == 717);

        Assert.True(module.ShouldDisplayReplayMarker(initialStack, markers, positions, mechanics, ForsakenStart.AddSeconds(9.95)));
        Assert.False(module.ShouldDisplayReplayMarker(nextCone, markers, positions, mechanics, ForsakenStart.AddSeconds(9.95)));
        Assert.False(module.ShouldDisplayReplayMarker(initialStack, markers, positions, mechanics, ForsakenStart.AddSeconds(10.1)));
        Assert.True(module.ShouldDisplayReplayMarker(nextCone, markers, positions, mechanics, ForsakenStart.AddSeconds(10.1)));
    }

    [Fact]
    public void P2ForsakenResolveCountControlsGroupIncludingFinalRound()
    {
        var module = ReplayEncounterModules.Get(1363);
        var (markers, positions, mechanics) = CreateForsakenScenario();
        var groupAMarker = markers.Single(marker => marker.ActorKey == "tank-one" && marker.MarkerId == 717);
        var groupBMarker = markers.Single(marker => marker.ActorKey == "tank-two" && marker.MarkerId == 715);

        Assert.True(module.ShouldDisplayReplayMarker(groupAMarker, markers, positions, mechanics, ForsakenStart.AddSeconds(29.0)));
        Assert.False(module.ShouldDisplayReplayMarker(groupBMarker, markers, positions, mechanics, ForsakenStart.AddSeconds(29.0)));
        Assert.False(module.ShouldDisplayReplayMarker(groupAMarker, markers, positions, mechanics, ForsakenStart.AddSeconds(31.0)));
        Assert.True(module.ShouldDisplayReplayMarker(groupBMarker, markers, positions, mechanics, ForsakenStart.AddSeconds(31.0)));
        Assert.True(module.ShouldDisplayReplayMarker(groupAMarker, markers, positions, mechanics, ForsakenStart.AddSeconds(71.0)));
        Assert.False(module.ShouldDisplayReplayMarker(groupAMarker, markers, positions, mechanics, ForsakenStart.AddSeconds(81.0)));
    }

    [Fact]
    public void P2ForsakenPrefersIconOverDuplicateHiddenStatus()
    {
        var module = ReplayEncounterModules.Get(1363);
        var (markers, positions, mechanics) = CreateForsakenScenario(includeDuplicateStatus: true);
        var status = markers.Single(marker => marker.ActorKey == "tank-one" && marker.MarkerId == 5084);
        var icon = markers.Single(marker => marker.ActorKey == "tank-one" && marker.MarkerId == 715);

        Assert.False(module.ShouldDisplayReplayMarker(status, markers, positions, mechanics, ForsakenStart.AddSeconds(5.0)));
        Assert.True(module.ShouldDisplayReplayMarker(icon, markers, positions, mechanics, ForsakenStart.AddSeconds(5.0)));
    }

    [Fact]
    public void P2ForsakenResolvedShapesAndDuplicateTowersAreNotDisplayed()
    {
        var module = ReplayEncounterModules.Get(1363);
        var shape = CreateMechanic(ForsakenStart.AddSeconds(10.0), 47808, "dmu-p2-spelldriver");
        var customTower = CreateMechanic(ForsakenStart.AddSeconds(1.0), 47806, "dmu-p2-path-of-light");
        var resolvedTower = CreateMechanic(ForsakenStart.AddSeconds(11.0), 47806, "bossmod-action");

        Assert.False(module.ShouldDisplayReplayMechanic(shape, [shape]));
        Assert.False(module.ShouldDisplayReplayMechanic(resolvedTower, [customTower, resolvedTower]));
        Assert.True(module.ShouldDisplayReplayMechanic(customTower, [customTower, resolvedTower]));
    }

    private static readonly DateTime ForsakenStart = new(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc);

    private static (IReadOnlyList<ReplayMarkerSnapshot> Markers, IReadOnlyList<ReplayPositionSnapshot> Positions, IReadOnlyList<ReplayMechanicSnapshot> Mechanics) CreateForsakenScenario(
        bool includeDuplicateStatus = false)
    {
        var actors = new[]
        {
            (Key: "tank-one", Job: "PLD", PartyIndex: 0, MarkerId: 715u),
            (Key: "healer-one", Job: "WHM", PartyIndex: 1, MarkerId: 716u),
            (Key: "dps-one", Job: "MNK", PartyIndex: 2, MarkerId: 715u),
            (Key: "dps-two", Job: "BRD", PartyIndex: 3, MarkerId: 716u),
            (Key: "tank-two", Job: "WAR", PartyIndex: 4, MarkerId: 715u),
            (Key: "healer-two", Job: "SCH", PartyIndex: 5, MarkerId: 715u),
            (Key: "dps-three", Job: "RPR", PartyIndex: 6, MarkerId: 717u),
            (Key: "dps-four", Job: "BLM", PartyIndex: 7, MarkerId: 717u),
        };
        var markers = actors
            .Select(actor => CreateMarker(ForsakenStart, actor.Key, actor.Job, actor.PartyIndex, actor.MarkerId))
            .ToList();
        if (includeDuplicateStatus)
        {
            markers.Add(CreateMarker(ForsakenStart.AddMilliseconds(-1), "tank-one", "PLD", 0, 5084));
        }

        markers.Add(CreateMarker(ForsakenStart.AddSeconds(9.9), "tank-one", "PLD", 0, 717));
        var positions = actors
            .Select(actor => CreatePosition(actor.Key, actor.Job, actor.PartyIndex))
            .ToList();
        var mechanics = Enumerable.Range(0, 8)
            .Select(index => CreateMechanic(ForsakenStart.AddSeconds(10.0 + (index * 10.0)), 47808u + (uint)(index % 3), "dmu-p2-resolve"))
            .ToList();
        return (markers, positions, mechanics);
    }

    private static ReplayMarkerSnapshot CreateMarker(
        DateTime seenAtUtc,
        string actorKey,
        string job,
        int partyIndex,
        uint markerId)
    {
        return new ReplayMarkerSnapshot(
            seenAtUtc,
            (float)(seenAtUtc - ForsakenStart).TotalSeconds,
            actorKey,
            actorKey,
            ReplayActorKind.Player,
            partyIndex,
            (uint)(partyIndex + 1),
            1,
            job,
            markerId,
            markerId);
    }

    private static ReplayPositionSnapshot CreatePosition(string actorKey, string job, int partyIndex)
    {
        return new ReplayPositionSnapshot(
            ForsakenStart,
            0.0f,
            actorKey,
            actorKey,
            ReplayActorKind.Player,
            partyIndex,
            (uint)(partyIndex + 1),
            1,
            job,
            96.0f + partyIndex,
            0.0f,
            100.0f,
            0.0f,
            1,
            0,
            1,
            false,
            true);
    }

    private static ReplayMechanicSnapshot CreateMechanic(DateTime seenAtUtc, uint actionId, string rawEventKind)
    {
        return new ReplayMechanicSnapshot(
            seenAtUtc,
            (float)(seenAtUtc - ForsakenStart).TotalSeconds,
            1.0f,
            $"{rawEventKind}:{seenAtUtc.Ticks}",
            "Kefka",
            ReplayMechanicShape.Circle,
            100.0f,
            0.0f,
            100.0f,
            0.0f,
            5.0f,
            0.0f,
            0.0f,
            0.0f,
            "Forsaken",
            rawEventKind,
            actionId,
            0,
            true);
    }
}
