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

    public static TheoryData<uint, double> UniversalModifiers => new()
    {
        { 0x31, 0.15 },
        { 0x2B, -0.25 },
        { 0x2C, -0.50 },
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

    [Fact]
    public void PersonalModifierIsRemovedFromCalibrationAndReappliedAtDotSnapshot()
    {
        var module = new DamageParsingModule();
        var lanceCharge = Status(0x748);
        module.Process(DirectPacket(1_100, [lanceCharge]));
        module.ObserveStatus(PeriodicApplication([lanceCharge]));

        var damageEvent = Assert.Single(ProcessPeriodicTick(module));

        Assert.Equal(239.0, damageEvent.EffectiveMeterAmount);
        Assert.Equal(3u, damageEvent.ActionCategoryId);
    }

    [Fact]
    public void PersonalSnapshotModifiersStackAdditively()
    {
        var module = new DamageParsingModule();
        module.Process(DirectPacket(1_000, []));
        module.ObserveStatus(PeriodicApplication([
            Status(0x748),
            Status(0x77A),
            Status(0xF04),
        ]));

        var damageEvent = Assert.Single(ProcessPeriodicTick(module));

        Assert.Equal(294.0, damageEvent.EffectiveMeterAmount);
    }

    [Fact]
    public void PersonalModifierDoesNotBecomeRaidBuffCredit()
    {
        Assert.Empty(RaidBuffPolicy.GetEffects(
            Status(0x748),
            isTargetStatus: false,
            recipient: Dealer));
    }

    private static DamageStatusSnapshot Status(uint statusId)
    {
        return new DamageStatusSnapshot(statusId, Dealer, 0, 20.0f);
    }

    private static DamageActionPacket DirectPacket(
        uint amount,
        IReadOnlyList<DamageStatusSnapshot> sourceStatuses)
    {
        return new DamageActionPacket(
            1,
            SeenAtUtc,
            1,
            Dealer,
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
        IReadOnlyList<DamageStatusSnapshot> sourceStatuses)
    {
        return new DamageStatusApplication(
            Target,
            Dealer,
            0x500,
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

    private static IReadOnlyList<ParsedDamageEvent> ProcessPeriodicTick(DamageParsingModule module)
    {
        var tick = new PeriodicDamageTick(
            2,
            SeenAtUtc.AddSeconds(3),
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
