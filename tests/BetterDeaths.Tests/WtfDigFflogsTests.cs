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
