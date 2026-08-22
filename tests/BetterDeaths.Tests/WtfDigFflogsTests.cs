using System.Numerics;
using System.Net;
using System.Text;
using BetterDeaths.WtfDig;

namespace BetterDeaths.Tests;

public sealed class WtfDigFflogsTests
{
    [Theory]
    [InlineData("aBcDeFgHiJkLmNoP", "aBcDeFgHiJkLmNoP", null, false)]
    [InlineData("a:nbTH9pv47tZR82C6", "a:nbTH9pv47tZR82C6", null, false)]
    [InlineData("https://www.fflogs.com/reports/aBcDeFgHiJkLmNoP", "aBcDeFgHiJkLmNoP", null, false)]
    [InlineData("https://www.fflogs.com/reports/aBcDeFgHiJkLmNoP?fight=12&type=damage-done", "aBcDeFgHiJkLmNoP", 12, false)]
    [InlineData("https://www.fflogs.com/reports/aBcDeFgHiJkLmNoP#fight=7&type=summary", "aBcDeFgHiJkLmNoP", 7, false)]
    [InlineData("https://www.fflogs.com/reports/aBcDeFgHiJkLmNoP?fight=last", "aBcDeFgHiJkLmNoP", null, true)]
    public void ParseReportInput_AcceptsAnalyzerLinkForms(
        string value,
        string expectedCode,
        int? expectedFight,
        bool expectedLast)
    {
        var parsed = FflogsClient.ParseReportInput(value);

        Assert.NotNull(parsed);
        Assert.Equal(expectedCode, parsed.Code);
        Assert.Equal(expectedFight, parsed.FightId);
        Assert.Equal(expectedLast, parsed.UseLastFight);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-report")]
    [InlineData("https://www.fflogs.com/character/id/123")]
    [InlineData("shortcode")]
    public void ParseReportInput_RejectsUnrecognizedInput(string value)
    {
        Assert.Null(FflogsClient.ParseReportInput(value));
    }

    [Fact]
    public void ArrowStrategies_AlwaysProduceSixteenTargets()
    {
        foreach (var strategy in Enum.GetValues<ArrowStrategy>())
        {
            Assert.Equal(16, ArrowsAnalyzer.ExpectedSlots(strategy).Count);
        }
    }

    [Fact]
    public void ArrowError_UsesNearestStrategyTarget()
    {
        var slots = ArrowsAnalyzer.ExpectedSlots(ArrowStrategy.MerryGoRound);

        Assert.Equal(0, ArrowsAnalyzer.Error(new Vector2(-12, -6), slots));
        Assert.Equal(1, ArrowsAnalyzer.Error(new Vector2(-11, -6), slots));
        Assert.Null(ArrowsAnalyzer.Error(null, slots));
    }

    [Theory]
    [InlineData(0, 0, -20)]
    [InlineData(90, 20, 0)]
    [InlineData(180, 0, 20)]
    [InlineData(270, -20, 0)]
    public void LimitCutAnglesMatchArenaCompass(double angle, float expectedX, float expectedY)
    {
        var position = LimitCutAnalyzer.PositionAtAngle(angle, 20);

        Assert.InRange(position.X, expectedX - 0.001f, expectedX + 0.001f);
        Assert.InRange(position.Y, expectedY - 0.001f, expectedY + 0.001f);
        Assert.InRange(LimitCutAnalyzer.AngleOf(position), angle - 0.001, angle + 0.001);
    }

    [Fact]
    public async Task ReportSummary_UsesProxyContractAndParsesResponse()
    {
        var handler = new RecordingHandler(
            """
            {
              "data": {
                "reportData": {
                  "report": {
                    "title": "Dancing Mad test",
                    "startTime": 1000,
                    "endTime": 2000,
                    "zone": { "id": 65, "name": "The Omega Protocol" },
                    "fights": [{ "id": 7, "name": "Dancing Mad", "encounterID": 1085, "startTime": 0, "endTime": 900000 }],
                    "masterData": { "abilities": [], "actors": [] }
                  }
                }
              }
            }
            """);
        using var httpClient = new HttpClient(handler);
        using var client = new FflogsClient(httpClient, new Uri("https://example.test/api/fflogs"));

        var report = await client.FetchReportSummaryAsync("aBcDeFgHiJkLmNoP", CancellationToken.None);

        Assert.Equal("Dancing Mad test", report.Title);
        Assert.Single(report.Fights);
        Assert.Contains("BetterDeaths/FFLogsAnalyzer", handler.UserAgent);
        Assert.Equal("native-analyzer", handler.ClientHeader);
        Assert.Contains("\"operation\":\"reportSummary\"", handler.Body);
        Assert.Contains("\"code\":\"aBcDeFgHiJkLmNoP\"", handler.Body);
        Assert.Contains("\"cacheTtl\":30", handler.Body);
    }

    [Fact]
    public void EligibleFights_OnlyReturnsDancingMadPullsThatReachedTheMechanic()
    {
        var report = new FflogsReportSummary
        {
            Code = "aBcDeFgHiJkLmNoP",
            Fights =
            [
                new FflogsFight { Id = 3, Name = "Dancing Mad", EncounterID = 1085, StartTime = 300, EndTime = 600_300 },
                new FflogsFight { Id = 1, Name = "Dancing Mad", EncounterID = 1085, StartTime = 100, EndTime = 200_100 },
                new FflogsFight { Id = 2, Name = "Another Fight", EncounterID = 9999, StartTime = 200, EndTime = 900_200 },
            ],
        };
        var forsaken = WtfDigAnalyzerCatalog.All.Single(analyzer => analyzer.Key == "forsaken");

        var eligible = WtfDigAnalyzerCatalog.EligibleFights(report, forsaken);

        Assert.Equal([3], eligible.Select(fight => fight.Id));
    }

    [Fact]
    public void ForsakenRaidwide_HasTheCorrectDisplayIdentity()
    {
        var resolution = new ForsakenResolution { SetNumber = 0 };

        Assert.True(resolution.IsRaidwide);
        Assert.Equal("Forsaken (raidwide)", resolution.Label);
        Assert.Equal(string.Empty, resolution.Parity);
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(1, 2, 2)]
    [InlineData(2, 1, 3)]
    [InlineData(3, 2, 3)]
    [InlineData(4, 1, 2)]
    [InlineData(4, 2, 1)]
    public void BlackHoleExpectedSoaks_MatchesTheMechanic(int set, int tether, int expected)
    {
        Assert.Equal(expected, BlackHoleAnalyzer.ExpectedSoaks(set, tether));
    }

    [Fact]
    public async Task LocalPull_UsesRecordedReplayDataWithTheExistingArrowAnalyzer()
    {
        var source = LocalPullEventSource.Create(CreateLocalArrowPull());

        var analysis = await new ArrowsAnalyzer(source)
            .AnalyzeAsync(source.Report, source.Fight, CancellationToken.None);

        var wave = Assert.Single(analysis.Waves);
        var arrow = Assert.Single(wave.Arrows);
        Assert.Equal("Nai", arrow.Name);
        Assert.Equal(1004876u, arrow.StatusId);
        Assert.Equal(new Vector2(12, -6), arrow.Position);
        var start = Assert.Single(analysis.Starts);
        Assert.Equal(ArrowStartRole.Sleep, start.Role);
        Assert.Equal("SAM", start.Job.Abbreviation);
    }

    [Fact]
    public async Task LocalPull_NormalizesGameStatusesAndKeepsLocalQueriesOffline()
    {
        var source = LocalPullEventSource.Create(CreateLocalArrowPull());

        var debuffs = await source.FetchAllEventsAsync(
            new FflogsEventQuery(
                source.Report.Code,
                source.Fight.Id,
                source.Fight.StartTime,
                source.Fight.EndTime,
                FflogsEventDataType.Debuffs),
            CancellationToken.None);

        Assert.Contains(debuffs, entry => entry.Type == "applydebuff" && entry.AbilityGameID == 1004894);
        Assert.Contains(debuffs, entry => entry.Type == "removedebuff" && entry.AbilityGameID == 1004876);
        Assert.Single(source.Report.Fights);
        Assert.StartsWith("local:", source.Report.Code);
    }

    [Fact]
    public async Task LocalPull_ForsakenUsesRecordedTowerTargetsInsteadOfPositionGuessing()
    {
        var source = LocalPullEventSource.Create(CreateLocalForsakenEvidencePull());
        var damage = await source.FetchAllEventsAsync(
            new FflogsEventQuery(
                source.Report.Code,
                source.Fight.Id,
                source.Fight.StartTime,
                source.Fight.EndTime,
                FflogsEventDataType.DamageTaken,
                FflogsHostilityType.Friendlies,
                AbilityId: 47806),
            CancellationToken.None);
        var expectedTarget = source.Report.Actors.Single(actor => actor.Name == "Actual Soaker").Id;
        var guessedTarget = source.Report.Actors.Single(actor => actor.Name == "Nearby Player").Id;

        Assert.Contains(damage, entry => entry.Type == "calculateddamage" && entry.TargetID == expectedTarget);
        Assert.DoesNotContain(damage, entry => entry.TargetID == guessedTarget);
    }

    [Fact]
    public async Task LocalPull_ForsakenCloneAndCleaveKeepExactTargetSourceAndFinalFacing()
    {
        var source = LocalPullEventSource.Create(CreateLocalForsakenClonePull());
        var cloneCasts = await source.FetchAllEventsAsync(
            new FflogsEventQuery(
                source.Report.Code,
                source.Fight.Id,
                source.Fight.StartTime,
                source.Fight.EndTime,
                FflogsEventDataType.Casts,
                FflogsHostilityType.Enemies,
                AbilityId: 47833),
            CancellationToken.None);
        var expectedTarget = source.Report.Actors.Single(actor => actor.Name == "Actual Bait").Id;
        var cloneCast = Assert.Single(cloneCasts, entry => entry.Type == "cast");
        Assert.Equal(expectedTarget, cloneCast.TargetID);
        Assert.Equal(11200, cloneCast.TargetResources!.X);

        var cleaveCasts = await source.FetchAllEventsAsync(
            new FflogsEventQuery(
                source.Report.Code,
                source.Fight.Id,
                source.Fight.StartTime,
                source.Fight.EndTime,
                FflogsEventDataType.Casts,
                FflogsHostilityType.Enemies,
                AbilityId: 47837),
            CancellationToken.None);
        Assert.Contains(cleaveCasts, entry => entry.Type == "begincast" && entry.Timestamp == 10_000);
        var resolvedCleave = Assert.Single(cleaveCasts, entry => entry.Type == "cast");
        Assert.Equal(cloneCast.SourceID, resolvedCleave.SourceID);
        Assert.Equal(cloneCast.SourceInstance, resolvedCleave.SourceInstance);
        Assert.Equal(15_000, resolvedCleave.Timestamp);
        Assert.Equal(10500, resolvedCleave.SourceResources!.X);
        Assert.Equal(10100, resolvedCleave.SourceResources.Y);
        Assert.Equal((-1.25 * 100.0) - (150.0 * Math.PI), resolvedCleave.SourceResources.Facing, 4);
    }

    private static PullDeathSnapshot CreateLocalArrowPull()
    {
        var startedAt = new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);
        var playerEntityId = 0x10000001u;
        var bossEntityId = 0x40000001u;
        ReplayPositionSnapshot Position(float seconds, string key, string name, ReplayActorKind kind, uint entityId, float x, float z) =>
            new(
                startedAt.AddSeconds(seconds), seconds, key, name, kind,
                kind == ReplayActorKind.Player ? 0 : 2000,
                entityId,
                kind == ReplayActorKind.Player ? 34u : 0u,
                kind == ReplayActorKind.Player ? "SAM" : string.Empty,
                x, 0, z, 0, 100_000, 0, 100_000, false, true);

        return new PullDeathSnapshot(
            startedAt.AddSeconds(180),
            "Wipe",
            1363,
            "Dancing Mad",
            180,
            [])
        {
            PullNumber = 12,
            ReplayPositions =
            [
                Position(148, "enemy:40000001", "Kefka", ReplayActorKind.Enemy, bossEntityId, 100, 100),
                Position(149, "player:member-1", "Nai", ReplayActorKind.Player, playerEntityId, 108, 96),
                Position(150, "player:member-1", "Nai", ReplayActorKind.Player, playerEntityId, 110, 95),
                Position(160, "player:member-1", "Nai", ReplayActorKind.Player, playerEntityId, 112, 94),
            ],
            ReplayMechanics = [],
            ReplayDebuffs =
            [
                new ReplayDebuffSnapshot(
                    startedAt.AddSeconds(150), 150, "member-1", "Nai", 0, 34, "SAM",
                    new StatusSnapshot(4894, "Sleep", 0, bossEntityId, 0, 12), true),
                new ReplayDebuffSnapshot(
                    startedAt.AddSeconds(151), 151, "member-1", "Nai", 0, 34, "SAM",
                    new StatusSnapshot(4876, "Tele-Portent", 0, bossEntityId, 0, 9), true),
                new ReplayDebuffSnapshot(
                    startedAt.AddSeconds(160), 160, "member-1", "Nai", 0, 34, "SAM",
                    new StatusSnapshot(4876, "Tele-Portent", 0, bossEntityId, 0, 0), false),
            ],
            ReplayDebuffsCaptured = true,
        };
    }

