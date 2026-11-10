using System.Text.Json;
using System.Text.RegularExpressions;
using BetterDeaths.WtfDig;

namespace BetterDeaths.Tests;

public sealed class P4AnalyzerTests
{
    private const int Neo = 10, Chaos = 11, Kefka = 12, Helper = 13, OtherNeo = 14;
    private const uint WhiteWound = 1004887, BlackWound = 1004888, Swap = 1000454, Keep = 1005464;
    private const uint ResolvedWhiteWound = 1005541, ResolvedBlackWound = 1005542;
    private static readonly DateTime Start = new(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc);
    private static FflogsEvent Cast(uint id, double time, int source = Neo) =>
        new() { Timestamp = time * 1000, Type = "cast", SourceID = source, AbilityGameID = id };
    private static FflogsEvent Tell(double time, long value, int target = Neo, string type = "applydebuff") =>
        new() { Timestamp = time * 1000, Type = type, TargetID = target, AbilityGameID = 1002056, ExtraInfo = value };
    private static FflogsEvent Debuff(uint id, double time = 40, int target = 1, double duration = 15) =>
        new() { Timestamp = time * 1000, Type = "applydebuff", TargetID = target, AbilityGameID = id, Duration = duration * 1000 };
    private static FflogsEvent Damage(uint id, long amount, double time = 60, int target = 1, bool estimated = false,
        string type = "damage") => new() { Timestamp = time * 1000, Type = type, SourceID = Helper,
            TargetID = target, AbilityGameID = id, Amount = amount, IsEstimated = estimated };
    private static FflogsEvent Death(uint id, double time = 60.1, int target = 1) =>
        new() { Timestamp = time * 1000, Type = "death", TargetID = target, KillingAbilityGameID = id };

    private static readonly FflogsFight Fight = new() { Id = 1, StartTime = 0, EndTime = 200000, FriendlyPlayers = [1, 2] };
    private static readonly FflogsReportSummary Report = new()
    {
        Code = "p4-regression", Fights = [Fight],
        Actors = [new() { Id = 1, Name = "Player 1", Type = "Player", SubType = "Warrior" },
            new() { Id = 2, Name = "Player 2", Type = "Player", SubType = "WhiteMage" },
            new() { Id = Neo, Name = "Neo Exdeath", Type = "NPC" },
            new() { Id = Chaos, Name = "Chaos", Type = "NPC" },
            new() { Id = Kefka, Name = "Kefka", Type = "NPC" },
            new() { Id = Helper, Name = "Chaos", Type = "NPC" },
            new() { Id = OtherNeo, Name = "Neo Exdeath", Type = "NPC" }],
        Abilities = [new() { GameID = WhiteWound, Name = "White Wound" },
            new() { GameID = BlackWound, Name = "Black Wound" }, new() { GameID = Swap, Name = "Allagan Field" },
            new() { GameID = Keep, Name = "Beyond Death" }, new() { GameID = 50068, Name = "White Antilight" },
            new() { GameID = 50069, Name = "Black Antilight" }, new() { GameID = 50070, Name = "Edge of Death" },
            new() { GameID = 50071, Name = "Death Surge" }],
    };

    private static Task<P4Analysis> Analyze(params FflogsEvent[] events) =>
        new P4Analyzer(new MemorySource([Cast(49884, 1, Kefka), .. events])).AnalyzeAsync(Report, Fight, CancellationToken.None);

