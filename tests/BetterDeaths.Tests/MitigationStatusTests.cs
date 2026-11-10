using System.Reflection;
using System.Text.Json;

namespace BetterDeaths.Tests;

public sealed class MitigationStatusTests
{
    private static readonly DateTime Start = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
    private const uint Boss = 0x40000001;
    private const uint Provider = 0x10000002;
    private static DateTime At(double seconds) => Start.AddSeconds(seconds);
    private static StatusSnapshot Status(uint id = 1203, float duration = 15, uint source = Provider, string name = "Addle") =>
        new(id, name, id, source, 0, duration);
    private static StatusSetSnapshot Set(double seconds, bool complete, params StatusSnapshot[] statuses) =>
        new(At(seconds), statuses) { IsComplete = complete };
    private static SourceMitigationSnapshot Enemy(double seconds, bool complete, params StatusSnapshot[] statuses) =>
        new(At(seconds), (float)seconds, Boss, "Kefka", statuses) { IsComplete = complete };
    private static PlayerStatusSnapshot Player(double seconds, bool complete, params StatusSnapshot[] statuses) =>
        new(At(seconds), (float)seconds, "player", "Player", 0, 31, "MCH", 0x10000001, statuses) { IsComplete = complete };
    private static CombatEventRecord Hit(double seconds = 5, uint source = Boss, params StatusSnapshot[] sourceStatuses) =>
        new(At(seconds), (float)seconds, "player", "Player", 0, source, "Kefka", 500, "Fatal hit", 0,
            DeathEventKind.Damage, 100000, 90000, 0, 100000, DamageType.Magic, false, false, false, false, "", [], sourceStatuses)
        { HpSource = CombatEventHpSource.DirectCombatEventSnapshot, EventIdentity = $"hit:{seconds}:{source}" };
    private static PartyDeathRecord Death(CombatEventRecord hit, params SourceMitigationSnapshot[] history) =>
        new(hit.SeenAtUtc.AddMilliseconds(300), hit.PullElapsedSeconds + .3f, "player", "Player", 0, 31, "MCH",
            0, 0, 100000, hit, [hit], [], [])
        { SourceMitigationHistory = history };

    public static IEnumerable<object[]> BossDebuffs => new[]
    {
        new object[] { 1203u, "Addle" }, new object[] { 1195u, "Feint" },
        new object[] { 1193u, "Reprisal" }, new object[] { 860u, "Dismantled" },
    };

    [Theory]
    [MemberData(nameof(BossDebuffs))]
    public void RemovalEndsOnlyThatEffectAndDoesNotResurrectOldObservations(uint id, string name)
    {
        var status = Status(id, name: name);
        var timeline = new StatusTimeline([Set(1, true, status), Set(2, false) with { RemovedStatuses = [status] }]);
        Assert.Single(timeline.At(At(1.9)));
        Assert.Empty(timeline.At(At(2)));
        Assert.Empty(timeline.At(At(8)));
    }

    [Fact]
    public void ConfirmedEmptyListClearsButUnavailableReadDoesNot()
    {
        var capture = new PlayerStatusHistory();
        Assert.True(capture.Observe(Player(1, true, Status())));
        Assert.False(capture.Observe(Player(2, true), available: false));
        Assert.Single(new StatusTimeline(capture.Between(At(0), At(3)).Select(value => value.ToStatusSet())).At(At(3)));
        capture.Observe(Player(4, true));
        Assert.Empty(new StatusTimeline(capture.Between(At(0), At(5)).Select(value => value.ToStatusSet())).At(At(5)));
    }