    private static PullDeathSnapshot CreateLocalForsakenEvidencePull()
    {
        var startedAt = new DateTime(2026, 8, 21, 13, 0, 0, DateTimeKind.Utc);
        ReplayPositionSnapshot Position(float seconds, string key, string name, uint entityId, float x, float z, int partyIndex) =>
            new(
                startedAt.AddSeconds(seconds), seconds, key, name,
                entityId == 0x40000001 ? ReplayActorKind.Enemy : ReplayActorKind.Player,
                partyIndex,
                entityId,
                entityId == 0x40000001 ? 0u : 34u,
                entityId == 0x40000001 ? string.Empty : "SAM",
                x, 0, z, 0, 100_000, 0, 100_000, false, true);
        ReplayMechanicSnapshot Mechanic(
            float seconds,
            float duration,
            string sourceKey,
            ReplayMechanicShape shape,
            float x,
            float z,
            string rawEventKind,
            uint rawState = 0) =>
            new(
                startedAt.AddSeconds(seconds), seconds, duration, sourceKey, "Kefka",
                shape, x, 0, z, 0, 4, 0, 0, 0, "The Path of Light",
                rawEventKind, 47806, rawState, true);

        return new PullDeathSnapshot(
            startedAt.AddSeconds(20),
            "Wipe",
            1363,
            "Dancing Mad",
            20,
            [])
        {
            PullNumber = 13,
            ReplayPositions =
            [
                Position(9, "enemy:40000001", "Kefka", 0x40000001, 100, 100, 2000),
                Position(9, "nearby-player", "Nearby Player", 0x10000001, 96, 100, 0),
                Position(9, "actual-soaker", "Actual Soaker", 0x10000002, 110, 100, 1),
            ],
            ReplayMechanics =
            [
                Mechanic(4, 5, "dmu-p2-path-of-light:1:1", ReplayMechanicShape.Tower, 96, 100, "dmu-p2-path-of-light"),
                Mechanic(
                    9,
                    0.05f,
                    "dmu-p2-path-of-light-activation:1:40000001:100:actual-soaker",
                    ReplayMechanicShape.Label,
                    110,
                    100,
                    ReplayEncounterModules.DmuP2PathOfLightActivationRawEventKind,
                    1234),
            ],
        };
    }

