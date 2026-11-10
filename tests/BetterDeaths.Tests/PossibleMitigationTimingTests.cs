using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;

namespace BetterDeaths.Tests;

public sealed class PossibleMitigationTimingTests
{
    private static readonly DateTime Start = new(2026, 11, 10, 16, 0, 0, DateTimeKind.Utc);
    private const uint Player = 0x10000001;
    private const uint Boss = 0x40000001;
    private static DateTime At(double seconds) => Start.AddSeconds(seconds);
    private static StatusSnapshot Rampart => new(1191, "Rampart", 1, Player, 0, 20);
    private static PossibleMitigationSnapshot Candidate(PossibleMitigationScope scope = PossibleMitigationScope.Personal) =>
        new("player:rampart", "player", "Player", 0, 19, "PLD", 7531, "Rampart", 1,
            scope, 90, null, null, "Ready", [Rampart])
        { EffectStatusIds = [1191] };
    private static CombatEventRecord Hit(double seconds = 10, uint source = Boss) =>
        new(At(seconds), (float)seconds, "player", "Player", 0, source, "Boss", 500, "Fatal hit", 0,
            DeathEventKind.Damage, 100000, 80000, 0, 100000, DamageType.Magic, false, false, false, false, "", [], [])
        { HasPlayerStatusSnapshot = true, HasSourceStatusSnapshot = true, HpSource = CombatEventHpSource.DirectCombatEventSnapshot };
    private static PartyDeathRecord Death(CombatEventRecord? hit = null)
    {
        hit ??= Hit();
        return new(At(10.3), 10.3f, "player", "Player", 0, 19, "PLD", 0, 0, 100000,
            hit, [hit], [], [])
        { EntityId = Player };
    }
    private static MitigationUseEvidence Use(double time, uint sequence = 1, uint target = Player, long capture = 1) =>
        new(capture, "player:rampart", "player", Player, sequence, At(time), (float)time, 7531, "Rampart", 1, [target]);
    private static MitigationApplicationEvidence Application(double time, uint sequence = 1, uint target = Player,
        string provider = "player", long capture = 1) => new(capture, sequence, At(time), target, provider, Rampart);
    private static PossibleMitigationSnapshot? Resolve(MitigationUseTracker tracker, PossibleMitigationSnapshot? candidate = null,
        PartyDeathRecord? death = null) => PossibleMitigationTimingPolicy.Resolve(candidate ?? Candidate(), death ?? Death(), tracker, At(10.8));

    [Fact]
    public void UnusedAbilityRemainsReadyWithoutInventingLateUse()
    {
        var option = Assert.IsType<PossibleMitigationSnapshot>(Resolve(new()));
        Assert.False(option.WasUsedTooLate);
        Assert.Null(option.LastUsedAtUtc);
    }

    [Theory]
    [InlineData(9.9, 10.1)]
    [InlineData(10.1, 10.2)]
    public void UseBeforeDelayedApplicationAndUseAfterHitShareOneSelectableLateState(double useTime, double applied)
    {
        var tracker = new MitigationUseTracker();
        tracker.ObserveUse(Use(useTime));
        tracker.ObserveApplication(Application(applied));
        var option = Assert.IsType<PossibleMitigationSnapshot>(Resolve(tracker));
        Assert.True(option.WasUsedTooLate);
        Assert.Equal(At(useTime), option.LastUsedAtUtc);
        Assert.Equal("Used too late", option.Availability);
        Assert.Equal(80000UL, WhatIfMitigationRules.DamageWithOptions([Hit()], [option]));
    }

    [Fact]
    public void ConfirmedActionAfterHitCanProveLateUseWithoutAStatusPacket()
    {
        var tracker = new MitigationUseTracker();
        tracker.ObserveUse(Use(10.1));
        Assert.True(Resolve(tracker)!.WasUsedTooLate);
    }

    [Fact]
    public void MissingBuffAloneDoesNotRestoreSpentAbility()
    {
        var tracker = new MitigationUseTracker();
        tracker.ObserveUse(Use(9.9));
        Assert.Null(Resolve(tracker));
    }

