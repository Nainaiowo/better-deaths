namespace BetterDeaths.DamageParsing;

public sealed class DamageParsingTests
{
    private static readonly DateTime SeenAtUtc = new(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DamageActorIdentity Source = new(0x1001, "Source", 0, string.Empty, true, 21);
    private static readonly DamageActorIdentity Target = new(0x2001, "Target", 0, string.Empty, false, 0);

    [Fact]
    public void ParsesDirectDamageAmountAndFlags()
    {
        var parser = new DirectDamageParser();
        var packet = CreatePacket(
            new DamageActionEffect(0, 5, 0x60, 0xB5, 3, 0x40, 125)
            {
                Param2 = 7,
            }) with
        {
            ActionCategoryId = 1,
            IsAutoAttack = true,
            ActionType = 2,
            SourceSequence = 44,
            SpellId = 55,
            AnimationVariation = 3,
            AnimationTargetEntityId = Target.EntityId,
        };

        var damageEvent = Assert.Single(parser.Parse(packet));

        Assert.Equal(DamageEventOutcome.Damage, damageEvent.Outcome);
        Assert.Equal(196733u, damageEvent.Amount);
        Assert.Equal(5, damageEvent.DamageType);
        Assert.Equal(11, damageEvent.ElementType);
        Assert.True(damageEvent.Critical);
        Assert.True(damageEvent.DirectHit);
        Assert.True(damageEvent.Blocked);
        Assert.False(damageEvent.Parried);
        Assert.True(damageEvent.IsAutoAttack);
        Assert.Equal(1u, damageEvent.ActionCategoryId);
        Assert.Equal(2, damageEvent.ActionType);
        Assert.Equal(44, damageEvent.SourceSequence);
        Assert.Equal(55, damageEvent.SpellId);
        Assert.Equal(3, damageEvent.AnimationVariation);
        Assert.Equal(Target.EntityId, damageEvent.AnimationTargetEntityId);
        Assert.Equal(7, damageEvent.RawParam2);

    }

    [Fact]
    public void ResolvesSourceEntriesBackToTheSource()
    {
        var parser = new DirectDamageParser();
        var packet = CreatePacket(
            new DamageActionEffect(0, 3, 0, 0, 0, 0x80, 321));

        var damageEvent = Assert.Single(parser.Parse(packet));

        Assert.True(damageEvent.IsSourceEntry);
        Assert.Equal(Source, damageEvent.Target);
        Assert.Equal(Target, damageEvent.PacketTarget);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(6, 0)]
    [InlineData(7, 3)]
    [InlineData(74, 3)]
    public void PreservesNonStandardDamageOutcomes(byte type, int expectedValue)
    {
        var parser = new DirectDamageParser();
        var expected = (DamageEventOutcome)expectedValue;

        var damageEvent = Assert.Single(parser.Parse(CreatePacket(
            new DamageActionEffect(0, type, 0, 0, 0, 0, 900))));

        Assert.Equal(expected, damageEvent.Outcome);
        Assert.Equal(expected == DamageEventOutcome.Damage ? 900u : 0u, damageEvent.Amount);
        Assert.Equal(type == 6, damageEvent.Parried);
    }

    [Fact]
    public void IgnoresHealingStatusesAndEmptyEffects()
    {
        var parser = new DirectDamageParser();
        var packet = CreatePacket(
            new DamageActionEffect(0, 0, 0, 0, 0, 0, 100),
            new DamageActionEffect(1, 4, 0, 0, 0, 0, 200),
            new DamageActionEffect(2, 14, 0, 0, 0, 0, 300));

        Assert.Empty(parser.Parse(packet));
    }

    [Fact]
    public void KeepsEveryTargetAndDamageSlotDistinct()
    {
        var parser = new DirectDamageParser();
        var secondTarget = Target with { EntityId = 0x2002, Name = "Second target" };
        var packet = new DamageActionPacket(
            88,
            SeenAtUtc,
            701,
            Source,
            100,
            "Multi-hit",
            [
                new DamageActionTarget(0, Target,
                [
                    new DamageActionEffect(0, 3, 0, 0, 0, 0, 100),
                    new DamageActionEffect(1, 3, 0, 0, 0, 0, 200),
                ]),
                new DamageActionTarget(1, secondTarget,
                [
                    new DamageActionEffect(0, 3, 0, 0, 0, 0, 300),
                ]),
            ]);

        var parsed = parser.Parse(packet);

        Assert.Equal(3, parsed.Count);
        Assert.Equal(3, parsed.Select(entry => entry.EventId).Distinct().Count());
        Assert.Equal(600u, parsed.Aggregate(0u, (total, entry) => total + entry.Amount));
    }

    [Fact]
    public void AggregatesSourcesActionsTargetsAndOutcomes()
    {
        var module = new DamageParsingModule();
        module.Process(CreatePacket(
            new DamageActionEffect(0, 3, 0x60, 0, 0, 0, 1000),
            new DamageActionEffect(1, 1, 0, 0, 0, 0, 0)));
        module.Process(CreatePacket(
            [new DamageActionEffect(0, 3, 0, 0, 0, 0, 500)],
            packetSequence: 2,
            seenAtUtc: SeenAtUtc.AddSeconds(5),
            actionId: 101,
            actionName: "Second action"));

        var snapshot = Assert.IsType<DamageEncounterSnapshot>(module.GetCurrentEncounter());
        var source = Assert.Single(snapshot.Sources);
        var target = Assert.Single(snapshot.Targets);

        Assert.Equal(1500ul, snapshot.TotalDamage);
        Assert.Empty(snapshot.Events);
        Assert.Equal(2, snapshot.PacketCount);
        Assert.Equal(1500ul, source.TotalDamage);
        Assert.Equal(2, source.Hits);
        Assert.Equal(1, source.Misses);
        Assert.Equal(1, source.CriticalDirectHits);
        Assert.Equal(2, source.Actions.Count);
        Assert.Equal(1500ul, target.TotalDamage);
    }

    [Fact]
    public void UsesCaptureTimestampsForMeterDurationWithoutChangingEventOrder()
    {
        var module = new DamageParsingModule();
        module.Process(CreatePacket(
            [new DamageActionEffect(0, 3, 0, 0, 0, 0, 100)],
            seenAtUtc: SeenAtUtc,
            actionId: 100) with
        {
            CapturedAtUtc = SeenAtUtc.AddMilliseconds(100),
        });
        module.Process(CreatePacket(
            [new DamageActionEffect(0, 3, 0, 0, 0, 0, 200)],
            packetSequence: 2,
            seenAtUtc: SeenAtUtc.AddSeconds(4.8),
            actionId: 101) with
        {
            CapturedAtUtc = SeenAtUtc.AddSeconds(5.013),
        });

        var snapshot = Assert.IsType<DamageEncounterSnapshot>(
            module.EndEncounter(SeenAtUtc.AddSeconds(10), "Combat ended"));

        Assert.Equal(SeenAtUtc, snapshot.StartedAtUtc);
        Assert.Equal(SeenAtUtc.AddSeconds(4.8), snapshot.EndedAtUtc);
        Assert.Equal(SeenAtUtc.AddMilliseconds(100), snapshot.MeterStartedAtUtc);
        Assert.Equal(SeenAtUtc.AddSeconds(5.013), snapshot.MeterEndedAtUtc);
        Assert.Equal(4.913, snapshot.DurationSeconds, 3);
        Assert.Equal(SeenAtUtc, snapshot.Events[0].SeenAtUtc);
        Assert.Equal(SeenAtUtc.AddSeconds(4.8), snapshot.Events[1].SeenAtUtc);
    }

    [Fact]
    public void TracksDeathsAndTheLargestHitForWidgetColumns()
    {
        var module = new DamageParsingModule();
        module.Process(CreatePacket(
            [new DamageActionEffect(0, 3, 0, 0, 0, 0, 400)],
            actionId: 100,
            actionName: "First action"));
        module.Process(CreatePacket(
            [new DamageActionEffect(0, 3, 0, 0, 0, 0, 900)],
            packetSequence: 2,
            seenAtUtc: SeenAtUtc.AddSeconds(1),
            actionId: 101,
            actionName: "Largest action"));
        module.RecordDeath(Source);

        var snapshot = Assert.IsType<DamageEncounterSnapshot>(module.GetCurrentEncounter());
        var source = Assert.Single(snapshot.Sources);
        var largestAction = Assert.Single(source.Actions, action => action.ActionId == 101);

        Assert.Equal(1, source.Deaths);
        Assert.Equal(900ul, source.MaxHitAmount);
        Assert.Equal("Largest action", source.MaxHitActionName);
        Assert.Equal(900ul, largestAction.MaxHitAmount);
    }

    [Fact]
    public void DeathOutsideAnEncounterDoesNotLeakIntoTheNextPull()
    {
        var module = new DamageParsingModule();
        module.RecordDeath(Source);
        module.Process(CreatePacket(new DamageActionEffect(0, 3, 0, 0, 0, 0, 400)));

        var snapshot = Assert.IsType<DamageEncounterSnapshot>(module.GetCurrentEncounter());

        Assert.Equal(0, Assert.Single(snapshot.Sources).Deaths);
    }

    [Fact]
    public void DeduplicatesTheSameDecodedEvent()
    {
        var module = new DamageParsingModule();
        var packet = CreatePacket(new DamageActionEffect(0, 3, 0, 0, 0, 0, 700));

        module.Process(packet);
        module.Process(packet);

        var snapshot = Assert.IsType<DamageEncounterSnapshot>(module.GetCurrentEncounter());
        Assert.Equal(700ul, snapshot.TotalDamage);
        Assert.Equal(1, snapshot.DuplicateEventCount);

        var ended = Assert.IsType<DamageEncounterSnapshot>(module.EndEncounter(SeenAtUtc.AddSeconds(10), "Combat ended"));
        Assert.Single(ended.Events);
    }

    [Fact]
    public void DeduplicatesRepeatedCallbackForTheSameGameAction()
    {
        var module = new DamageParsingModule();
        var first = CreatePacket(new DamageActionEffect(0, 3, 0, 0, 0, 0, 700));
        var repeatedCallback = first with { PacketSequence = 2 };

        module.Process(first);
        module.Process(repeatedCallback);

        var snapshot = Assert.IsType<DamageEncounterSnapshot>(module.GetCurrentEncounter());
        Assert.Equal(700ul, snapshot.TotalDamage);
        Assert.Equal(1, snapshot.DuplicateEventCount);

        var ended = Assert.IsType<DamageEncounterSnapshot>(module.EndEncounter(SeenAtUtc.AddSeconds(10), "Combat ended"));
        Assert.Single(ended.Events);
    }

    [Fact]
    public void EndingEncounterPreservesSnapshotAndStartsFresh()
    {
        var module = new DamageParsingModule();
        module.Process(CreatePacket(new DamageActionEffect(0, 3, 0, 0, 0, 0, 1000)));
        module.Process(CreatePacket(
            [new DamageActionEffect(0, 3, 0, 0, 0, 0, 500)],
            packetSequence: 2,
            seenAtUtc: SeenAtUtc.AddSeconds(5),
            actionId: 101));

        var ended = module.EndEncounter(SeenAtUtc.AddSeconds(10), "Combat ended");

        Assert.NotNull(ended);
        Assert.Same(ended, module.LastEncounter);
        Assert.Equal(5.0, ended.DurationSeconds);
        Assert.Equal(300.0, ended.DamagePerSecond);
        Assert.Equal("Combat ended", ended.EndReason);
        Assert.Null(module.GetCurrentEncounter());

        module.Process(CreatePacket(
            [new DamageActionEffect(0, 3, 0, 0, 0, 0, 250)],
            packetSequence: 2,
            seenAtUtc: SeenAtUtc.AddSeconds(20)));

        var next = Assert.IsType<DamageEncounterSnapshot>(module.GetCurrentEncounter());
        Assert.Equal(250ul, next.TotalDamage);
    }

    [Fact]
    public void EncounterDpsUsesTheFullFractionalDuration()
    {
        var module = new DamageParsingModule();
        module.Process(CreatePacket(new DamageActionEffect(0, 3, 0, 0, 0, 0, 1_000_000)));
        module.Process(CreatePacket(
            [new DamageActionEffect(0, 3, 0, 0, 0, 0, 1_356_123)],
            packetSequence: 2,
            seenAtUtc: SeenAtUtc.AddMilliseconds(43_879),
            actionId: 101));

        var snapshot = Assert.IsType<DamageEncounterSnapshot>(module.GetCurrentEncounter());

        Assert.Equal(43.879, snapshot.DurationSeconds, 3);
        Assert.Equal(53_695.91, snapshot.DamagePerSecond, 2);
    }

    [Fact]
    public void CommitsBufferedOpenerWhenCombatBecomesActive()
    {
        var module = new DamageParsingModule();
        var packet = CreatePacket(new DamageActionEffect(0, 3, 0, 0, 0, 0, 1000));

        var parsed = module.Process(packet, allowAutomaticEncounterStart: false);
        module.SetCombatActive(true, SeenAtUtc.AddMilliseconds(100));

        var snapshot = Assert.IsType<DamageEncounterSnapshot>(module.GetCurrentEncounter());
        Assert.Single(parsed);
        Assert.Equal(1000ul, snapshot.TotalDamage);
        Assert.Equal(SeenAtUtc, snapshot.StartedAtUtc);
        Assert.Equal(1, snapshot.PacketCount);
    }

    [Fact]
    public void DiscardsBufferedTrafficWhenCombatNeverStarts()
    {
        var module = new DamageParsingModule();
        module.Process(
            CreatePacket(new DamageActionEffect(0, 3, 0, 0, 0, 0, 1000)),
            allowAutomaticEncounterStart: false);

        module.SetCombatActive(false, SeenAtUtc.AddSeconds(2.1));
        module.SetCombatActive(true, SeenAtUtc.AddSeconds(2.2));

        Assert.Null(module.GetCurrentEncounter());
    }

    [Fact]
    public void EmptyEncounterCleanupDoesNotLeakStatusesIntoTheNextPull()
    {
        var module = new DamageParsingModule();
        module.ObserveStatus(CreatePeriodicStatus(Source, 900, "Burn"));

        Assert.Null(module.EndEncounter(SeenAtUtc.AddSeconds(1), "Pull reset"));

        var damageEvent = Assert.Single(ProcessPeriodicTick(
            module,
            CreatePeriodicTick(500, 2, SeenAtUtc.AddSeconds(3))));
        Assert.Equal(DamageAttributionQuality.Unattributed, damageEvent.AttributionQuality);
    }

    [Fact]
    public void LiveDurationStopsAtTheLatestParsedEvent()
    {
        var module = new DamageParsingModule();
        module.Process(CreatePacket(new DamageActionEffect(0, 3, 0, 0, 0, 0, 1000)));
        module.Process(CreatePacket(
            [new DamageActionEffect(0, 3, 0, 0, 0, 0, 500)],
            packetSequence: 2,
            seenAtUtc: SeenAtUtc.AddSeconds(5),
            actionId: 101));

        var first = Assert.IsType<DamageEncounterSnapshot>(module.GetCurrentEncounter());
        var unchanged = Assert.IsType<DamageEncounterSnapshot>(module.GetCurrentEncounter());

        Assert.Equal(5.0, first.DurationSeconds);
        Assert.Equal(300.0, first.DamagePerSecond);
        Assert.Same(first, unchanged);
    }

    [Fact]
    public void ExplicitCombatLifecycleStopsLiveDpsAtTheLatestAlliedEvent()
    {
        var module = new DamageParsingModule();
        module.Process(
            CreatePacket(new DamageActionEffect(0, 3, 0, 0, 0, 0, 1000)),
            allowAutomaticEncounterStart: false);
        module.SetCombatActive(true, SeenAtUtc.AddMilliseconds(100));
        module.Process(
            CreatePacket(
                [new DamageActionEffect(0, 3, 0, 0, 0, 0, 500)],
                packetSequence: 2,
                seenAtUtc: SeenAtUtc.AddSeconds(5),
                actionId: 101),
            allowAutomaticEncounterStart: false);

        var snapshot = Assert.IsType<DamageEncounterSnapshot>(
            module.GetCurrentEncounter(SeenAtUtc.AddSeconds(10)));

        Assert.Equal(5.0, snapshot.DurationSeconds);
        Assert.Equal(300.0, snapshot.DamagePerSecond);
    }

    [Fact]
    public void ExplicitCombatLifecycleUsesRecordedEventsForEncounterBounds()
    {
        var module = new DamageParsingModule();
        module.SetCombatActive(true, SeenAtUtc.AddSeconds(-2));
        module.Process(
            CreatePacket(new DamageActionEffect(0, 3, 0, 0, 0, 0, 1000)),
            allowAutomaticEncounterStart: false);
        module.Process(
            CreatePacket(
                [new DamageActionEffect(0, 3, 0, 0, 0, 0, 500)],
                packetSequence: 2,
                seenAtUtc: SeenAtUtc.AddSeconds(5),
                actionId: 101),
            allowAutomaticEncounterStart: false);
        module.SetCombatActive(false, SeenAtUtc.AddSeconds(6));

        var ended = Assert.IsType<DamageEncounterSnapshot>(
            module.EndEncounter(SeenAtUtc.AddSeconds(9), "Combat ended"));

        Assert.Equal(SeenAtUtc, ended.StartedAtUtc);
        Assert.Equal(SeenAtUtc.AddSeconds(5), ended.EndedAtUtc);
        Assert.Equal(5.0, ended.DurationSeconds);
        Assert.Equal(300.0, ended.DamagePerSecond, precision: 6);
    }

    [Fact]
    public void EncounterDurationEndsAtLatestAlliedOutgoingEvent()
    {
        var module = new DamageParsingModule();
        module.Process(CreatePacket(
            [new DamageActionEffect(0, 3, 0, 0, 0, 0, 1000)],
            source: Source with { IsPartyMember = true }));
        module.Process(CreatePacket(
            [new DamageActionEffect(0, 3, 0, 0, 0, 0, 500)],
            packetSequence: 2,
            seenAtUtc: SeenAtUtc.AddSeconds(5),
            actionId: 101,
            source: Source with { IsPartyMember = true }));
        module.Process(CreatePacket(
            [new DamageActionEffect(0, 3, 0, 0, 0, 0, 250)],
            packetSequence: 3,
            seenAtUtc: SeenAtUtc.AddSeconds(7),
            actionId: 102,
            source: new DamageActorIdentity(0x4001, "Enemy", 0, string.Empty, false, 0)));

        var ended = Assert.IsType<DamageEncounterSnapshot>(
            module.EndEncounter(SeenAtUtc.AddSeconds(10), "Combat ended"));

        Assert.Equal(SeenAtUtc.AddSeconds(5), ended.EndedAtUtc);
        Assert.Equal(5.0, ended.DurationSeconds);
        Assert.Equal(1750ul, ended.TotalDamage);
    }

    [Fact]
    public void CreditsRecognizedPetDamageToItsOwner()
    {
        var module = new DamageParsingModule();
        var owner = new DamageActorIdentity(0x1001, "Owner", 0, string.Empty, true, 27);
        var pet = new DamageActorIdentity(0x3001, "Pet", 0x1001, "Owner", false, 0)
        {
            IsPet = true,
        };
        var parsed = module.Process(CreatePacket(
            [new DamageActionEffect(0, 3, 0, 0, 0, 0, 400)],
            source: pet,
            sourceOwner: owner));

        var snapshot = Assert.IsType<DamageEncounterSnapshot>(module.GetCurrentEncounter());
        var source = Assert.Single(snapshot.Sources);
        var damageEvent = Assert.Single(parsed);

        Assert.Equal(owner.EntityId, source.Source.EntityId);
        Assert.Equal(pet.EntityId, damageEvent.Source.EntityId);
        Assert.Equal(owner, damageEvent.AttributedSource);
        Assert.Equal(400ul, source.TotalDamage);
    }

    [Fact]
    public void RetainsPetOwnershipWhenLaterPacketsHaveIncompleteIdentityData()
    {
        var module = new DamageParsingModule();
        var owner = new DamageActorIdentity(0x1001, "Owner", 0, string.Empty, true, 27)
        {
            IsPartyMember = true,
        };
        var pet = new DamageActorIdentity(0x3001, "Pet", 0x1001, "Owner", false, 0)
        {
            IsPet = true,
        };
        module.Process(CreatePacket(
            [new DamageActionEffect(0, 3, 0, 0, 0, 0, 400)],
            source: pet,
            sourceOwner: owner));
        module.Process(CreatePacket(
            [new DamageActionEffect(0, 3, 0, 0, 0, 0, 300)],
            packetSequence: 2,
            seenAtUtc: SeenAtUtc.AddSeconds(1),
            actionId: 101,
            source: new DamageActorIdentity(0x3001, "Entity 00003001", 0, string.Empty, false, 0)));

        var source = Assert.Single(Assert.IsType<DamageEncounterSnapshot>(module.GetCurrentEncounter()).Sources);

        Assert.Equal(owner.EntityId, source.Source.EntityId);
        Assert.Equal(700ul, source.TotalDamage);
    }

    [Fact]
    public void DoesNotCombineNonPetOwnedObjects()
    {
        var module = new DamageParsingModule();
        module.SetCombatActive(true, SeenAtUtc);
        var owner = new DamageActorIdentity(0x1001, "Owner", 0, string.Empty, true, 27);
        var ownedObject = new DamageActorIdentity(0x3001, "Owned object", 0x1001, "Owner", false, 0);
        module.Process(CreatePacket(
            [new DamageActionEffect(0, 3, 0, 0, 0, 0, 400)],
            source: ownedObject,
            sourceOwner: owner));

        var source = Assert.Single(Assert.IsType<DamageEncounterSnapshot>(module.GetCurrentEncounter()).Sources);

        Assert.Equal(ownedObject.EntityId, source.Source.EntityId);
    }

    [Fact]
    public void UpgradesTransientSourceIdentityWhenBetterDataArrives()
    {
        var module = new DamageParsingModule();
        var unresolved = new DamageActorIdentity(Source.EntityId, "Entity 00001001", 0, string.Empty, false, 0);
        var resolved = Source with { IsPartyMember = true };
        module.Process(CreatePacket(
            [new DamageActionEffect(0, 3, 0, 0, 0, 0, 100)],
            source: unresolved));
        module.Process(CreatePacket(
            [new DamageActionEffect(0, 3, 0, 0, 0, 0, 200)],
            packetSequence: 2,
            source: resolved) with
        { ActionSequence = 701 });

        var source = Assert.Single(Assert.IsType<DamageEncounterSnapshot>(module.GetCurrentEncounter()).Sources);

        Assert.Equal(resolved, source.Source);
        Assert.Equal(300ul, source.TotalDamage);
    }

    [Fact]
    public void CreditsLimitBreaksToTheirOwnSource()
    {
        var module = new DamageParsingModule();
        var packet = CreatePacket(new DamageActionEffect(0, 3, 0, 0, 0, 0, 1200)) with
        {
            ActionCategoryId = 9,
        };

        module.Process(packet);

        var source = Assert.Single(Assert.IsType<DamageEncounterSnapshot>(module.GetCurrentEncounter()).Sources);
        Assert.True(source.Source.IsLimitBreak);
        Assert.Equal("Limit Break", source.Source.Name);
        Assert.Equal(1200ul, source.TotalDamage);
    }

    [Fact]
    public void CreditsSinglePeriodicStatusExactly()
    {
        var module = new DamageParsingModule();
        module.ObserveStatus(CreatePeriodicStatus(Source, 900, "Burn"));

        var parsed = ProcessPeriodicTick(module, CreatePeriodicTick(600));

        var damageEvent = Assert.Single(parsed);
        var snapshot = Assert.IsType<DamageEncounterSnapshot>(module.GetCurrentEncounter());
        Assert.Equal(Source, damageEvent.AttributedSource);
        Assert.Equal(DamageAttributionQuality.Exact, damageEvent.AttributionQuality);
        Assert.True(damageEvent.IsPeriodic);
        Assert.Equal(900u, damageEvent.StatusId);
        Assert.Equal(600ul, snapshot.TotalDamage);
        Assert.Equal(600ul, snapshot.ExactDamage);
        Assert.Equal(0ul, snapshot.EstimatedDamage);
    }

    [Fact]
    public void PeriodicTrackerKeepsAnActiveStatusEligible()
    {
        var tracker = new PeriodicDamageTracker();
        tracker.Observe(CreatePeriodicStatus(Source, 900, "Burn"));

        var damageEvent = Assert.Single(tracker.Process(CreatePeriodicTick(600)));

        Assert.Equal(Source, damageEvent.AttributedSource);
        Assert.Equal(DamageAttributionQuality.Exact, damageEvent.AttributionQuality);
    }

    [Fact]
    public void DefersCombinedTickUntilNearbyStatusUpdateArrives()
    {
        var module = new DamageParsingModule();
        var tick = CreatePeriodicTick(600) with { Source = Target };

        Assert.Empty(module.ProcessPeriodicTick(tick));
        module.ObserveStatus(CreatePeriodicStatus(Source, 900, "Burn") with
        {
            SeenAtUtc = tick.SeenAtUtc.AddMilliseconds(10),
        });
        var parsed = module.FlushPendingPeriodicTicks(tick.SeenAtUtc.AddMilliseconds(50));

        var damageEvent = Assert.Single(parsed);
        Assert.Equal(Source, damageEvent.AttributedSource);
        Assert.Equal(DamageAttributionQuality.Exact, damageEvent.AttributionQuality);
    }

    [Fact]
    public void PendingPeriodicTickCanStartAnEncounter()
    {
        var module = new DamageParsingModule();
        var tick = CreatePeriodicTick(600);
        module.ObserveStatus(CreatePeriodicStatus(Source, 900, "Burn"));
        Assert.Empty(module.ProcessPeriodicTick(tick));

        var snapshot = Assert.IsType<DamageEncounterSnapshot>(module.GetCurrentEncounter());

        Assert.Equal(600ul, snapshot.TotalDamage);
        Assert.Equal(tick.SeenAtUtc, snapshot.StartedAtUtc);
    }

    [Fact]
    public void UnrelatedStatusRemovalDoesNotReleasePendingTick()
    {
        var module = new DamageParsingModule();
        var tick = CreatePeriodicTick(600);
        module.ObserveStatus(CreatePeriodicStatus(Source, 900, "Burn"));
        Assert.Empty(module.ProcessPeriodicTick(tick));

        module.ObserveStatus(new DamageStatusApplication(
            Target,
            Source,
            999,
            "Unrelated status",
            0,
            0,
            string.Empty,
            tick.SeenAtUtc.AddMilliseconds(10),
            0,
            false,
            false,
            true));

        Assert.Empty(module.FlushPendingPeriodicTicks(tick.SeenAtUtc.AddMilliseconds(10)));
        var damageEvent = Assert.Single(module.FlushPendingPeriodicTicks(tick.SeenAtUtc.AddMilliseconds(50)));
        Assert.Equal(Source, damageEvent.AttributedSource);
    }

    [Fact]
    public void ResolvesPendingCombinedTickBeforeItsStatusIsRemoved()
    {
        var module = new DamageParsingModule();
        var status = CreatePeriodicStatus(Source, 900, "Burn");
        var tick = CreatePeriodicTick(600);
        module.ObserveStatus(status);
        Assert.Empty(module.ProcessPeriodicTick(tick));

        module.ObserveStatus(status with
        {
            IsRemoval = true,
            SeenAtUtc = tick.SeenAtUtc.AddMilliseconds(10),
        });

        var snapshot = Assert.IsType<DamageEncounterSnapshot>(module.GetCurrentEncounter());
        var source = Assert.Single(snapshot.Sources);
        Assert.Equal(Source.EntityId, source.Source.EntityId);
        Assert.Equal(600ul, source.TotalDamage);
        Assert.Equal(600ul, snapshot.ExactDamage);
    }

    [Fact]
    public void ReportsPeriodicResolutionWhenAnotherPacketFlushesThePendingTick()
    {
        var module = new DamageParsingModule();
        IReadOnlyList<ParsedDamageEvent>? reported = null;
        module.PeriodicEventsResolved = parsed => reported = parsed;
        var status = CreatePeriodicStatus(Source, 900, "Burn");
        var tick = CreatePeriodicTick(600);
        module.ObserveStatus(status);
        Assert.Empty(module.ProcessPeriodicTick(tick));

        module.RefreshStatus(Target.EntityId, status.StatusId, tick.SeenAtUtc.AddMilliseconds(50));

        var damageEvent = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<ParsedDamageEvent>>(reported));
        Assert.Equal(600u, damageEvent.Amount);
        Assert.Equal(Source.EntityId, damageEvent.Source.EntityId);
    }