    private static async Task<P4WoundInfo> Wound(uint initial, uint field, params FflogsEvent[] evidence)
    {
        var analysis = await Analyze([Cast(47892, 10), Cast(47892, 25), Cast(47892, 40), Cast(50067, 60),
            Debuff(initial), Debuff(field), .. evidence]);
        return Assert.Single(analysis.Rounds[2].Wounds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedTellObservationsDoNotShiftRoundsOrDownstreamCalls(bool snapshots)
    {
        var type = snapshots ? "statussnapshot" : "applydebuff";
        var analysis = await Analyze(Cast(47892, 10), Cast(47892, 25), Cast(47892, 40), Cast(50067, 60),
            Tell(5, 1121, type: type), Tell(15, 1121, type: type), Tell(20, 1122, type: type),
            Tell(30, 1122, type: type), Tell(35, 1121, type: type), Tell(55, 1122, type: type),
            Debuff(1005544, 25, 1, 40), Debuff(1005545, 25, 2, 40), Debuff(1005546, 25, 1, 40));
        Assert.Equal([P4RealFake.Fake, P4RealFake.Real, P4RealFake.Fake], analysis.Rounds.Select(round => round.NeoExdeath));
        Assert.Equal(P4RealFake.Real, analysis.FloodOfNaught);
        var marks = analysis.Resolution!.Block1.Single(value => value.Key == "short-marks");
        Assert.Equal("spread", marks.Players.Single(value => value.ActorId == 1).Call);
        Assert.Equal("stack", marks.Players.Single(value => value.ActorId == 2).Call);
        Assert.Equal("stillness", Assert.Single(analysis.Resolution.Block1.Single(value => value.Key == "short-accel").Players).Call);
    }

    [Fact]
    public async Task MissingTellDoesNotBorrowAnotherRoundsSignalOrShiftTheFollowingRound()
    {
        var analysis = await Analyze(Cast(47892, 10), Cast(47892, 25), Cast(47892, 40),
            Tell(5, 1121), Tell(35, 1122));
        Assert.Equal(P4RealFake.Fake, analysis.Rounds[0].NeoExdeath);
        Assert.Null(analysis.Rounds[1].NeoExdeath);
        Assert.Equal(P4RealFake.Real, analysis.Rounds[2].NeoExdeath);
        Assert.Null(analysis.FloodOfNaught);
    }

    [Fact]
    public async Task EachCastAcceptsItsOwnSameValueTellWithoutMergingDistinctRounds()
    {
        var analysis = await Analyze(Cast(47892, 10), Cast(47892, 25), Cast(47892, 40),
            Tell(5, 1122), Tell(20, 1122), Tell(35, 1121));
        Assert.Equal([P4RealFake.Real, P4RealFake.Real, P4RealFake.Fake], analysis.Rounds.Select(round => round.NeoExdeath));
    }

    [Fact]
    public async Task BossAndHelperActorsKeepTheirOwnRolesAndCannotStealAnotherBossTell()
    {
        var analysis = await Analyze(Cast(47892, 10), Cast(47892, 25), Cast(47904, 15, Helper), Cast(47905, 30, Helper),
            Tell(5, 1121), Tell(8, 1120, Chaos), Tell(20, 1122), Tell(23, 1119, Chaos));
        Assert.Equal(P4RealFake.Fake, analysis.Rounds[0].NeoExdeath);
        Assert.Equal(P4RealFake.Real, analysis.Rounds[0].Chaos);
        Assert.Equal(P4RealFake.Real, analysis.Rounds[1].NeoExdeath);
        Assert.Equal(P4RealFake.Fake, analysis.Rounds[1].Chaos);
    }

    [Fact]
    public async Task AmbiguousSameRoleActorsAreNotGuessedButAnExactActorMatchStillWorks()
    {
        var analysis = await Analyze(Cast(47892, 10, Helper), Cast(47892, 25, Neo),
            Tell(5, 1121), Tell(6, 1122, OtherNeo), Tell(20, 1121), Tell(21, 1122, OtherNeo));
        Assert.Null(analysis.Rounds[0].NeoExdeath);
        Assert.Equal(P4RealFake.Fake, analysis.Rounds[1].NeoExdeath);
    }

    [Fact]
    public async Task RefreshEventsAndNonEnglishBossNamesStillUseTheEncodedTellRole()
    {
        var report = new FflogsReportSummary { Code = Report.Code, Actors = Report.Actors.Select(actor =>
            new FflogsActor { Id = actor.Id, Type = actor.Type, Name = $"Localized {actor.Id}", SubType = actor.SubType }).ToArray() };
        var events = new[] { Cast(49884, 1, Kefka), Cast(47892, 10), Cast(47892, 25),
            Tell(5, 1121), Tell(20, 1122, type: "refreshdebuff") };
        var analysis = await new P4Analyzer(new MemorySource(events)).AnalyzeAsync(report, Fight, CancellationToken.None);
        Assert.Equal([P4RealFake.Fake, P4RealFake.Real], analysis.Rounds.Select(round => round.NeoExdeath));
    }

    [Fact]
    public async Task FutureAndUnknownTellValuesAreNotConvertedIntoARealFakeCall()
    {
        var analysis = await Analyze(Cast(47892, 10), Cast(47892, 25), Tell(10.1, 1121), Tell(20, 7777));
        Assert.Null(analysis.Rounds[0].NeoExdeath);
        Assert.Null(analysis.Rounds[1].NeoExdeath);
    }

    [Theory]
    [InlineData(BlackWound, Keep, 50068, true)]
    [InlineData(WhiteWound, Keep, 50069, true)]
    [InlineData(BlackWound, Swap, 50068, false)]
    [InlineData(WhiteWound, Swap, 50069, false)]
    [InlineData(BlackWound, Keep, 50069, false)]
    [InlineData(WhiteWound, Keep, 50068, false)]
    public async Task ConfirmedAntilightColorDeterminesSideEvenWithoutAWoundReapplication(uint initial, uint field,
        uint damage, bool wrong)
    {
        var wound = await Wound(initial, field, Damage(damage, 975999), Death(damage));
        Assert.Equal(wrong, wound.WrongSide);
        Assert.Equal(!wrong, wound.Success);
        Assert.True(wound.Died);
    }

    [Fact]
    public async Task CapturedPull2324WrongSideWarriorDeathIsNotReportedCorrect()
    {
        var wound = await Wound(BlackWound, Keep, Damage(50068, 975999, 60), Death(50068));
        Assert.True(wound.WrongSide);
        Assert.False(wound.Success);
        Assert.True(wound.Died);
        Assert.False(wound.CaughtMiddle);
    }

    [Theory]
    [InlineData(999)]
    [InlineData(975999)]
    public async Task DamageSizeDoesNotOverrideTheConfirmedSide(long amount)
    {
        var wound = await Wound(BlackWound, Keep, Damage(50069, amount));
        Assert.False(wound.WrongSide);
        Assert.True(wound.Success);
    }

    [Fact]
    public async Task CollateralDeathDoesNotMakeCorrectlyResolvedPlayerFail()
    {
        var wound = await Wound(BlackWound, Swap, Damage(50068, 999), Death(50071));
        Assert.False(wound.WrongSide);
        Assert.True(wound.Success);
        Assert.True(wound.Died);
    }

    [Fact]
    public async Task EdgeOfDeathIsMiddleFailureEvenIfThePlayerDiedAndWasRaisedEarlier()
    {
        var wound = await Wound(BlackWound, Keep, Death(999, 15), Death(50070));
        Assert.True(wound.CaughtMiddle);
        Assert.False(wound.Success);
        Assert.True(wound.Died);
    }

    [Fact]
    public async Task TwoConfirmedColorsAreMiddleFailureButTrivialSplashIsNot()
    {
        var middle = await Wound(BlackWound, Keep, Damage(50068, 999), Damage(50069, 999));
        Assert.True(middle.CaughtMiddle);
        Assert.False(middle.Success);
        var splash = await Wound(BlackWound, Keep, Damage(50068, 1), Damage(50069, 999));
        Assert.False(splash.CaughtMiddle);
        Assert.True(splash.Success);
    }

    [Fact]
    public async Task OnePlayersWrongSideHitCannotChangeAnotherPlayersResult()
    {
        var analysis = await Analyze(Cast(47892, 10), Cast(47892, 25), Cast(47892, 40), Cast(50067, 60),
            Debuff(BlackWound, target: 1), Debuff(Keep, target: 1),
            Debuff(WhiteWound, target: 2), Debuff(Keep, target: 2),
            Damage(50068, 975999, target: 1), Damage(50068, 999, target: 2));
        var wounds = analysis.Rounds[2].Wounds.ToDictionary(value => value.ActorId);
        Assert.True(wounds[1].WrongSide);
        Assert.False(wounds[1].Success);
        Assert.False(wounds[2].WrongSide);
        Assert.True(wounds[2].Success);
    }

    [Theory]
    [InlineData(BlackWound, Swap)]
    [InlineData(BlackWound, Keep)]
    [InlineData(WhiteWound, Swap)]
    [InlineData(WhiteWound, Keep)]
    [InlineData(ResolvedBlackWound, Swap)]
    [InlineData(ResolvedBlackWound, Keep)]
    [InlineData(ResolvedWhiteWound, Swap)]
    [InlineData(ResolvedWhiteWound, Keep)]
    public async Task InitialAssignmentsAndAnEarlyWipeAreNotAResolvedFlood(uint initial, uint field)
    {
        var analysis = await Analyze(Cast(47892, 10), Cast(47892, 25), Cast(47892, 40),
            Debuff(initial), Debuff(field), Death(999, 48));
        var wound = Assert.Single(analysis.Rounds[2].Wounds);
        Assert.Equal(initial is WhiteWound or ResolvedWhiteWound ? P4WoundColor.White : P4WoundColor.Black, wound.Wound);
        Assert.NotNull(wound.Field);
        Assert.Null(wound.FinalWound);
        Assert.Null(wound.Success);
        Assert.False(wound.WrongSide);
        Assert.False(wound.FloodResolved);
        Assert.Equal("Not resolved", wound.ResultLabel);
    }

    [Fact]
    public async Task AForecastCastFinishCannotTurnAnEarlyWipeIntoAResolvedFlood()
    {
        var predicted = new FflogsEvent { Timestamp = 60000, Type = "cast", AbilityGameID = 50068,
            SourceID = Helper, IsEstimated = true };
        var analysis = await Analyze(Cast(47892, 40), Debuff(BlackWound), Debuff(Swap), predicted, Death(999, 48));
        var wound = Assert.Single(analysis.Rounds[0].Wounds);
        Assert.Null(wound.Success);
        Assert.False(wound.FloodResolved);
        Assert.Equal("Not resolved", wound.ResultLabel);
    }

    [Theory]
    [InlineData(BlackWound, Keep, ResolvedBlackWound, true)]
    [InlineData(BlackWound, Keep, ResolvedWhiteWound, false)]
    [InlineData(BlackWound, Swap, ResolvedWhiteWound, true)]
    [InlineData(WhiteWound, Swap, ResolvedWhiteWound, false)]
    [InlineData(ResolvedWhiteWound, Swap, BlackWound, true)]
    [InlineData(ResolvedBlackWound, Keep, BlackWound, true)]
    public async Task CapturedResolvedWoundProvesTheOutcomeWhenNoDamagePacketWasCaptured(uint initial,
        uint field, uint resolved, bool correct)
    {
        var wound = await Wound(initial, field, Debuff(resolved, 60.2));
        Assert.True(wound.FloodResolved);
        Assert.Equal(correct, wound.Success);
        Assert.Equal(correct ? "Correct" : "Wrong side", wound.ResultLabel);
        Assert.Equal(resolved is WhiteWound or ResolvedWhiteWound ? P4WoundColor.White : P4WoundColor.Black, wound.FinalWound);
    }

    [Theory]
    [InlineData(50068, false)]
    [InlineData(50069, true)]
    public async Task ARecordedFatalHitAloneStillProvesCorrectOrWrongWhenTheWipePreventsBuffUpdates(uint fatal,
        bool correct)
    {
        var wound = await Wound(BlackWound, Keep, Death(fatal));
        Assert.True(wound.FloodResolved);
        Assert.Equal(correct, wound.Success);
        Assert.Equal(correct ? "Correct" : "Wrong side", wound.ResultLabel);
        Assert.Equal(correct ? P4WoundColor.Black : P4WoundColor.White, wound.FinalWound);
    }

    [Fact]
    public async Task ConfirmedFloodWithoutThatPlayersResolutionDataIsNotLabelledNotResolvedOrCorrect()
    {
        var wound = await Wound(BlackWound, Keep);
        Assert.True(wound.FloodResolved);
        Assert.Null(wound.FinalWound);
        Assert.Null(wound.Success);
        Assert.Equal("No result captured", wound.ResultLabel);
    }

    [Fact]
    public async Task AResolvedWoundDoesNotInventAMissingInitialAssignment()
    {
        var analysis = await Analyze(Cast(47892, 40), Cast(50067, 59.9), Debuff(Keep), Debuff(ResolvedBlackWound, 60));
        var wound = Assert.Single(analysis.Rounds[0].Wounds);
        Assert.True(wound.FloodResolved);
        Assert.Null(wound.Wound);
        Assert.Equal(P4WoundColor.Black, wound.FinalWound);
        Assert.Null(wound.Success);
        Assert.Equal("No result captured", wound.ResultLabel);
    }

    [Fact]
    public async Task AResolvedHitDoesNotTreatAMissingFieldAsKeep()
    {
        var analysis = await Analyze(Cast(47892, 40), Debuff(BlackWound), Damage(50069, 999));
        var wound = Assert.Single(analysis.Rounds[0].Wounds);
        Assert.True(wound.FloodResolved);
        Assert.Null(wound.Field);
        Assert.Null(wound.Success);
        Assert.Equal("No result captured", wound.ResultLabel);
    }

    [Fact]
    public async Task AnotherPlayersResolvedWoundProvesFloodHappenedButDoesNotInventThisPlayersOutcome()
    {
        var analysis = await Analyze(Cast(47892, 40), Debuff(BlackWound, target: 1), Debuff(Keep, target: 1),
            Debuff(BlackWound, target: 2), Debuff(Keep, target: 2), Debuff(ResolvedBlackWound, 60, 1));
        var wounds = analysis.Rounds[0].Wounds.ToDictionary(value => value.ActorId);
        Assert.Equal("Correct", wounds[1].ResultLabel);
        Assert.True(wounds[2].FloodResolved);
        Assert.Null(wounds[2].Success);
        Assert.Equal("No result captured", wounds[2].ResultLabel);
    }

    [Theory]
    [InlineData("refreshdebuff")]
    [InlineData("applydebuffstack")]
    public async Task ResolvedWoundRefreshOrStackCanConfirmFloodWithoutAnyCastRecord(string type)
    {
        var resolved = new FflogsEvent { Timestamp = 60200, Type = type, TargetID = 1, AbilityGameID = ResolvedBlackWound };
        var analysis = await Analyze(Cast(47892, 40), Debuff(BlackWound), Debuff(Keep), resolved);
        var wound = Assert.Single(analysis.Rounds[0].Wounds);
        Assert.True(wound.FloodResolved);
        Assert.True(wound.Success);
        Assert.Equal("Correct", wound.ResultLabel);
    }

    [Fact]
    public async Task RefreshingTheInitialWoundDoesNotInventAResolution()
    {
        var refresh = new FflogsEvent { Timestamp = 45000, Type = "refreshdebuff", TargetID = 1, AbilityGameID = BlackWound };
        var analysis = await Analyze(Cast(47892, 40), Debuff(BlackWound), Debuff(Keep), refresh);
        var wound = Assert.Single(analysis.Rounds[0].Wounds);
        Assert.False(wound.FloodResolved);
        Assert.Null(wound.FinalWound);
        Assert.Null(wound.Success);
        Assert.Equal("Not resolved", wound.ResultLabel);
    }

    [Fact]
    public async Task FatalAntilightCanConfirmAResolvedWipeWithoutTheCastOrDamagePacket()
    {
        var analysis = await Analyze(Cast(47892, 40), Debuff(BlackWound), Debuff(Keep), Death(50068));
        var wound = Assert.Single(analysis.Rounds[0].Wounds);
        Assert.True(wound.FloodResolved);
        Assert.False(wound.Success);
        Assert.Equal("Wrong side", wound.ResultLabel);
    }

    [Theory]
    [InlineData(WhiteWound, Keep, true)]
    [InlineData(BlackWound, Keep, true)]
    [InlineData(ResolvedWhiteWound, Keep, true)]
    [InlineData(ResolvedBlackWound, Keep, true)]
    [InlineData(BlackWound, Swap, false)]
    public async Task UnchangedWoundObservedThroughFloodIsStillValidResolutionEvidence(uint initial, uint field, bool correct)
    {
        var removal = new FflogsEvent { Timestamp = 60200, Type = "removedebuff", TargetID = 1, AbilityGameID = initial };
        var analysis = await Analyze(Cast(47892, 40), Cast(50067, 60), Debuff(initial, duration: 0), Debuff(field), removal);
        var wound = Assert.Single(analysis.Rounds[0].Wounds);
        Assert.True(wound.FloodResolved);
        Assert.Equal(correct, wound.Success);
        Assert.Equal(correct ? "Correct" : "Wrong side", wound.ResultLabel);
        Assert.Equal(wound.Wound, wound.FinalWound);
    }

    [Fact]
    public async Task DeathClearingAnInitialWoundIsNotProofOfACorrectSide()
    {
        var removal = new FflogsEvent { Timestamp = 60200, Type = "removedebuff", TargetID = 1, AbilityGameID = BlackWound };
        var analysis = await Analyze(Cast(47892, 40), Cast(50067, 60), Debuff(BlackWound, duration: 0),
            Debuff(Keep), Death(50071), removal);
        var wound = Assert.Single(analysis.Rounds[0].Wounds);
        Assert.True(wound.FloodResolved);
        Assert.Null(wound.Success);
        Assert.Null(wound.FinalWound);
        Assert.Equal("No result captured", wound.ResultLabel);
    }

    [Theory]
    [InlineData(5, 60.2)]
    [InlineData(0, 45)]
    public async Task ExpiredOrPreviouslyRemovedWoundDoesNotProveAPostFloodColor(double duration, double removedAt)
    {
        var removal = new FflogsEvent { Timestamp = removedAt * 1000, Type = "removedebuff", TargetID = 1, AbilityGameID = BlackWound };
        var analysis = await Analyze(Cast(47892, 40), Cast(50067, 60), Debuff(BlackWound, duration: duration), Debuff(Keep), removal);
        var wound = Assert.Single(analysis.Rounds[0].Wounds);
        Assert.True(wound.FloodResolved);
        Assert.Null(wound.Success);
        Assert.Equal("No result captured", wound.ResultLabel);
    }

    [Fact]
    public async Task RecordedLaterPhaseConfirmsFloodHappenedWithoutInventingAPlayerOutcome()
    {
        var analysis = await Analyze(Cast(47892, 40), Debuff(BlackWound), Debuff(Keep), Cast(47780, 65, Kefka));
        var wound = Assert.Single(analysis.Rounds[0].Wounds);
        Assert.True(wound.FloodResolved);
        Assert.Null(wound.Success);
        Assert.Equal("No result captured", wound.ResultLabel);
    }

    [Fact]
    public async Task CapturedPull2330UnchangedSecondVariantWoundRemainsAResolvedCorrectResult()
    {
        const uint neo = 0x40000001;
        const float initialAt = 779.42285f, floodAt = 790.10223f, removedAt = 790.9034f;
        var pull = new PullDeathSnapshot(Start.AddSeconds(800), "Wipe", 1363, "Dancing Mad", 800, [])
        {
            ReplayAnalyzerEvents = new[] { (49884u, 734.1613f), (47892u, 777.4313f), (50067u, floodAt) }
                .Select(value => new ReplayAnalyzerEventSnapshot(Start.AddSeconds(value.Item2), value.Item2, "cast",
                    neo, "Neo Exdeath", value.Item1, "", false, 0, 0, 0, 0)).ToArray(),
            ReplayDebuffs = [new(Start.AddSeconds(initialAt), initialAt, "member", "Player 1", 0, 21, "WAR",
                new(5541, "White Wound", 0, neo, 0, 0), true),
                new(Start.AddSeconds(initialAt), initialAt, "member", "Player 1", 0, 21, "WAR",
                new(1382, "Beyond Death", 0, neo, 0, 15), true),
                new(Start.AddSeconds(removedAt), removedAt, "member", "Player 1", 0, 21, "WAR",
                new(5541, "White Wound", 0, neo, 0, 0), false)],
        };
        var source = LocalPullEventSource.Create(pull);
        var analysis = await new P4Analyzer(source).AnalyzeAsync(source.Report, source.Fight, CancellationToken.None);
        var wound = Assert.Single(analysis.Rounds[0].Wounds);
        Assert.Equal(P4WoundColor.White, wound.Wound);
        Assert.Equal(P4WoundColor.White, wound.FinalWound);
        Assert.True(wound.FloodResolved);
        Assert.Equal("Correct", wound.ResultLabel);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task LocalAdapterDistinguishesCancelledForecastFromObservedCompletion(bool mechanicCompleted, bool packetCompleted)
    {
        const uint neo = 0x40000001;
        var predicted = new ReplayMechanicSnapshot(Start.AddSeconds(60), 60, 0,
            $"dmu-p4-white-antilight-predicted:cast:{neo:X8}:50068:main", "Neo Exdeath", ReplayMechanicShape.Circle,
            100, 0, 100, 0, 40, 0, 0, 0, "White Antilight", "dmu-p4-white-antilight-predicted", 50068, neo, true);
        var casts = new[] { (49884u, 1f), (47892u, 40f) }.Select(value =>
            new ReplayAnalyzerEventSnapshot(Start.AddSeconds(value.Item2), value.Item2, "cast", neo,
                "Neo Exdeath", value.Item1, "", false, 0, 0, 0, 0)).ToList();
        if (packetCompleted)
            casts.Add(new(Start.AddSeconds(60), 60, "cast", neo, "Neo Exdeath", 50068, "White Antilight", false, 0, 0, 0, 0));
        var pull = new PullDeathSnapshot(Start.AddSeconds(61), "Wipe", 1363, "Dancing Mad", 61, [])
        {
            ReplayDebuffs = [new(Start.AddSeconds(40), 40, "member", "Player 1", 0, 21, "WAR",
                new(4888, "Black Wound", 0, neo, 0, 15), true),
                new(Start.AddSeconds(40), 40, "member", "Player 1", 0, 21, "WAR",
                new(5464, "Beyond Death", 0, neo, 0, 15), true)],
            ReplayAnalyzerEvents = casts,
            ReplayMechanics = mechanicCompleted ? [predicted, predicted with
                { SourceKey = $"dmu-p4-white-antilight:{neo:X8}:100", RawEventKind = "dmu-p4-white-antilight" }] : [predicted],
        };
        var source = LocalPullEventSource.Create(pull);
        var events = await source.FetchAllEventsAsync(new(source.Report.Code, source.Fight.Id, 0, source.Fight.EndTime,
            AbilityId: 50068), CancellationToken.None);
        Assert.Equal(mechanicCompleted || packetCompleted, events.Any(value => value.Type == "cast" && !value.IsEstimated));
        var analysis = await new P4Analyzer(source).AnalyzeAsync(source.Report, source.Fight, CancellationToken.None);
        var wound = Assert.Single(analysis.Rounds[0].Wounds);
        Assert.Equal(mechanicCompleted || packetCompleted, wound.FloodResolved);
        Assert.Null(wound.Success);
        Assert.Equal(mechanicCompleted || packetCompleted ? "No result captured" : "Not resolved", wound.ResultLabel);
    }

    [Fact]
    public async Task EstimatedAndPreAppliedDamageCannotAccuseAPlayerOfTheWrongSide()
    {
        var estimated = await Wound(BlackWound, Keep, Damage(50068, 975999, estimated: true));
        var calculated = await Wound(BlackWound, Keep, Damage(50068, 975999, type: "calculateddamage"));
        Assert.False(estimated.WrongSide);
        Assert.False(calculated.WrongSide);
        Assert.Null(estimated.Antilight);
        Assert.Null(calculated.Antilight);
    }

    [Fact]
    public async Task LocalGeometryDamageKeepsItsEstimatedProvenanceThroughTheActualAdapter()
    {
        const uint player = 0x10000001, neo = 0x40000001;
        ReplayPositionSnapshot Position(float seconds, uint hp) => new(Start.AddSeconds(seconds), seconds,
            "player:member", "Player 1", ReplayActorKind.Player, 0, player, 21, "WAR", 100, 0, 100, 0,
            hp, 0, 100000, false, true);
        var pull = new PullDeathSnapshot(Start.AddSeconds(100), "Wipe", 1363, "Dancing Mad", 100, [])
        {
            ReplayPositions = [Position(59.8f, 100000), Position(60.1f, 1)],
            ReplayDebuffs = [new(Start.AddSeconds(40), 40, "member", "Player 1", 0, 21, "WAR",
                new(4888, "Black Wound", 0, neo, 0, 15), true),
                new(Start.AddSeconds(40), 40, "member", "Player 1", 0, 21, "WAR",
                new(5464, "Beyond Death", 0, neo, 0, 15), true)],
            ReplayAnalyzerEvents = new[] { (49884u, 1f), (47892u, 10f), (47892u, 25f), (47892u, 40f) }
                .Select(value => new ReplayAnalyzerEventSnapshot(Start.AddSeconds(value.Item2), value.Item2, "cast",
                    neo, "Neo Exdeath", value.Item1, "", false, 0, 0, 0, 0)).ToArray(),
            ReplayMechanics = [new(Start.AddSeconds(60), 60, 1, $"event:{neo:X8}:50068", "Neo Exdeath",
                ReplayMechanicShape.Circle, 100, 0, 100, 0, 40, 0, 0, 0, "White Antilight", "action-effect", 50068, neo, true)],
        };
        var source = LocalPullEventSource.Create(pull);
        var damage = await source.FetchAllEventsAsync(new(source.Report.Code, source.Fight.Id, 0, 100000,
            FflogsEventDataType.DamageTaken, FflogsHostilityType.Friendlies, AbilityId: 50068), CancellationToken.None);
        var estimated = Assert.Single(damage, value => value.Type == "damage" && value.TargetID is not null);
        Assert.Equal(99999, estimated.Amount);
        Assert.True(estimated.IsEstimated);
        var analysis = await new P4Analyzer(source).AnalyzeAsync(source.Report, source.Fight, CancellationToken.None);
        var wound = Assert.Single(analysis.Rounds[2].Wounds);
        Assert.False(wound.WrongSide);
        Assert.Null(wound.Antilight);
    }

    public static IEnumerable<object[]> CapturedSignals()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var fixtures = JsonSerializer.Deserialize<CapturedSignalsFixture[]>(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "P4CapturedSignals.json")), options)!;
        foreach (var fixture in fixtures)
            yield return [fixture];
    }