    [Theory]
    [InlineData(1, 1.1)]
    [InlineData(9, 9.1)]
    public void ExpiredRemovedOrConsumedEffectsAreNotMislabelledAsLate(double use, double applied)
    {
        var tracker = new MitigationUseTracker();
        tracker.ObserveUse(Use(use));
        tracker.ObserveApplication(Application(applied));
        Assert.Null(Resolve(tracker));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(9.99)]
    public void ApplicationAlreadyRecordedAtOrBeforeHitIsNotLate(double time)
    {
        var tracker = new MitigationUseTracker();
        tracker.ObserveUse(Use(9.9));
        tracker.ObserveApplication(Application(time));
        Assert.Null(Resolve(tracker));
    }

    [Fact]
    public void ActiveBuffCannotBeSelectedOrCountedAgain()
    {
        var tracker = new MitigationUseTracker();
        tracker.ObserveUse(Use(10.1));
        var hit = Hit() with { Statuses = [Rampart] };
        Assert.Null(Resolve(tracker, death: Death(hit)));
    }

    [Fact]
    public void ExistingStatusOnAnyHitOfAnAtomicFatalSequencePreventsDoubleCounting()
    {
        var tracker = new MitigationUseTracker();
        tracker.ObserveUse(Use(10.1));
        var first = Hit();
        var second = Hit(10.05) with { Statuses = [Rampart] };
        var death = Death(second) with
        {
            RecentEvents = [first, second],
            FatalSequence = new(At(9.9), At(10.8), null, [first, second], [])
        };
        Assert.Null(Resolve(tracker, death: death));
    }

    [Theory]
    [InlineData(2, 0x10000001, "player")]
    [InlineData(0, 0x10000001, "player")]
    [InlineData(1, 0x10000002, "player")]
    [InlineData(1, 0x10000001, "other")]
    public void DelayedApplicationMustMatchSequenceProviderAndRecipient(uint sequence, uint target, string provider)
    {
        var tracker = new MitigationUseTracker();
        tracker.ObserveUse(Use(9.9));
        tracker.ObserveApplication(Application(10.1, sequence, target, provider));
        Assert.Null(Resolve(tracker));
    }

    [Fact]
    public void UnrelatedStatusFromSameActionCannotProveLateMitigation()
    {
        var tracker = new MitigationUseTracker();
        tracker.ObserveUse(Use(9.9));
        tracker.ObserveApplication(Application(10.1) with { Status = Rampart with { Id = 1892, Name = "Neutral Sect" } });
        Assert.Null(Resolve(tracker));
    }

    [Fact]
    public void LaterAreaEntryIsNotProofThatTheOriginalAbilityWasUsedLate()
    {
        var tracker = new MitigationUseTracker();
        tracker.ObserveUse(Use(9, target: Boss));
        tracker.ObserveApplication(Application(10.1));
        Assert.Null(Resolve(tracker, Candidate(PossibleMitigationScope.Party)));
    }

    [Fact]
    public void UnknownFatalSnapshotDoesNotTurnUncertainApplicationIntoALateWarning()
    {
        var tracker = new MitigationUseTracker();
        tracker.ObserveUse(Use(9.9));
        tracker.ObserveApplication(Application(10.1));
        var hit = Hit() with { HasPlayerStatusSnapshot = false, HpSource = CombatEventHpSource.NoPreHitSample };
        Assert.Null(Resolve(tracker, death: Death(hit)));
    }

    [Fact]
    public void LateEvidenceAfterDeathCanUpdateTheSameFrozenDeath()
    {
        var tracker = new MitigationUseTracker();
        var death = Death();
        tracker.ObserveUse(Use(9.9));
        Assert.Null(Resolve(tracker, death: death));
        tracker.ObserveApplication(Application(10.4));
        Assert.True(Resolve(tracker, death: death)!.WasUsedTooLate);
        Assert.Empty(death.StatusesAtDeath);
        Assert.Equal(100000u, death.LikelyCause!.Amount);
    }

    [Fact]
    public void ApplicationOutsideExistingFatalEvidenceWindowDoesNotReopenSpentAbility()
    {
        var tracker = new MitigationUseTracker();
        tracker.ObserveUse(Use(9.9));
        tracker.ObserveApplication(Application(10.81));
        Assert.Null(Resolve(tracker));
    }