    [Fact]
    public void SplitsCombinedTicksWithoutChangingReportedTotal()
    {
        var module = new DamageParsingModule();
        var secondSource = Source with { EntityId = 0x1002, Name = "Second source" };
        module.ObserveStatus(CreatePeriodicStatus(Source, 900, "Burn"));
        module.ObserveStatus(CreatePeriodicStatus(secondSource, 901, "Poison"));

        var parsed = ProcessPeriodicTick(module, CreatePeriodicTick(601));

        var snapshot = Assert.IsType<DamageEncounterSnapshot>(module.GetCurrentEncounter());
        Assert.Equal(2, parsed.Count);
        Assert.All(parsed, entry => Assert.Equal(DamageAttributionQuality.Estimated, entry.AttributionQuality));
        Assert.Equal(601u, parsed.Aggregate(0u, (total, entry) => total + entry.Amount));
        Assert.Equal(601ul, snapshot.TotalDamage);
        Assert.Equal(601ul, snapshot.EstimatedDamage);
    }

    [Fact]
    public void DoesNotReuseLearnedTicksAfterStatusesAreReapplied()
    {
        var module = new DamageParsingModule();
        var secondSource = Source with { EntityId = 0x1002, Name = "Second source" };
        var first = CreatePeriodicStatus(Source, 900, "Burn");
        var second = CreatePeriodicStatus(secondSource, 901, "Poison") with
        {
            SeenAtUtc = SeenAtUtc.AddSeconds(4.5),
        };
        module.ObserveStatus(first);
        ProcessPeriodicTick(module, CreatePeriodicTick(300));
        module.ObserveStatus(first with { IsRemoval = true, SeenAtUtc = SeenAtUtc.AddSeconds(4) });
        module.ObserveStatus(second);
        ProcessPeriodicTick(module, CreatePeriodicTick(100, 2, SeenAtUtc.AddSeconds(7.5)));
        module.ObserveStatus(second with { IsRemoval = true, SeenAtUtc = SeenAtUtc.AddSeconds(8) });
        module.ObserveStatus(first with { SeenAtUtc = SeenAtUtc.AddSeconds(9) });
        module.ObserveStatus(second with { SeenAtUtc = SeenAtUtc.AddSeconds(9) });

        var parsed = ProcessPeriodicTick(module, CreatePeriodicTick(400, 3, SeenAtUtc.AddSeconds(12)));

        Assert.Equal(200u, parsed.Single(entry => entry.Source.EntityId == Source.EntityId).Amount);
        Assert.Equal(200u, parsed.Single(entry => entry.Source.EntityId == secondSource.EntityId).Amount);
    }