    public sealed record TellObservation(double Time, uint Param);
    public sealed record CapturedSignalsFixture(int Pull, double Anchor, double End, double[] GrandCrosses,
        TellObservation[] Tells, double Flood, string[] Expected, string ExpectedFlood);

    [Theory]
    [MemberData(nameof(CapturedSignals))]
    public async Task RecordedTellStreamsArePairedByCastTimeIncludingLegacyDuplicateSnapshots(CapturedSignalsFixture fixture)
    {
        const uint neo = 0x40000001, kefka = 0x40000002, helper = 0x40000003;
        ReplayAnalyzerEventSnapshot CastEvent(uint id, double seconds, uint source, string name) =>
            new(Start.AddSeconds(seconds), (float)seconds, "cast", source, name, id, "", false, 0, 0, 0, 0);
        var pull = new PullDeathSnapshot(Start.AddSeconds(fixture.End), "Wipe", 1363, "Dancing Mad", (float)fixture.End, [])
        {
            PullNumber = fixture.Pull,
            ReplayMarkers = fixture.Tells.Select(value => new ReplayMarkerSnapshot(Start.AddSeconds(value.Time),
                (float)value.Time, $"enemy:{neo:X8}", "Neo Exdeath", ReplayActorKind.Enemy, 2000, neo, 0, "", 2056, value.Param)).ToArray(),
            ReplayAnalyzerEvents = [CastEvent(49884, fixture.Anchor, kefka, "Kefka"),
                .. fixture.GrandCrosses.Select(time => CastEvent(47892, time, neo, "Neo Exdeath")),
                CastEvent(50068, fixture.Flood, helper, "Neo Exdeath")],
        };
        var source = LocalPullEventSource.Create(pull);
        var observations = await source.FetchAllEventsAsync(new(source.Report.Code, source.Fight.Id, 0, fixture.End * 1000,
            FilterExpression: "ability.id in (1002056)"), CancellationToken.None);
        Assert.Equal(fixture.Tells.Length, observations.Count);
        Assert.All(observations, observation => Assert.Equal("statussnapshot", observation.Type));
        var analysis = await new P4Analyzer(source).AnalyzeAsync(source.Report, source.Fight, CancellationToken.None);
        Assert.Equal(fixture.Expected.Select(value => (P4RealFake?)Enum.Parse<P4RealFake>(value)), analysis.Rounds.Select(round => round.NeoExdeath));
        Assert.Equal(Enum.Parse<P4RealFake>(fixture.ExpectedFlood), analysis.FloodOfNaught);
    }

    private sealed class MemorySource(IReadOnlyList<FflogsEvent> events) : IWtfDigEventSource
    {
        public Task<IReadOnlyList<FflogsEvent>> FetchAllEventsAsync(FflogsEventQuery query, CancellationToken cancellationToken)
        {
            var ids = Regex.Matches(query.FilterExpression ?? "", @"\b\d+\b").Select(value => uint.Parse(value.Value)).ToHashSet();
            var result = events.Where(value => value.Timestamp >= query.StartTime && value.Timestamp <= query.EndTime)
                .Where(value => query.AbilityId is null || value.AbilityGameID == query.AbilityId)
                .Where(value => query.DataType != FflogsEventDataType.Casts || value.Type == "cast")
                .Where(value => query.FilterExpression is null || ids.Contains(value.AbilityGameID ?? 0) ||
                    value.Type == "headmarker" && query.FilterExpression.Contains("type='headmarker'") ||
                    value.Type == "death" && query.FilterExpression.Contains("type='death'"))
                .OrderBy(value => value.Timestamp).ToArray();
            return Task.FromResult<IReadOnlyList<FflogsEvent>>(result);
        }
    }
}