    [Fact]
    public void ShortenedLatestObservationExpiresWithoutRevivingEarlierLongerDuration()
    {
        var timeline = new StatusTimeline([Set(1, true, Status(duration: 15)), Set(2, true, Status(duration: 1))]);
        Assert.Single(timeline.At(At(2.5)));
        Assert.Empty(timeline.At(At(3)));
        Assert.Empty(timeline.At(At(5)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownClientTimerCannotEraseKnownExpiryOrReviveExpiredApplication(bool authoritative)
    {
        var initial = Set(1, !authoritative, Status(duration: 5)) with { IsAuthoritative = authoritative };
        var timeline = new StatusTimeline([initial, Set(3, true, Status(duration: 0)), Set(7, true, Status(duration: 0))]);
        Assert.Equal(2, Assert.Single(timeline.At(At(4))).RemainingTime);
        Assert.Empty(timeline.At(At(6)));
        Assert.Empty(timeline.At(At(8)));
        var cleared = new StatusTimeline([initial, Set(6.5, true), Set(7, true, Status(duration: 0))]);
        Assert.Empty(cleared.At(At(8)));
        var newApplication = Set(9, false, Status(duration: 0)) with { IsAuthoritative = true };
        cleared.Add(newApplication);
        Assert.Equal(0, Assert.Single(cleared.At(At(10))).RemainingTime);
    }

    [Fact]
    public void CompleteSnapshotRetiresOverwrittenProviderAndLateOldRemovalCannotEraseNewProvider()
    {
        var first = Status(source: 100);
        var second = Status(source: 200);
        var timeline = new StatusTimeline([Set(1, true, first), Set(2, true, second),
            Set(3, false) with { RemovedStatuses = [first] }]);
        Assert.Equal(200u, Assert.Single(timeline.At(At(4))).SourceId);
    }

    [Fact]
    public void PartialObservationsDoNotClearUnrelatedEffects()
    {
        var timeline = new StatusTimeline([Set(1, true, Status()), Set(2, false, Status(1195, name: "Feint")), Set(3, false)]);
        Assert.Equal(2, timeline.At(At(4)).Count);
    }

    [Fact]
    public void UnsortedCaptureIsResolvedByEventTimeAndFutureObservationsDoNotLeakBackwards()
    {
        var timeline = new StatusTimeline([Set(4, true), Set(1, true, Status()), Set(3, true, Status(1195, name: "Feint"))]);
        Assert.Empty(timeline.At(At(.9)));
        Assert.Equal("Addle", Assert.Single(timeline.At(At(2))).Name);
        Assert.Equal("Feint", Assert.Single(timeline.At(At(3))).Name);
        Assert.Empty(timeline.At(At(4)));
    }

    [Fact]
    public void IncrementalCaptureMatchesSortedResolutionIncludingLateChangesAndEqualTimestamps()
    {
        var observations = new[]
        {
            Set(1, false, Status()) with { IsAuthoritative = true },
            Set(4, true, Status(duration: 12)),
            Set(3, false) with { IsAuthoritative = true, RemovedStatuses = [Status(duration: 13)] },
            Set(3, false, Status(1195, name: "Feint")) with { IsAuthoritative = true },
            Set(2, true),
            Set(6, false, Status()) with { IsAuthoritative = true },
        };
        var incremental = new StatusTimeline([]);
        foreach (var observation in observations)
            incremental.Add(observation);
        var sorted = new StatusTimeline(observations);
        for (var time = 0d; time <= 22; time += .25)
            Assert.Equal(sorted.At(At(time)), incremental.At(At(time)));
    }

    [Fact]
    public void IndexedCaptureRangeSeedsOnlyEarlierEvidenceAndKeepsProviderMetadataInTimeOrder()
    {
        var history = new SourceMitigationHistory();
        var original = new StatusSourceSnapshot(Provider, "caster", "Original", 1, 25, "BLM");
        var renamed = original with { MemberName = "Renamed" };
        history.Observe(Enemy(1, false, Status()) with { IsAuthoritative = true, Sources = [original] });
        history.Observe(Enemy(6, false, Status(1195, name: "Feint")) with { IsAuthoritative = true, Sources = [renamed] });
        history.Observe(Enemy(3, false) with { IsAuthoritative = true, RemovedStatuses = [Status(duration: 13)] });
        var range = history.Between(At(2), At(4));
        Assert.All(range.Where(value => value.SeenAtUtc == At(2)), value => Assert.Equal("Original", Assert.Single(value.Sources).MemberName));
        var timeline = new StatusTimeline(range.Select(value => value.ToStatusSet()));
        Assert.Single(timeline.At(At(2)));
        Assert.Empty(timeline.At(At(3)));
        Assert.Empty(timeline.At(At(4)));
        Assert.DoesNotContain(range, value => value.Statuses.Any(status => status.Id == 1195));
        var onBoundary = history.Between(At(3), At(4));
        Assert.Empty(new StatusTimeline(onBoundary.Select(value => value.ToStatusSet())).At(At(3)));
        Assert.Empty(history.Between(At(4), At(2)));
    }

    [Fact]
    public void PollingCoalescesCountdownButRecordsExpiryRefreshAndStackChanges()
    {
        var capture = new PlayerStatusHistory();
        capture.Observe(Player(1, true, Status()));
        Assert.False(capture.Observe(Player(2, true, Status(duration: 14))));
        Assert.True(capture.Observe(Player(3, true, Status())));
        Assert.True(capture.Observe(Player(4, true, Status(duration: 14) with { StackCount = 2 })));
        Assert.True(capture.Observe(Player(5, true)));
        Assert.Equal(4, capture.Between(At(0), At(6)).Count);
    }

    [Fact]
    public void RetentionSeedsLongEffectsButDoesNotCarryRemovedEffectsIntoNewWindow()
    {
        var capture = new PlayerStatusHistory();
        capture.Observe(Player(0, true, Status(duration: 120)));
        capture.Prune(At(30));
        var seed = Assert.Single(capture.Between(At(30), At(31)));
        Assert.Equal(90, Assert.Single(seed.Statuses).RemainingTime);
        capture.Observe(Player(40, true));
        capture.Prune(At(50));
        Assert.Empty(capture.Between(At(50), At(51)));
    }

    [Fact]
    public void StartingNextPullKeepsOnlyActivePrepullEffectsAndTheirRemovalEvidence()
    {
        var players = new PlayerStatusHistory();
        var enemies = new SourceMitigationHistory();
        players.Observe(Player(1, false, Status(3896, 15, name: "Sun Sign")) with { IsAuthoritative = true });
        players.Observe(Player(1, false, Status(1191, 2, name: "Rampart")) with { IsAuthoritative = true });
        enemies.Observe(Enemy(2, false, Status()) with { IsAuthoritative = true });
        enemies.Observe(Enemy(4, false) with { IsAuthoritative = true, RemovedStatuses = [Status(duration: 13)] });
        players.Prune(At(5));
        enemies.Prune(At(5));
        enemies.Observe(Enemy(6, true, Status(duration: 11)));
        var timeline = new ReplayMitigationTimeline(players.Between(At(5), At(7)), enemies.Between(At(5), At(7)));
        var effect = Assert.Single(timeline.At(At(7), 2));
        Assert.Equal("Sun Sign", effect.ActionName);
        Assert.Equal(9, effect.DurationSeconds);
        Assert.Empty(timeline.At(At(16), 11));
    }

    [Fact]
    public void StartingNextPullPreservesEffectsThatEndedLaterInTheSameReceiveBatch()
    {
        var capture = new PlayerStatusHistory();
        var effect = Status(3896, 15, name: "Sun Sign");
        capture.Observe(Player(1, false, effect) with { IsAuthoritative = true });
        capture.Observe(Player(1.2, false) with { IsAuthoritative = true, RemovedStatuses = [effect] });
        capture.Prune(At(1.1));
        var timeline = new StatusTimeline(capture.Between(At(1.1), At(1.5)).Select(value => value.ToStatusSet()));
        Assert.Single(timeline.At(At(1.15)));
        Assert.Empty(timeline.At(At(1.2)));
    }

    [Theory]
    [MemberData(nameof(BossDebuffs))]
    public void ActualCaptureHistoryFeedsReviewReplayAndChatWithoutLosingRemoval(uint id, string name)
    {
        var capture = new SourceMitigationHistory();
        var effect = Status(id, name: name);
        capture.Observe(Enemy(1, true, effect));
        Assert.False(capture.Observe(Enemy(2, true), available: false));
        var before = Death(Hit(3), capture.Between(At(0), At(3)).ToArray());
        Assert.Contains(name, DeathMitigationChatFormatter.ActiveMitigations(
            DeathMitigationResolver.For(before).Resolve()));
        capture.Observe(Enemy(4, false) with { RemovedStatuses = [effect] });
        var after = Death(Hit(5), capture.Between(At(0), At(5)).ToArray());
        Assert.Empty(DeathMitigationResolver.For(after).ForHit(after.LikelyCause!));
        Assert.Equal("none captured", DeathMitigationChatFormatter.ActiveMitigations(
            DeathMitigationResolver.For(after).Resolve()));
        Assert.Empty(new ReplayMitigationTimeline([], capture.Between(At(0), At(6))).At(At(5), 5));
    }

    [Fact]
    public void SourceHistoryDoesNotFillWithEmptyEnemiesOrTruncateRealTransitionsAtOldEightySampleLimit()
    {
        var capture = new SourceMitigationHistory();
        Assert.False(capture.Observe(Enemy(0, true)));
        capture.Observe(Enemy(0, true, Status(duration: 120)));
        for (var index = 1; index <= 100; index++)
            Assert.True(capture.Observe(Enemy(index * .01, true, Status(duration: 120) with { StackCount = (ushort)index })));
        Assert.Equal(101, capture.Between(At(0), At(2)).Count);
        capture.Prune(At(.5));
        var retained = capture.Between(At(.5), At(2));
        Assert.True(retained.Count >= 51);
        var timeline = new StatusTimeline(retained.Select(value => value.ToStatusSet()));
        Assert.Equal(50, Assert.Single(timeline.At(At(.5))).StackCount);
        Assert.Equal(100, Assert.Single(timeline.At(At(1))).StackCount);
    }

    [Fact]
    public void RepeatedSourceReadsCoalesceButProviderMetadataImprovementsAreSaved()
    {
        var capture = new SourceMitigationHistory();
        capture.Observe(Enemy(1, true, Status()));
        Assert.False(capture.Observe(Enemy(2, true, Status(duration: 14))));
        Assert.True(capture.Observe(Enemy(3, true, Status(duration: 13)) with
        { Sources = [new(Provider, "caster", "Caster", 1, 33, "AST")] }));
        Assert.Equal(2, capture.Between(At(0), At(4)).Count);
        var active = Assert.Single(new ReplayMitigationTimeline([], capture.Between(At(0), At(4))).At(At(4), 4));
        Assert.Equal("Caster", active.MemberName);
    }

    [Fact]
    public void ConfirmedApplicationSurvivesLaggingEmptyClientReadUntilRealRemoval()
    {
        var capture = new SourceMitigationHistory();
        capture.Observe(Enemy(1, false, Status()) with { IsAuthoritative = true });
        capture.Observe(Enemy(2, true));
        var hit = Hit(3) with { HasSourceStatusSnapshot = true };
        var death = Death(hit, capture.Between(At(0), At(3)).ToArray());
        Assert.Equal("Addle", Assert.Single(DeathMitigationResolver.For(death).ForHit(hit)).Status.Name);
        Assert.Contains("Addle", DeathMitigationChatFormatter.ActiveMitigations(DeathMitigationResolver.For(death).Resolve()));
        Assert.Single(new ReplayMitigationTimeline([], death.SourceMitigationHistory).At(At(3), 3));
        capture.Observe(Enemy(4, false) with { IsAuthoritative = true, RemovedStatuses = [Status()] });
        Assert.Empty(new ReplayMitigationTimeline([], capture.Between(At(0), At(5))).At(At(5), 5));
    }

    [Fact]
    public void RetiredGenerationCannotBeRevivedByStaleMemoryButNewApplicationCanReplaceIt()
    {
        var initial = Set(1, false, Status()) with { IsAuthoritative = true };
        var removed = Set(2, false) with { RemovedStatuses = [Status()] };
        var stale = Set(3, true, Status(duration: 13));
        var timeline = new StatusTimeline([initial, removed, stale]);
        Assert.Empty(timeline.At(At(4)));
        var reapplied = Set(5, false, Status()) with { IsAuthoritative = true };
        Assert.Single(new StatusTimeline([initial, removed, stale, reapplied]).At(At(6)));
        Assert.Single(new StatusTimeline([initial, removed, Set(5, true, Status())]).At(At(6)));
    }

    [Fact]
    public void RetentionPreservesEvidencePriorityAndRemovalGeneration()
    {
        var capture = new PlayerStatusHistory();
        var effect = Status(3896, name: "Sun Sign");
        capture.Observe(Player(1, false, effect) with { IsAuthoritative = true });
        capture.Prune(At(2));
        capture.Observe(Player(3, true));
        var current = new StatusTimeline(capture.Between(At(2), At(4)).Select(value => value.ToStatusSet()));
        Assert.Single(current.At(At(4)));
        capture.Observe(Player(5, false) with { IsAuthoritative = true, RemovedStatuses = [effect] });
        capture.Prune(At(6));
        capture.Observe(Player(7, true, effect with { RemainingTime = 9 }));
        Assert.Empty(new StatusTimeline(capture.Between(At(6), At(8)).Select(value => value.ToStatusSet())).At(At(8)));
    }

    [Fact]
    public void StatusSlotReplacementRemovesOnlyItsPreviousOccupant()
    {
        var before = Status(3896, source: 100, name: "Sun Sign");
        var after = before with { SourceId = 200 };
        var replacement = StatusTimeline.SlotChange(At(2), before, after);
        Assert.Equal(before, Assert.Single(replacement.RemovedStatuses));
        Assert.True(replacement.IsAuthoritative);
        var timeline = new StatusTimeline([Set(1, false, before) with { IsAuthoritative = true }, replacement]);
        Assert.Equal(200u, Assert.Single(timeline.At(At(3))).SourceId);
        Assert.Empty(StatusTimeline.SlotChange(At(4), after, after with { RemainingTime = 12 }).RemovedStatuses);
        Assert.Single(StatusTimeline.SlotChange(At(5), after, null).RemovedStatuses);
    }

    [Fact]
    public void StatusPrepassMayContainFutureChangesButDoesNotApplyThemToEarlierHits()
    {
        var capture = new SourceMitigationHistory();
        capture.Observe(Enemy(1, false, Status()) with { IsAuthoritative = true });
        capture.Observe(Enemy(5.1, false) with { IsAuthoritative = true, RemovedStatuses = [Status()] });
        var death = Death(Hit(5) with { HasSourceStatusSnapshot = true }, capture.Between(At(0), At(5.5)).ToArray());
        Assert.Contains("Addle", DeathMitigationChatFormatter.ActiveMitigations(DeathMitigationResolver.For(death).Resolve()));
        Assert.Empty(new ReplayMitigationTimeline([], death.SourceMitigationHistory).At(At(5.2), 5.2f));
    }

    [Fact]
    public void VerifiedPetProviderIdentityIsShownWithoutChangingEffectOwnership()
    {
        const uint petId = 0x40000020;
        var effect = Status(317, source: petId, name: "Fey Illumination");
        var observed = Player(1, false, effect) with
        {
            IsAuthoritative = true,
            Sources = [new(petId, "scholar", "Scholar", 1, 28, "SCH")]
        };
        var entry = Assert.Single(new ReplayMitigationTimeline([observed], []).At(At(2), 2));
        Assert.Equal("Scholar", entry.MemberName);
        Assert.Equal(petId, entry.Statuses[0].SourceId);
    }

    [Fact]
    public void ImmutableDeathContextIsCachedButRecordReplacementRebuildsEvidence()
    {
        var death = Death(Hit(), Enemy(1, true, Status()));
        var resolver = DeathMitigationResolver.For(death);
        Assert.Same(resolver.Resolve(), resolver.Resolve());
        var updated = death with { SourceMitigationHistory = [Enemy(1, true)] };
        Assert.Empty(DeathMitigationResolver.For(updated).Resolve().BossStatuses);
    }

    [Fact]
    public void PrepullEffectIsSeededAtPullStartWithRemainingLifetimeAndProviderIdentity()
    {
        var capture = new PlayerStatusHistory();
        capture.Observe(Player(-5, true, Status(3896, 15, name: "Sun Sign")) with
        { Sources = [new(Provider, "ast", "Astrologian", 1, 33, "AST")] });
        var recorded = capture.Between(At(0), At(20)).Select(value => value with
        { PullElapsedSeconds = (float)(value.SeenAtUtc - Start).TotalSeconds }).ToArray();
        Assert.Equal(10, Assert.Single(recorded).Statuses[0].RemainingTime);
        var replay = new ReplayMitigationTimeline(recorded, []);
        Assert.Equal("Astrologian", Assert.Single(replay.At(At(1), 1)).MemberName);
        Assert.Empty(replay.At(At(10), 10));
    }

    [Theory]
    [MemberData(nameof(BossDebuffs))]
    public void ReviewAndChatRecoverTheSameCapturedEnemyEvidence(uint id, string name)
    {
        var hit = Hit();
        var death = Death(hit, Enemy(1, true, Status(id, name: name)));
        var resolver = DeathMitigationResolver.For(death);
        var context = resolver.Resolve();
        Assert.Equal(name, Assert.Single(resolver.ForHit(hit)).Status.Name);
        Assert.Contains(name, DeathMitigationChatFormatter.ActiveMitigations(context));
    }

    [Fact]
    public void ConfirmedAbsenceOnHitOverridesOldPositiveHistoryEverywhere()
    {
        var hit = Hit() with { HasSourceStatusSnapshot = true };
        var death = Death(hit, Enemy(1, true, Status()));
        var resolver = DeathMitigationResolver.For(death);
        var context = resolver.Resolve();
        Assert.Empty(resolver.ForHit(hit));
        Assert.Empty(context.BossStatuses);
        Assert.Equal("none captured", DeathMitigationChatFormatter.ActiveMitigations(context));
    }

    [Fact]
    public void RemovedEnemyEffectIsNotReintroducedFromEarlierHit()
    {
        var earlier = Hit(1, Boss, Status()) with { HasSourceStatusSnapshot = true };
        var hit = Hit();
        var death = Death(hit, Enemy(2, true)) with { RecentEvents = [earlier, hit] };
        Assert.Empty(DeathMitigationResolver.For(death).ForHit(hit));
    }

    [Fact]
    public void OtherEnemyAndUnknownSourceNeverBorrowEncounterEffects()
    {
        var hit = Hit(source: Boss + 1);
        var death = Death(hit, Enemy(1, true, Status()));
        var resolver = DeathMitigationResolver.For(death);
        Assert.Single(resolver.BossAt(At(5)));
        Assert.Empty(resolver.ForHit(hit));
        Assert.Empty(resolver.ForHit(hit with { SourceEntityId = 0 }));
    }

    [Fact]
    public void SameNamedEnemiesRemainSeparateInChat()
    {
        var context = new DeathMitigationContext([], [new(Boss, "Kefka", Status()),
            new(Boss + 1, "Kefka", Status(1195, name: "Feint"))]);
        Assert.Equal("Kefka #1: Addle | Kefka #2: Feint", DeathMitigationChatFormatter.ActiveMitigations(context));
    }

    [Fact]
    public void PlayerEvidenceNeverImportsBuffAppliedAfterTheHit()
    {
        var hit = Hit();
        var rampart = Status(1191, 20, name: "Rampart");
        var death = Death(hit) with
        {
            PlayerStatusHistory = [Player(4, true), Player(5.5, true, rampart)],
            HpHistory = [new(At(5.5), 5.5f, 90000, 0, 100000, [rampart])]
        };
        Assert.Empty(DeathMitigationResolver.For(death).PlayerForHit(hit));
    }

    [Fact]
    public void UnknownDurationRemainsUnknownUntilExplicitRemoval()
    {
        var effect = Status(3896, 0, name: "Sun Sign");
        var timeline = new ReplayMitigationTimeline([Player(1, true, effect),
            Player(4, false) with { RemovedStatuses = [effect] }], []);
        var unknown = Assert.Single(timeline.At(At(3), 3));
        Assert.Equal(0, unknown.DurationSeconds);
        Assert.Equal(0, ReplayMitigationTimeline.RemainingSeconds(unknown, 3));
        Assert.Empty(timeline.At(At(4), 4));
    }

    [Fact]
    public void SunSignUsesCapturedRecipientsAndRealExpiryRatherThanItsGrantingCooldown()
    {
        var effect = Status(3896, 15, name: "Sun Sign");
        var caster = new StatusSourceSnapshot(Provider, "ast", "Astrologian", 1, 33, "AST");
        var first = Player(1, true, effect) with { Sources = [caster] };
        var second = first with { MemberKey = "other", MemberName = "Other Player", EntityId = 0x10000003, PartyIndex = 2 };
        var timeline = new ReplayMitigationTimeline([first, second, first with { SeenAtUtc = At(5), Statuses = [] }], []);
        var active = Assert.Single(timeline.At(At(6), 6));
        Assert.Equal("Sun Sign", active.ActionName);
        Assert.Equal("Astrologian", active.MemberName);
        Assert.Equal(["Other Player"], active.TargetMemberNames);
        Assert.Equal(10, active.DurationSeconds);
        Assert.True(active.ObservedEffect);
        Assert.Empty(timeline.At(At(16), 16));
    }

    [Theory]
    [MemberData(nameof(BossDebuffs))]
    public void ReplayIncludesEnemyDebuffsAndHonorsTheirRemoval(uint id, string name)
    {
        var effect = Status(id, name: name);
        var first = Enemy(1, true, effect) with { Sources = [new(Provider, "caster", "Caster", 0, 35, "RDM")] };
        var timeline = new ReplayMitigationTimeline([], [first, Enemy(3, false) with { RemovedStatuses = [effect] }]);
        var active = Assert.Single(timeline.At(At(2), 2));
        Assert.Equal("Caster", active.MemberName);
        Assert.Equal(PossibleMitigationScope.Boss, active.Scope);
        Assert.Equal(["Kefka"], active.TargetMemberNames);
        Assert.Empty(timeline.At(At(3), 3));
    }

    public static IEnumerable<object[]> DefensiveCatalogue()
    {
        var field = typeof(Plugin).GetField("DefensiveStatusDefinitions", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var definition in (Array)field.GetValue(null)!)
        {
            var type = definition!.GetType();
            var name = (string)type.GetProperty("Name")!.GetValue(definition)!;
            var id = (uint?)type.GetProperty("StatusId")!.GetValue(definition);
            var ids = id is { } known ? [known] : (uint[]?)type.GetProperty("StatusIds")!.GetValue(definition) ?? [];
            Assert.NotEmpty(ids);
            foreach (var statusId in ids)
                yield return [statusId, name];
        }
    }

    [Theory]
    [MemberData(nameof(DefensiveCatalogue))]
    public void EveryReviewDefensiveDefinitionIsAlsoCapturedByReplay(uint id, string name)
    {
        var effect = Status(id, 10, name: name);
        Assert.True(Plugin.IsDefensiveStatus(effect));
        var timeline = new ReplayMitigationTimeline([Player(1, true, effect)], []);
        var active = Assert.Single(timeline.At(At(2), 2));
        Assert.Equal(name, active.ActionName);
        Assert.Empty(timeline.At(At(11), 11));
        Assert.True(Plugin.IsDefensiveStatus(effect with { Name = "Localized status name" }));
    }

    [Fact]
    public void SunSignIsRecognizedByIdEvenWithLocalizedDisplayName()
    {
        Assert.True(Plugin.IsDefensiveStatus(Status(3896, name: "Localized Sun Sign")));
        Assert.Equal(10, Assert.Single(Plugin.GetMitigationDisplayInfo(Status(3896, name: "Localized Sun Sign")).MitigationPercents).Percent);
    }

    [Fact]
    public void NewCaptureMetadataRoundTripsAndOldFilesDoNotClaimCompleteReads()
    {
        var hit = Hit() with { HasSourceStatusSnapshot = true, HasPlayerStatusSnapshot = true };
        var death = Death(hit, Enemy(1, true, Status())) with { PlayerStatusHistory = [Player(1, true)] };
        var pull = new PullDeathSnapshot(At(6), "Test", 1, "Duty", 6, [death])
        {
            ReplayStatusHistoryCaptured = true,
            ReplayStatusHistory = death.PlayerStatusHistory,
            ReplaySourceStatusHistory = death.SourceMitigationHistory,
        };
        var copy = JsonSerializer.Deserialize<PullDeathSnapshot>(JsonSerializer.Serialize(pull))!;
        Assert.True(copy.ReplayStatusHistoryCaptured);
        Assert.True(Assert.Single(copy.ReplayStatusHistory).IsComplete);
        Assert.True(Assert.Single(copy.ReplaySourceStatusHistory).IsComplete);
        Assert.True(copy.Deaths[0].LikelyCause!.HasSourceStatusSnapshot);
        var legacy = JsonSerializer.Deserialize<SourceMitigationSnapshot>(
            """{"SeenAtUtc":"2026-11-10T12:00:00Z","PullElapsedSeconds":0,"SourceEntityId":1,"SourceName":"Boss","Statuses":[]}""")!;
        Assert.False(legacy.IsComplete);
        Assert.Empty(legacy.RemovedStatuses);
    }
}