    [Fact]
    public void DoesNotCarryLearnedTickWeightsIntoTheNextEncounter()
    {
        var module = new DamageParsingModule();
        var secondSource = Source with { EntityId = 0x1002, Name = "Second source" };
        var first = CreatePeriodicStatus(Source, 900, "Burn");
        var second = CreatePeriodicStatus(secondSource, 901, "Poison") with
        {
            SeenAtUtc = SeenAtUtc.AddSeconds(4.5),
        };
        module.ObserveStatus(first);
        ProcessPeriodicTick(module, CreatePeriodicTick(300));
        module.ObserveStatus(first with { IsRemoval = true, SeenAtUtc = SeenAtUtc.AddSeconds(4) });
        module.ObserveStatus(second);
        ProcessPeriodicTick(module, CreatePeriodicTick(100, 2, SeenAtUtc.AddSeconds(7.5)));
        Assert.NotNull(module.EndEncounter(SeenAtUtc.AddSeconds(8), "Combat ended"));

        module.ObserveStatus(first with { SeenAtUtc = SeenAtUtc.AddSeconds(10) });
        module.ObserveStatus(second with { SeenAtUtc = SeenAtUtc.AddSeconds(10) });
        var parsed = ProcessPeriodicTick(module, CreatePeriodicTick(400, 3, SeenAtUtc.AddSeconds(13)));

        Assert.Equal(200u, parsed.Single(entry => entry.Source.EntityId == Source.EntityId).Amount);
        Assert.Equal(200u, parsed.Single(entry => entry.Source.EntityId == secondSource.EntityId).Amount);
    }

