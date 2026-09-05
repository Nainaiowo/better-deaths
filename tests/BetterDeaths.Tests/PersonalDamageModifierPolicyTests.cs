namespace BetterDeaths.Tests;

using BetterDeaths.DamageParsing;
using System;
using System.Collections.Generic;
using Xunit;

public sealed class PersonalDamageModifierPolicyTests
{
    private static readonly DateTime SeenAtUtc = new(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DamageActorIdentity Dealer = new(
        0x1001,
        "Dealer",
        0,
        string.Empty,
        true,
        22)
    {
        IsPartyMember = true,
    };

    private static readonly DamageActorIdentity Target = new(
        0x40000001,
        "Target",
        0,
        string.Empty,
        false,
        0);

    private static readonly DamageActorIdentity ReferenceDealer = Dealer with
    {
        EntityId = 0x1002,
        Name = "Reference dealer",
    };

    public static TheoryData<uint, double> UniversalModifiers => new()
    {
        { 0x7D, 0.15 },
        { 0x748, 0.10 },
        { 0x77A, 0.10 },
        { 0xAA0, 0.10 },
        { 0x5AD, 0.10 },
        { 0x776, 0.10 },
        { 0xF04, 0.15 },
        { 0x727, 0.20 },
        { 0x4C, 0.25 },
        { 0x512, 0.13 },
        { 0x6B6, 0.50 },
        { 0x6B7, -0.40 },
        { 0x9C2, 1.00 },
    };

    [Theory]
    [MemberData(nameof(UniversalModifiers))]
    public void ResolvesUniversalModifier(uint statusId, double expectedAmount)
    {
        Assert.True(PersonalDamageModifierPolicy.IsRelevantStatus(statusId));
        var effect = Assert.Single(PersonalDamageModifierPolicy.GetEffects(
            Status(statusId),
            actionCategoryId: 3,
            damageType: 3));

        Assert.Equal(RaidBuffEffectKind.DamageMultiplier, effect.Kind);
        Assert.Equal(expectedAmount, effect.Amount, 6);
    }

    [Fact]
    public void AppliesRestrictedBlueMageModifiersOnlyToMatchingSpells()
    {
        Assert.Equal(0.50, Assert.Single(PersonalDamageModifierPolicy.GetEffects(
            Status(0x6B4),
            actionCategoryId: 2,
            damageType: 5)).Amount, 6);
        Assert.Empty(PersonalDamageModifierPolicy.GetEffects(
            Status(0x6B4),
            actionCategoryId: 3,
            damageType: 3));

        Assert.Equal(0.80, Assert.Single(PersonalDamageModifierPolicy.GetEffects(
            Status(0x846),
            actionCategoryId: 2,
            damageType: 3)).Amount, 6);
        Assert.Empty(PersonalDamageModifierPolicy.GetEffects(
            Status(0x846),
            actionCategoryId: 2,
            damageType: 5));
    }

    [Theory]
    [InlineData(0x31u)]
    [InlineData(0x2Bu)]
    [InlineData(0x2Cu)]
    public void AttributeModifiersAreCapturedButNotTreatedAsFlatDamage(uint id)
    {
        Assert.True(PersonalDamageModifierPolicy.IsRelevantStatus(id));
        Assert.True(PersonalDamageModifierPolicy.ChangesAttributes(id));
        Assert.Empty(PersonalDamageModifierPolicy.GetEffects(Status(id), 3, 3));
        var module = new DamageParsingModule();
        module.Process(DirectPacket(1_150, [Status(id)]));
        module.ObserveStatus(PeriodicApplication([]));
        var tick = Assert.Single(ProcessPeriodicTick(module));
        Assert.Null(tick.SimulatedPeriodicAmount);
        Assert.Equal("Missing application-time calibration", tick.PeriodicEstimateUnavailableReason);
        Assert.Equal(600u, tick.Amount);
    }

    [Fact]
    public void LateCalibrationDoesNotHideMissingApplicationTimeEstimate()
    {
        var module = new DamageParsingModule();
        module.ObserveStatus(PeriodicApplication([]));
        module.Process(DirectPacket(1_000, []) with { SeenAtUtc = SeenAtUtc.AddSeconds(1) });
        var tick = Assert.Single(ProcessPeriodicTick(module));
        Assert.Null(tick.SimulatedPeriodicAmount);
        Assert.Equal("Missing application-time calibration", tick.PeriodicEstimateUnavailableReason);
        var restored = System.Text.Json.JsonSerializer.Deserialize<ParsedDamageEvent>(
            System.Text.Json.JsonSerializer.Serialize(tick))!;
        Assert.Equal(tick.PeriodicEstimateUnavailableReason, restored.PeriodicEstimateUnavailableReason);
        Assert.Null(restored.SimulatedPeriodicAmount);
    }

    [Theory]
    [InlineData(1000u, (byte)0)]
    [InlineData(1500u, (byte)0x20)]
    [InlineData(1250u, (byte)0x40)]
    [InlineData(1875u, (byte)0x60)]
    public void CriticalAndDirectHitsCanCalibrateWithoutChangingCapturedDamage(uint amount, byte flags)
    {
        var module = new DamageParsingModule();
        var packet = DirectPacket(amount, []) with
        {
            SourceBaseRates = new DamageBaseRateSnapshot(0.15, 0.05),
            Targets = [new DamageActionTarget(0, Target, [new DamageActionEffect(0, 3, flags, 0, 0, 0, amount)])],
        };
        var direct = Assert.Single(module.Process(packet));
        module.ObserveStatus(PeriodicApplication([]));
        var tick = Assert.Single(ProcessPeriodicTick(module));
        Assert.Equal(amount, direct.Amount);
        Assert.Equal(217.6875, tick.SimulatedPeriodicAmount!.Value, 6);
        Assert.Equal(10, tick.PeriodicEstimateInputs!.DamagePerPotency);
        Assert.Equal(200, tick.PeriodicEstimateInputs.BaseDamage);
    }

    [Fact]
    public void CalibrationRemovesPersonalDamageAndKnownHitBonusesTogether()
    {
        var module = new DamageParsingModule();
        var packet = DirectPacket(1980, [Status(0x748)]) with
        {
            SourceBaseRates = new DamageBaseRateSnapshot(0.25, 0.05),
            Targets = [new DamageActionTarget(0, Target, [new DamageActionEffect(0, 3, 0x60, 0, 0, 0, 1980)])],
        };
        var direct = Assert.Single(module.Process(packet));
        module.ObserveStatus(PeriodicApplication([]));
        var tick = Assert.Single(ProcessPeriodicTick(module));
        Assert.Equal(1980u, direct.Amount);
        Assert.Equal(9.0, tick.PeriodicEstimateInputs!.DamagePerPotency, 6);
        Assert.Equal(180.0, tick.PeriodicEstimateInputs.BaseDamage);
    }

    [Fact]
    public void GuaranteedHitsWithChanceBuffsDoNotContaminateCalibration()
    {
        var module = new DamageParsingModule();
        var packet = DirectPacket(3000, [Status(0x312)]) with
        {
            ActionId = 0x4051, // Inner Chaos under Battle Litany.
            Targets = [new DamageActionTarget(0, Target, [new DamageActionEffect(0, 3, 0x60, 0, 0, 0, 3000)])],
        };
        module.Process(packet);
        module.ObserveStatus(PeriodicApplication([]));
        var tick = Assert.Single(ProcessPeriodicTick(module));
        Assert.Null(tick.SimulatedPeriodicAmount);
    }

    [Fact]
    public void IndependentEstimateIsSeparateFromObservedAllocationAndFrozenAtApplication()
    {
        var module = new DamageParsingModule();
        module.Process(DirectPacket(1_000, []));
        module.ObserveStatus(PeriodicApplication([]));
        module.Process(DirectPacket(9_000, [], packetSequence: 3) with { SeenAtUtc = SeenAtUtc.AddSeconds(1) });
        var tick = Assert.Single(ProcessPeriodicTick(module));
        // 20 potency * 10 damage/potency, 15% crit with 1.5 multiplier, 5% DH.
        Assert.Equal(217.6875, tick.SimulatedPeriodicAmount!.Value, 6);
        Assert.Equal(600u, tick.Amount);
        Assert.Equal(600, tick.EffectiveMeterAmount);
        var restored = System.Text.Json.JsonSerializer.Deserialize<ParsedDamageEvent>(
            System.Text.Json.JsonSerializer.Serialize(tick))!;
        Assert.Equal(tick.SimulatedPeriodicAmount, restored.SimulatedPeriodicAmount);
        Assert.Equal(tick.PeriodicEstimateInputs, restored.PeriodicEstimateInputs);
        Assert.Equal(tick.SimulatedPeriodicAmount, restored.PeriodicEstimateInputs!.ExpectedAmount);
        Assert.Equal(tick.EffectiveMeterAmount, restored.EffectiveMeterAmount);
    }

    [Fact]
    public void AttributeChangedApplicationDoesNotReuseUnbuffedIndependentEstimate()
    {
        var module = new DamageParsingModule();
        module.Process(DirectPacket(1_000, []));
        module.ObserveStatus(PeriodicApplication([Status(0x31)]));
        var tick = Assert.Single(ProcessPeriodicTick(module));
        Assert.Null(tick.SimulatedPeriodicAmount);
        Assert.Equal("Attribute-changing status", tick.PeriodicEstimateUnavailableReason);
    }

    [Fact]
    public void PersonalModifierIsRemovedFromCalibrationAndReappliedAtDotSnapshot()
    {
        var module = new DamageParsingModule();
        var lanceCharge = Status(0x748);
        module.Process(DirectPacket(1_100, [lanceCharge]));
        module.Process(DirectPacket(1_000, [], ReferenceDealer, 2));
        module.ObserveStatus(PeriodicApplication([lanceCharge]));
        module.ObserveStatus(PeriodicApplication([], ReferenceDealer, 0x501));

        var damageEvents = ProcessPeriodicTick(module);
        var damageEvent = damageEvents.Single(entry =>
            entry.AttributedSource?.EntityId == Dealer.EntityId);

        Assert.Equal(314.0, damageEvent.EffectiveMeterAmount);
        Assert.Equal(600.0, damageEvents.Sum(entry => entry.EffectiveMeterAmount));
        Assert.Equal(3u, damageEvent.ActionCategoryId);
    }

    [Fact]
    public void PersonalSnapshotModifiersStackMultiplicatively()
    {
        var module = new DamageParsingModule();
        module.Process(DirectPacket(1_000, []));
        module.Process(DirectPacket(1_000, [], ReferenceDealer, 2));
        module.ObserveStatus(PeriodicApplication([
            Status(0x748),
            Status(0x77A),
            Status(0xF04),
        ]));
        module.ObserveStatus(PeriodicApplication([], ReferenceDealer, 0x501));

        var damageEvents = ProcessPeriodicTick(module);
        var damageEvent = damageEvents.Single(entry =>
            entry.AttributedSource?.EntityId == Dealer.EntityId);

        Assert.Equal(349.0, damageEvent.EffectiveMeterAmount);
        Assert.Equal(600.0, damageEvents.Sum(entry => entry.EffectiveMeterAmount));
    }

    [Fact]
    public void PersonalModifierDoesNotBecomeRaidBuffCredit()
    {
        Assert.Empty(RaidBuffPolicy.GetEffects(
            Status(0x748),
            isTargetStatus: false,
            recipient: Dealer));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DamageDownNormalizesCalibrationWithoutChangingDirectDamage(bool useCachedStatus)
    {
        var module = new DamageParsingModule();
        var debuff = Status(0xB5F, Target) with { Parameter = 0xA6 };
        Assert.True(DamageStatusCapturePolicy.IsRelevant(debuff.StatusId));
        Assert.Equal(180, PersonalDamageModifierPolicy.GetDefaultDurationSeconds(debuff.StatusId));
        Assert.Empty(RaidBuffPolicy.GetEffects(debuff, false, Dealer));
        if (useCachedStatus)
        {
            module.ObserveStatus(new(Dealer, Target, debuff.StatusId, "Damage Down", 0, 0, "",
                SeenAtUtc.AddSeconds(-1), 180, false, false, false)
            { Parameter = 0xA6 });
        }
        var packet = DirectPacket(100, useCachedStatus ? [] : [debuff]) with
        {
            HasSourceStatusSnapshot = !useCachedStatus,
        };
        var direct = Assert.Single(module.Process(packet));
        Assert.Equal(100u, direct.Amount);
        Assert.Contains(direct.SourceStatuses, status => status.StatusId == debuff.StatusId);
        module.ObserveStatus(PeriodicApplication([useCachedStatus ? debuff with { Parameter = 0 } : debuff]));
        var tick = Assert.Single(ProcessPeriodicTick(module));
        Assert.Equal(10, tick.PeriodicEstimateInputs!.DamagePerPotency, 6);
        Assert.Equal(0.1, tick.PeriodicEstimateInputs.DamageMultiplier, 6);
        Assert.Equal(20, tick.PeriodicEstimateInputs.BaseDamage, 6);
    }

    [Theory]
    [InlineData(false, 0.1)]
    [InlineData(true, 0.125)]
    public void DamageDownScalesOnlyTheAffectedSnapshotAndConservesCombinedDamage(bool fightOrFlight, double multiplier)
    {
        var module = new DamageParsingModule();
        module.Process(DirectPacket(1000, []));
        module.Process(DirectPacket(1000, [], ReferenceDealer, 2));
        var statuses = new List<DamageStatusSnapshot> { Status(0xB5F, Target) with { Parameter = 0xA6 } };
        if (fightOrFlight)
        {
            statuses.Add(Status(0x4C));
        }
        module.ObserveStatus(PeriodicApplication(statuses));
        module.ObserveStatus(PeriodicApplication([], ReferenceDealer, 0x501));
        var ticks = ProcessPeriodicTick(module);
        var affected = ticks.Single(tick => tick.Source.EntityId == Dealer.EntityId);
        var unaffected = ticks.Single(tick => tick.Source.EntityId == ReferenceDealer.EntityId);
        Assert.Equal(multiplier, affected.PeriodicEstimateInputs!.DamageMultiplier, 6);
        Assert.Equal(1, unaffected.PeriodicEstimateInputs!.DamageMultiplier, 6);
        Assert.True(affected.Amount < unaffected.Amount);
        Assert.Equal(600.0, ticks.Sum(tick => tick.EffectiveMeterAmount));
    }

    [Fact]
    public void ExpiredDamageDownIsNotAppliedToSnapshot()
    {
        var module = new DamageParsingModule();
        module.Process(DirectPacket(1000, []));
        module.ObserveStatus(PeriodicApplication([Status(0xB5F, Target) with { RemainingTime = 0 }]));
        Assert.Equal(1, Assert.Single(ProcessPeriodicTick(module)).PeriodicEstimateInputs!.DamageMultiplier);
    }

    [Theory]
    [InlineData(0xF6, -0.10)]
    [InlineData(0xE2, -0.30)]
    [InlineData(0xCE, -0.50)]
    [InlineData(0xA6, -0.90)]
    [InlineData(0x9C, -1.00)]
    public void DamageDownUsesCapturedSignedPercentage(ushort parameter, double expected)
    {
        var status = Status(0xB5F, Target) with { Parameter = parameter };
        Assert.False(PersonalDamageModifierPolicy.HasUnknownStrength(status));
        Assert.Equal(expected, Assert.Single(PersonalDamageModifierPolicy.GetEffects(status, 3, 3)).Amount, 6);
        var module = new DamageParsingModule();
        module.Process(DirectPacket(1000, []));
        module.ObserveStatus(PeriodicApplication([status]));
        Assert.Equal(1 + expected, Assert.Single(ProcessPeriodicTick(module)).PeriodicEstimateInputs!.DamageMultiplier, 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(128)]
    [InlineData(155)]
    [InlineData(256)]
    [InlineData(65535)]
    public void UnknownDamageDownStrengthDoesNotInventCalibrationOrLoseObservedDamage(ushort parameter)
    {
        var status = Status(0xB5F, Target) with { Parameter = parameter };
        var module = new DamageParsingModule();
        Assert.Empty(PersonalDamageModifierPolicy.GetEffects(status, 3, 3));
        module.Process(DirectPacket(100, [status]));
        module.ObserveStatus(PeriodicApplication([]));
        Assert.Null(Assert.Single(ProcessPeriodicTick(module)).PeriodicEstimateInputs);

        module = new DamageParsingModule();
        module.Process(DirectPacket(1000, []));
        module.ObserveStatus(PeriodicApplication([status]));
        var tick = Assert.Single(ProcessPeriodicTick(module));
        Assert.Equal("Unknown damage modifier", tick.PeriodicEstimateUnavailableReason);
        Assert.Null(tick.SimulatedPeriodicAmount);
        Assert.Equal(600u, tick.Amount);
    }

    [Fact]
    public void CachedDamageDownSnapshotIsFrozenBeforeStatusConfirmation()
    {
        var module = new DamageParsingModule();
        module.Process(DirectPacket(1000, []));
        var damageDown = new DamageStatusApplication(Dealer, Target, 0xB5F, "Damage Down", 0, 1, "",
            SeenAtUtc.AddMilliseconds(10), 180, false, false, false)
        { Parameter = 0xA6 };
        module.ObserveStatus(damageDown);
        var dot = PeriodicApplication([]) with { DurationSeconds = 0, HasSourceStatusSnapshot = false };
        module.ObserveStatus(dot);
        module.ObserveStatus(damageDown with { SeenAtUtc = SeenAtUtc.AddSeconds(1), Parameter = 0xF6 });
        module.ObserveStatus(dot with
        {
            ActionId = 0,
            SeenAtUtc = SeenAtUtc.AddSeconds(1.1),
            DurationSeconds = 30,
            CriticalRateLowByte = null,
        });
        var tick = Assert.Single(ProcessPeriodicTick(module));
        Assert.Equal(0.1, tick.PeriodicEstimateInputs!.DamageMultiplier, 6);
        Assert.Equal((ushort)0xA6, Assert.Single(tick.SourceStatuses).Parameter);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingNewDamageDownStrengthDoesNotReuseRemovedOrExpiredApplication(bool expires)
    {
        var module = new DamageParsingModule();
        module.Process(DirectPacket(1000, []));
        var damageDown = new DamageStatusApplication(Dealer, Target, 0xB5F, "Damage Down", 0, 1, "",
            SeenAtUtc.AddSeconds(-2), 1, false, false, false)
        { Parameter = 0xA6 };
        if (!expires)
        {
            damageDown = damageDown with { DurationSeconds = 180 };
        }
        module.ObserveStatus(damageDown);
        if (!expires)
        {
            module.ObserveStatus(damageDown with { IsRemoval = true, SeenAtUtc = SeenAtUtc.AddSeconds(-1) });
        }
        module.ObserveStatus(damageDown with { ActionId = 0, Parameter = 0, SeenAtUtc = SeenAtUtc, DurationSeconds = 180 });
        module.ObserveStatus(PeriodicApplication([Status(0xB5F, Target)]));
        var tick = Assert.Single(ProcessPeriodicTick(module));
        Assert.Equal("Unknown damage modifier", tick.PeriodicEstimateUnavailableReason);
    }

    [Fact]
    public void LearnedProfileDoesNotCrossDamageDownStrengthsWithIdenticalMemoryStatusKeys()
    {
        var module = new DamageParsingModule();
        module.Process(DirectPacket(1000, []));
        module.Process(DirectPacket(1000, [], ReferenceDealer, 2));
        var application = PeriodicApplication([Status(0xB5F, Target) with { Parameter = 0xA6 }]) with
        {
            SnapshotKey = "0B5F:40000001:0",
        };
        module.ObserveStatus(application);
        Assert.Equal(600u, Assert.Single(ProcessPeriodicTick(module)).Amount);
        module.ObserveStatus(application with
        {
            SeenAtUtc = SeenAtUtc.AddSeconds(4),
            SourceStatuses = [Status(0xB5F, Target) with { Parameter = 0xF6 }],
        });
        module.ObserveStatus(PeriodicApplication([], ReferenceDealer, 0x501) with { SeenAtUtc = SeenAtUtc.AddSeconds(4) });
        var ticks = ProcessPeriodicTick(module, 6, 3);
        var affected = Assert.Single(ticks, tick => tick.Source.EntityId == Dealer.EntityId);
        var unaffected = Assert.Single(ticks, tick => tick.Source.EntityId == ReferenceDealer.EntityId);
        Assert.Equal(0.9, affected.PeriodicEstimateInputs!.DamageMultiplier, 6);
        Assert.Equal(PeriodicAllocationBasis.PotencyEstimate, affected.PeriodicAllocationBasis);
        Assert.True(affected.Amount < unaffected.Amount);
        Assert.Equal(600.0, ticks.Sum(tick => tick.EffectiveMeterAmount));
    }

    private static DamageStatusSnapshot Status(
        uint statusId,
        DamageActorIdentity? source = null)
    {
        return new DamageStatusSnapshot(statusId, source ?? Dealer, 0, 20.0f);
    }

    [Fact]
    public void PetHitsSampleOwnerRatesWithoutCalibratingOwnerPotency()
    {
        var module = new DamageParsingModule();
        module.Process(DirectPacket(1_000, []));
        var pet = Dealer with
        {
            EntityId = 0x40000002,
            IsPlayer = false,
            IsPartyMember = false,
            IsPet = true,
            OwnerEntityId = Dealer.EntityId,
        };
        for (var index = 0; index < 11; index++)
        {
            module.Process(DirectPacket(9_000, [], pet, index + 2) with { SourceOwner = Dealer });
        }
        module.ObserveStatus(PeriodicApplication([]));
        var tick = Assert.Single(ProcessPeriodicTick(module));
        Assert.Equal(215, tick.SimulatedPeriodicAmount!.Value, 6);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DelayedTickUsesTheApplicationBeforeARefresh(bool tickArrivesFirst)
    {
        var tracker = new PeriodicDamageTracker();
        var original = PeriodicApplication([]);
        var refreshed = original with { SeenAtUtc = SeenAtUtc.AddSeconds(3), SourceStatuses = [Status(0x748)] };
        tracker.Observe(original);
        var tick = new PeriodicDamageTick(1, SeenAtUtc.AddSeconds(2.98), Target, 0, "", 0, 600, null);
        IReadOnlyList<ParsedDamageEvent> events;
        if (tickArrivesFirst)
        {
            events = tracker.Process(tick);
            tracker.Observe(refreshed);
        }
        else
        {
            tracker.Observe(refreshed);
            events = tracker.Process(tick);
        }
        Assert.Empty(Assert.Single(events).SourceStatuses);
        Assert.Equal(600u, Assert.Single(events).Amount);
        var later = Assert.Single(tracker.Process(tick with { PacketSequence = 2, SeenAtUtc = SeenAtUtc.AddSeconds(6) }));
        Assert.Equal(0x748u, Assert.Single(later.SourceStatuses).StatusId);
    }

    [Fact]
    public void LateRemovalDoesNotRemoveTheNewApplication()
    {
        var tracker = new PeriodicDamageTracker();
        var application = PeriodicApplication([]);
        tracker.Observe(application);
        tracker.Observe(application with { SeenAtUtc = SeenAtUtc.AddSeconds(4), SourceStatuses = [Status(0x748)] });
        tracker.Retire(Target.EntityId, application.StatusId, Dealer.EntityId, SeenAtUtc.AddSeconds(3));
        var tick = Assert.Single(tracker.Process(new PeriodicDamageTick(1, SeenAtUtc.AddSeconds(5), Target, 0, "", 0, 600, null)));
        Assert.Equal(0x748u, Assert.Single(tick.SourceStatuses).StatusId);
    }

    private static DamageActionPacket DirectPacket(
        uint amount,
        IReadOnlyList<DamageStatusSnapshot> sourceStatuses,
        DamageActorIdentity? source = null,
        long packetSequence = 1)
    {
        source ??= Dealer;
        return new DamageActionPacket(
            packetSequence,
            SeenAtUtc,
            (uint)packetSequence,
            source,
            100,
            "Direct action",
            [new DamageActionTarget(
                0,
                Target,
                [new DamageActionEffect(0, 3, 0, 0, 0, 0, amount)])])
        {
            ActionCategoryId = 3,
            DirectPotency = 100,
            CanCalibratePotency = true,
            SourceStatuses = sourceStatuses,
            HasSourceStatusSnapshot = true,
        };
    }

    private static DamageStatusApplication PeriodicApplication(
        IReadOnlyList<DamageStatusSnapshot> sourceStatuses,
        DamageActorIdentity? source = null,
        uint statusId = 0x500)
    {
        source ??= Dealer;
        return new DamageStatusApplication(
            Target,
            source,
            statusId,
            "Periodic effect",
            0,
            200,
            "Periodic action",
            SeenAtUtc.AddMilliseconds(100),
            30.0f,
            true,
            false,
            false)
        {
            PeriodicPotency = 20,
            CriticalRateLowByte = 150,
            ActionCategoryId = 3,
            DamageType = 3,
            SourceStatuses = sourceStatuses,
            HasSourceStatusSnapshot = true,
        };
    }

    private static IReadOnlyList<ParsedDamageEvent> ProcessPeriodicTick(DamageParsingModule module, double seconds = 3, long sequence = 2)
    {
        var tick = new PeriodicDamageTick(
            sequence,
            SeenAtUtc.AddSeconds(seconds),
            Target,
            0,
            string.Empty,
            0,
            600,
            null);
        var immediate = module.ProcessPeriodicTick(tick);
        return immediate.Count > 0
            ? immediate
            : module.FlushPendingPeriodicTicks(tick.SeenAtUtc.AddMilliseconds(50));
    }
}
