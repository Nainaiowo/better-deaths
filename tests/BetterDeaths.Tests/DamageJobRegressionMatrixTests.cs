namespace BetterDeaths.Tests;

using BetterDeaths.DamageParsing;

public sealed class DamageJobRegressionMatrixTests
{
    private static readonly DateTime SeenAtUtc = new(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DamageActorIdentity Target = new(
        0x40000001,
        "Target",
        0,
        string.Empty,
        false,
        0);

    public static TheoryData<string, uint, uint, double, bool> CurrentPeriodicDamageMatrix => new()
    {
        { "PLD Circle of Scorn", 19, 0x00F8, 30, false },
        { "DRK Salted Earth", 32, 0x02ED, 50, true },
        { "GNB Sonic Break", 37, 0x072D, 120, false },
        { "GNB Bow Shock", 37, 0x072E, 60, false },
        { "DRG Chaos Thrust", 22, 0x0076, 40, false },
        { "DRG Chaotic Spring", 22, 0x0A9F, 45, false },
        { "NIN Doton", 30, 0x01F5, 80, true },
        { "SAM Higanbana", 34, 0x04CC, 50, false },
        { "BRD Venomous Bite", 23, 0x007C, 15, false },
        { "BRD Windbite", 23, 0x0081, 20, false },
        { "BRD Caustic Bite", 23, 0x04B0, 20, false },
        { "BRD Stormbite", 23, 0x04B1, 25, false },
        { "MCH Bioblaster", 31, 0x074A, 50, false },
        { "MCH Flamethrower", 31, 0x04B5, 120, true },
        { "MCH Wildfire", 31, 0x035D, 1, true },
        { "BLM Thunder", 25, 0x00A1, 45, false },
        { "BLM Thunder II", 25, 0x00A2, 30, false },
        { "BLM Thunder III", 25, 0x00A3, 50, false },
        { "BLM Thunder IV", 25, 0x04BA, 35, false },
        { "BLM High Thunder", 25, 0x0F1F, 60, false },
        { "BLM High Thunder II", 25, 0x0F20, 40, false },
        { "SMN Slipstream", 27, 0x0A92, 30, true },
        { "WHM Aero", 24, 0x008F, 30, false },
        { "WHM Aero II", 24, 0x0090, 50, false },
        { "WHM Dia", 24, 0x074F, 85, false },
        { "SCH Bio", 28, 0x00B3, 20, false },
        { "SCH Bio II", 28, 0x00BD, 40, false },
        { "SCH Biolysis", 28, 0x0767, 85, false },
        { "SCH Baneful Impaction", 28, 0x0F2B, 140, false },
        { "AST Combust", 33, 0x0346, 50, false },
        { "AST Combust II", 33, 0x034B, 60, false },
        { "AST Combust III", 33, 0x0759, 70, false },
        { "SGE Eukrasian Dosis", 40, 0x0A36, 40, false },
        { "SGE Eukrasian Dosis II", 40, 0x0A37, 60, false },
        { "SGE Eukrasian Dosis III", 40, 0x0A38, 90, false },
        { "SGE Eukrasian Dyskrasia", 40, 0x0F39, 40, false },
        { "BLU Bad Breath", 36, 0x0012, 20, false },
        { "BLU Song of Torment", 36, 0x06B2, 50, false },
        { "BLU Nightbloom", 36, 0x06B2, 75, false },
        { "BLU Feather Rain", 36, 0x06BB, 40, false },
        { "BLU Dropsy", 36, 0x06C8, 20, false },
        { "BLU Incendiary Burns", 36, 0x09C3, 50, false },
        { "BLU Phantom Flurry", 36, 0x09C6, 200, true },
        { "BLU Begrimed", 36, 0x0E34, 10, false },
        { "BLU Mortal Flame", 36, 0x0E3B, 40, false },
        { "BLU Apokalypsis", 36, 0x0E3C, 140, true },
        { "BLU Breath of Magic", 36, 0x0E80, 120, false },
        { "Duty action Lost Flare Star", 19, 0x0988, 350, false },
    };

    public static TheoryData<string, uint, uint> CurrentPetMatrix => new()
    {
        { "DRK Living Shadow", 32, 0x3001 },
        { "MCH Automaton Queen", 31, 0x3002 },
        { "SMN Carbuncle and demi-summons", 27, 0x3003 },
        { "SCH faeries and Seraph", 28, 0x3004 },
        { "Player chocobo companion", 19, 0x3005 },
    };

    public static TheoryData<string, uint> UnconditionalGuaranteedCriticalMatrix => new()
    {
        { "NIN Assassinate", 0x08C6 },
        { "SAM Midare Setsugekka", 0x1D3F },
        { "WAR Inner Chaos", 0x4051 },
        { "WAR Chaotic Cyclone", 0x404F },
        { "SAM Kaeshi Setsugekka", 0x4066 },
        { "WAR Primal Rend", 0x6499 },
        { "DNC Starfall Dance", 0x64C0 },
        { "SAM Ogi Namikiri", 0x64B5 },
        { "SAM Kaeshi Namikiri", 0x64B6 },
        { "PCT Hammer Stamp", 0x8776 },
        { "PCT Hammer Brush", 0x8777 },
        { "PCT Polishing Hammer", 0x8778 },
        { "WAR Primal Ruination", 0x903D },
        { "SAM Tendo Setsugekka", 0x9066 },
        { "SAM Tendo Kaeshi Setsugekka", 0x9068 },
        { "MCH Full Metal Field", 0x9076 },
    };

    public static TheoryData<string, uint> OpoOpoGuaranteedCriticalMatrix => new()
    {
        { "MNK Bootshine", 0x0035 },
        { "MNK Shadow of the Destroyer", 0x64A7 },
        { "MNK Leaping Opo", 0x9051 },
    };

    public static TheoryData<string, uint> MonkFormMatrix => new()
    {
        { "Opo-opo Form", 0x006B },
        { "Perfect Balance", 0x006E },
        { "Formless Fist", 0x09D1 },
    };

    public static TheoryData<string, uint> UnconditionalGuaranteedDirectHitMatrix => new()
    {
        { "NIN Assassinate", 0x08C6 },
        { "WAR Inner Chaos", 0x4051 },
        { "WAR Chaotic Cyclone", 0x404F },
        { "WAR Primal Rend", 0x6499 },
        { "DNC Starfall Dance", 0x64C0 },
        { "PCT Hammer Stamp", 0x8776 },
        { "PCT Hammer Brush", 0x8777 },
        { "PCT Polishing Hammer", 0x8778 },
        { "WAR Primal Ruination", 0x903D },
        { "MCH Full Metal Field", 0x9076 },
    };

    [Theory]
    [MemberData(nameof(CurrentPeriodicDamageMatrix))]
    public void EveryCurrentJobDotCanBeTrackedAndSimulated(
        string _,
        uint classJobId,
        uint statusId,
        double periodicPotency,
        bool isGroundDamage)
    {
        var source = Player(0x1001, classJobId);
        var module = new DamageParsingModule();
        module.Process(DirectPacket(source));
        module.ObserveStatus(new DamageStatusApplication(
            Target,
            source,
            statusId,
            "Periodic effect",
            0,
            100,
            "Action",
            SeenAtUtc.AddMilliseconds(100),
            30.0f,
            true,
            false,
            false)
        {
            PeriodicPotency = periodicPotency,
            CriticalRateLowByte = 150,
            HasSourceStatusSnapshot = true,
        });
        if (isGroundDamage)
        {
            module.RefreshStatus(Target.EntityId, statusId, SeenAtUtc.AddSeconds(1));
        }

        var tick = new PeriodicDamageTick(
            2,
            SeenAtUtc.AddSeconds(3),
            Target,
            isGroundDamage ? statusId : 0,
            "Periodic effect",
            0,
            600,
            isGroundDamage ? source : null);
        var events = module.ProcessPeriodicTick(tick);
        if (events.Count == 0)
        {
            events = module.FlushPendingPeriodicTicks(tick.SeenAtUtc.AddMilliseconds(50));
        }

        var damageEvent = Assert.Single(events);
        Assert.Equal(statusId, damageEvent.StatusId);
        Assert.Equal(source.EntityId, damageEvent.AttributedSource?.EntityId);
        Assert.True(damageEvent.EffectiveMeterAmount > 0.0);
    }

    [Theory]
    [MemberData(nameof(CurrentPetMatrix))]
    public void EveryCurrentOwnedCombatPetCreditsItsOwner(string _, uint ownerClassJobId, uint petEntityId)
    {
        var owner = Player(0x1001, ownerClassJobId);
        var pet = new DamageActorIdentity(
            petEntityId,
            "Pet",
            owner.EntityId,
            owner.Name,
            false,
            0)
        {
            IsPet = true,
        };
        var module = new DamageParsingModule();

        var damageEvent = Assert.Single(module.Process(new DamageActionPacket(
            1,
            SeenAtUtc,
            1,
            pet,
            100,
            "Pet action",
            [new DamageActionTarget(0, Target, [new DamageActionEffect(0, 3, 0, 0, 0, 0, 1_000)])])
        {
            SourceOwner = owner,
        }));
        var encounter = Assert.IsType<DamageEncounterSnapshot>(module.GetCurrentEncounter());

        Assert.Equal(owner.EntityId, damageEvent.AttributedSource?.EntityId);
        Assert.Equal(owner.EntityId, Assert.Single(encounter.Sources).Source.EntityId);
    }

    [Theory]
    [MemberData(nameof(UnconditionalGuaranteedCriticalMatrix))]
    public void EveryUnconditionalGuaranteedCriticalActionIsRecognized(string _, uint actionId)
    {
        Assert.True(RaidBuffPolicy.IsGuaranteedCritical(Event(actionId, [])));
    }

    [Theory]
    [MemberData(nameof(UnconditionalGuaranteedDirectHitMatrix))]
    public void EveryUnconditionalGuaranteedDirectHitActionIsRecognized(string _, uint actionId)
    {
        Assert.True(RaidBuffPolicy.IsGuaranteedDirectHit(Event(actionId, [])));
    }

    [Theory]
    [InlineData("DRG Life Surge", 0x0074, true, false)]
    [InlineData("MCH Reassemble", 0x0353, true, true)]
    [InlineData("WAR Berserk", 0x0056, true, true)]
    public void EveryOneShotGuaranteedStatusIsRecognized(
        string _,
        uint statusId,
        bool critical,
        bool directHit)
    {
        var damageEvent = Event(
            100,
            [new DamageStatusSnapshot(statusId, Player(0x1001, 21), 0, 10.0f)]);

        Assert.Equal(critical, RaidBuffPolicy.IsGuaranteedCritical(damageEvent));
        Assert.Equal(directHit, RaidBuffPolicy.IsGuaranteedDirectHit(damageEvent));
    }

    [Theory]
    [InlineData(0x0DDD)]
    [InlineData(0x0DDE)]
    public void InnerReleaseGuaranteesItsCurrentActions(uint actionId)
    {
        var damageEvent = Event(
            actionId,
            [new DamageStatusSnapshot(0x0499, Player(0x1001, 21), 0, 10.0f)]);

        Assert.True(RaidBuffPolicy.IsGuaranteedCritical(damageEvent));
        Assert.True(RaidBuffPolicy.IsGuaranteedDirectHit(damageEvent));
    }

    [Fact]
    public void EveryMonkOpoOpoCriticalRequiresAndAcceptsEveryValidFormState()
    {
        uint[] actionIds = [0x0035, 0x64A7, 0x9051];
        uint[] formStatusIds = [0x006B, 0x006E, 0x09D1];
        foreach (var actionId in actionIds)
        {
            Assert.False(RaidBuffPolicy.IsGuaranteedCritical(Event(actionId, [])));
            foreach (var formStatusId in formStatusIds)
            {
                Assert.True(RaidBuffPolicy.IsGuaranteedCritical(Event(
                    actionId,
                    [new DamageStatusSnapshot(formStatusId, Player(0x1001, 20), 0, 10.0f)])));
            }

            Assert.False(RaidBuffPolicy.IsGuaranteedCritical(Event(
                actionId,
                [new DamageStatusSnapshot(0x006B, Player(0x1001, 20), 0, 0.0f)])));
        }
    }

    [Theory]
    [InlineData(0x0DE8, true)]
    [InlineData(0x1D3B, false)]
    public void CurrentDotRefreshActionsAreRecognized(uint actionId, bool expected)
    {
        Assert.Equal(expected, PeriodicDamageRefreshPolicy.IsExplicitResnapshot(actionId));
    }

    [Theory]
    [InlineData(0x008D)]
    [InlineData(0x008E)]
    [InlineData(0x0092)]
    [InlineData(0x0093)]
    [InlineData(0x0098)]
    [InlineData(0x009A)]
    [InlineData(0x009F)]
    [InlineData(0x00A2)]
    [InlineData(0x0DF8)]
    [InlineData(0x0DF9)]
    [InlineData(0x4079)]
    [InlineData(0x64C2)]
    [InlineData(0x64C3)]
    [InlineData(0x907D)]
    public void BlackMageElementalActionsDoNotCalibrateTheSharedPotencyBaseline(uint actionId)
    {
        Assert.Null(JobDamageCalibrationPolicy.GetCalibrationPotency(
            Event(actionId, [], classJobId: 25) with
            {
                DirectPotency = 300,
                CanCalibratePotency = true,
            }));
    }

    [Fact]
    public void BlackMageNeutralActionCanCalibrateTheSharedPotencyBaseline()
    {
        Assert.Equal(890.0, JobDamageCalibrationPolicy.GetCalibrationPotency(
            Event(0x407B, [], classJobId: 25) with
            {
                DirectPotency = 890,
                CanCalibratePotency = true,
            }));
    }

    [Theory]
    [InlineData(0x0B32)]
    [InlineData(0x0B34)]
    [InlineData(0x0B38)]
    [InlineData(0x0B3A)]
    [InlineData(0x0B39)]
    [InlineData(0x1CF2)]
    [InlineData(0x1CF3)]
    [InlineData(0x4072)]
    [InlineData(0x1CF4)]
    [InlineData(0x1CF5)]
    [InlineData(0x9072)]
    [InlineData(0x4074)]
    public void EveryMachinistOverheatedActionAddsItsPotencyBeforeCalibration(uint actionId)
    {
        var source = Player(0x1001, 31);
        var damageEvent = Event(
            actionId,
            [new DamageStatusSnapshot(
                JobDamageCalibrationPolicy.MachinistOverheatedStatusId,
                source,
                0,
                5.0f)],
            classJobId: 31) with
        {
            DirectPotency = 200,
            CanCalibratePotency = true,
        };

        Assert.True(JobDamageCalibrationPolicy.IsRelevantStatus(
            JobDamageCalibrationPolicy.MachinistOverheatedStatusId));
        Assert.Equal(220.0, JobDamageCalibrationPolicy.GetCalibrationPotency(damageEvent));
    }

    [Fact]
    public void ExpiredOverheatedDoesNotChangeCalibrationPotency()
    {
        var source = Player(0x1001, 31);
        var damageEvent = Event(
            0x1CF2,
            [new DamageStatusSnapshot(
                JobDamageCalibrationPolicy.MachinistOverheatedStatusId,
                source,
                0,
                0.0f)],
            classJobId: 31) with
        {
            DirectPotency = 200,
            CanCalibratePotency = true,
        };

        Assert.Equal(200.0, JobDamageCalibrationPolicy.GetCalibrationPotency(damageEvent));
    }

    [Theory]
    [InlineData(0x0748)] // Lance Charge: personal modifier
    [InlineData(0x0A80)] // Overheated: calibration state
    [InlineData(0x006B)] // Opo-opo Form: guaranteed-hit state
    [InlineData(0x0312)] // Battle Litany: raid buff
    public void ReducedDamageSnapshotsKeepEveryRequiredStatusCategory(uint statusId)
    {
        Assert.True(DamageStatusCapturePolicy.IsRelevant(statusId));
    }

    [Fact]
    public void TrackedMonkFormStateSurvivesAMissingActionSnapshot()
    {
        var monk = Player(0x1001, 20);
        var module = new DamageParsingModule();
        module.ObserveStatus(new DamageStatusApplication(
            monk,
            monk,
            0x006B,
            "Opo-opo Form",
            0,
            0,
            string.Empty,
            SeenAtUtc,
            30.0f,
            false,
            false,
            false));
        var damageEvent = Assert.Single(module.Process(new DamageActionPacket(
            1,
            SeenAtUtc.AddSeconds(1),
            1,
            monk,
            0x0035,
            "Bootshine",
            [new DamageActionTarget(0, Target, [new DamageActionEffect(0, 3, 0, 0, 0, 0, 1_000)])])));

        Assert.True(RaidBuffPolicy.IsGuaranteedCritical(damageEvent));
    }

    private static DamageActionPacket DirectPacket(DamageActorIdentity source)
    {
        return new DamageActionPacket(
            1,
            SeenAtUtc,
            1,
            source,
            0x407B,
            "Calibration",
            [new DamageActionTarget(0, Target, [new DamageActionEffect(0, 3, 0, 0, 0, 0, 1_000)])])
        {
            DirectPotency = 100,
            CanCalibratePotency = true,
            HasSourceStatusSnapshot = true,
        };
    }

    private static ParsedDamageEvent Event(
        uint actionId,
        IReadOnlyList<DamageStatusSnapshot> statuses,
        uint classJobId = 20)
    {
        var source = Player(0x1001, classJobId);
        return new ParsedDamageEvent(
            $"event:{actionId}",
            1,
            SeenAtUtc,
            1,
            source,
            Target,
            actionId,
            "Action",
            0,
            0,
            DamageEventOutcome.Damage,
            1_000,
            3,
            true,
            false,
            false,
            false,
            3,
            0,
            0,
            0,
            0)
        {
            AttributedSource = source,
            ActionCategoryId = 3,
            SourceStatuses = statuses,
            HasSourceStatusSnapshot = true,
        };
    }

    private static DamageActorIdentity Player(uint entityId, uint classJobId)
    {
        return new DamageActorIdentity(
            entityId,
            $"Player {entityId:X}",
            0,
            string.Empty,
            true,
            classJobId)
        {
            IsPartyMember = true,
        };
    }
}