    [Fact]
    public void ReusesExactTickWeightsForMatchingApplicationSnapshots()
    {
        var module = new DamageParsingModule();
        var secondSource = Source with { EntityId = 0x1002, Name = "Second source" };
        var first = CreatePeriodicStatus(Source, 900, "Burn") with { SnapshotKey = "snapshot-a" };
        var second = CreatePeriodicStatus(secondSource, 901, "Poison") with
        {
            SeenAtUtc = SeenAtUtc.AddSeconds(5),
            SnapshotKey = "snapshot-b",
        };
        module.ObserveStatus(first);
        ProcessPeriodicTick(module, CreatePeriodicTick(100));
        module.ObserveStatus(first with { IsRemoval = true, SeenAtUtc = SeenAtUtc.AddSeconds(4) });
        module.ObserveStatus(second);
        ProcessPeriodicTick(module, CreatePeriodicTick(300, 2, SeenAtUtc.AddSeconds(8)));
        module.ObserveStatus(second with { IsRemoval = true, SeenAtUtc = SeenAtUtc.AddSeconds(9) });
        module.ObserveStatus(first with { SeenAtUtc = SeenAtUtc.AddSeconds(10) });
        module.ObserveStatus(second with { SeenAtUtc = SeenAtUtc.AddSeconds(10) });

        var parsed = ProcessPeriodicTick(module, CreatePeriodicTick(400, 3, SeenAtUtc.AddSeconds(13)));

        Assert.Equal(100u, parsed.Single(entry => entry.Source.EntityId == Source.EntityId).Amount);
        Assert.Equal(300u, parsed.Single(entry => entry.Source.EntityId == secondSource.EntityId).Amount);
        Assert.Equal(400u, parsed.Aggregate(0u, (total, entry) => total + entry.Amount));
    }

