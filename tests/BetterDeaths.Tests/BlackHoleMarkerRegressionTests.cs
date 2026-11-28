using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using BetterDeaths.WtfDig;

namespace BetterDeaths.Tests;

public sealed class BlackHoleMarkerRegressionTests
{
    // Independent compass readings at appearance, not the later beam snapshots.
    [Theory]
    [InlineData(2649, "S SW NE SW", "CED60C6E8CB69CA3AE19166BA72AC0A3D74147ADCBD85011AB7D474BCC873BA2", "924F4860C583445F6204C3DE3A60A0F328CA344C687065FA02843EA0758D1C1B")]
    [InlineData(2671, "NE SE NE SW", "41E9B57C1D9968EFBDFE894265E3896B7499CD958B65927C5129FD17B645F09E", "1487AEAA2793F3CF2B43CF2494D4891B973782F435FAAE9E63E0115059A1D772")]
    [InlineData(2673, "W E N N", "37DBAC352C063997EADAF2E65175A41915F6B3D79A0628121784CE782E8A14E6", "F6AA7505ED05071A61D2721226005AF759C425DC09E5C8D200E150F9C1B67633")]
    [InlineData(2674, "E NW SE N", "54F666637682FCA956708E82BD8BB5EDD3D15AA47074E4EF8C90EB10916AD33F", "4892A139410C63FFB549081E6F359F0311806877675073A4850BF5485DDBE592")]
    [InlineData(2675, "W W NW SW", "B3A5C21CA3E40ADA9E16C2B1505D89714A7A83C5954E2255624641C25395BB89", "2CC200E23D6C589345A2A44F2656BA4D1EEE4F4EC466BAA854D9117458393B83")]
    [InlineData(2681, "N SW NW E", "E7842F4E09AF11090FCABFAAA10BAB5721B36CB3A57E974DEC842FE5240A8264", "D3D32441483B614FE5328D0F7F43FA7B496FA7EC0C607EF3CFAC0667D4585952")]
    [InlineData(2683, "E NW NE W", "EB8547DA874E743FBCA69591C9AC860CBADD823E8A5BDD50B52E339AA474D5D2", "B7A916FC69471AC47B76B1BA4A4C529A990C08D13211C9AD30EF528ECC8B0301")]
    public async Task CompletedPullKeepsMarkerAndRecordedTethers(int number, string compass, string fixtureHash, string fullHash)
    {
        var directory = Environment.GetEnvironmentVariable("BETTERDEATHS_BLACK_HOLE_PULL_DIR");
        using var input = directory is null
            ? typeof(BlackHoleMarkerRegressionTests).Assembly.GetManifestResourceStream($"BlackHole.{number}.json.gz")!
            : File.OpenRead(Directory.GetFiles(directory, $"pull-{number:D6}-*.json.gz").Single());
        using var stream = new GZipStream(input, CompressionMode.Decompress);
        var pull = JsonSerializer.Deserialize<PullDeathSnapshot>(stream)!;
        var source = LocalPullEventSource.Create(pull);
        var analysis = await new BlackHoleAnalyzer(source).AnalyzeAsync(source.Report, source.Fight, default);
        // Avoid depending on the last bit of platform-specific trigonometry in CI.
        var portable = JsonSerializer.SerializeToUtf8Bytes(analysis.Tethers.Select(t => new
        {
            t.Set,
            t.Tether,
            t.Time,
            Beams = t.Beams.Select(b => new
            {
                b.Instance,
                X = (long)Math.Round(b.Origin.X * 10000d),
                Y = (long)Math.Round(b.Origin.Y * 10000d),
                Facing = (long)Math.Round(b.FacingRadians * 100000d),
                b.TetherHolder,
                b.Hits,
            }),
        }));
        Assert.Equal(directory is null ? fixtureHash : fullHash, Convert.ToHexString(SHA256.HashData(portable)));
        Assert.Equal(new[] { 2, 3, 3, 2 }, analysis.Tethers.GroupBy(t => t.Set).Select(g => g.Count()));
        var directions = compass.Split(' ');
        foreach (var set in analysis.Tethers.GroupBy(t => t.Set))
            Assert.Single(set.Select(t => t.BigKefka).Distinct());
        foreach (var t in analysis.Tethers)
        {
            Assert.False(t.KefkaInferredFromSlapHappy);
            Assert.True(t.BigKefka.HasValue);
            var marker = t.BigKefka.Value;
            Assert.InRange(marker.Length(), 24.99f, 25.01f);
            var angle = Math.Atan2(marker.X, -marker.Y);
            var expectedAngle = Array.IndexOf(new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" }, directions[t.Set - 1]) * Math.PI / 4;
            Assert.InRange(Math.Abs(Math.IEEERemainder(angle - expectedAngle, Math.Tau)), 0, 0.02);
            var clockwise = t.Beams.OrderBy(b =>
            {
                var delta = (Math.Atan2(b.Origin.X, -b.Origin.Y) - angle + Math.Tau) % Math.Tau;
                return delta > Math.Tau - 0.02 ? 0 : delta;
            });
            var roles = clockwise.Select(b => analysis.Players.Single(p => p.ActorId == b.TetherHolder).Role.Type);
            var expectedRoles = (t.Set, t.Tether) switch
            {
                (1, 1) => "D",
                (1, 2) or (4, 1) => "DS",
                (4, 2) => "S",
                _ => "DSA",
            };
            Assert.Equal(expectedRoles, string.Concat(roles.Select(role => role.ToString()[0])));
        }
    }

    [Theory]
    [InlineData("no caster")]
    [InlineData("two casters")]
    [InlineData("no set two appearance")]
    public async Task IncompleteRecordingDoesNotGuess(string loss)
    {
        using var input = typeof(BlackHoleMarkerRegressionTests).Assembly.GetManifestResourceStream("BlackHole.2673.json.gz")!;
        using var stream = new GZipStream(input, CompressionMode.Decompress);
        var pull = JsonSerializer.Deserialize<PullDeathSnapshot>(stream)!;
        var mechanics = pull.ReplayMechanics.ToList();
        if (loss == "no caster")
            mechanics.RemoveAll(m => m.RawEventId is 50167 or 47846 or 47847);
        else if (loss == "two casters")
        {
            var cast = mechanics.First(m => m.RawEventId == 50167);
            var real = cast.SourceKey.Split(':')[1];
            var other = pull.ReplayPositions.First(p => p.ActorName == "Kefka" && p.ActorKey != $"enemy:{real}");
            mechanics.Add(cast with { SourceKey = cast.SourceKey.Replace(real, other.EntityId.ToString("X8")) });
        }
        else
            mechanics.RemoveAll(m => m.RawEventKind == "object" && m.PullElapsedSeconds is > 600 and < 630);

        var source = LocalPullEventSource.Create(pull with { ReplayMechanics = mechanics });
        var result = await new BlackHoleAnalyzer(source).AnalyzeAsync(source.Report, source.Fight, default);
        Assert.Equal(10, result.Tethers.Count);
        Assert.All(result.Tethers, t =>
        {
            Assert.NotEmpty(t.Beams);
            Assert.Equal(loss == "no set two appearance" && t.Set != 2, t.BigKefka.HasValue);
        });
    }

    [Fact]
    public async Task WrongPlayerTakingFirstTetherChangesTheLineNotKefka()
    {
        using var input = typeof(BlackHoleMarkerRegressionTests).Assembly.GetManifestResourceStream("BlackHole.2673.json.gz")!;
        using var stream = new GZipStream(input, CompressionMode.Decompress);
        var pull = JsonSerializer.Deserialize<PullDeathSnapshot>(stream)!;
        static async Task<BlackHoleAnalysis> Read(PullDeathSnapshot pull)
        {
            var source = LocalPullEventSource.Create(pull);
            return await new BlackHoleAnalyzer(source).AnalyzeAsync(source.Report, source.Fight, default);
        }
        var original = await Read(pull);
        var first = pull.ReplayMechanics.First(m => m.RawEventKind == "dmu-p3-nothingness");
        var prefix = $"black-hole-tether:{first.SourceKey.Split(':')[1]}:";
        var healer = pull.ReplayPositions.First(p => p.ActorKind == ReplayActorKind.Player && p.ClassJobId == 24);
        var changed = await Read(pull with
        {
            ReplayMechanics = pull.ReplayMechanics.Select(m => m.SourceKey.StartsWith(prefix, StringComparison.Ordinal)
                ? m with { SourceKey = prefix + healer.EntityId.ToString("X8") } : m).ToArray(),
        });
        var before = Assert.Single(original.Tethers[0].Beams);
        var after = Assert.Single(changed.Tethers[0].Beams);
        Assert.NotEqual(before.TetherHolder, after.TetherHolder);
        Assert.Equal(healer.ActorName, changed.Players.Single(p => p.ActorId == after.TetherHolder).Name);
        Assert.Equal(before.Origin, after.Origin);
        Assert.Equal(before.Hits.ToArray(), after.Hits.ToArray());
        Assert.Equal(original.Tethers.Select(t => t.BigKefka), changed.Tethers.Select(t => t.BigKefka));
    }

    [Fact]
    public void ReportUsesOnlyMatchingSpawnAndFreshBossPose()
    {
        var boss = new HashSet<int> { 4 };
        var hole = new BlackHoleActor(7, 2);
        FflogsEvent Cast(int id, double time, FflogsResources resources) => new()
        { Type = "cast", SourceID = id, Timestamp = time, SourceResources = resources };
        FflogsEvent Spawn(int instance) => new() { Type = "summon", TargetID = 7, TargetInstance = instance, Timestamp = 6000 };
        FflogsResources East() => new() { X = 10000, Y = 10000, Facing = -200 * Math.PI };
        var valid = Cast(4, 5800, East());
        var late = Cast(4, 6100, new() { X = 10000, Y = 10000, Facing = 0 });
        var copy = Cast(5, 5900, late.SourceResources!);
        Vector2? Marker(FflogsEvent[] casts, params FflogsEvent[] events) =>
            BlackHoleMarkerEvidence.FromReport(boss, casts, events, new(100, 100)).AtFirstAppearance([hole], 0, 12000);
        var west = Marker([valid, copy, late], Spawn(2));
        Assert.True(west.HasValue && Vector2.Distance(west.Value, new(-25, 0)) < 0.001f);
        Assert.Null(Marker([valid], Spawn(1)));
        Assert.Null(Marker([copy, late], Spawn(2)));
        Assert.Null(Marker([Cast(4, 1000, East())], Spawn(2)));
        Assert.Null(Marker([valid, Cast(4, 5900, new() { X = 10000, Y = 10000 })], Spawn(2)));
        Assert.Null(Marker([valid, Cast(4, 5900, new() { X = 12000, Y = 10000, Facing = 0 })], Spawn(2)));
        Assert.Null(Marker([valid], new FflogsEvent { Type = "tether", SourceID = 7, SourceInstance = 2, Timestamp = 11000 }));
    }
}
