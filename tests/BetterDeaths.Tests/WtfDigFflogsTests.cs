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

    [Fact]
    public void ForsakenConeVictimsDoNotRequireClonePositionData()
    {
        var victimIds = ForsakenAnalyzer.ConeVictimIdsAt(
            [
                new FflogsEvent
                {
                    Timestamp = 10_000,
                    Type = "calculateddamage",
                    AbilityGameID = 47810,
                    TargetID = 42,
                    SourceResources = null,
                    TargetResources = new FflogsResources { X = 11200, Y = 10000 },
                },
            ],
            10_000,
            new Dictionary<uint, string> { [47810] = "Spellwave" });

        Assert.Contains(42, victimIds);
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

        Assert.Equal(2, analysis.Waves.Count);
        var firstArrow = Assert.Single(analysis.Waves[0].Arrows);
        Assert.Equal("Nai", firstArrow.Name);
        Assert.Equal(1004876u, firstArrow.StatusId);
        Assert.Equal(new Vector2(12, -6), firstArrow.Position);
        var secondArrow = Assert.Single(analysis.Waves[1].Arrows);
        Assert.Equal(1004876u, secondArrow.StatusId);
        Assert.Equal(new Vector2(-12, 6), secondArrow.Position);
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
        Assert.Equal(2, debuffs.Count(entry => entry.Type == "applydebuff" && entry.AbilityGameID == 1004876));
        var arrowRemovals = debuffs
            .Where(entry => entry.Type == "removedebuff" && entry.AbilityGameID == 1004876)
            .ToArray();
        Assert.Equal(2, arrowRemovals.Length);
        Assert.Equal([160_000, 163_000], arrowRemovals.Select(entry => entry.Timestamp));
        Assert.Single(source.Report.Fights);
        Assert.StartsWith("local:", source.Report.Code);
    }

    [Fact]
    public async Task LocalPull_ForsakenUsesRecordedTowerTargetsInsteadOfPositionGuessing()
    {
        var source = LocalPullEventSource.Create(CreateLocalForsakenEvidencePull());
        var anchors = await source.FetchAllEventsAsync(
            new FflogsEventQuery(
                source.Report.Code,
                source.Fight.Id,
                source.Fight.StartTime,
                source.Fight.EndTime,
                FflogsEventDataType.Casts,
                FflogsHostilityType.Enemies,
                AbilityId: ForsakenAnalyzer.ForsakenCastGameId),
            CancellationToken.None);
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

        Assert.Single(anchors, entry => entry.Type == "cast");
        Assert.Equal(WtfDigLocalDataQuality.Estimated, source.AnalyzerAvailability["forsaken"].Quality);
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

    [Fact]
    public async Task LocalPull_ForsakenPredictedCleaveUsesCompletionFacingWhenNoResultPacketExists()
    {
        var pull = CreateLocalForsakenClonePull();
        pull = pull with
        {
            ReplayMechanics = pull.ReplayMechanics
                .Where(mechanic => mechanic.RawEventKind != "dmu-p2-all-things-ending")
                .ToArray(),
        };
        var source = LocalPullEventSource.Create(pull);

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

        var predictedCleave = Assert.Single(cleaveCasts, entry => entry.Type == "cast");
        Assert.Equal(15_000, predictedCleave.Timestamp);
        Assert.Equal(10500, predictedCleave.SourceResources!.X);
        Assert.Equal(10100, predictedCleave.SourceResources.Y);
        Assert.Equal((-1.25 * 100.0) - (150.0 * Math.PI), predictedCleave.SourceResources.Facing, 4);
    }

    [Fact]
    public void ForsakenReplay_PredictedPastCleaveFacesRearWhenNoResultPacketExists()
    {
        var pull = CreateLocalForsakenClonePull();
        pull = pull with
        {
            ReplayMechanics = pull.ReplayMechanics
                .Where(mechanic => mechanic.RawEventKind != "dmu-p2-all-things-ending")
                .ToArray(),
        };

        var normalized = ForsakenCleavePosePolicy.NormalizeTimeline(pull.ReplayMechanics, pull.ReplayPositions);
        var cleave = Assert.Single(normalized, mechanic => mechanic.RawEventKind == "bossmod-cast");

        Assert.Equal(1.25f + MathF.PI, cleave.Rotation, 4);
        Assert.Equal("All Things Ending (Past's End)", cleave.Label);
    }

    [Fact]
    public void ForsakenReplay_PredictedFutureCleaveKeepsFrontFacing()
    {
        var pull = CreateLocalForsakenClonePull();
        var mechanics = pull.ReplayMechanics
            .Where(mechanic => mechanic.RawEventKind != "dmu-p2-all-things-ending")
            .Select(mechanic => mechanic.RawEventKind == ReplayEncounterModules.DmuP2ForsakenCloneDropRawEventKind
                ? mechanic with { RawEventId = 47832, Label = "Future's End" }
                : mechanic.RawEventKind == "bossmod-cast"
                    ? mechanic with { RawEventId = 47836 }
                    : mechanic)
            .ToArray();

        var normalized = ForsakenCleavePosePolicy.NormalizeTimeline(mechanics, pull.ReplayPositions);
        var cleave = Assert.Single(normalized, mechanic => mechanic.RawEventKind == "bossmod-cast");

        Assert.Equal(1.25f, cleave.Rotation, 4);
        Assert.Equal("All Things Ending (Future's End)", cleave.Label);
    }

    [Fact]
    public void ForsakenReplay_ExactPastCleaveSupersedesCastAndFlipsOnlyOnce()
    {
        var pull = CreateLocalForsakenClonePull();

        var normalized = ForsakenCleavePosePolicy.NormalizeTimeline(pull.ReplayMechanics, pull.ReplayPositions);
        var cleave = Assert.Single(normalized, mechanic => mechanic.RawEventId == 47837);

        Assert.Equal(ForsakenCleavePosePolicy.PredictedRawEventKind, cleave.RawEventKind);
        Assert.Equal(1.25f + MathF.PI, cleave.Rotation, 4);
        Assert.Equal("All Things Ending (Past's End)", cleave.Label);
        Assert.Equal(5.0f, cleave.DurationSeconds, 4);
    }

    [Fact]
    public void ForsakenCleavePose_UsesRecordedPoseNearPredictedResult()
    {
        var pull = CreateLocalForsakenClonePull();
        var cast = pull.ReplayMechanics.Single(mechanic => mechanic.RawEventKind == "bossmod-cast");

        var normalized = ForsakenCleavePosePolicy.UseCompletionPose(cast, pull.ReplayPositions);

        Assert.Equal(105, normalized.X);
        Assert.Equal(101, normalized.Z);
        Assert.Equal(1.25f, normalized.Rotation);
        Assert.Equal(cast.SeenAtUtc, normalized.SeenAtUtc);
        Assert.Equal(cast.DurationSeconds, normalized.DurationSeconds);
    }

    [Fact]
    public void ForsakenCleavePose_KeepsCapturedPoseWithoutANearbySourceSample()
    {
        var pull = CreateLocalForsakenClonePull();
        var cast = pull.ReplayMechanics.Single(mechanic => mechanic.RawEventKind == "bossmod-cast");

        var normalized = ForsakenCleavePosePolicy.UseCompletionPose(cast, []);

        Assert.Same(cast, normalized);
    }

    [Fact]
    public async Task LocalPull_BlackHoleKeepsDistinctObjectsAndOnlyReturnsRequestedTethers()
    {
        var source = LocalPullEventSource.Create(CreateLocalBlackHolePull());

        var tethers = await source.FetchAllEventsAsync(
            new FflogsEventQuery(
                source.Report.Code,
                source.Fight.Id,
                source.Fight.StartTime,
                source.Fight.EndTime,
                FilterExpression: "type='tether'"),
            CancellationToken.None);
        var objectCasts = await source.FetchAllEventsAsync(
            new FflogsEventQuery(
                source.Report.Code,
                source.Fight.Id,
                source.Fight.StartTime,
                source.Fight.EndTime,
                FflogsEventDataType.Casts,
                FflogsHostilityType.Enemies,
                AbilityId: 19512),
            CancellationToken.None);

        Assert.Equal(2, tethers.Count);
        Assert.Equal(2, tethers.Select(entry => entry.SourceID).Distinct().Count());
        Assert.Equal(2, tethers.Select(entry => entry.SourceInstance).Distinct().Count());
        Assert.All(tethers, entry => Assert.Equal("tether", entry.Type));
        Assert.Empty(objectCasts);
    }

    [Fact]
    public async Task LocalPull_BlackHoleAnalysisKeepsEveryBeamAndTetherHolder()
    {
        var source = LocalPullEventSource.Create(CreateLocalBlackHolePull());

        var analysis = await new BlackHoleAnalyzer(source)
            .AnalyzeAsync(source.Report, source.Fight, CancellationToken.None);

        var tether = Assert.Single(analysis.Tethers);
        Assert.Equal(2, tether.Beams.Count);
        Assert.Equal(2, tether.Beams.Select(beam => beam.Instance).Distinct().Count());
        Assert.Equal(2, tether.Beams.Select(beam => beam.Origin).Distinct().Count());
        Assert.All(tether.Beams, beam => Assert.NotNull(beam.TetherHolder));
    }

    [Fact]
    public async Task LocalPull_FiltersCastsBySourceNameLikeWtfDig()
    {
        var source = LocalPullEventSource.Create(CreateLocalBlackHolePull());

        var bossCasts = await source.FetchAllEventsAsync(
            new FflogsEventQuery(
                source.Report.Code,
                source.Fight.Id,
                source.Fight.StartTime,
                source.Fight.EndTime,
                FflogsEventDataType.Casts,
                FflogsHostilityType.Enemies,
                FilterExpression: "source.name in ('Kefka', 'Chaos', 'Exdeath')"),
            CancellationToken.None);

        Assert.NotEmpty(bossCasts);
        Assert.All(bossCasts, entry => Assert.Equal(47867u, entry.AbilityGameID));
    }

    [Fact]
    public async Task LocalPull_UsesExactAnalyzerTimelineEventInsteadOfAddingAnEstimatedAnchor()
    {
        var pull = CreateLocalArrowPull() with
        {
            ReplayAnalyzerEvents =
            [
                new ReplayAnalyzerEventSnapshot(
                    new DateTime(2026, 8, 21, 12, 2, 28, DateTimeKind.Utc),
                    148,
                    "cast",
                    0x40000001,
                    "Kefka",
                    47801,
                    "Tele-Trouncing",
                    true,
                    101,
                    0,
                    99,
                    0.5f),
            ],
        };
        var source = LocalPullEventSource.Create(pull);

        var casts = await source.FetchAllEventsAsync(
            new FflogsEventQuery(
                source.Report.Code,
                source.Fight.Id,
                source.Fight.StartTime,
                source.Fight.EndTime,
                FflogsEventDataType.Casts,
                FflogsHostilityType.Enemies,
                AbilityId: 47801),
            CancellationToken.None);

        var cast = Assert.Single(casts);
        Assert.Equal(148_000, cast.Timestamp);
        Assert.Equal(10_100, cast.SourceResources!.X);
        Assert.Equal(9_900, cast.SourceResources.Y);
        Assert.Equal(WtfDigLocalDataQuality.Exact, source.AnalyzerAvailability["arrows"].Quality);
        Assert.Equal(WtfDigLocalDataQuality.Unavailable, source.AnalyzerAvailability["kefka-says"].Quality);
    }

    [Fact]
    public void LocalPull_LabelsOlderFallbackDataAsEstimated()
    {
        var arrows = LocalPullEventSource.Create(CreateLocalArrowPull());
        var blackHole = LocalPullEventSource.Create(CreateLocalBlackHolePull());

        Assert.Equal(WtfDigLocalDataQuality.Estimated, arrows.AnalyzerAvailability["arrows"].Quality);
        Assert.Equal(WtfDigLocalDataQuality.Estimated, blackHole.AnalyzerAvailability["black-hole"].Quality);
    }

    [Fact]
    public async Task LocalPull_DoesNotMistakeLegacyEightDigitSequencesForActorIds()
    {
        var pull = CreateLocalForsakenEvidencePull();
        var template = pull.ReplayMechanics[0];
        pull = pull with
        {
            ReplayMechanics =
            [
                template with
                {
                    SourceKey = "legacy-target:47868:12345678:nearby-player",
                    RawEventKind = "legacy-target",
                    RawEventId = 47868,
                    PullElapsedSeconds = 9,
                },
                template with
                {
                    SourceKey = "legacy-target:47868:87654321:actual-soaker",
                    RawEventKind = "legacy-target",
                    RawEventId = 47868,
                    PullElapsedSeconds = 10,
                },
            ],
        };
        var source = LocalPullEventSource.Create(pull);

        var damage = await source.FetchAllEventsAsync(
            new FflogsEventQuery(
                source.Report.Code,
                source.Fight.Id,
                source.Fight.StartTime,
                source.Fight.EndTime,
                FflogsEventDataType.DamageTaken,
                FflogsHostilityType.Friendlies,
                AbilityId: 47868),
            CancellationToken.None);

        Assert.NotEmpty(damage);
        Assert.Single(damage.Select(entry => entry.SourceID).Distinct());
    }

    [Fact]
    public async Task LocalPull_KeepsSameNamedCombatSourcesSeparateWhenEntityIdsDiffer()
    {
        var pull = CreateLocalForsakenEvidencePull();
        var evidence = pull.ReplayMechanics[1];
        pull = pull with
        {
            ReplayMechanics =
            [
                evidence,
                evidence with
                {
                    SourceKey = "dmu-p2-path-of-light-activation:2:40000002:101:actual-soaker",
                    PullElapsedSeconds = 10,
                },
            ],
        };
        var source = LocalPullEventSource.Create(pull);

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

        var calculated = damage.Where(entry => entry.Type == "calculateddamage").ToArray();
        Assert.Equal(2, calculated.Length);
        Assert.Equal(2, calculated.Select(entry => entry.SourceID).Distinct().Count());
    }

    private static PullDeathSnapshot CreateLocalArrowPull()
    {
        var startedAt = new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);
        var playerEntityId = 0x10000001u;
        var bossEntityId = 0x40000001u;
        var secondBossEntityId = 0x40000002u;
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
                Position(148, "enemy:40000002", "Kefka", ReplayActorKind.Enemy, secondBossEntityId, 100, 100),
                Position(149, "player:member-1", "Nai", ReplayActorKind.Player, playerEntityId, 108, 96),
                Position(150, "player:member-1", "Nai", ReplayActorKind.Player, playerEntityId, 110, 95),
                Position(160, "player:member-1", "Nai", ReplayActorKind.Player, playerEntityId, 112, 94),
                Position(163, "player:member-1", "Nai", ReplayActorKind.Player, playerEntityId, 88, 106),
            ],
            ReplayMechanics = [],
            ReplayDebuffs =
            [
                new ReplayDebuffSnapshot(
                    startedAt.AddSeconds(150), 150, "member-1", "Nai", 0, 34, "SAM",
                    new StatusSnapshot(4894, "Sleep", 0, bossEntityId, 0, 12), true),
                new ReplayDebuffSnapshot(
                    startedAt.AddSeconds(151), 151, "member-1", "Nai", 0, 34, "SAM",
                    new StatusSnapshot(4876, "Tele-Portent", 0, 0, 0, 9), true),
                new ReplayDebuffSnapshot(
                    startedAt.AddSeconds(151.01), 151.01f, "member-1", "Nai", 0, 34, "SAM",
                    new StatusSnapshot(4876, "Tele-Portent", 0, bossEntityId, 0, 9), true),
                new ReplayDebuffSnapshot(
                    startedAt.AddSeconds(151.02), 151.02f, "member-1", "Nai", 0, 34, "SAM",
                    new StatusSnapshot(4876, "Tele-Portent", 0, secondBossEntityId, 0, 12), true),
                new ReplayDebuffSnapshot(
                    startedAt.AddSeconds(151.03), 151.03f, "member-1", "Nai", 0, 34, "SAM",
                    new StatusSnapshot(4876, "Tele-Portent", 0, 0, 0, 0), false),
                new ReplayDebuffSnapshot(
                    startedAt.AddSeconds(160), 160, "member-1", "Nai", 0, 34, "SAM",
                    new StatusSnapshot(4876, "Tele-Portent", 0, bossEntityId, 0, 0), false),
                new ReplayDebuffSnapshot(
                    startedAt.AddSeconds(163), 163, "member-1", "Nai", 0, 34, "SAM",
                    new StatusSnapshot(4876, "Tele-Portent", 0, secondBossEntityId, 0, 0), false),
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
                Position(10, "clone", "Future Fragment", ReplayActorKind.Enemy, 0x40000002, 100, 100, 0.25f, 2000),
                Position(15, "clone", "Future Fragment", ReplayActorKind.Enemy, 0x40000002, 105, 101, 1.25f, 2000),
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

    private static PullDeathSnapshot CreateLocalBlackHolePull()
    {
        var startedAt = new DateTime(2026, 8, 21, 15, 0, 0, DateTimeKind.Utc);
        ReplayPositionSnapshot Player(float seconds, string key, string name, uint entityId, float x, float z, int partyIndex) =>
            new(
                startedAt.AddSeconds(seconds), seconds, key, name, ReplayActorKind.Player,
                partyIndex, entityId, 34, "SAM", x, 0, z, 0,
                100_000, 0, 100_000, false, true);
        ReplayMechanicSnapshot Mechanic(
            float seconds,
            string sourceKey,
            string sourceName,
            ReplayMechanicShape shape,
            float x,
            float z,
            float rotation,
            float length,
            string rawEventKind,
            uint actionId,
            uint rawState) =>
            new(
                startedAt.AddSeconds(seconds), seconds, 1.0f, sourceKey, sourceName,
                shape, x, 0, z, rotation, 2, length, 4, 0,
                actionId == 47868 ? "Nothingness" : "Black Hole",
                rawEventKind, actionId, rawState, true);

        return new PullDeathSnapshot(
            startedAt.AddSeconds(30),
            "Wipe",
            1363,
            "Dancing Mad",
            30,
            [])
        {
            PullNumber = 15,
            ReplayPositions =
            [
                Player(10, "member-1", "First Holder", 0x10000001, 100, 90, 0),
                Player(10, "member-2", "Second Holder", 0x10000002, 100, 110, 1),
                Player(12, "member-1", "First Holder", 0x10000001, 100, 90, 0),
                Player(12, "member-2", "Second Holder", 0x10000002, 100, 110, 1),
            ],
            ReplayMechanics =
            [
                Mechanic(9, "object:40000011", "Black Hole", ReplayMechanicShape.Circle, 90, 90, 0, 0, "object", 19512, 0x40000011),
                Mechanic(9, "object:40000012", "Black Hole", ReplayMechanicShape.Circle, 110, 110, 0, 0, "object", 19512, 0x40000012),
                Mechanic(10, "black-hole-tether:40000011:10000001", "Black Hole -> First Holder", ReplayMechanicShape.Tether, 95, 90, MathF.PI / 2, 10, "black-hole-tether", 1, 0),
                Mechanic(10, "black-hole-tether:40000012:10000002", "Black Hole -> Second Holder", ReplayMechanicShape.Tether, 105, 110, -MathF.PI / 2, 10, "black-hole-tether", 1, 0),
                Mechanic(12, "black-hole-blast:40000011:member-1", "Black Hole -> First Holder", ReplayMechanicShape.Line, 95, 90, MathF.PI / 2, 10, "black-hole-blast", 47868, 50_000),
                Mechanic(12, "black-hole-blast:40000012:member-2", "Black Hole -> Second Holder", ReplayMechanicShape.Line, 105, 110, -MathF.PI / 2, 10, "black-hole-blast", 47868, 50_000),
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