    [Fact]
    public void DoesNotReuseTickWeightsForDifferentApplicationSnapshots()
    {
        var module = new DamageParsingModule();
        var secondSource = Source with { EntityId = 0x1002, Name = "Second source" };
        var first = CreatePeriodicStatus(Source, 900, "Burn") with { SnapshotKey = "snapshot-a" };
        var second = CreatePeriodicStatus(secondSource, 901, "Poison") with
        {
            SeenAtUtc = SeenAtUtc.AddSeconds(5),
            SnapshotKey = "snapshot-b",
        };
        module.ObserveStatus(first);
        ProcessPeriodicTick(module, CreatePeriodicTick(100));
        module.ObserveStatus(first with { IsRemoval = true, SeenAtUtc = SeenAtUtc.AddSeconds(4) });
        module.ObserveStatus(second);
        ProcessPeriodicTick(module, CreatePeriodicTick(300, 2, SeenAtUtc.AddSeconds(8)));
        module.ObserveStatus(second with { IsRemoval = true, SeenAtUtc = SeenAtUtc.AddSeconds(9) });
        module.ObserveStatus(first with
        {
            SeenAtUtc = SeenAtUtc.AddSeconds(10),
            SnapshotKey = "snapshot-c",
        });
        module.ObserveStatus(second with
        {
            SeenAtUtc = SeenAtUtc.AddSeconds(10),
            SnapshotKey = "snapshot-d",
        });

        var parsed = ProcessPeriodicTick(module, CreatePeriodicTick(400, 3, SeenAtUtc.AddSeconds(13)));

        Assert.Equal(200u, parsed.Single(entry => entry.Source.EntityId == Source.EntityId).Amount);
        Assert.Equal(200u, parsed.Single(entry => entry.Source.EntityId == secondSource.EntityId).Amount);
    }

    [Fact]
    public void UsesMedianExactTicksSoOneOutlierDoesNotDistortAnEstimatedSplit()
    {
        var module = new DamageParsingModule();
        var secondSource = Source with { EntityId = 0x1002, Name = "Second source" };
        var first = CreatePeriodicStatus(Source, 900, "Burn") with { SnapshotKey = "snapshot-a" };
        var second = CreatePeriodicStatus(secondSource, 901, "Poison") with
        {
            SeenAtUtc = SeenAtUtc.AddSeconds(10.5),
            SnapshotKey = "snapshot-b",
        };
        module.ObserveStatus(first);
        ProcessPeriodicTick(module, CreatePeriodicTick(100));
        ProcessPeriodicTick(module, CreatePeriodicTick(100, 2, SeenAtUtc.AddSeconds(6)));
        ProcessPeriodicTick(module, CreatePeriodicTick(1000, 3, SeenAtUtc.AddSeconds(9)));
        module.ObserveStatus(first with { IsRemoval = true, SeenAtUtc = SeenAtUtc.AddSeconds(10) });
        module.ObserveStatus(second);
        ProcessPeriodicTick(module, CreatePeriodicTick(300, 4, SeenAtUtc.AddSeconds(13.5)));
        module.ObserveStatus(second with { IsRemoval = true, SeenAtUtc = SeenAtUtc.AddSeconds(14) });
        module.ObserveStatus(first with { SeenAtUtc = SeenAtUtc.AddSeconds(15) });
        module.ObserveStatus(second with { SeenAtUtc = SeenAtUtc.AddSeconds(15) });

        var parsed = ProcessPeriodicTick(module, CreatePeriodicTick(400, 5, SeenAtUtc.AddSeconds(18)));

        Assert.Equal(100u, parsed.Single(entry => entry.Source.EntityId == Source.EntityId).Amount);
        Assert.Equal(300u, parsed.Single(entry => entry.Source.EntityId == secondSource.EntityId).Amount);
    }

    [Fact]
    public void KeepsUnknownPeriodicDamageVisible()
    {
        var module = new DamageParsingModule();

        var damageEvent = Assert.Single(ProcessPeriodicTick(module, CreatePeriodicTick(777)));

        var snapshot = Assert.IsType<DamageEncounterSnapshot>(module.GetCurrentEncounter());
        Assert.Equal("Unattributed outgoing", damageEvent.Source.Name);
        Assert.Equal(DamageAttributionQuality.Unattributed, damageEvent.AttributionQuality);
        Assert.Equal(777ul, snapshot.TotalDamage);
        Assert.Equal(777ul, snapshot.UnattributedDamage);
    }

    [Fact]
    public void UsesPacketSourceForGroundEffectTick()
    {
        var module = new DamageParsingModule();
        var tick = CreatePeriodicTick(450) with
        {
            StatusId = 902,
            StatusName = "Ground fire",
            Source = Source,
        };

        var damageEvent = Assert.Single(ProcessPeriodicTick(module, tick));

        Assert.Equal(Source, damageEvent.Source);
        Assert.Equal(DamageAttributionQuality.Exact, damageEvent.AttributionQuality);
        Assert.Equal("Ground fire", damageEvent.ActionName);
    }

    [Fact]
    public void RotatesSourceLessGroundTicksAcrossDueOwners()
    {
        const uint statusId = 0x1234;
        var module = new DamageParsingModule();
        var secondSource = Source with { EntityId = 0x1002, Name = "Second source" };
        module.ObserveStatus(CreatePeriodicStatus(Source, statusId, "Ground fire"));
        module.ObserveStatus(CreatePeriodicStatus(secondSource, statusId, "Ground fire") with
        {
            SeenAtUtc = SeenAtUtc.AddMilliseconds(100),
        });

        var first = Assert.Single(ProcessPeriodicTick(module, CreatePeriodicTick(
            450,
            1,
            SeenAtUtc.AddSeconds(3)) with
        {
            StatusId = statusId,
            StatusName = "Ground fire",
        }));
        var second = Assert.Single(ProcessPeriodicTick(module, CreatePeriodicTick(
            460,
            2,
            SeenAtUtc.AddSeconds(6)) with
        {
            StatusId = statusId,
            StatusName = "Ground fire",
        }));

        Assert.Equal(Source.EntityId, first.AttributedSource?.EntityId);
        Assert.Equal(secondSource.EntityId, second.AttributedSource?.EntityId);
        Assert.Equal(DamageAttributionQuality.Estimated, first.AttributionQuality);
        Assert.Equal(DamageAttributionQuality.Estimated, second.AttributionQuality);
    }

    [Fact]
    public void ExactGroundTickAdvancesThatOwnersSchedule()
    {
        const uint statusId = 0x1234;
        var module = new DamageParsingModule();
        var secondSource = Source with { EntityId = 0x1002, Name = "Second source" };
        module.ObserveStatus(CreatePeriodicStatus(Source, statusId, "Ground fire"));
        module.ObserveStatus(CreatePeriodicStatus(secondSource, statusId, "Ground fire") with
        {
            SeenAtUtc = SeenAtUtc.AddMilliseconds(100),
        });

        Assert.Single(ProcessPeriodicTick(module, CreatePeriodicTick(
            450,
            1,
            SeenAtUtc.AddSeconds(3)) with
        {
            StatusId = statusId,
            StatusName = "Ground fire",
            Source = Source,
        }));
        var sourceLess = Assert.Single(ProcessPeriodicTick(module, CreatePeriodicTick(
            460,
            2,
            SeenAtUtc.AddSeconds(6)) with
        {
            StatusId = statusId,
            StatusName = "Ground fire",
        }));

        Assert.Equal(secondSource.EntityId, sourceLess.AttributedSource?.EntityId);
    }

    [Fact]
    public void GroundTickCanResolveFromStatusHeldByTheCaster()
    {
        const uint statusId = 0x01F5;
        var module = new DamageParsingModule();
        module.ObserveStatus(CreatePeriodicStatus(Source, statusId, "Doton") with
        {
            Target = Source,
        });

        var damageEvent = Assert.Single(ProcessPeriodicTick(module, CreatePeriodicTick(
            450,
            1,
            SeenAtUtc.AddSeconds(3)) with
        {
            StatusId = statusId,
            StatusName = "Doton",
        }));

        Assert.Equal(Source.EntityId, damageEvent.AttributedSource?.EntityId);
        Assert.Equal(Target.EntityId, damageEvent.Target.EntityId);
        Assert.Equal(DamageAttributionQuality.Exact, damageEvent.AttributionQuality);
    }

    [Fact]
    public void TargetHeldGroundStatusTakesPriorityOverAnUnrelatedTarget()
    {
        const uint statusId = 0x035D;
        var module = new DamageParsingModule();
        var unrelatedTarget = Target with { EntityId = 0x4002, Name = "Other target" };
        var secondSource = Source with { EntityId = 0x1002, Name = "Second source" };
        module.ObserveStatus(CreatePeriodicStatus(Source, statusId, "Wildfire") with
        {
            Target = unrelatedTarget,
        });
        module.ObserveStatus(CreatePeriodicStatus(secondSource, statusId, "Wildfire") with
        {
            Target = Target,
        });

        var damageEvent = Assert.Single(ProcessPeriodicTick(module, CreatePeriodicTick(
            450,
            1,
            SeenAtUtc.AddSeconds(3)) with
        {
            StatusId = statusId,
            StatusName = "Wildfire",
        }));

        Assert.Equal(secondSource.EntityId, damageEvent.AttributedSource?.EntityId);
        Assert.Equal(DamageAttributionQuality.Exact, damageEvent.AttributionQuality);
    }