    [Fact]
    public void DuplicateOrLaterRefreshCannotHideOriginalTimelyApplication()
    {
        var tracker = new MitigationUseTracker();
        tracker.ObserveUse(Use(9.9));
        tracker.ObserveApplication(Application(9.95));
        tracker.ObserveApplication(Application(10.1, capture: 2));
        Assert.Null(Resolve(tracker));
    }

    [Fact]
    public void ApplicationMayArriveBeforeItsUseRecordWithoutLosingCorrelation()
    {
        var tracker = new MitigationUseTracker();
        var application = Application(10.1);
        Assert.True(tracker.ObserveApplication(application));
        Assert.False(tracker.ObserveApplication(application));
        var use = Use(9.9);
        Assert.True(tracker.ObserveUse(use));
        Assert.False(tracker.ObserveUse(use));
        Assert.True(Resolve(tracker)!.WasUsedTooLate);
        tracker.Clear();
        Assert.Empty(tracker.UsesFor(use.Key));
        Assert.Empty(tracker.ApplicationsFor(use));
    }

    [Fact]
    public void LaterUseCannotOverrideEarlierUseAlreadyOnCooldownAtFatalHit()
    {
        var tracker = new MitigationUseTracker();
        tracker.ObserveUse(Use(0));
        tracker.ObserveUse(Use(10.1, 2, capture: 2));
        Assert.Null(Resolve(tracker));
    }

    [Fact]
    public void CooldownIsJudgedAtHitNotAtDelayedDeathNotification()
    {
        var tracker = new MitigationUseTracker();
        tracker.ObserveUse(Use(9.9));
        Assert.Null(Resolve(tracker, Candidate() with { CooldownSeconds = .2f }));
    }

    [Fact]
    public void UseLongAfterTheDeathDoesNotRewriteAnEarlierReadyOption()
    {
        var tracker = new MitigationUseTracker();
        tracker.ObserveUse(Use(11));
        var option = Resolve(tracker)!;
        Assert.False(option.WasUsedTooLate);
        Assert.Null(option.LastUsedAtUtc);
    }

    [Fact]
    public void SameNamedEnemyIsNotEnoughToProveLateBossMitigation()
    {
        var tracker = new MitigationUseTracker();
        tracker.ObserveUse(Use(9.9, target: Boss + 1));
        tracker.ObserveApplication(Application(10.1, target: Boss + 1));
        Assert.Null(Resolve(tracker, Candidate(PossibleMitigationScope.Boss)));
    }

    [Fact]
    public void LateBossMitigationRemainsBoundToTheEnemyActuallyTargeted()
    {
        var tracker = new MitigationUseTracker();
        tracker.ObserveUse(Use(10.1, target: Boss));
        var option = Resolve(tracker, Candidate(PossibleMitigationScope.Boss))!;
        Assert.Equal([Boss], option.SimulationSourceEntityIds);
        Assert.Equal(180000UL, WhatIfMitigationRules.DamageWithOptions([Hit(), Hit(source: Boss + 1)], [option]));
        Assert.True(WhatIfMitigationRules.HasMixedSourceCoverage([Hit(), Hit(source: Boss + 1)], [option]));
        Assert.False(WhatIfMitigationRules.HasMixedSourceCoverage([Hit()], [option]));
    }

    [Fact]
    public void LocalizedAndAlternativeEffectsUseAuditedIdsRatherThanDisplayNames()
    {
        var option = Candidate() with { EffectStatusIds = [3853, 1232] };
        Assert.True(PossibleMitigationTimingPolicy.MatchesStatus(option, Rampart with { Id = 1232, Name = "Localized Third Eye" }));
        Assert.False(PossibleMitigationTimingPolicy.MatchesStatus(option, Rampart with { Id = 1883, Name = "Tengentsu" }));
        var tactician = Candidate() with { EffectStatusIds = [1951], Statuses = [Rampart with { Id = 0, Name = "Tactician" }] };
        Assert.False(PossibleMitigationTimingPolicy.MatchesStatus(tactician, Rampart with { Id = 1197, Name = "Tactician" }));
    }

