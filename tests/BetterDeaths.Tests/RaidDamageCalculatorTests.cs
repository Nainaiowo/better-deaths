namespace BetterDeaths.DamageParsing;

public sealed class RaidDamageCalculatorTests
{
    private static readonly DateTime SeenAtUtc = new(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DamageActorIdentity Dealer = Player(0x1001, "Dealer", 34);
    private static readonly DamageActorIdentity Buffer = Player(0x1002, "Buffer", 20);
    private static readonly DamageActorIdentity SecondBuffer = Player(0x1003, "Second Buffer", 22);
    private static readonly DamageActorIdentity Target = new(0x4001, "Target", 0, string.Empty, false, 0);

    public static TheoryData<string, uint, bool, int, double, int>
        CurrentStandardPveRaidBuffEffects => new()
        {
            { "AST The Balance", 0xF2F, false, (int)RaidBuffEffectKind.DamageMultiplier, 0.06, (int)RaidBuffDamageScope.All },
            { "AST The Spear", 0xF31, false, (int)RaidBuffEffectKind.DamageMultiplier, 0.06, (int)RaidBuffDamageScope.All },
            { "AST Divination", 0x756, false, (int)RaidBuffEffectKind.DamageMultiplier, 0.06, (int)RaidBuffDamageScope.All },
            { "BRD Battle Voice", 0x8D, false, (int)RaidBuffEffectKind.DirectHitChance, 0.20, (int)RaidBuffDamageScope.All },
            { "BRD Wanderer's Minuet", 0x8A8, false, (int)RaidBuffEffectKind.CriticalChance, 0.02, (int)RaidBuffDamageScope.All },
            { "BRD Mage's Ballad", 0x8A9, false, (int)RaidBuffEffectKind.DamageMultiplier, 0.01, (int)RaidBuffDamageScope.All },
            { "BRD Army's Paeon", 0x8AA, false, (int)RaidBuffEffectKind.DirectHitChance, 0.03, (int)RaidBuffDamageScope.All },
            { "BRD Radiant Finale", 0xB94, false, (int)RaidBuffEffectKind.DamageMultiplier, 0.06, (int)RaidBuffDamageScope.All },
            { "DNC Technical Finish", 0x71E, false, (int)RaidBuffEffectKind.DamageMultiplier, 0.05, (int)RaidBuffDamageScope.All },
            { "DNC Standard Finish", 0x839, false, (int)RaidBuffEffectKind.DamageMultiplier, 0.05, (int)RaidBuffDamageScope.All },
            { "DNC Devilment critical", 0x721, false, (int)RaidBuffEffectKind.CriticalChance, 0.20, (int)RaidBuffDamageScope.All },
            { "DNC Devilment direct", 0x721, false, (int)RaidBuffEffectKind.DirectHitChance, 0.20, (int)RaidBuffDamageScope.All },
            { "DRG Battle Litany", 0x312, false, (int)RaidBuffEffectKind.CriticalChance, 0.10, (int)RaidBuffDamageScope.All },
            { "MNK Brotherhood", 0x4A1, false, (int)RaidBuffEffectKind.DamageMultiplier, 0.05, (int)RaidBuffDamageScope.All },
            { "NIN Mug", 0x27E, true, (int)RaidBuffEffectKind.DamageMultiplier, 0.05, (int)RaidBuffDamageScope.All },
            { "NIN Dokumori", 0xF09, true, (int)RaidBuffEffectKind.DamageMultiplier, 0.05, (int)RaidBuffDamageScope.All },
            { "RPR Arcane Circle", 0xA27, false, (int)RaidBuffEffectKind.DamageMultiplier, 0.03, (int)RaidBuffDamageScope.All },
            { "RDM Embolden", 0x511, false, (int)RaidBuffEffectKind.DamageMultiplier, 0.05, (int)RaidBuffDamageScope.All },
            { "SMN Searing Light", 0xA8F, false, (int)RaidBuffEffectKind.DamageMultiplier, 0.05, (int)RaidBuffDamageScope.All },
            { "PCT Starry Muse", 0xE65, false, (int)RaidBuffEffectKind.DamageMultiplier, 0.05, (int)RaidBuffDamageScope.All },
            { "SCH Chain Stratagem", 0x4C5, true, (int)RaidBuffEffectKind.CriticalChance, 0.10, (int)RaidBuffDamageScope.All },
            { "BLU Off-guard", 0x6B5, true, (int)RaidBuffEffectKind.DamageMultiplier, 0.05, (int)RaidBuffDamageScope.All },
            { "BLU Peculiar Light", 0x6B9, true, (int)RaidBuffEffectKind.DamageMultiplier, 0.05, (int)RaidBuffDamageScope.Magic },
            { "BLU Astral Attenuation", 0x849, true, (int)RaidBuffEffectKind.DamageMultiplier, 0.05, (int)RaidBuffDamageScope.Astral },
            { "BLU Umbral Attenuation", 0x84A, true, (int)RaidBuffEffectKind.DamageMultiplier, 0.05, (int)RaidBuffDamageScope.Umbral },
            { "BLU Physical Attenuation", 0x84B, true, (int)RaidBuffEffectKind.DamageMultiplier, 0.05, (int)RaidBuffDamageScope.Physical },
        };

    [Theory]
    [MemberData(nameof(CurrentStandardPveRaidBuffEffects))]
    public void PolicyRecognizesEveryCurrentStandardPveRaidBuffEffect(
        string _,
        uint statusId,
        bool isTargetStatus,
        int expectedKind,
        double expectedAmount,
        int expectedScope)
    {
        var status = Status(statusId, Buffer) with
        {
            Parameter = statusId is 0xF2F or 0xF31 or 0xB94 ? (ushort)6 :
                statusId is 0x71E or 0x839 ? (ushort)5 : (ushort)0,
        };

        var effects = RaidBuffPolicy.GetEffects(status, isTargetStatus, Dealer);

        Assert.True(RaidBuffPolicy.IsRelevantStatus(statusId));
        Assert.Contains(effects, effect =>
            effect.Kind == (RaidBuffEffectKind)expectedKind &&
            Math.Abs(effect.Amount - expectedAmount) < 0.000001 &&
            effect.DamageScope == (RaidBuffDamageScope)expectedScope);
    }

    [Fact]
    public void PercentageBuffMovesDamageToProviderAndConservesTotal()
    {
        var damageEvent = CreateEvent(105) with
        {
            SourceStatuses = [Status(0x4A1, Buffer)],
            HasSourceStatusSnapshot = true,
        };
        var result = Calculate(damageEvent);

        Assert.Equal(5.0, Received(result, Dealer), 6);
        Assert.Equal(5.0, Given(result, Buffer), 6);
        AssertConserved(105.0, result, Dealer, Buffer);
    }

    [Fact]
    public void OverlappingPercentageBuffsUseLogWeightedCredit()
    {
        var damageEvent = CreateEvent(441) with
        {
            SourceStatuses =
            [
                Status(0x4A1, Buffer),
                Status(0xA8F, SecondBuffer),
            ],
            HasSourceStatusSnapshot = true,
        };
        var result = Calculate(damageEvent);

        Assert.Equal(20.5, Given(result, Buffer), 6);
        Assert.Equal(20.5, Given(result, SecondBuffer), 6);
        AssertConserved(441.0, result, Dealer, Buffer, SecondBuffer);
    }

    [Fact]
    public void SelfProvidedBuffDoesNotMoveDamage()
    {
        var damageEvent = CreateEvent(105) with
        {
            SourceStatuses = [Status(0x4A1, Dealer)],
            HasSourceStatusSnapshot = true,
        };
        var result = Calculate(damageEvent);

        Assert.Equal(0.0, Received(result, Dealer), 6);
        Assert.Equal(0.0, Given(result, Dealer), 6);
        AssertConserved(105.0, result, Dealer);
    }

    [Fact]
    public void TargetDebuffMovesDamageToProvider()
    {
        var damageEvent = CreateEvent(105) with
        {
            TargetStatuses = [Status(0xF09, Buffer)],
            HasTargetStatusSnapshot = true,
        };
        var result = Calculate(damageEvent);

        Assert.Equal(5.0, Received(result, Dealer), 6);
        Assert.Equal(5.0, Given(result, Buffer), 6);
        AssertConserved(105.0, result, Dealer, Buffer);
    }

    [Fact]
    public void SyncedMugDebuffMovesDamageToProvider()
    {
        var damageEvent = CreateEvent(105) with
        {
            TargetStatuses = [Status(0x27E, Buffer)],
            HasTargetStatusSnapshot = true,
        };
        var result = Calculate(damageEvent);

        Assert.Equal(5.0, Received(result, Dealer), 6);
        Assert.Equal(5.0, Given(result, Buffer), 6);
        AssertConserved(105.0, result, Dealer, Buffer);
    }

    [Theory]
    [InlineData(0x6B5, 0, 0, 5)]
    [InlineData(0x6B9, 5, 7, 5)]
    [InlineData(0x6B9, 4, 7, 0)]
    [InlineData(0x84B, 1, 7, 5)]
    [InlineData(0x84B, 5, 7, 0)]
    [InlineData(0x849, 5, 1, 5)]
    [InlineData(0x849, 5, 3, 5)]
    [InlineData(0x849, 5, 5, 5)]
    [InlineData(0x849, 5, 2, 0)]
    [InlineData(0x84A, 5, 2, 5)]
    [InlineData(0x84A, 5, 4, 5)]
    [InlineData(0x84A, 5, 6, 5)]
    [InlineData(0x84A, 5, 1, 0)]
    public void BlueMageTargetBuffOnlyCreditsMatchingDamage(
        uint statusId,
        byte damageType,
        byte elementType,
        double expectedCredit)
    {
        var damageEvent = CreateEvent(105) with
        {
            DamageType = damageType,
            ElementType = elementType,
            TargetStatuses = [Status(statusId, Buffer)],
            HasTargetStatusSnapshot = true,
        };
        var result = Calculate(damageEvent);

        Assert.Equal(expectedCredit, Given(result, Buffer), 6);
        AssertConserved(105.0, result, Dealer, Buffer);
    }

    [Fact]
    public void ExpiredStatusSnapshotDoesNotMoveDamage()
    {
        var damageEvent = CreateEvent(105) with
        {
            SourceStatuses = [Status(0x4A1, Buffer) with { RemainingTime = 0.0f }],
            HasSourceStatusSnapshot = true,
        };
        var result = Calculate(damageEvent);

        Assert.Equal(0.0, Received(result, Dealer), 6);
        Assert.Equal(0.0, Given(result, Buffer), 6);
        AssertConserved(105.0, result, Dealer, Buffer);
    }

    [Fact]
    public void CriticalChanceBuffMovesOnlyItsShareOfCriticalDamage()
    {
        var damageEvent = CreateEvent(150, critical: true) with
        {
            SourceStatuses = [Status(0x312, Buffer)],
            HasSourceStatusSnapshot = true,
        };
        var result = Calculate(damageEvent);

        Assert.Equal(20.0, Given(result, Buffer), 6);
        AssertConserved(150.0, result, Dealer, Buffer);
    }

    [Fact]
    public void DirectHitChanceBuffMovesOnlyItsShareOfDirectHitDamage()
    {
        var damageEvent = CreateEvent(125, directHit: true) with
        {
            SourceStatuses = [Status(0x8D, Buffer)],
            HasSourceStatusSnapshot = true,
        };
        var result = Calculate(damageEvent);

        Assert.Equal(20.0, Given(result, Buffer), 6);
        AssertConserved(125.0, result, Dealer, Buffer);
    }

    [Fact]
    public void GuaranteedCriticalActionCreditsConvertedCriticalBuffDamage()
    {
        var damageEvent = CreateEvent(150, critical: true, actionId: 0x35) with
        {
            SourceStatuses = [Status(0x312, Buffer)],
            HasSourceStatusSnapshot = true,
        };
        var result = Calculate(damageEvent);

        Assert.True(Given(result, Buffer) > 0.0);
        AssertConserved(150.0, result, Dealer, Buffer);
    }

    [Fact]
    public void CurrentGuaranteedCriticalDirectActionCreditsConvertedBuffDamage()
    {
        var damageEvent = CreateEvent(200, critical: true, directHit: true, actionId: 0x903D) with
        {
            SourceStatuses =
            [
                Status(0x312, Buffer),
                Status(0x8D, SecondBuffer),
            ],
            HasSourceStatusSnapshot = true,
        };
        var result = Calculate(damageEvent);

        Assert.True(Given(result, Buffer) > 0.0);
        Assert.True(Given(result, SecondBuffer) > 0.0);
        Assert.True(Given(result, Buffer) > Given(result, SecondBuffer));
        AssertConserved(200.0, result, Dealer, Buffer, SecondBuffer);
    }

    [Fact]
    public void OneShotGuaranteeDoesNotApplyToAbilityUsedBeforeWeaponskill()
    {
        var damageEvent = CreateEvent(150, critical: true) with
        {
            ActionCategoryId = 4,
            SourceStatuses =
            [
                Status(0x74, Dealer),
                Status(0x312, Buffer),
            ],
            HasSourceStatusSnapshot = true,
        };
        var result = Calculate(damageEvent);

        Assert.True(Given(result, Buffer) > 0.0);
        AssertConserved(150.0, result, Dealer, Buffer);
    }

    [Fact]
    public void OneShotGuaranteeUsesConvertedBuffCreditForWeaponskill()
    {
        var damageEvent = CreateEvent(150, critical: true) with
        {
            ActionCategoryId = 3,
            SourceStatuses =
            [
                Status(0x74, Dealer),
                Status(0x312, Buffer),
            ],
            HasSourceStatusSnapshot = true,
        };
        var result = Calculate(damageEvent);

        Assert.True(Given(result, Buffer) > 0.0);
        AssertConserved(150.0, result, Dealer, Buffer);
    }

    [Fact]
    public void PeriodicDamageUsesExpectedCriticalAndDirectHitContribution()
    {
        var damageEvent = CreateEvent(1_000) with
        {
            IsPeriodic = true,
            SourceStatuses =
            [
                Status(0x312, Buffer),
                Status(0x8D, SecondBuffer),
            ],
            HasSourceStatusSnapshot = true,
        };
        var result = Calculate(damageEvent);

        Assert.True(Given(result, Buffer) > 0.0);
        Assert.True(Given(result, SecondBuffer) > 0.0);
        AssertConserved(1_000.0, result, Dealer, Buffer, SecondBuffer);
    }

    [Fact]
    public void LaterRateSamplesDoNotRetroactivelyChangeEarlierBuffCredit()
    {
        var earlyBuffedHit = CreateEvent(150, critical: true) with
        {
            SourceStatuses = [Status(0x312, Buffer)],
            HasSourceStatusSnapshot = true,
        };
        var laterUnbuffedHits = Enumerable.Range(1, 20)
            .Select(index => CreateEvent(100, actionId: (uint)(200 + index)) with
            {
                SeenAtUtc = SeenAtUtc.AddSeconds(index),
            })
            .ToArray();

        var earlyOnly = Calculate(earlyBuffedHit);
        var completed = Calculate([earlyBuffedHit, .. laterUnbuffedHits]);

        Assert.Equal(Given(earlyOnly, Buffer), Given(completed, Buffer), 6);
        AssertConserved(2_150.0, completed, Dealer, Buffer, SecondBuffer);
    }

    [Fact]
    public void ModuleUsesTrackedStatusWhenActionSnapshotIsUnavailable()
    {
        var module = new DamageParsingModule();
        module.ObserveStatus(new DamageStatusApplication(
            Dealer,
            Buffer,
            0x4A1,
            "Brotherhood",
            0,
            0,
            string.Empty,
            SeenAtUtc,
            20.0f,
            false,
            false,
            false));
        module.SetCombatActive(true, SeenAtUtc);
        module.Process(new DamageActionPacket(
            1,
            SeenAtUtc.AddSeconds(1),
            1,
            Dealer,
            100,
            "Action",
            [new DamageActionTarget(0, Target, [new DamageActionEffect(0, 3, 0, 0, 0, 0, 105)])]),
            allowAutomaticEncounterStart: false);

        var snapshot = Assert.IsType<DamageEncounterSnapshot>(module.GetCurrentEncounter(SeenAtUtc.AddSeconds(2)));
        var dealer = Assert.Single(snapshot.Sources, source => source.Source.EntityId == Dealer.EntityId);
        var buffer = Assert.Single(snapshot.Sources, source => source.Source.EntityId == Buffer.EntityId);

        Assert.Equal(100.0, dealer.RaidAdjustedDamage, 6);
        Assert.Equal(5.0, buffer.RaidAdjustedDamage, 6);
        Assert.Equal(snapshot.TotalDamage, snapshot.RaidAdjustedDamage, 6);
    }

    [Fact]
    public void TrackedApplicationSuppliesVariableBuffStrengthToStatusSnapshot()
    {
        var tracker = new RaidBuffTracker();
        tracker.Observe(new DamageStatusApplication(
            Dealer,
            Buffer,
            0x71E,
            "Technical Finish",
            0,
            0,
            string.Empty,
            SeenAtUtc,
            20.0f,
            false,
            false,
            false)
        {
            Parameter = 2,
        });
        var damageEvent = tracker.ApplyFallback(CreateEvent(102) with
        {
            SourceStatuses = [Status(0x71E, Buffer)],
            HasSourceStatusSnapshot = true,
        });
        var result = Calculate(damageEvent);

        Assert.Equal((ushort)2, Assert.Single(damageEvent.SourceStatuses).Parameter);
        Assert.Equal(2.0, Given(result, Buffer), 6);
        AssertConserved(102.0, result, Dealer, Buffer);
    }

    [Fact]
    public void AppliedStrengthOverridesAmbiguousLiveStatusParameter()
    {
        var tracker = new RaidBuffTracker();
        tracker.Observe(new DamageStatusApplication(
            Dealer,
            Buffer,
            0xB94,
            "Radiant Finale",
            0,
            0,
            string.Empty,
            SeenAtUtc,
            20.0f,
            false,
            false,
            false)
        {
            Parameter = 4,
        });
        var damageEvent = tracker.ApplyFallback(CreateEvent(104) with
        {
            SourceStatuses = [Status(0xB94, Buffer) with { Parameter = 2 }],
            HasSourceStatusSnapshot = true,
        });
        var result = Calculate(damageEvent);

        Assert.Equal((ushort)4, Assert.Single(damageEvent.SourceStatuses).Parameter);
        Assert.Equal(4.0, Given(result, Buffer), 6);
        AssertConserved(104.0, result, Dealer, Buffer);
    }

    [Theory]
    [InlineData(0xB94, 2, 102, 2)]
    [InlineData(0xB94, 4, 104, 4)]
    [InlineData(0xB94, 6, 106, 6)]
    [InlineData(0x71E, 1, 101, 1)]
    [InlineData(0x71E, 2, 102, 2)]
    [InlineData(0x71E, 3, 103, 3)]
    [InlineData(0x71E, 5, 105, 5)]
    [InlineData(0x839, 2, 102, 2)]
    [InlineData(0x839, 1, 102, 2)]
    [InlineData(0x839, 5, 105, 5)]
    public void VariableStrengthDamageBuffUsesAppliedPercentage(
        uint statusId,
        ushort parameter,
        uint damage,
        double expectedCredit)
    {
        var damageEvent = CreateEvent(damage) with
        {
            SourceStatuses = [Status(statusId, Buffer) with { Parameter = parameter }],
            HasSourceStatusSnapshot = true,
        };
        var result = Calculate(damageEvent);

        Assert.Equal(expectedCredit, Given(result, Buffer), 6);
        AssertConserved(damage, result, Dealer, Buffer);
    }

    [Fact]
    public void TrackedSourceRestoresPartyIdentityForLiveSnapshot()
    {
        var tracker = new RaidBuffTracker();
        tracker.Observe(new DamageStatusApplication(
            Dealer,
            Buffer,
            0x4A1,
            "Brotherhood",
            0,
            0,
            string.Empty,
            SeenAtUtc,
            20.0f,
            false,
            false,
            false));
        var incompleteSource = new DamageActorIdentity(
            Buffer.EntityId,
            Buffer.Name,
            0,
            string.Empty,
            true,
            0);
        var damageEvent = tracker.ApplyFallback(CreateEvent(105) with
        {
            SourceStatuses = [Status(0x4A1, incompleteSource)],
            HasSourceStatusSnapshot = true,
        });
        var result = Calculate(damageEvent);

        Assert.True(Assert.Single(damageEvent.SourceStatuses).Source.IsPartyMember);
        Assert.Equal(5.0, Given(result, Buffer), 6);
        AssertConserved(105.0, result, Dealer, Buffer);
    }

    [Theory]
    [InlineData(3, 103, 3)]
    [InlineData(6, 106, 6)]
    public void AstrologianCardUsesAppliedStrength(ushort parameter, uint damage, double expectedCredit)
    {
        var tracker = new RaidBuffTracker();
        tracker.Observe(new DamageStatusApplication(
            Dealer,
            Buffer,
            0xF2F,
            "The Balance",
            0,
            0,
            string.Empty,
            SeenAtUtc,
            15.0f,
            false,
            false,
            false)
        {
            Parameter = parameter,
        });
        var damageEvent = tracker.ApplyFallback(CreateEvent(damage) with
        {
            SourceStatuses = [Status(0xF2F, Buffer)],
            HasSourceStatusSnapshot = true,
        });
        var result = Calculate(damageEvent);

        Assert.Equal(parameter, Assert.Single(damageEvent.SourceStatuses).Parameter);
        Assert.Equal(expectedCredit, Given(result, Buffer), 6);
        AssertConserved(damage, result, Dealer, Buffer);
    }

    [Theory]
    [InlineData(20, 106, 6)]
    [InlineData(34, 106, 6)]
    [InlineData(24, 103, 3)]
    [InlineData(42, 103, 3)]
    public void BalanceUsesRecipientRoleWhenStatusHasNoStrength(
        uint classJobId,
        uint damage,
        double expectedCredit)
    {
        var recipient = Player(Dealer.EntityId, Dealer.Name, classJobId);
        var damageEvent = CreateEvent(damage) with
        {
            Source = recipient,
            AttributedSource = recipient,
            SourceStatuses = [Status(0xF2F, Buffer)],
            HasSourceStatusSnapshot = true,
        };
        var result = Calculate(damageEvent);

        Assert.Equal(expectedCredit, Given(result, Buffer), 6);
        AssertConserved(damage, result, recipient, Buffer);
    }

    [Theory]
    [InlineData(24, 106, 6)]
    [InlineData(42, 106, 6)]
    [InlineData(20, 103, 3)]
    [InlineData(37, 103, 3)]
    public void SpearUsesRecipientRoleWhenStatusHasNoStrength(
        uint classJobId,
        uint damage,
        double expectedCredit)
    {
        var recipient = Player(Dealer.EntityId, Dealer.Name, classJobId);
        var damageEvent = CreateEvent(damage) with
        {
            Source = recipient,
            AttributedSource = recipient,
            SourceStatuses = [Status(0xF31, Buffer)],
            HasSourceStatusSnapshot = true,
        };
        var result = Calculate(damageEvent);

        Assert.Equal(expectedCredit, Given(result, Buffer), 6);
        AssertConserved(damage, result, recipient, Buffer);
    }

    [Fact]
    public void DirectParserCarriesStatusSnapshotsOntoDamageEvent()
    {
        var sourceStatus = Status(0x4A1, Buffer);
        var targetStatus = Status(0xF09, SecondBuffer);
        var packet = new DamageActionPacket(
            1,
            SeenAtUtc,
            1,
            Dealer,
            100,
            "Action",
            [new DamageActionTarget(0, Target, [new DamageActionEffect(0, 3, 0, 0, 0, 0, 100)])
            {
                TargetStatuses = [targetStatus],
                HasTargetStatusSnapshot = true,
            }])
        {
            SourceStatuses = [sourceStatus],
            HasSourceStatusSnapshot = true,
        };

        var damageEvent = Assert.Single(new DirectDamageParser().Parse(packet));

        Assert.Same(sourceStatus, Assert.Single(damageEvent.SourceStatuses));
        Assert.Same(targetStatus, Assert.Single(damageEvent.TargetStatuses));
        Assert.True(damageEvent.HasSourceStatusSnapshot);
        Assert.True(damageEvent.HasTargetStatusSnapshot);
    }

    [Fact]
    public void DuplicatePeriodicObservationPreservesApplicationBuffSnapshot()
    {
        var tracker = new PeriodicDamageTracker();
        var snapshotted = new DamageStatusApplication(
            Target,
            Dealer,
            0x500,
            "Periodic effect",
            0,
            100,
            "Action",
            SeenAtUtc,
            30.0f,
            true,
            false,
            false)
        {
            DamageType = 5,
            ElementType = 1,
            SourceStatuses = [Status(0x4A1, Buffer)],
            HasSourceStatusSnapshot = true,
            TargetStatuses = [Status(0x6B5, Buffer)],
            HasTargetStatusSnapshot = true,
        };
        tracker.Observe(snapshotted);
        tracker.Observe(snapshotted with
        {
            SeenAtUtc = SeenAtUtc.AddMilliseconds(25),
            DamageType = 0,
            ElementType = 0,
            SourceStatuses = [],
            HasSourceStatusSnapshot = false,
        });

        var damageEvent = Assert.Single(tracker.Process(new PeriodicDamageTick(
            1,
            SeenAtUtc.AddSeconds(3),
            Target,
            0,
            string.Empty,
            0,
            100,
            null)));

        Assert.Equal(0x4A1u, Assert.Single(damageEvent.SourceStatuses).StatusId);
        Assert.True(damageEvent.HasSourceStatusSnapshot);
        Assert.Equal(5, damageEvent.DamageType);
        Assert.Equal(1, damageEvent.ElementType);
        Assert.Equal(0x6B5u, Assert.Single(damageEvent.TargetStatuses).StatusId);
        Assert.True(damageEvent.HasTargetStatusSnapshot);
    }

    [Fact]
    public void PeriodicTickDoesNotUseTargetDebuffAppliedAfterDotApplication()
    {
        var module = new DamageParsingModule();
        module.SetCombatActive(true, SeenAtUtc);
        module.ObserveStatus(PeriodicApplication() with
        {
            DamageType = 5,
            ElementType = 1,
            TargetStatuses = [],
            HasTargetStatusSnapshot = true,
        });
        module.ObserveStatus(new DamageStatusApplication(
            Target,
            Buffer,
            0x6B5,
            "Off-guard",
            0,
            0,
            string.Empty,
            SeenAtUtc.AddSeconds(1),
            15.0f,
            false,
            false,
            false));
        module.ProcessPeriodicTick(new PeriodicDamageTick(
            1,
            SeenAtUtc.AddSeconds(3),
            Target,
            0,
            string.Empty,
            0,
            105,
            null), allowAutomaticEncounterStart: false);

        module.FlushPendingPeriodicTicks(SeenAtUtc.AddSeconds(4), force: true);
        var snapshot = Assert.IsType<DamageEncounterSnapshot>(
            module.GetCurrentEncounter(SeenAtUtc.AddSeconds(4)));
        var dealer = Assert.Single(snapshot.Sources, source => source.Source.EntityId == Dealer.EntityId);

        Assert.Equal(105.0, dealer.RaidAdjustedDamage, 6);
        Assert.DoesNotContain(snapshot.Sources, source => source.Source.EntityId == Buffer.EntityId);
    }

    [Fact]
    public void PeriodicTickKeepsTargetDebuffFromDotApplication()
    {
        var module = new DamageParsingModule();
        module.SetCombatActive(true, SeenAtUtc);
        module.ObserveStatus(PeriodicApplication() with
        {
            DamageType = 5,
            ElementType = 1,
            TargetStatuses = [Status(0x6B5, Buffer)],
            HasTargetStatusSnapshot = true,
        });
        module.ProcessPeriodicTick(new PeriodicDamageTick(
            1,
            SeenAtUtc.AddSeconds(3),
            Target,
            0,
            string.Empty,
            0,
            105,
            null), allowAutomaticEncounterStart: false);

        module.FlushPendingPeriodicTicks(SeenAtUtc.AddSeconds(4), force: true);
        var snapshot = Assert.IsType<DamageEncounterSnapshot>(
            module.GetCurrentEncounter(SeenAtUtc.AddSeconds(4)));
        var dealer = Assert.Single(snapshot.Sources, source => source.Source.EntityId == Dealer.EntityId);
        var buffer = Assert.Single(snapshot.Sources, source => source.Source.EntityId == Buffer.EntityId);

        Assert.Equal(100.0, dealer.RaidAdjustedDamage, 6);
        Assert.Equal(5.0, buffer.RaidAdjustedDamage, 6);
    }

    private static IReadOnlyDictionary<string, RaidDamageAdjustment> Calculate(params ParsedDamageEvent[] events)
    {
        var sources = new[] { Dealer, Buffer, SecondBuffer }
            .Select(actor => SourceSummary(actor, events
                .Where(damageEvent => (damageEvent.AttributedSource ?? damageEvent.Source).EntityId == actor.EntityId)
                .Aggregate(0UL, (total, damageEvent) => total + damageEvent.Amount)))
            .ToList();
        return RaidDamageCalculator.Calculate(events, sources);
    }

    private static ParsedDamageEvent CreateEvent(
        uint amount,
        bool critical = false,
        bool directHit = false,
        uint actionId = 100)
    {
        return new ParsedDamageEvent(
            $"event:{actionId}:{amount}",
            1,
            SeenAtUtc,
            1,
            Dealer,
            Target,
            actionId,
            "Action",
            0,
            0,
            DamageEventOutcome.Damage,
            amount,
            0,
            critical,
            directHit,
            false,
            false,
            3,
            0,
            0,
            0,
            0)
        {
            AttributedSource = Dealer,
            PacketTarget = Target,
        };
    }

    private static DamageStatusSnapshot Status(uint statusId, DamageActorIdentity source)
    {
        return new DamageStatusSnapshot(statusId, source, 0, 10.0f);
    }

    private static DamageStatusApplication PeriodicApplication()
    {
        return new DamageStatusApplication(
            Target,
            Dealer,
            0x500,
            "Periodic effect",
            0,
            100,
            "Action",
            SeenAtUtc,
            30.0f,
            true,
            false,
            false);
    }

    private static DamageSourceSummary SourceSummary(DamageActorIdentity source, ulong damage)
    {
        return new DamageSourceSummary(source, damage, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, []);
    }

    private static double Received(
        IReadOnlyDictionary<string, RaidDamageAdjustment> result,
        DamageActorIdentity actor)
    {
        return result[RaidDamageCalculator.GetActorKey(actor)].ExternalBuffDamageReceived;
    }

    private static double Given(
        IReadOnlyDictionary<string, RaidDamageAdjustment> result,
        DamageActorIdentity actor)
    {
        return result[RaidDamageCalculator.GetActorKey(actor)].RaidBuffDamageGiven;
    }

    private static void AssertConserved(
        double totalDamage,
        IReadOnlyDictionary<string, RaidDamageAdjustment> result,
        params DamageActorIdentity[] actors)
    {
        var rawByActor = actors.ToDictionary(
            actor => RaidDamageCalculator.GetActorKey(actor),
            actor => actor.EntityId == Dealer.EntityId ? totalDamage : 0.0);
        var raidAdjustedTotal = actors.Sum(actor =>
        {
            var key = RaidDamageCalculator.GetActorKey(actor);
            var adjustment = result[key];
            return rawByActor[key] - adjustment.ExternalBuffDamageReceived + adjustment.RaidBuffDamageGiven;
        });
        Assert.Equal(totalDamage, raidAdjustedTotal, 6);
    }

    private static DamageActorIdentity Player(uint entityId, string name, uint classJobId)
    {
        return new DamageActorIdentity(entityId, name, 0, string.Empty, true, classJobId)
        {
            IsPartyMember = true,
        };
    }
}