    [Fact]
    public void LearnsAnUnfamiliarGroundEffectFromItsPacketStatusId()
    {
        const uint statusId = 0x1234;
        var module = new DamageParsingModule();
        module.ObserveStatus(CreatePeriodicStatus(Source, statusId, "Future ground effect") with
        {
            IsPeriodicDamage = false,
        });
        var tick = CreatePeriodicTick(450) with
        {
            StatusId = statusId,
            StatusName = "Future ground effect",
        };

        var damageEvent = Assert.Single(ProcessPeriodicTick(module, tick));

        Assert.Equal(Source, damageEvent.AttributedSource);
        Assert.Equal(DamageAttributionQuality.Exact, damageEvent.AttributionQuality);
        Assert.Equal("Future ground effect", damageEvent.ActionName);
    }

    [Fact]
    public void KeepsAnUnknownDurationGroundOwnerForLaterTicks()
    {
        const uint statusId = 0x1234;
        var module = new DamageParsingModule();
        module.ObserveStatus(CreatePeriodicStatus(Source, statusId, "Future ground effect") with
        {
            DurationSeconds = 0,
            IsPeriodicDamage = false,
        });
        var tick = CreatePeriodicTick(450, 1, SeenAtUtc.AddSeconds(15)) with
        {
            StatusId = statusId,
            StatusName = "Future ground effect",
        };

        var damageEvent = Assert.Single(ProcessPeriodicTick(module, tick));

        Assert.Equal(Source, damageEvent.AttributedSource);
        Assert.Equal(DamageAttributionQuality.Exact, damageEvent.AttributionQuality);
    }

    [Fact]
    public void GroundDamageDoesNotParticipateInCombinedDotTicks()
    {
        var module = new DamageParsingModule();
        var standardSource = Source with { EntityId = 0x1002, Name = "Standard source" };
        module.ObserveStatus(CreatePeriodicStatus(Source, 0x01F5, "Doton"));
        module.ObserveStatus(CreatePeriodicStatus(standardSource, 900, "Burn"));

        var groundTick = CreatePeriodicTick(450) with
        {
            StatusId = 0x01F5,
            StatusName = "Doton",
            Source = Source,
        };
        var groundEvent = Assert.Single(ProcessPeriodicTick(module, groundTick));
        var combinedEvent = Assert.Single(ProcessPeriodicTick(
            module,
            CreatePeriodicTick(300, 2, SeenAtUtc.AddSeconds(6))));

        Assert.Equal(Source.EntityId, groundEvent.AttributedSource?.EntityId);
        Assert.Equal(standardSource.EntityId, combinedEvent.AttributedSource?.EntityId);
        Assert.Equal(900u, combinedEvent.StatusId);
    }

    [Fact]
    public void SimulatesMeterDotDamageWithoutChangingTheRawPacketTotal()
    {
        var module = new DamageParsingModule();
        module.Process(CreatePacket(
            new DamageActionEffect(0, 3, 0, 0, 0, 0, 1000)) with
        {
            DirectPotency = 100,
            CanCalibratePotency = true,
        });
        module.ObserveStatus(CreatePeriodicStatus(Source, 900, "Burn") with
        {
            SeenAtUtc = SeenAtUtc.AddMilliseconds(100),
            PeriodicPotency = 20,
            BaseDamageLowByte = 200,
            CriticalRateLowByte = 150,
        });

        var periodicEvent = Assert.Single(ProcessPeriodicTick(
            module,
            CreatePeriodicTick(600, 2, SeenAtUtc.AddSeconds(3))));
        var snapshot = Assert.IsType<DamageEncounterSnapshot>(module.GetCurrentEncounter());

        Assert.Equal(600u, periodicEvent.Amount);
        Assert.Equal(218.0, periodicEvent.EffectiveMeterAmount);
        Assert.Equal(1600ul, snapshot.TotalDamage);
        Assert.Equal(1218.0, snapshot.EffectiveMeterDamage);
        Assert.Equal(1600ul, Assert.Single(snapshot.Sources).TotalDamage);
        Assert.Equal(1218.0, Assert.Single(snapshot.Sources).EffectiveMeterDamage);
    }

    [Fact]
    public void SimulatesEachActiveDotInsteadOfForcingTheirMeterValuesToTheCombinedTick()
    {
        var module = new DamageParsingModule();
        module.Process(CreatePacket(
            new DamageActionEffect(0, 3, 0, 0, 0, 0, 1000)) with
        {
            DirectPotency = 100,
            CanCalibratePotency = true,
        });
        module.ObserveStatus(CreatePeriodicStatus(Source, 900, "First DoT") with
        {
            SeenAtUtc = SeenAtUtc.AddMilliseconds(100),
            PeriodicPotency = 25,
            BaseDamageLowByte = 250,
            CriticalRateLowByte = 150,
        });
        module.ObserveStatus(CreatePeriodicStatus(Source, 901, "Second DoT") with
        {
            SeenAtUtc = SeenAtUtc.AddMilliseconds(100),
            PeriodicPotency = 20,
            BaseDamageLowByte = 200,
            CriticalRateLowByte = 150,
        });

        var periodicEvents = ProcessPeriodicTick(
            module,
            CreatePeriodicTick(700, 2, SeenAtUtc.AddSeconds(3)));
        var snapshot = Assert.IsType<DamageEncounterSnapshot>(module.GetCurrentEncounter());

        Assert.Equal(700u, periodicEvents.Aggregate(0u, (total, entry) => total + entry.Amount));
        Assert.Equal(490.0, periodicEvents.Sum(entry => entry.EffectiveMeterAmount));
        Assert.Equal(1700ul, snapshot.TotalDamage);
        Assert.Equal(1490.0, snapshot.EffectiveMeterDamage);
    }

    [Fact]
    public void CapsAResolvedKillingHitWithoutChangingTheRawDamage()
    {
        var module = new DamageParsingModule();
        var packet = WithTargetHp(
            CreatePacket(new DamageActionEffect(0, 3, 0, 0, 0, 0, 6262)),
            905,
            0,
            10000);

        module.Process(packet);
        Assert.Equal(6262.0, Assert.IsType<DamageEncounterSnapshot>(module.GetCurrentEncounter()).EffectiveMeterDamage);

        module.ObserveEffectResult(new DamageEffectResult(
            SeenAtUtc.AddMilliseconds(100),
            packet.ActionSequence,
            Target,
            new DamageHpSnapshot(0, 0, 10000)));

        var snapshot = Assert.IsType<DamageEncounterSnapshot>(module.GetCurrentEncounter());
        Assert.Equal(6262ul, snapshot.TotalDamage);
        Assert.Equal(905.0, snapshot.EffectiveMeterDamage);
        var ended = Assert.IsType<DamageEncounterSnapshot>(module.EndEncounter(SeenAtUtc.AddSeconds(1), "test"));
        var damageEvent = Assert.Single(ended.Events);
        Assert.Equal(905.0, damageEvent.CalculatedAmount);
        Assert.Equal(5357.0, damageEvent.OverkillDamage);
        Assert.Equal(DamageResolutionQuality.Resolved, damageEvent.ResolutionQuality);
    }

    [Fact]
    public void SeparatesShieldAbsorptionFromEffectiveHpDamage()
    {
        var module = new DamageParsingModule();
        var packet = WithTargetHp(
            CreatePacket(new DamageActionEffect(0, 3, 0, 0, 0, 0, 600)),
            1000,
            500,
            1000);

        module.Process(packet);
        module.ObserveEffectResult(new DamageEffectResult(
            SeenAtUtc.AddMilliseconds(100),
            packet.ActionSequence,
            Target,
            new DamageHpSnapshot(900, 0, 1000)));

        var ended = Assert.IsType<DamageEncounterSnapshot>(module.EndEncounter(SeenAtUtc.AddSeconds(1), "test"));
        var damageEvent = Assert.Single(ended.Events);
        Assert.Equal(100.0, damageEvent.CalculatedAmount);
        Assert.Equal(500.0, damageEvent.AbsorbedDamage);
        Assert.Equal(0.0, damageEvent.OverkillDamage);
    }

    [Fact]
    public void RecognizesFullShieldAbsorptionDespitePercentRounding()
    {
        var module = new DamageParsingModule();
        var packet = WithTargetHp(
            CreatePacket(new DamageActionEffect(0, 3, 0, 0, 0, 0, 50)),
            1000,
            100,
            1000);

        module.Process(packet);
        module.ObserveEffectResult(new DamageEffectResult(
            SeenAtUtc.AddMilliseconds(100),
            packet.ActionSequence,
            Target,
            new DamageHpSnapshot(1000, 100, 1000)));

        var ended = Assert.IsType<DamageEncounterSnapshot>(module.EndEncounter(SeenAtUtc.AddSeconds(1), "test"));
        var damageEvent = Assert.Single(ended.Events);
        Assert.Equal(0.0, damageEvent.CalculatedAmount);
        Assert.Equal(50.0, damageEvent.AbsorbedDamage);
    }

    [Fact]
    public void KeepsMissingResultsOnTheExistingRawMeterPath()
    {
        var module = new DamageParsingModule();
        module.Process(WithTargetHp(
            CreatePacket(new DamageActionEffect(0, 3, 0, 0, 0, 0, 600)),
            1000,
            0,
            1000));

        var snapshot = Assert.IsType<DamageEncounterSnapshot>(module.GetCurrentEncounter());

        Assert.Equal(600.0, snapshot.EffectiveMeterDamage);
    }

