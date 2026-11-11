using System.Reflection;
using System.Text.Json;

namespace BetterDeaths.Tests;

public sealed class SunSignWhatIfTests
{
    private static readonly DateTime Start = new(2026, 11, 10, 21, 0, 0, DateTimeKind.Utc);
    private const uint Ast = 0x10000002, Victim = 0x10000001, Enemy = 0x40000001;
    private const uint SunSign = 37031, NeutralSect = 16559, Suntouched = 3895, SunSignBuff = 3896;
    private static readonly Dictionary<uint, ActionData> Actions = JsonSerializer.Deserialize<ActionData[]>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "PartyCooldownActions.json")))!
        .ToDictionary(action => action.Id);

    private sealed record ActionData(uint Id, string Name, uint Icon, byte Level, byte Group, float Recast);
    private static DateTime At(double seconds) => Start.AddSeconds(seconds);
    private static StatusSnapshot Grant(float duration = 30) => new(Suntouched, "Suntouched", 213266, Ast, 0, duration);
    private static StatusSnapshot Mit(float duration = 15) => new(SunSignBuff, "Sun Sign", 213267, Ast, 0, duration);
    private static StatusSetSnapshot Gain(double time = 0) => new(At(time), [Grant()]) { IsAuthoritative = true };

    private static PossibleMitigationSnapshot Candidate(params StatusSetSnapshot[] history)
    {
        var action = Actions[SunSign];
        var parent = Actions[NeutralSect];
        return new("ast:sun-sign", "ast", "Astrologian", 1, 33, "AST", action.Id, action.Name, action.Icon,
            PossibleMitigationScope.Party, action.Recast, null, null, "Ready", [Mit()])
        {
            CooldownGroup = action.Group,
            EffectStatusIds = [SunSignBuff],
            Prerequisite = new(parent.Id, parent.Group, parent.Recast, 1, Suntouched, history),
        };
    }

    private static MitigationUseEvidence Use(uint actionId, double time, uint sequence, string member = "ast")
    {
        var action = Actions[actionId];
        return new(sequence, actionId == SunSign ? $"{member}:sun-sign" : $"{member}:cooldown-group:{action.Group}",
            member, Ast, sequence, At(time), (float)time, actionId, action.Name, action.Icon, [Victim])
        { CooldownGroup = action.Group, CooldownSeconds = action.Recast };
    }

    private static PartyDeathRecord Death(double time, params StatusSnapshot[] statuses)
    {
        var hit = new CombatEventRecord(At(time), (float)time, "victim", "Victim", 0, Enemy, "Boss", 500, "Hit", 0,
            DeathEventKind.Damage, 100000, 80000, 0, 100000, DamageType.Magic, false, false, false, false, "", statuses, [])
        { HasPlayerStatusSnapshot = true, HasSourceStatusSnapshot = true };
        return new(At(time + .2), (float)time + .2f, "victim", "Victim", 0, 21, "WAR", 0, 0, 100000,
            hit, [hit], [], [])
        { EntityId = Victim };
    }

    private static PossibleMitigationSnapshot? Resolve(MitigationUseTracker tracker, double time = 10,
        PossibleMitigationSnapshot? candidate = null, PartyDeathRecord? death = null) =>
        PossibleMitigationTimingPolicy.Resolve(candidate ?? Candidate(Gain()), death ?? Death(time), tracker, At(time + .5));

    [Fact]
    public void ProductionDefinitionUsesSunSignIdentityAndNeutralSectOnlyAsAPrerequisite()
    {
        var definitions = (Array)typeof(Plugin).GetField("PossibleMitigationDefinitions", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var definition = definitions.Cast<object>().Single(value =>
            (uint)value.GetType().GetProperty("ActionId")!.GetValue(value)! == SunSign);
        var type = definition.GetType();
        Assert.Equal("Sun Sign", type.GetProperty("ActionName")!.GetValue(definition));
        Assert.Equal(PossibleMitigationScope.Party, type.GetProperty("Scope")!.GetValue(definition));
        Assert.Equal(new uint[] { 33 }, (IReadOnlyList<uint>)type.GetProperty("JobIds")!.GetValue(definition)!);
        Assert.Equal(NeutralSect, type.GetProperty("GrantedByActionId")!.GetValue(definition));
        Assert.Equal(Suntouched, type.GetProperty("ReadyStatusId")!.GetValue(definition));
        Assert.Equal(15f, type.GetProperty("DurationSeconds")!.GetValue(definition));
        Assert.Equal(100, Actions[SunSign].Level);
        Assert.Equal([SunSignBuff], Plugin.GetPossibleMitigationEffectStatusIds("sun-sign"));
        Assert.True(Plugin.IsPossibleMitigationEffectStatusId(SunSignBuff));
        Assert.True(Plugin.IsPossibleMitigationReadyStatusId(Suntouched));
        Assert.False(Plugin.IsPossibleMitigationReadyStatusId(SunSignBuff));
        Assert.False(Plugin.IsDefensiveStatus(Grant()));
    }

    [Fact]
    public void ReadyNeutralSectAllowsTheEarlierTwoButtonSimulationWithoutRenamingSunSign()
    {
        var option = Assert.IsType<PossibleMitigationSnapshot>(Resolve(new(), candidate: Candidate()));
        Assert.Equal(SunSign, option.ActionId);
        Assert.Equal("Sun Sign", option.ActionName);
        Assert.Equal(Actions[SunSign].Icon, option.ActionIconId);
        Assert.False(option.WasUsedTooLate);
        Assert.Equal("Sun Sign (AST)", MitigationReviewPresentation.ChatOption(option));
        Assert.Equal(90000UL, WhatIfMitigationRules.DamageWithOptions([Death(10).LikelyCause!], [option]));
    }

    [Fact]
    public void AnUnusedSuntouchedGrantKeepsSunSignAvailableWhileNeutralSectIsOnCooldown()
    {
        var tracker = new MitigationUseTracker();
        tracker.ObserveUse(Use(NeutralSect, 0, 1));
        Assert.NotNull(Resolve(tracker));
        Assert.Null(Resolve(tracker, candidate: Candidate()));
    }

    [Theory]
    [InlineData(29.999, true)]
    [InlineData(30, false)]
    [InlineData(60, false)]
    [InlineData(119.999, false)]
    [InlineData(120, true)]
    public void GrantExpiryAndParentRechargeUseTheirOwnExactBoundaries(double time, bool ready)
    {
        var tracker = new MitigationUseTracker();
        tracker.ObserveUse(Use(NeutralSect, 0, 1));
        Assert.Equal(ready, Resolve(tracker, time) is not null);
    }

    [Fact]
    public void SpendingSunSignDoesNotMakeItReadyAgainAfterItsOneSecondButtonRecast()
    {
        var tracker = new MitigationUseTracker();
        tracker.ObserveUse(Use(NeutralSect, 0, 1));
        tracker.ObserveUse(Use(SunSign, 2, 2));
        var stale = new StatusSetSnapshot(At(5), [Grant(25)]) { IsComplete = true };
        Assert.Null(Resolve(tracker, candidate: Candidate(Gain(), stale)));
        Assert.Null(Resolve(tracker, 119.999));
        Assert.NotNull(Resolve(tracker, 120));
    }

    [Fact]
    public void RemovedGrantCannotBeRestoredByAStaleClientSnapshot()
    {
        var tracker = new MitigationUseTracker();
        tracker.ObserveUse(Use(NeutralSect, 0, 1));
        var removed = StatusTimeline.SlotChange(At(5), Grant(25), null);
        var stale = new StatusSetSnapshot(At(6), [Grant(24)]) { IsComplete = true };
        Assert.Null(Resolve(tracker, candidate: Candidate(Gain(), removed, stale)));
    }

    [Fact]
    public void AlreadyActiveSunSignIsNotOfferedForDoubleMitigation()
    {
        Assert.Null(Resolve(new(), death: Death(10, Mit())));
    }

    [Theory]
    [InlineData(9.9)]
    [InlineData(10.1)]
    public void ConfirmedLateUseRetainsSunSignNameIconAndShortChatNote(double castAt)
    {
        var tracker = new MitigationUseTracker();
        tracker.ObserveUse(Use(NeutralSect, 0, 1));
        tracker.ObserveUse(Use(SunSign, castAt, 2));
        tracker.ObserveApplication(new(3, 2, At(10.2), Victim, "ast", Mit()));
        var consumed = StatusTimeline.SlotChange(At(castAt + .01), Grant(), null);
        var option = Assert.IsType<PossibleMitigationSnapshot>(Resolve(tracker, candidate: Candidate(Gain(), consumed)));
        Assert.True(option.WasUsedTooLate);
        Assert.Equal("Sun Sign", option.ActionName);
        Assert.Equal(SunSign, option.ActionId);
        Assert.Equal(Actions[SunSign].Icon, option.ActionIconId);
        Assert.Equal("Sun Sign (AST; used late)", MitigationReviewPresentation.ChatOption(option));
    }

    [Fact]
    public void EarlierSpentSunSignCannotBeReopenedByALaterUseOrAnUnrelatedApplication()
    {
        var tracker = new MitigationUseTracker();
        tracker.ObserveUse(Use(NeutralSect, 0, 1));
        tracker.ObserveUse(Use(SunSign, 2, 2));
        tracker.ObserveUse(Use(SunSign, 10.1, 3));
        tracker.ObserveApplication(new(4, 3, At(10.2), Victim, "other-ast", Mit()));
        Assert.Null(Resolve(tracker));
    }

    [Fact]
    public void AnotherAstrologiansCooldownDoesNotSpendThisAstrologiansGrant()
    {
        var tracker = new MitigationUseTracker();
        tracker.ObserveUse(Use(NeutralSect, 0, 1, "other-ast"));
        tracker.ObserveUse(Use(SunSign, 2, 2, "other-ast"));
        Assert.NotNull(Resolve(tracker, candidate: Candidate()));
    }

    [Fact]
    public void ANewNeutralSectCycleAllowsOneNewSunSignWithoutRevivingTheOldGrant()
    {
        var tracker = new MitigationUseTracker();
        tracker.ObserveUse(Use(NeutralSect, 0, 1));
        tracker.ObserveUse(Use(SunSign, 2, 2));
        tracker.ObserveUse(Use(NeutralSect, 120, 3));
        Assert.NotNull(Resolve(tracker, 125, Candidate(Gain(), Gain(120))));
        tracker.ObserveUse(Use(SunSign, 126, 4));
        Assert.Null(Resolve(tracker, 130, Candidate(Gain(), Gain(120))));
    }

    [Fact]
    public void SavedWhatIfKeepsTheActualAbilityAndDecisionWithoutStoringWorkingHistory()
    {
        var option = Assert.IsType<PossibleMitigationSnapshot>(Resolve(new()));
        var json = JsonSerializer.Serialize(option);
        Assert.DoesNotContain("Prerequisite", json);
        var copy = JsonSerializer.Deserialize<PossibleMitigationSnapshot>(json)!;
        Assert.Equal(SunSign, copy.ActionId);
        Assert.Equal("Sun Sign", copy.ActionName);
        Assert.Equal(90000UL, WhatIfMitigationRules.DamageWithOptions([Death(10).LikelyCause!], [copy]));
    }

    [Fact]
    public void ReceivedSunSignSurvivesSavedReplayAndEndsForEachRecipientIndependently()
    {
        var provider = new StatusSourceSnapshot(Ast, "ast", "Astrologian", 1, 33, "AST");
        var first = new PlayerStatusSnapshot(At(1), 1, "victim", "Victim", 0, 21, "WAR", Victim, [Mit()])
        { IsAuthoritative = true, Sources = [provider] };
        var second = first with { MemberKey = "other", MemberName = "Other Player", EntityId = 0x10000003, PartyIndex = 2 };
        var removed = first with { SeenAtUtc = At(5), PullElapsedSeconds = 5, Statuses = [], RemovedStatuses = [Mit(11)] };
        var caster = first with
        {
            MemberKey = "ast",
            MemberName = "Astrologian",
            EntityId = Ast,
            ClassJobId = 33,
            ClassJobName = "AST",
            PartyIndex = 1,
            Statuses = [Grant(29)]
        };
        var pull = new PullDeathSnapshot(At(20), "Test", 1, "Duty", 20, [])
        { ReplayStatusHistoryCaptured = true, ReplayStatusHistory = [first, second, removed, caster] };
        var copy = JsonSerializer.Deserialize<PullDeathSnapshot>(JsonSerializer.Serialize(pull))!;
        var timeline = ReplayMitigationTimeline.For(copy);
        var both = Assert.Single(timeline.At(At(4), 4));
        Assert.Equal("Sun Sign", both.ActionName);
        Assert.Equal("Astrologian", both.MemberName);
        Assert.Equal(["Victim", "Other Player"], both.TargetMemberNames);
        Assert.Equal(10, Assert.Single(Plugin.GetMitigationDisplayInfo(Assert.Single(both.Statuses)).MitigationPercents).Percent);
        var remaining = Assert.Single(timeline.At(At(6), 6));
        Assert.Equal(["Other Player"], remaining.TargetMemberNames);
        Assert.Equal(10, remaining.DurationSeconds);
        Assert.Empty(timeline.At(At(16), 16));
    }
}