    private static PullDeathSnapshot CreateLocalForsakenClonePull()
    {
        var startedAt = new DateTime(2026, 8, 21, 14, 0, 0, DateTimeKind.Utc);
        ReplayPositionSnapshot Position(float seconds, string key, string name, ReplayActorKind kind, uint entityId, float x, float z, float rotation, int partyIndex) =>
            new(
                startedAt.AddSeconds(seconds), seconds, key, name, kind, partyIndex, entityId,
                kind == ReplayActorKind.Player ? 34u : 0u,
                kind == ReplayActorKind.Player ? "SAM" : string.Empty,
                x, 0, z, rotation, 100_000, 0, 100_000, false, true);
        ReplayMechanicSnapshot Mechanic(
            float seconds,
            float duration,
            string sourceKey,
            string sourceName,
            ReplayMechanicShape shape,
            float x,
            float z,
            float rotation,
            string label,
            string rawEventKind,
            uint actionId,
            uint rawState = 0) =>
            new(
                startedAt.AddSeconds(seconds), seconds, duration, sourceKey, sourceName,
                shape, x, 0, z, rotation, 5, 100, 0, 180, label,
                rawEventKind, actionId, rawState, true);

        return new PullDeathSnapshot(
            startedAt.AddSeconds(20),
            "Wipe",
            1363,
            "Dancing Mad",
            20,
            [])
        {
            PullNumber = 14,
            ReplayPositions =
            [
                Position(5, "clone", "Future Fragment", ReplayActorKind.Enemy, 0x40000002, 100, 100, 0, 2000),
                Position(5, "nearby", "Nearby Player", ReplayActorKind.Player, 0x10000001, 101, 100, 0, 0),
                Position(5, "actual-bait", "Actual Bait", ReplayActorKind.Player, 0x10000002, 112, 99, 0, 1),
            ],
            ReplayMechanics =
            [
                Mechanic(
                    5,
                    2.4f,
                    "dmu-p2-forsaken-clone-drop:40000002:100:actual-bait",
                    "Future Fragment -> Actual Bait",
                    ReplayMechanicShape.Spread,
                    112,
                    99,
                    0,
                    "Past's End",
                    ReplayEncounterModules.DmuP2ForsakenCloneDropRawEventKind,
                    47833,
                    1234),
                Mechanic(
                    10,
                    5.6f,
                    $"bossmod-cast:40000002:47837:{startedAt.AddSeconds(10).Ticks}",
                    "Future Fragment",
                    ReplayMechanicShape.Cone,
                    100,
                    100,
                    0.25f,
                    "All Things Ending",
                    "bossmod-cast",
                    47837,
                    0x40000002),
                Mechanic(
                    15,
                    2,
                    "dmu-p2-all-things-ending:40000002:101",
                    "Future Fragment",
                    ReplayMechanicShape.Cone,
                    105,
                    101,
                    1.25f,
                    "All Things Ending",
                    "dmu-p2-all-things-ending",
                    47837,
                    0x40000002),
            ],
        };
    }

    private sealed class RecordingHandler(string responseBody) : HttpMessageHandler
    {
        internal string Body { get; private set; } = string.Empty;
        internal string UserAgent { get; private set; } = string.Empty;
        internal string ClientHeader { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            UserAgent = request.Headers.UserAgent.ToString();
            ClientHeader = request.Headers.GetValues("X-Better-Deaths-Client").Single();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
