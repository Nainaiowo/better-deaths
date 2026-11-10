using System.Reflection;
using System.Text.Json;

namespace BetterDeaths.Tests;

public sealed class MitigationRegressionTests
{
    private static readonly DateTime Start = new(2026, 11, 10, 18, 0, 0, DateTimeKind.Utc);
    private const uint Player = 0x10000001, Enemy = 0x40000001;
    private static DateTime At(double seconds) => Start.AddSeconds(seconds);
    private static StatusSnapshot Status(uint id = 1191, uint source = Player, float duration = 20) =>
        new(id, "Mit", id, source, 0, duration);
    private static CombatEventRecord Hit(double time = 10, uint source = Enemy) =>
        new(At(time), (float)time, "player", "Player", 0, source, "Boss", 500, "Hit", 0,
            DeathEventKind.Damage, 60000, 100000, 0, 100000, DamageType.Magic,
            false, false, false, false, "", [], [])
        { EventIdentity = $"hit:{time}:{source}", HpSource = CombatEventHpSource.DirectCombatEventSnapshot,
            HasPlayerStatusSnapshot = true, HasSourceStatusSnapshot = true };
    private static PartyDeathRecord Death(params CombatEventRecord[] hits) =>
        new(At(10.3), 10.3f, "player", "Player", 0, 21, "WAR", 0, 0, 100000, hits[^1], hits, [], [])
        { EntityId = Player, FatalSequence = new(At(9.9), At(10.8), null, hits, []) };

    [Theory]
    [InlineData(1191)]
    [InlineData(1203)]
    [InlineData(2613)]
    public void MissingProviderUpdateCannotCreateAnImmortalDuplicate(uint id)
    {
        var status = Status(id);
        var timeline = new StatusTimeline([
            new(At(1), [status]) { IsAuthoritative = true },
            new(At(2), [status with { SourceId = 0, RemainingTime = 0 }]) { IsAuthoritative = true, IsUpdateOnly = true },
            new(At(3), []) { IsAuthoritative = true, RemovedStatuses = [status with { RemainingTime = 18 }] },
            new(At(4), []) { IsComplete = true },
        ]);
        Assert.Equal(Player, Assert.Single(timeline.At(At(2.5))).SourceId);
        Assert.Empty(timeline.At(At(4)));
        Assert.Empty(timeline.At(At(60)));
    }

    [Fact]
    public void AmbiguousProviderUpdateDoesNotInventAnotherBuffOrChangeEitherProvider()
    {
        var first = Status(source: Player);
        var second = Status(source: Player + 1, duration: 15);
        var timeline = new StatusTimeline([
            new(At(1), [first, second]) { IsAuthoritative = true },
            new(At(2), [first with { SourceId = 0, RemainingTime = 0 }]) { IsAuthoritative = true, IsUpdateOnly = true },
            new(At(3), []) { IsAuthoritative = true, RemovedStatuses = [first] },
        ]);
        Assert.Equal(Player + 1, Assert.Single(timeline.At(At(4))).SourceId);
        Assert.Empty(timeline.At(At(22)));
    }

    [Fact]
    public void LaterConcreteProviderResolvesUnknownObservationAndRemovalEndsIt()
    {
        var status = Status();
        var timeline = new StatusTimeline([
            new(At(1), [status with { SourceId = 0 }]) { IsAuthoritative = true },
            new(At(2), [status with { RemainingTime = 19 }]) { IsComplete = true },
            new(At(3), []) { IsAuthoritative = true, RemovedStatuses = [status with { RemainingTime = 18 }] },
        ]);
        Assert.Equal(Player, Assert.Single(timeline.At(At(2))).SourceId);
        Assert.Empty(timeline.At(At(4)));
    }

