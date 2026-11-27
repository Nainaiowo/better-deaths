using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Text.Json;
using BetterDeaths.WtfDig;

namespace BetterDeaths.Tests;

public sealed class DmuKefkaReplayTests
{
    // Measured independently from the real boss's periodic poses and the four
    // impact packets in each original completed pull, not from party positions.
    [Theory]
    [InlineData(2649, "570.7675:S 598.9628:SW 626.0574:NE 662.89:SW 683.99445:E", 700.00433f, "N", "706.2113 707.5157 708.787 710.21436")]
    [InlineData(2671, "570.50323:NE 598.8824:SE 625.9201:NE 662.67804:SW 683.9578:W", 699.72455f, "S", "706.11615 707.44824 708.78265 710.0515")]
    [InlineData(2673, "570.0606:W 598.3014:E 625.59406:N 683.54553:SE", 699.4166f, "NE", "705.65 706.8867 708.25226 709.60925")]
    [InlineData(2674, "570.3711:E 598.46185:NW 625.98193:SE 662.7698:N 684.07263:NE", 699.7882f, "NW", "706.1361 707.3408 708.6489 709.9008")]
    [InlineData(2675, "570.45404:W 625.76154:NW 662.34033:SW 683.7839:SE", 699.69696f, "NE", "705.87524 707.17017 708.495 709.7435")]
    [InlineData(2681, "571.1367:N 599.58435:SW 626.55743:NW 663.47675:E 684.5838:NW", 700.1057f, "SW", "706.582 707.94324 709.30945 710.5715")]
    [InlineData(2683, "570.26025:E 598.3539:NW 625.86005:NE 662.65875:W 683.97906:NE", 699.4972f, "NW", "706.05066 707.3387 708.6843 710.04926")]
    public void ScrubsAllWallChangesAndFootfalls(int number, string wallReadings, float platform, string firstFoot, string landingTimes)
    {
        var pull = ReadPull(number);
        var replay = Build(pull);
        var wall = wallReadings.Split(' ').Select(s => s.Split(':'))
            .Select(p => (Time: Parse(p[0]), Point: Center + Direction(p[1]) * 20)).ToArray();
        var hits = landingTimes.Split(' ').Select(Parse).ToArray();
        Assert.Null(replay.At(MathF.BitDecrement(wall[0].Time)).Body);
        // Deliberately scrub backwards as well as forwards: the track cannot depend on playback history.
        for (var i = wall.Length - 1; i >= 0; i--)
        {
            var until = i == wall.Length - 1 ? platform : wall[i + 1].Time;
            WallAt(wall[i].Time);
            WallAt(MathF.BitDecrement(until));
            for (var t = wall[i].Time; t < until; t += 0.125f) WallAt(t);
            void WallAt(float t)
            {
                var frame = replay.At(t);
                Assert.True(frame.Body is { OnPlatform: false } body && Vector2.Distance(wall[i].Point, body.Position) < 0.1f,
                    $"Pull {number} at {t}: {frame.Body} instead of {wall[i].Point}");
                Assert.Null(frame.Footfall);
            }
        }
        Assert.True(replay.At(platform).Body is { OnPlatform: true });
        Assert.Null(replay.At(platform).Footfall);
        for (var i = 0; i < hits.Length; i++)
        {
            var t = hits[i];
            var frame = replay.At(t);
            Assert.True(frame.Body is { OnPlatform: true } body && Vector2.Distance(body.Position, Center) < 0.1f);
            Assert.True(frame.Footfall is { } foot && Vector2.Distance(foot.Position,
                Center + Direction(firstFoot) * (i % 2 == 0 ? 10 : -10)) < 0.1f);
            Assert.Null(replay.At(MathF.BitDecrement(t)).Footfall);
            Assert.Null(replay.At(t + DmuKefkaReplayTrack.ImpactDisplaySeconds).Footfall);
        }
        Assert.Equal(default, replay.At(hits[^1] + 3));
    }

    [Fact]
    public async Task Pull2673ReplayMovesWhileSetFourAnalyzerStaysNorth()
    {
        var track = Build(ReadPull(2673));
        Assert.True(track.At(684).Body is { } body && Vector2.Distance(body.Position, Center + Direction("SE") * 20) < .1f);
        var source = LocalPullEventSource.Create(ReadPull(2673, "BlackHole"));
        var analysis = await new BlackHoleAnalyzer(source).AnalyzeAsync(source.Report, source.Fight, default);
        var set = analysis.Tethers.Where(t => t.Set == 4).ToArray();
        Assert.Equal(2, set.Length);
        Assert.All(set, t => Assert.True(t.BigKefka is { } point && Vector2.Distance(point, Direction("N") * 25) < .1f));
    }

    [Fact]
    public void MissingOrAmbiguousEvidenceDoesNotInventMovementsOrHits()
    {
        var pull = ReadPull(2673);
        var poses = pull.ReplayPositions.Where(p => p.PullElapsedSeconds is < 610 or > 616).ToArray();
        var gap = Build(pull with { ReplayPositions = poses }).At(615);
        Assert.Null(gap.Body);
        var knownBody = Assert.IsType<KefkaReplayBody>(Build(pull).At(610).Body);
        Assert.True(gap.Replaces(pull.ReplayPositions.First(p => p.EntityId == knownBody.EntityId)));
        var noHits = pull.ReplayMechanics.Where(m => m.RawEventKind != "dmu-p3-stomp-a-mole").ToArray();
        var interrupted = Build(pull with { ReplayMechanics = noHits });
        Assert.True(interrupted.At(699.7f).Body is { OnPlatform: true });
        Assert.Null(interrupted.At(705.65f).Footfall);
        var cast = pull.ReplayMechanics.First(m => m.RawEventId == 50167);
        var ambiguous = Build(pull with { ReplayMechanics = [.. pull.ReplayMechanics, cast with { SourceKey = "bossmod-cast:4000FFFF:50167:0" }] });
        Assert.Null(ambiguous.At(684).Body);
        Assert.NotNull(ambiguous.At(705.65f).Footfall);
        Assert.Equal(default, Build(pull with { TerritoryId = 1 }).At(705.65f));
    }

    private static readonly Vector2 Center = new(100, 100);
    private static Vector2 Direction(string compass) => Vector2.Normalize(new Vector2(
        compass.Contains('E') ? 1 : compass.Contains('W') ? -1 : 0,
        compass.Contains('S') ? 1 : compass.Contains('N') ? -1 : 0));
    private static float Parse(string value) => float.Parse(value, CultureInfo.InvariantCulture);
    private static DmuKefkaReplayTrack Build(PullDeathSnapshot p) => DmuKefkaReplayTrack.Build(p.TerritoryId, p.ReplayPositions, p.ReplayMechanics);
    private static PullDeathSnapshot ReadPull(int number, string fixture = "KefkaReplay")
    {
        var directory = Environment.GetEnvironmentVariable("BETTERDEATHS_BLACK_HOLE_PULL_DIR");
        using var input = directory is null
            ? typeof(DmuKefkaReplayTests).Assembly.GetManifestResourceStream($"{fixture}.{number}.json.gz")!
            : File.OpenRead(Directory.GetFiles(directory, $"pull-{number:D6}-*.json.gz").Single());
        using var stream = new GZipStream(input, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<PullDeathSnapshot>(stream)!;
    }
}