    [Fact]
    public void KeepsRawDamageWhenHpDidNotDropAndAbsorptionDoesNotExplainTheHit()
    {
        var module = new DamageParsingModule();
        var packet = WithTargetHp(
            CreatePacket(new DamageActionEffect(0, 3, 0, 0, 0, 0, 600)),
            1000,
            0,
            1000);

        module.Process(packet);
        module.ObserveEffectResult(new DamageEffectResult(
            SeenAtUtc.AddMilliseconds(100),
            packet.ActionSequence,
            Target,
            new DamageHpSnapshot(1000, 0, 1000)));

        Assert.Equal(600.0, Assert.IsType<DamageEncounterSnapshot>(module.GetCurrentEncounter()).EffectiveMeterDamage);
    }

    [Fact]
    public void ResolvesAnEffectResultThatWasQueuedBeforeItsAction()
    {
        var module = new DamageParsingModule();
        var packet = WithTargetHp(
            CreatePacket(new DamageActionEffect(0, 3, 0, 0, 0, 0, 6262)),
            905,
            0,
            10000);
        module.ObserveEffectResult(new DamageEffectResult(
            SeenAtUtc.AddMilliseconds(100),
            packet.ActionSequence,
            Target,
            new DamageHpSnapshot(0, 0, 10000)));

        var damageEvent = Assert.Single(module.Process(packet));

        Assert.Equal(905.0, damageEvent.EffectiveMeterAmount);
        Assert.Equal(905.0, Assert.IsType<DamageEncounterSnapshot>(module.GetCurrentEncounter()).EffectiveMeterDamage);
    }

    [Fact]
    public void KeepsDamageAtZeroWhileATargetRemainsAliveAtZeroHp()
    {
        var module = new DamageParsingModule();
        var packet = WithTargetHp(
            CreatePacket(new DamageActionEffect(0, 3, 0, 0, 0, 0, 600)),
            0,
            0,
            1000);

        var damageEvent = Assert.Single(module.Process(packet));

        Assert.Equal(600u, damageEvent.Amount);
        Assert.Equal(0.0, damageEvent.EffectiveMeterAmount);
        Assert.Equal(DamageResolutionQuality.KnownZeroHp, damageEvent.ResolutionQuality);
        Assert.Equal(0.0, Assert.IsType<DamageEncounterSnapshot>(module.GetCurrentEncounter()).EffectiveMeterDamage);
    }

    [Fact]
    public void DoesNotTreatUnavailableZeroOverZeroHpAsADeadTarget()
    {
        var module = new DamageParsingModule();
        var damageEvent = Assert.Single(module.Process(WithTargetHp(
            CreatePacket(new DamageActionEffect(0, 3, 0, 0, 0, 0, 600)),
            0,
            0,
            0)));

        Assert.Equal(600.0, damageEvent.EffectiveMeterAmount);
        Assert.Equal(DamageResolutionQuality.Observed, damageEvent.ResolutionQuality);
    }

    [Fact]
    public void PositiveActionHpStartsANewTargetLifecycleAfterZeroHp()
    {
        var module = new DamageParsingModule();
        module.Process(WithTargetHp(
            CreatePacket(new DamageActionEffect(0, 3, 0, 0, 0, 0, 600)),
            0,
            0,
            1000));
        var resetPacket = WithTargetHp(
            CreatePacket(
                [new DamageActionEffect(0, 3, 0, 0, 0, 0, 50)],
                packetSequence: 2,
                seenAtUtc: SeenAtUtc.AddSeconds(1)),
            100,
            0,
            1000) with
        {
            ActionSequence = 701,
        };

        module.Process(resetPacket);
        module.ObserveEffectResult(new DamageEffectResult(
            SeenAtUtc.AddSeconds(1.1),
            resetPacket.ActionSequence,
            Target,
            new DamageHpSnapshot(50, 0, 1000)));

        Assert.Equal(50.0, Assert.IsType<DamageEncounterSnapshot>(module.GetCurrentEncounter()).EffectiveMeterDamage);
    }

    [Fact]
    public void ResolvesAoeTargetsIndependentlyWhenTheyShareAnActionSequence()
    {
        var secondTarget = Target with { EntityId = 0x2002, Name = "Second target" };
        var packet = new DamageActionPacket(
            1,
            SeenAtUtc,
            700,
            Source,
            100,
            "AoE",
            [
                new DamageActionTarget(0, Target, [new DamageActionEffect(0, 3, 0, 0, 0, 0, 800)])
                {
                    TargetHp = new DamageHpSnapshot(1000, 0, 1000),
                },
                new DamageActionTarget(1, secondTarget, [new DamageActionEffect(0, 3, 0, 0, 0, 0, 800)])
                {
                    TargetHp = new DamageHpSnapshot(1000, 0, 1000),
                },
            ]);
        var module = new DamageParsingModule();

        module.Process(packet);
        module.ObserveEffectResult(new DamageEffectResult(
            SeenAtUtc.AddMilliseconds(100),
            packet.ActionSequence,
            Target,
            new DamageHpSnapshot(900, 0, 1000)));
        module.ObserveEffectResult(new DamageEffectResult(
            SeenAtUtc.AddMilliseconds(110),
            packet.ActionSequence,
            secondTarget,
            new DamageHpSnapshot(500, 0, 1000)));

        Assert.Equal(600.0, Assert.IsType<DamageEncounterSnapshot>(module.GetCurrentEncounter()).EffectiveMeterDamage);
    }

    [Fact]
    public void ScalesSimulatedDotDamageOnlyWhenHpProvesOverkill()
    {
        var module = new DamageParsingModule();
        module.Process(CreatePacket(
            new DamageActionEffect(0, 3, 0, 0, 0, 0, 1000)) with
        {
            DirectPotency = 100,
            CanCalibratePotency = true,
        });
        module.ObserveStatus(CreatePeriodicStatus(Source, 900, "Burn") with
        {
            SeenAtUtc = SeenAtUtc.AddMilliseconds(100),
            PeriodicPotency = 20,
            BaseDamageLowByte = 200,
            CriticalRateLowByte = 150,
        });
        module.ObserveEffectResult(new DamageEffectResult(
            SeenAtUtc.AddSeconds(2),
            999,
            Target,
            new DamageHpSnapshot(100, 0, 1000)));
        var tick = CreatePeriodicTick(600, 2, SeenAtUtc.AddSeconds(3)) with
        {
            TargetHp = new DamageHpSnapshot(0, 0, 1000),
        };

        var periodicEvent = Assert.Single(ProcessPeriodicTick(module, tick));

        Assert.Equal(600u, periodicEvent.Amount);
        Assert.Equal(218.0 / 6.0, periodicEvent.EffectiveMeterAmount, 6);
        Assert.Equal(500.0, periodicEvent.OverkillDamage);
        Assert.Equal(DamageResolutionQuality.Resolved, periodicEvent.ResolutionQuality);
    }

    [Fact]
    public void SuppressesPeriodicDamageWhileAKnownTargetRemainsAtZeroHp()
    {
        var module = new DamageParsingModule();
        module.ObserveEffectResult(new DamageEffectResult(
            SeenAtUtc,
            999,
            Target,
            new DamageHpSnapshot(0, 0, 1000)));

        var periodicEvent = Assert.Single(ProcessPeriodicTick(
            module,
            CreatePeriodicTick(600, 2, SeenAtUtc.AddSeconds(3))));

        Assert.Equal(600u, periodicEvent.Amount);
        Assert.Equal(0.0, periodicEvent.EffectiveMeterAmount);
        Assert.Equal(DamageResolutionQuality.KnownZeroHp, periodicEvent.ResolutionQuality);
    }

    [Fact]
    public void PacketConfirmedGroundDamageRemainsSeparatedAcrossEncounters()
    {
        const uint futureGroundStatusId = 0x1234;
        var module = new DamageParsingModule();
        var directTick = CreatePeriodicTick(450) with
        {
            StatusId = futureGroundStatusId,
            StatusName = "Future ground effect",
            Source = Source,
        };
        Assert.Single(ProcessPeriodicTick(module, directTick));
        Assert.NotNull(module.EndEncounter(SeenAtUtc.AddSeconds(4), "Combat ended"));

        var standardSource = Source with { EntityId = 0x1002, Name = "Standard source" };
        module.ObserveStatus(CreatePeriodicStatus(Source, futureGroundStatusId, "Future ground effect") with
        {
            SeenAtUtc = SeenAtUtc.AddSeconds(5),
        });
        module.ObserveStatus(CreatePeriodicStatus(standardSource, 900, "Burn") with
        {
            SeenAtUtc = SeenAtUtc.AddSeconds(5),
        });
        var combinedEvent = Assert.Single(ProcessPeriodicTick(
            module,
            CreatePeriodicTick(300, 2, SeenAtUtc.AddSeconds(8))));

        Assert.Equal(standardSource.EntityId, combinedEvent.AttributedSource?.EntityId);
    }