    [Fact]
    public void UpdateWithoutNewApplicationCannotReviveRemovedGeneration()
    {
        var status = Status();
        var timeline = new StatusTimeline([
            new(At(1), [status]) { IsAuthoritative = true },
            new(At(2), []) { IsAuthoritative = true, RemovedStatuses = [status with { RemainingTime = 19 }] },
            new(At(3), [status with { RemainingTime = 18 }]) { IsAuthoritative = true, IsUpdateOnly = true },
        ]);
        Assert.Empty(timeline.At(At(4)));
        timeline.Add(new(At(5), [status]) { IsAuthoritative = true });
        Assert.Single(timeline.At(At(6)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0x10000001)]
    public void TimerlessUpdateCannotEstablishANewPermanentBuff(uint source)
    {
        var update = new StatusSetSnapshot(At(10), [Status(source: source, duration: 0)])
        { IsAuthoritative = true, IsUpdateOnly = true };
        var timeline = new StatusTimeline([update]);
        Assert.Empty(timeline.At(At(11)));
    }

    [Fact]
    public void MissingProviderUpdateUsesTheOnlyStillActiveProviderNotExpiredHistory()
    {
        var first = Status(duration: 1);
        var second = Status(source: Player + 1);
        var timeline = new StatusTimeline([
            new(At(1), [first, second]) { IsAuthoritative = true },
            new(At(3), [first with { SourceId = 0, RemainingTime = 0, StackCount = 2 }])
            { IsAuthoritative = true, IsUpdateOnly = true },
        ]);
        var active = Assert.Single(timeline.At(At(4)));
        Assert.Equal(Player + 1, active.SourceId);
        Assert.Equal(2, active.StackCount);
        Assert.Empty(timeline.At(At(22)));
    }

    [Fact]
    public void RepeatedRedrawsOfGroupedHitsDoNotGrowStatusCaches()
    {
        var first = Hit();
        var second = Hit(10.003) with { Statuses = [Status()], SourceStatuses = [Status(1203)] };
        var resolver = DeathMitigationResolver.For(Death(first, second));
        for (var frame = 0; frame < 2000; frame++)
        {
            var grouped = LeadUpHpTimelineState.CombineDamageBursts([first, second]);
            foreach (var hit in grouped)
            {
                _ = resolver.PlayerForHit(hit);
                _ = resolver.ForHit(hit);
            }
        }
        foreach (var field in new[] { "hitPlayerStatuses", "hitBossStatuses" })
        {
            var cache = typeof(DeathMitigationResolver).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(resolver)!;
            var count = (int)cache.GetType().GetProperty("Count")!.GetValue(cache)!;
            Assert.InRange(count, 1, 2);
        }
    }

    [Fact]
    public void SemanticCacheReusesClonesButDoesNotReturnOldStatusContents()
    {
        var hit = Hit();
        var resolver = DeathMitigationResolver.For(Death(hit));
        Assert.Empty(resolver.ForHit(hit));
        Assert.Empty(resolver.PlayerForHit(hit));
        var changed = hit with { Statuses = [Status()], SourceStatuses = [Status(1203)] };
        Assert.Single(resolver.ForHit(changed));
        Assert.Single(resolver.PlayerForHit(changed));
        var clone = changed with { Statuses = changed.Statuses.ToArray(), SourceStatuses = changed.SourceStatuses.ToArray() };
        Assert.Same(resolver.ForHit(changed), resolver.ForHit(clone));
        Assert.Same(resolver.PlayerForHit(changed), resolver.PlayerForHit(clone));
        Assert.Empty(resolver.ForHit(hit));
        Assert.Empty(resolver.PlayerForHit(hit));
    }

    [Fact]
    public void GroupedHitsDoNotMoveTheLaterBuffOntoTheEarlierHit()
    {
        var first = Hit();
        var second = Hit(10.003) with { Statuses = [Status()], SourceStatuses = [Status(1203)] };
        var resolver = DeathMitigationResolver.For(Death(first, second));
        var combined = Assert.Single(LeadUpHpTimelineState.CombineDamageBursts([first, second]));
        Assert.Single(resolver.ForHit(combined));
        Assert.Single(resolver.PlayerForHit(combined));
        Assert.Empty(resolver.ForHit(first));
        Assert.Empty(resolver.PlayerForHit(first));
        Assert.Single(resolver.ForHit(second));
        Assert.Single(resolver.PlayerForHit(second));
    }

    [Fact]
    public void LongerMultiEnemyGroupsPreserveEveryHitAndTheirTotalDamage()
    {
        var hits = new[] { Hit(), Hit(10.002, Enemy + 1), Hit(10.004, Enemy + 2) };
        var combined = Assert.Single(LeadUpHpTimelineState.CombineDamageBursts(hits));
        Assert.Equal(hits, CombatEventComponents.Expand(combined));
        Assert.Equal(180000UL, WhatIfMitigationRules.DamageWithOptions([combined], []));
        Assert.Equal(0u, combined.SourceEntityId);
        Assert.DoesNotContain("ConstituentEvents", JsonSerializer.Serialize(combined));
    }

    [Fact]
    public void ABlockedUnidentifiedUpdateCannotEraseAnotherProvidersRemovalEvidence()
    {
        var status = Status();
        var timeline = new StatusTimeline([
            new(At(1), [status]) { IsAuthoritative = true },
            new(At(2), []) { IsAuthoritative = true, RemovedStatuses = [status with { RemainingTime = 19 }] },
            new(At(3), []) { IsAuthoritative = true, RemovedStatuses = [status with { SourceId = 0, RemainingTime = 0 }] },
            new(At(4), [status with { RemainingTime = 30 }]) { IsComplete = true },
            new(At(5), [status with { SourceId = Player + 1 }]) { IsAuthoritative = true },
            new(At(6), [status with { RemainingTime = 15 }]) { IsComplete = true },
        ]);
        Assert.Equal(Player + 1, Assert.Single(timeline.At(At(7))).SourceId);
    }

    [Fact]
    public void PlayerAndEnemyHistoryRetainUpdateSemanticsAcrossPruningAndSerialization()
    {
        var status = Status();
        var player = new PlayerStatusHistory();
        var enemy = new SourceMitigationHistory();
        PlayerStatusSnapshot PlayerPoint(double time, IReadOnlyList<StatusSnapshot> statuses) =>
            new(At(time), (float)time, "player", "Player", 0, 21, "WAR", Player, statuses) { IsAuthoritative = true };
        SourceMitigationSnapshot EnemyPoint(double time, IReadOnlyList<StatusSnapshot> statuses) =>
            new(At(time), (float)time, Enemy, "Boss", statuses) { IsAuthoritative = true };
        player.Observe(PlayerPoint(1, [status]));
        enemy.Observe(EnemyPoint(1, [status]));
        player.Observe(PlayerPoint(2, [status with { SourceId = 0, RemainingTime = 0 }]) with { IsUpdateOnly = true });
        enemy.Observe(EnemyPoint(2, [status with { SourceId = 0, RemainingTime = 0 }]) with { IsUpdateOnly = true });
        Assert.True(player.Between(At(1), At(2)).Last().ToStatusSet().IsUpdateOnly);
        Assert.True(enemy.Between(At(1), At(2)).Last().ToStatusSet().IsUpdateOnly);
        player.Prune(At(2.5));
        enemy.Prune(At(2.5));
        player.Observe(PlayerPoint(3, []) with { RemovedStatuses = [status with { RemainingTime = 18 }] });
        enemy.Observe(EnemyPoint(3, []) with { RemovedStatuses = [status with { RemainingTime = 18 }] });
        var playerCopy = JsonSerializer.Deserialize<PlayerStatusSnapshot[]>(JsonSerializer.Serialize(player.Between(At(2.5), At(4))))!;
        var enemyCopy = JsonSerializer.Deserialize<SourceMitigationSnapshot[]>(JsonSerializer.Serialize(enemy.Between(At(2.5), At(4))))!;
        var playerTimeline = new StatusTimeline(playerCopy.Select(value => value.ToStatusSet()));
        var enemyTimeline = new StatusTimeline(enemyCopy.Select(value => value.ToStatusSet()));
        Assert.Equal(Player, Assert.Single(playerTimeline.At(At(2.5))).SourceId);
        Assert.Equal(Player, Assert.Single(enemyTimeline.At(At(2.5))).SourceId);
        Assert.Empty(playerTimeline.At(At(60)));
        Assert.Empty(enemyTimeline.At(At(60)));
    }

    [Fact]
    public void SameNamedEnemiesKeepTheirOwnRowsAndMitigations()
    {
        var first = Hit();
        var second = Hit(10.003, Enemy + 1) with { SourceStatuses = [Status(1203)] };
        var grouped = LeadUpHpTimelineState.CombineDamageBursts([first, second]);
        var combined = Assert.Single(grouped);
        Assert.Equal(0u, combined.SourceEntityId);
        Assert.Empty(combined.SourceStatuses);
        Assert.Equal([Enemy, Enemy + 1], combined.ConstituentEvents.Select(value => value.SourceEntityId));
        var death = Death(first, second);
        var fatal = Assert.Single(DeathDisplaySelector.Select(death).FatalEvents);
        Assert.Equal([Enemy, Enemy + 1], fatal.Events.Select(value => value.SourceEntityId));
        Assert.Equal(0u, fatal.DisplayEvent.SourceEntityId);
        var resolver = DeathMitigationResolver.For(death);
        Assert.Empty(resolver.ForHit(first));
        Assert.Equal(Enemy + 1, Assert.Single(resolver.ForHit(combined)).EntityId);
        Assert.Equal(Enemy + 1, Assert.Single(resolver.ForHit(fatal.DisplayEvent)).EntityId);
    }

    [Fact]
    public void KnownEnemyIdsCannotBeDeduplicatedJustBecauseTheirNamesMatch()
    {
        Assert.False(CombatEventComponents.SameSource(Hit(), Hit(source: Enemy + 1)));
        Assert.True(CombatEventComponents.SameSource(Hit(), Hit()));
        Assert.True(CombatEventComponents.SameSource(Hit(), Hit(source: 0)));
        Assert.False(CombatEventComponents.SameSource(Hit(), Hit(source: 0) with { SourceName = "Other boss" }));
    }

    [Fact]
    public void ActiveDebuffOnOneEnemyDoesNotHideLateUseOnAnother()
    {
        var first = Hit() with { SourceStatuses = [Status(1193, Player + 1)] };
        var second = Hit(10.05, Enemy + 1);
        var death = Death(first, second);
        var tracker = new MitigationUseTracker();
        tracker.ObserveUse(new(1, "player:reprisal", "player", Player, 1, At(10.1), 10.1f, 7535,
            "Reprisal", 0, [Enemy + 1]));
        var candidate = new PossibleMitigationSnapshot("player:reprisal", "player", "Player", 0, 21, "WAR",
            7535, "Reprisal", 0, PossibleMitigationScope.Boss, 60, null, null, "Ready", [Status(1193)])
        { EffectStatusIds = [1193] };
        var option = PossibleMitigationTimingPolicy.Resolve(candidate, death, tracker, At(10.8));
        Assert.NotNull(option);
        Assert.True(option.WasUsedTooLate);
        Assert.Equal([Enemy + 1], option.SimulationSourceEntityIds);
        Assert.Equal(114000UL, WhatIfMitigationRules.DamageWithOptions([first, second], [option]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LateClassificationUsesTheTargetEnemiesHitTimeAndSnapshot(bool delayedApplication)
    {
        var first = Hit() with { HasSourceStatusSnapshot = false, HpSource = CombatEventHpSource.NoPreHitSample };
        var second = Hit(10.05, Enemy + 1);
        var death = Death(first, second);
        var tracker = new MitigationUseTracker();
        tracker.ObserveUse(new(1, "player:reprisal", "player", Player, 1, At(10.02), 10.02f, 7535,
            "Reprisal", 0, [Enemy + 1]));
        if (delayedApplication)
            tracker.ObserveApplication(new(2, 1, At(10.06), Enemy + 1, "player", Status(1193)));
        var candidate = new PossibleMitigationSnapshot("player:reprisal", "player", "Player", 0, 21, "WAR",
            7535, "Reprisal", 0, PossibleMitigationScope.Boss, 60, null, null, "Ready", [Status(1193)])
        { EffectStatusIds = [1193] };
        var option = Assert.IsType<PossibleMitigationSnapshot>(PossibleMitigationTimingPolicy.Resolve(candidate, death, tracker, At(10.8)));
        Assert.Equal(delayedApplication, option.WasUsedTooLate);
        if (delayedApplication)
            Assert.Equal([PossibleMitigationTimingPolicy.SimulationIdentity(second)], option.SimulationHits);
    }

    [Fact]
    public void AlreadyProtectedHitOfTheSameEnemyIsNotMitigatedTwice()
    {
        var first = Hit() with { SourceStatuses = [Status(1193, duration: .01f)] };
        var second = Hit(10.05);
        var candidate = new PossibleMitigationSnapshot("other:reprisal", "other", "Other", 1, 21, "WAR",
            7535, "Reprisal", 0, PossibleMitigationScope.Boss, 60, null, null, "Ready", [Status(1193)])
        { EffectStatusIds = [1193] };
        var option = PossibleMitigationTimingPolicy.Resolve(candidate, Death(first, second), new(), At(10.8))!;
        Assert.Equal([PossibleMitigationTimingPolicy.SimulationIdentity(second)], option.SimulationHits);
        Assert.Equal(114000UL, WhatIfMitigationRules.DamageWithOptions([first, second], [option]));
    }
}
