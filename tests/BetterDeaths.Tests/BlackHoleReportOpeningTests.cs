using System.Numerics;
using BetterDeaths.WtfDig;

namespace BetterDeaths.Tests;

public sealed class BlackHoleReportOpeningTests
{
    // The marker-relevant events from report 9pGVza6TXdk1Ybnv, fight 11; no player names.
    private sealed class OpeningReport : IWtfDigEventSource
    {
        internal const int Anchor = 9352582, Tether = 9354371, Fire = 9359689;
        internal List<FflogsEvent> Bosses = [Cast(9348918, 10, 2, 47847, -629), Cast(9352314, 15, 12, 47849, -472)];
        internal List<FflogsEvent> Objects = [new() { Timestamp = Tether, Type = "tether", SourceID = 38, SourceInstance = 1 }];
        internal List<FflogsEvent> Beams = [Beam(Fire, 1), Beam(9366751, 3), Beam(9366751, 2)];
        internal static FflogsResources Pose(double facing) => new() { X = 10000, Y = 10000, Facing = facing };
        internal static FflogsEvent Cast(int time, int id, int instance, uint action, double facing) => new()
        { Type = "cast", Timestamp = time, SourceID = id, SourceInstance = instance, AbilityGameID = action, SourceResources = Pose(facing) };
        internal static FflogsEvent Beam(int time, int instance) => new()
        { Type = "cast", Timestamp = time, SourceID = 38, SourceInstance = instance, AbilityGameID = BlackHoleAnalyzer.NothingnessId };
        internal void AddSpawn() => Objects.Add(new()
        { Type = "summon", Timestamp = Tether - 500, TargetID = 38, TargetInstance = 1 });

        public Task<IReadOnlyList<FflogsEvent>> FetchAllEventsAsync(FflogsEventQuery q, CancellationToken ct)
        {
            IReadOnlyList<FflogsEvent> result = q switch
            {
                { AbilityId: BlackHoleAnalyzer.BlackHoleCastId } => [new() { Timestamp = Anchor, Type = "cast", AbilityGameID = q.AbilityId }],
                { DataType: FflogsEventDataType.Casts, AbilityId: BlackHoleAnalyzer.NothingnessId } => Beams,
                { DataType: null, HostilityType: FflogsHostilityType.Enemies } => Bosses,
                { FilterExpression: "type='tether' or type='summon'" } => Objects,
                _ => [],
            };
            return Task.FromResult(result);
        }

        internal Task<BlackHoleAnalysis> Analyze() => new BlackHoleAnalyzer(this).AnalyzeAsync(new()
        {
            Code = "9pGVza6TXdk1Ybnv",
            Actors = [new() { Id = 10, Name = "Kefka", SubType = "Boss" }, new() { Id = 15, Name = "Kefka", SubType = "NPC" }],
        }, new() { Id = 11, StartTime = 8773285, EndTime = 9376392 }, default);
    }

    [Fact]
    public async Task Fight11DrawsWestOnBothDiagrams()
    {
        var result = await new OpeningReport().Analyze();
        Assert.Equal(new[] { (1, 1, 586d), (1, 2, 593d) }, result.Tethers.Select(t => (t.Set, t.Tether, t.Time)));
        Assert.All(result.Tethers, t =>
        {
            Assert.True(t.KefkaInferredFromSlapHappy);
            Assert.True(t.BigKefka is { } p && Vector2.Distance(p, new(-25, 0)) < .2f);
        });
    }

    [Theory]
    [InlineData("real turns")]
    [InlineData("target turns")]
    [InlineData("ambiguous caster")]
    [InlineData("stale slap")]
    [InlineData("only copy")]
    [InlineData("wrong hole")]
    [InlineData("late volley")]
    [InlineData("spawn but no fresh pose")]
    public async Task InsufficientEvidenceLeavesBothDiagramsUnmarked(string reason)
    {
        var r = new OpeningReport();
        switch (reason)
        {
            case "real turns": r.Bosses.Add(OpeningReport.Cast(OpeningReport.Tether - 1, 10, 2, 1, -785)); break;
            case "target turns": r.Bosses.Add(new() { Timestamp = OpeningReport.Tether - 1, Type = "damage", TargetID = 10,
                TargetInstance = 2, TargetResources = OpeningReport.Pose(-785) }); break;
            case "ambiguous caster": r.Bosses.Add(OpeningReport.Cast(OpeningReport.Anchor - 1, 10, 3, 47846, -629)); break;
            case "stale slap": r.Bosses[0] = OpeningReport.Cast(OpeningReport.Anchor - 12000, 10, 2, 47847, -629); break;
            case "only copy": r.Bosses[0] = OpeningReport.Cast(OpeningReport.Anchor - 3000, 15, 12, 47847, -629); break;
            case "wrong hole": r.Objects[0] = new() { Timestamp = OpeningReport.Tether, Type = "tether", SourceID = 38, SourceInstance = 2 }; break;
            case "late volley": r.Beams = [OpeningReport.Beam(OpeningReport.Anchor + 14000, 1), OpeningReport.Beam(OpeningReport.Anchor + 21000, 2)]; break;
            case "spawn but no fresh pose": r.AddSpawn(); break;
        }
        var result = await r.Analyze();
        Assert.Equal(2, result.Tethers.Count);
        Assert.All(result.Tethers, t => { Assert.Null(t.BigKefka); Assert.False(t.KefkaInferredFromSlapHappy); });
    }

    [Fact]
    public async Task RecordedSouthAtSpawnWinsOverInferredWest()
    {
        var r = new OpeningReport();
        r.AddSpawn();
        r.Bosses.Add(OpeningReport.Cast(OpeningReport.Tether - 600, 10, 2, 1, -785));
        var result = await r.Analyze();
        Assert.Equal(2, result.Tethers.Count);
        Assert.All(result.Tethers, t =>
        {
            Assert.False(t.KefkaInferredFromSlapHappy);
            Assert.True(t.BigKefka is { } p && Vector2.Distance(p, new(0, 25)) < .2f);
        });
    }

    [Fact]
    public async Task LaterTurnDoesNotMoveOpeningOrCarryMarkerIntoNextSet()
    {
        var r = new OpeningReport();
        r.Bosses.Add(OpeningReport.Cast(OpeningReport.Tether + 100, 10, 2, 47846, -785));
        r.Beams.Add(OpeningReport.Beam(OpeningReport.Fire + 30000, 4));
        var result = await r.Analyze();
        Assert.Equal(3, result.Tethers.Count);
        Assert.All(result.Tethers.Take(2), t => Assert.True(t.BigKefka is { } p && Vector2.Distance(p, new(-25, 0)) < .2f));
        Assert.Equal(2, result.Tethers[2].Set);
        Assert.Null(result.Tethers[2].BigKefka);
        Assert.False(result.Tethers[2].KefkaInferredFromSlapHappy);
    }
}