    [Fact]
    public void RemovedPeriodicStatusDoesNotReceiveLaterTicks()
    {
        var module = new DamageParsingModule();
        var status = CreatePeriodicStatus(Source, 900, "Burn");
        module.ObserveStatus(status);
        module.ObserveStatus(status with { IsRemoval = true, SeenAtUtc = SeenAtUtc.AddSeconds(1) });

        var damageEvent = Assert.Single(ProcessPeriodicTick(module, CreatePeriodicTick(500)));

        Assert.Equal(DamageAttributionQuality.Unattributed, damageEvent.AttributionQuality);
    }

    [Fact]
    public void AttributesOnlyOneDelayedFinalTickAfterNominalExpiry()
    {
        var module = new DamageParsingModule();
        module.ObserveStatus(CreatePeriodicStatus(Source, 900, "Burn") with
        {
            DurationSeconds = 3,
        });

        var finalTick = Assert.Single(ProcessPeriodicTick(
            module,
            CreatePeriodicTick(500, 1, SeenAtUtc.AddSeconds(4.5))));
        var laterTick = Assert.Single(ProcessPeriodicTick(
            module,
            CreatePeriodicTick(500, 2, SeenAtUtc.AddSeconds(7.5))));

        Assert.Equal(Source, finalTick.AttributedSource);
        Assert.Equal(DamageAttributionQuality.Exact, finalTick.AttributionQuality);
        Assert.Equal(DamageAttributionQuality.Unattributed, laterTick.AttributionQuality);
    }

    [Fact]
    public void StatusRefreshExtendsUnknownDurationTracking()
    {
        var module = new DamageParsingModule();
        module.ObserveStatus(CreatePeriodicStatus(Source, 900, "Burn") with { DurationSeconds = 0 });
        module.RefreshStatus(Target.EntityId, 900, SeenAtUtc.AddSeconds(55));

        var damageEvent = Assert.Single(ProcessPeriodicTick(
            module,
            CreatePeriodicTick(500, 2, SeenAtUtc.AddSeconds(90))));

        Assert.Equal(Source, damageEvent.AttributedSource);
        Assert.Equal(DamageAttributionQuality.Exact, damageEvent.AttributionQuality);
    }

    [Fact]
    public void ExactStatusOwnerReplacesSourceLessObservation()
    {
        var module = new DamageParsingModule();
        var unknown = CreatePeriodicStatus(
            new DamageActorIdentity(0, "Unknown actor", 0, string.Empty, false, 0),
            900,
            "Burn");
        module.ObserveStatus(unknown);
        module.ObserveStatus(CreatePeriodicStatus(Source, 900, "Burn") with
        {
            SeenAtUtc = SeenAtUtc.AddMilliseconds(100),
        });

        var damageEvent = Assert.Single(ProcessPeriodicTick(module, CreatePeriodicTick(500)));

        Assert.Equal(Source, damageEvent.AttributedSource);
        Assert.Equal(DamageAttributionQuality.Exact, damageEvent.AttributionQuality);
    }

    [Fact]
    public void SourceLessRefreshDoesNotDuplicateKnownStatusOwner()
    {
        var module = new DamageParsingModule();
        module.ObserveStatus(CreatePeriodicStatus(Source, 900, "Burn"));
        module.ObserveStatus(CreatePeriodicStatus(
            new DamageActorIdentity(0, "Unknown actor", 0, string.Empty, false, 0),
            900,
            "Burn") with
        { SeenAtUtc = SeenAtUtc.AddSeconds(1) });

        var damageEvent = Assert.Single(ProcessPeriodicTick(module, CreatePeriodicTick(500)));

        Assert.Equal(Source, damageEvent.AttributedSource);
        Assert.Equal(DamageAttributionQuality.Exact, damageEvent.AttributionQuality);
    }

    [Fact]
    public void AttributesReactiveSourceEntriesToTheStatusOwner()
    {
        var module = new DamageParsingModule();
        var defender = Target;
        module.ObserveStatus(new DamageStatusApplication(
            defender,
            defender,
            903,
            "Counter",
            123,
            0,
            string.Empty,
            SeenAtUtc,
            15,
            false,
            true,
            false));
        var packet = CreatePacket(new DamageActionEffect(0, 3, 0, 0, 0, 0x80, 250)) with
        {
            ActionCategoryId = 1,
            IsAutoAttack = true,
        };

        var damageEvent = Assert.Single(module.Process(packet));

        Assert.Equal(defender, damageEvent.AttributedSource);
        Assert.Equal(Source, damageEvent.Target);
        Assert.Equal("Counter", damageEvent.ActionName);
        Assert.Equal(0u, damageEvent.ActionCategoryId);
        Assert.False(damageEvent.IsAutoAttack);
        Assert.Equal(DamageAttributionQuality.Exact, damageEvent.AttributionQuality);
    }

    [Fact]
    public void DoesNotCreditUnresolvedSourceEntriesToTheAttacker()
    {
        var module = new DamageParsingModule();

        var damageEvent = Assert.Single(module.Process(CreatePacket(
            new DamageActionEffect(0, 3, 0, 0, 0, 0x80, 250))));

        Assert.Equal("Unattributed incoming", damageEvent.AttributedSource?.Name);
        Assert.Equal(DamageAttributionQuality.Unattributed, damageEvent.AttributionQuality);
    }

    [Theory]
    [InlineData(0x0059)]
    [InlineData(0x00C5)]
    [InlineData(0x00C6)]
    [InlineData(0x0156)]
    [InlineData(0x01DD)]
    [InlineData(0x01DE)]
    [InlineData(0x06B8)]
    [InlineData(0x06BC)]
    [InlineData(0x08D7)]
    [InlineData(0x0E2F)]
    [InlineData(0x0EF8)]
    public void RecognizesConfirmedReactiveDamageStatuses(uint statusId)
    {
        Assert.True(ReactiveDamageStatusPolicy.IsKnown(statusId));
    }

    [Fact]
    public void DoesNotGuessReactiveDamageFromAnUnknownStatus()
    {
        Assert.False(ReactiveDamageStatusPolicy.IsKnown(0xFFFF));
    }

    [Theory]
    [InlineData(0x01F5)]
    [InlineData(0x02ED)]
    [InlineData(0x035D)]
    [InlineData(0x04B5)]
    [InlineData(0x09C6)]
    [InlineData(0x0A92)]
    [InlineData(0x0E3C)]
    public void RecognizesConfirmedGroundDamageStatuses(uint statusId)
    {
        Assert.True(GroundDamageStatusPolicy.IsKnown(statusId));
    }

    private static DamageStatusApplication CreatePeriodicStatus(
        DamageActorIdentity source,
        uint statusId,
        string statusName)
    {
        return new DamageStatusApplication(
            Target,
            source,
            statusId,
            statusName,
            0,
            0,
            string.Empty,
            SeenAtUtc,
            30,
            true,
            false,
            false);
    }

    private static PeriodicDamageTick CreatePeriodicTick(
        uint amount,
        long sequence = 1,
        DateTime? seenAtUtc = null)
    {
        return new PeriodicDamageTick(
            sequence,
            seenAtUtc ?? SeenAtUtc.AddSeconds(3),
            Target,
            0,
            string.Empty,
            0,
            amount,
            null);
    }

    private static IReadOnlyList<ParsedDamageEvent> ProcessPeriodicTick(
        DamageParsingModule module,
        PeriodicDamageTick tick)
    {
        var immediate = module.ProcessPeriodicTick(tick);
        return immediate.Count > 0
            ? immediate
            : module.FlushPendingPeriodicTicks(tick.SeenAtUtc.AddMilliseconds(50));
    }

    private static DamageActionPacket CreatePacket(
        params DamageActionEffect[] effects)
    {
        return CreatePacket(effects, 1, SeenAtUtc, 100, "Action", Source);
    }

    private static DamageActionPacket CreatePacket(
        DamageActionEffect[] effects,
        long packetSequence = 1,
        DateTime? seenAtUtc = null,
        uint actionId = 100,
        string actionName = "Action",
        DamageActorIdentity? source = null,
        DamageActorIdentity? sourceOwner = null)
    {
        return new DamageActionPacket(
            packetSequence,
            seenAtUtc ?? SeenAtUtc,
            700,
            source ?? Source,
            actionId,
            actionName,
            [new DamageActionTarget(0, Target, effects)])
        {
            SourceOwner = sourceOwner,
        };
    }

    private static DamageActionPacket WithTargetHp(
        DamageActionPacket packet,
        uint currentHp,
        uint shieldHp,
        uint maxHp)
    {
        return packet with
        {
            Targets = packet.Targets
                .Select(target => target with
                {
                    TargetHp = new DamageHpSnapshot(currentHp, shieldHp, maxHp),
                })
                .ToList(),
        };
    }
}