    [Fact]
    public void BatchResolutionKeepsTheSameRulesAndDoesNotModifyFrozenInput()
    {
        var tracker = new MitigationUseTracker();
        tracker.ObserveUse(Use(10.1));
        var candidate = Candidate();
        var death = Death();
        var options = PossibleMitigationTimingPolicy.ResolveAll([candidate], death, tracker, At(10.8), 64);
        Assert.True(Assert.Single(options).WasUsedTooLate);
        Assert.False(candidate.WasUsedTooLate);
        Assert.Empty(death.PossibleMitigations);
        Assert.True(PossibleMitigationTimingPolicy.SameOptions(options, options));
        Assert.False(PossibleMitigationTimingPolicy.SameOptions(options, [candidate]));
    }

    public static IEnumerable<object[]> CurrentWhatIfEffects()
    {
        var definitions = (Array)typeof(Plugin).GetField("PossibleMitigationDefinitions", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        foreach (var definition in definitions)
        {
            var type = definition!.GetType();
            var key = (string)type.GetProperty("Key")!.GetValue(definition)!;
            var scope = (PossibleMitigationScope)type.GetProperty("Scope")!.GetValue(definition)!;
            var ids = Plugin.GetPossibleMitigationEffectStatusIds(key);
            Assert.NotEmpty(ids);
            foreach (var id in ids)
                yield return [key, scope, id];
        }
    }

    [Theory]
    [MemberData(nameof(CurrentWhatIfEffects))]
    public void EveryCurrentWhatIfEffectSupportsConfirmedLateUseWithoutNameMatching(string key, PossibleMitigationScope scope, uint id)
    {
        Assert.True(Plugin.IsPossibleMitigationEffectStatusId(id));
        var tracker = new MitigationUseTracker();
        var candidate = Candidate(scope) with { EffectStatusIds = Plugin.GetPossibleMitigationEffectStatusIds(key) };
        var target = scope == PossibleMitigationScope.Boss ? Boss : Player;
        tracker.ObserveUse(Use(9.9, target: target));
        tracker.ObserveApplication(Application(10.1, target: target) with { Status = Rampart with { Id = id, Name = "Localized effect" } });
        Assert.True(Resolve(tracker, candidate)!.WasUsedTooLate);
    }

    [Fact]
    public void LateChatNoteIsShortAndNormalOptionsAreUnchanged()
    {
        Assert.Equal("Rampart (PLD; used late)", MitigationReviewPresentation.ChatOption(Candidate() with { WasUsedTooLate = true }));
        Assert.Equal("Rampart (PLD)", MitigationReviewPresentation.ChatOption(Candidate()));
        Assert.Contains("remains selectable for simulation purposes", MitigationReviewPresentation.LateUseTooltip);
    }

    [Fact]
    public void NewTimingEvidenceRoundTripsAndOldOptionsDoNotGainLateFlags()
    {
        var option = Candidate() with { WasUsedTooLate = true, SimulationSourceEntityIds = [Boss],
            SimulationHits = [PossibleMitigationTimingPolicy.SimulationIdentity(Hit())] };
        var copy = JsonSerializer.Deserialize<PossibleMitigationSnapshot>(JsonSerializer.Serialize(option))!;
        Assert.True(copy.WasUsedTooLate);
        Assert.Equal([Boss], copy.SimulationSourceEntityIds);
        Assert.Equal(option.SimulationHits, copy.SimulationHits);
        var json = JsonNode.Parse(JsonSerializer.Serialize(Candidate()))!.AsObject();
        json.Remove("WasUsedTooLate");
        json.Remove("EffectStatusIds");
        json.Remove("SimulationSourceEntityIds");
        var old = json.Deserialize<PossibleMitigationSnapshot>()!;
        Assert.False(old.WasUsedTooLate);
        Assert.Empty(old.SimulationSourceEntityIds);
    }

    [Fact]
    public void ExpiredTextBreathesSlowlyButReducedMotionRemainsStationary()
    {
        Assert.NotEqual(MitigationReviewPresentation.ExpiredPulse(0, false), MitigationReviewPresentation.ExpiredPulse(.875, false));
        for (var time = 0d; time < 20; time += .1)
        {
            Assert.InRange(MitigationReviewPresentation.ExpiredPulse(time, false), 0, 1);
            Assert.Equal(.5f, MitigationReviewPresentation.ExpiredPulse(time, true));
        }
        Assert.Equal(.5f, MitigationReviewPresentation.ExpiredPulse(double.NaN, false));
    }
}
