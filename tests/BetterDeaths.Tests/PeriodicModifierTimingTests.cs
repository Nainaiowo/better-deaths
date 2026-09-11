namespace BetterDeaths.Tests;

using BetterDeaths.DamageParsing;
using System.Text.Json;

public sealed class PeriodicModifierTimingTests
{
    private static readonly DateTime Start = new(2026, 9, 10, 18, 56, 35, DateTimeKind.Utc);
    private static readonly DamageActorIdentity Player = new(0x10000001, "Player", 0, "", true, 23) { Level = 100 };
    private static readonly DamageActorIdentity Provider = new(0x10000002, "Provider", 0, "", true, 33);
    private static readonly DamageActorIdentity Enemy = new(0x40000001, "Enemy", 0, "", false, 0);

    [Fact]
    public void IronJawsConfirmationBeforeBuffRemovalSnapshotsBothDotsAndKeepsThemFrozen()
    {
        var module = Create(Player);
        Gain(module, Player, 1878, 20);
        foreach (var (status, potency, low) in new[] { (1201u, 25.0, (byte)60), (1200u, 20.0, (byte)49) })
        {
            module.ObserveStatus(Dot(Player, status, potency, 2) with { DurationSeconds = 45, BaseDamageLowByte = 17 });
            var pending = Dot(Player, status, potency, 20.292) with
            {
                ActionId = 3560, ApplicationSequence = 2192, BaseDamageLowByte = low,
                SourceStatuses = [Buff(1878, .2f), new(2217, Player, 0, 5)],
            };
            module.ObserveStatus(pending);
            module.ObserveStatus(Confirm(pending, 20.914) with
            {
                SourceStatuses = [Buff(1878, 0), new(2217, Player, 0, 4)],
            });
        }
        Remove(module, Player, 20.96);
        var ticks = Tick(module, 22);
        Assert.Equal(2, ticks.Count);
        foreach (var tick in ticks)
        {
            var estimate = Assert.IsType<PeriodicCompatibilityEstimate>(tick.PeriodicCompatibilityEstimate);
            Assert.Equal(1.07, estimate.Inputs!.DamageMultiplier, 8);
            Assert.Equal(tick.StatusId == 1201 ? 2620 : 2097, estimate.Inputs.BaseDamage);
        }
        var later = Tick(module, 25);
        foreach (var tick in ticks)
            Assert.Equal(tick.PeriodicCompatibilityEstimate, later.Single(t => t.StatusId == tick.StatusId).PeriodicCompatibilityEstimate);
    }

    [Theory]
    [MemberData(nameof(DamageJobRegressionMatrixTests.CurrentPeriodicDamageMatrix), MemberType = typeof(DamageJobRegressionMatrixTests))]
    public void EveryPeriodicWorkflowUsesWireSourceModifierTiming(string _, uint job, uint status, double potency, bool ground)
    {
        var player = Player with { ClassJobId = job };
        var module = Create(player);
        Gain(module, player, 1878, 20);
        var pending = Dot(player, status, potency, 20.292) with { SourceStatuses = [Buff(1878, .2f)] };
        module.ObserveStatus(pending);
        module.ObserveStatus(Confirm(pending, 20.914) with { SourceStatuses = [Buff(1878, 0)] });
        Remove(module, player, 20.96);
        var tick = Assert.Single(Tick(module, 22, ground ? status : 0, ground ? player : null));
        if (ground)
        {
            Assert.False(tick.PeriodicMeterUsesEstimate);
            Assert.Equal(600, tick.RawMeterAmount);
        }
        else
        {
            Assert.Equal(1.06, tick.PeriodicCompatibilityEstimate!.Inputs!.DamageMultiplier, 8);
            Assert.Equal(tick.PeriodicCompatibilityEstimate,
                Assert.Single(Tick(module, 25)).PeriodicCompatibilityEstimate);
        }
    }

    [Theory]
    [InlineData("removed", 1.0)]
    [InlineData("replaced", 1.0)]
    [InlineData("expired", 1.0)]
    [InlineData("future", 1.0)]
    [InlineData("other-actor", 1.0)]
    [InlineData("no-wire", 1.0)]
    [InlineData("active", 1.06)]
    [InlineData("refreshed", 1.06)]
    public void ConfirmationHonorsWireIdentityExpiryAndRefresh(string scenario, double multiplier)
    {
        var module = Create(Player);
        if (scenario != "no-wire")
            Gain(module, scenario == "other-actor" ? Provider : Player, 1878, scenario is "expired" or "refreshed" ? 2 : 20);
        if (scenario == "removed") Remove(module, Player, 20.5);
        if (scenario == "replaced") module.ObserveStatusTiming(new(Player.EntityId, 0, Buff(50, 20), At(20.5)));
        if (scenario == "future")
        {
            Remove(module, Player, 20.5);
            module.ObserveStatusTiming(new(Player.EntityId, 0, Buff(1878, 20), At(21)));
        }
        if (scenario == "refreshed") module.ObserveStatusTiming(new(Player.EntityId, 0, Buff(1878, 20), At(20)));
        var pending = Dot(Player, 1201, 25, 20.6) with { SourceStatuses = [Buff(1878, 0)] };
        module.ObserveStatus(pending);
        module.ObserveStatus(Confirm(pending, 20.914));
        Assert.Equal(multiplier, Assert.Single(Tick(module, 22)).PeriodicCompatibilityEstimate!.Inputs!.DamageMultiplier, 8);
    }

    [Theory]
    [InlineData(21.0, 1.06)]
    [InlineData(21.5, 1.06)]
    [InlineData(22.0, 1.06)]
    [InlineData(22.001, 1.0)]
    public void SourceDamageWindowIsInclusiveAndBounded(double confirmationAt, double multiplier)
    {
        var module = Create(Player);
        Gain(module, Player, 1878, 20);
        var pending = Dot(Player, 1201, 25, 20) with { SourceStatuses = [Buff(1878, 0)] };
        module.ObserveStatus(pending);
        module.ObserveStatus(Confirm(pending, confirmationAt));
        Assert.Equal(multiplier, Assert.Single(Tick(module, confirmationAt + 1)).PeriodicCompatibilityEstimate!.Inputs!.DamageMultiplier, 8);
    }

    [Fact]
    public void SourceDamageWindowDoesNotExtendCriticalDirectHitOrTargetModifiers()
    {
        var module = Create(Player);
        Gain(module, Player, 1878, 20);
        module.ObserveStatusTiming(new(Player.EntityId, 1, Buff(2216, 20) with { StatusSlot = 1 }, At(1)));
        module.ObserveStatusTiming(new(Player.EntityId, 2, Buff(2218, 20) with { StatusSlot = 2 }, At(1)));
        module.ObserveStatusTiming(new(Enemy.EntityId, 3, Buff(638, 20) with { StatusSlot = 3 }, At(1)));
        var pending = Dot(Player, 1201, 25, 20) with
        {
            CriticalRateLowByte = 200,
            SourceStatuses = [Buff(1878, .1f), Buff(2216, .1f), Buff(2218, .1f)],
            TargetStatuses = [Buff(638, .1f)], HasTargetStatusSnapshot = true,
        };
        module.ObserveStatus(pending);
        module.ObserveStatus(Confirm(pending, 21.5) with { SourceStatuses = [], TargetStatuses = [] });
        var tick = Assert.Single(Tick(module, 23));
        var estimate = tick.PeriodicCompatibilityEstimate!;
        Assert.Equal(1.06, estimate.Inputs!.DamageMultiplier, 8);
        Assert.Equal(0, estimate.DirectHit.BuffRate);
        Assert.Equal(1.55, estimate.Inputs.CriticalMultiplier, 8);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(6)]
    public void WireTimingPreservesConfirmedVariableBuffStrength(byte strength)
    {
        var module = Create(Player);
        module.ObserveStatusTiming(new(Player.EntityId, 0, Buff(2964, 20) with { HasParameter = false }, At(1)));
        var pending = Dot(Player, 1201, 25, 20) with
        { SourceStatuses = [Buff(2964, 5) with { Parameter = strength, HasParameter = true, AppliedParameter = strength }] };
        module.ObserveStatus(pending);
        module.ObserveStatus(Confirm(pending, 21.5) with { HasSourceStatusSnapshot = false, SourceStatuses = [] });
        Assert.Equal(1 + strength / 100.0, Assert.Single(Tick(module, 23)).PeriodicCompatibilityEstimate!.Inputs!.DamageMultiplier, 8);
    }

    [Theory]
    [InlineData(true, 1.06)]
    [InlineData(false, 1.0)]
    public void OwnerAttributedDotUsesItsRawPetsTiming(bool petHasBuff, double multiplier)
    {
        var module = Create(Player);
        var pet = Player with { EntityId = 0x40000002, OwnerEntityId = Player.EntityId, IsPlayer = false, IsPet = true };
        Gain(module, petHasBuff ? pet : Player, 1878, 20);
        var pending = Dot(Player, 1201, 25, 20.292) with
        { SourceStatusActorId = pet.EntityId, SourceStatuses = [Buff(1878, .2f)] };
        module.ObserveStatus(pending);
        module.ObserveStatus(Confirm(pending, 20.914) with { SourceStatuses = [Buff(1878, 0)] });
        Assert.Equal(multiplier, Assert.Single(Tick(module, 22)).PeriodicCompatibilityEstimate!.Inputs!.DamageMultiplier, 8);
    }

    [Fact]
    public void RepeatedConfirmationAfterRemovalDoesNotReReadBuffTiming()
    {
        var module = Create(Player);
        Gain(module, Player, 1878, 20);
        var pending = Dot(Player, 1201, 25, 20.292) with { SourceStatuses = [Buff(1878, .2f)] };
        module.ObserveStatus(pending);
        var confirmation = Confirm(pending, 20.914) with { SourceStatuses = [Buff(1878, 0)] };
        module.ObserveStatus(confirmation);
        Remove(module, Player, 20.96);
        module.ObserveStatus(confirmation with { SeenAtUtc = At(21.1), SourceStatuses = [] });
        Assert.Equal(1.06, Assert.Single(Tick(module, 22)).PeriodicCompatibilityEstimate!.Inputs!.DamageMultiplier, 8);
    }

    [Fact]
    public void ResnapshotReplacesModifiersWithoutRewritingOlderTicks()
    {
        var module = Create(Player);
        Gain(module, Player, 1878, 20);
        var pending = Dot(Player, 1201, 25, 20.292) with { SourceStatuses = [Buff(1878, .2f)] };
        module.ObserveStatus(pending);
        module.ObserveStatus(Confirm(pending, 20.914) with { SourceStatuses = [Buff(1878, 0)] });
        var first = Assert.Single(Tick(module, 21));
        Remove(module, Player, 21.1);
        var refreshed = pending with { SeenAtUtc = At(23), ActionId = 3560, SourceStatuses = [] };
        module.ObserveStatus(refreshed);
        module.ObserveStatus(Confirm(refreshed, 23.6));
        Assert.Equal(1.0, Assert.Single(Tick(module, 25)).PeriodicCompatibilityEstimate!.Inputs!.DamageMultiplier, 8);
        Assert.Equal(1.06, first.PeriodicCompatibilityEstimate!.Inputs!.DamageMultiplier, 8);
    }

    [Fact]
    public async Task ConfirmedModifiersReachLiveFinalSavedAndAbilityTotals()
    {
        var module = Create(Player);
        Gain(module, Player, 1878, 20);
        var pending = Dot(Player, 1201, 25, 20.292) with { SourceStatuses = [Buff(1878, .2f)] };
        module.ObserveStatus(pending);
        module.ObserveStatus(Confirm(pending, 20.914) with { SourceStatuses = [Buff(1878, 0)] });
        Remove(module, Player, 20.96);
        var tick = Assert.Single(Tick(module, 22));
        Assert.Equal(1.06, tick.PeriodicCompatibilityEstimate!.Inputs!.DamageMultiplier, 8);
        var expectedPeriodic = tick.PeriodicCompatibilityEstimate.EstimatedDamage;
        Assert.Equal(expectedPeriodic, tick.RawMeterAmount);
        Assert.NotEqual((double)tick.Amount, expectedPeriodic);

        module.GetLiveEncounter();
        module.RefreshLiveEncounter(At(23));
        await module.PendingLiveSnapshot!;
        module.RefreshLiveEncounter(At(23));
        Check(module.GetLiveEncounter()!);
        var final = module.EndEncounter(At(24), "Duty complete")!;
        Check(final);
        Check(JsonSerializer.Deserialize<DamageEncounterSnapshot>(JsonSerializer.Serialize(final with { Events = [] }))!);

        void Check(DamageEncounterSnapshot snapshot)
        {
            var expectedTotal = 20 * 9829 + expectedPeriodic;
            Assert.Equal(expectedTotal, snapshot.ObservedMeterDamage, 6);
            Assert.Equal(expectedTotal / snapshot.DurationSeconds, snapshot.DamagePerSecond, 6);
            var source = Assert.Single(snapshot.Sources, source => source.Source.EntityId == Player.EntityId);
            Assert.Equal(expectedTotal, source.ObservedMeterDamage, 6);
            Assert.Equal(expectedTotal, snapshot.Sources.Sum(item => item.ObservedMeterDamage), 6);
            Assert.Equal(expectedTotal, source.Actions.Sum(action => action.ObservedMeterDamage), 6);
            Assert.Equal(expectedPeriodic, Assert.Single(source.Actions, action => action.ActionId == 1201).ObservedMeterDamage, 6);
        }
    }

    private static DamageParsingModule Create(DamageActorIdentity player)
    {
        var module = new DamageParsingModule();
        for (var i = 0; i < 20; i++)
            module.Process(new(i+1, Start.AddMilliseconds(i), (uint)(i+1), player, 100, "Hit",
                [new(0, Enemy, [new(0, 3, 0, 0, 0, 0, 9829)])]) { DirectPotency = 100, CanCalibratePotency = true });
        return module;
    }

    private static void Gain(DamageParsingModule module, DamageActorIdentity player, uint id, float duration) =>
        module.ObserveStatusTiming(new(player.EntityId, 0, Buff(id, duration), At(1)));
    private static void Remove(DamageParsingModule module, DamageActorIdentity player, double at) =>
        module.ObserveStatusTiming(new(player.EntityId, 0, Buff(0, 0), At(at)));
    private static DateTime At(double seconds) => Start.AddSeconds(seconds);
    private static DamageStatusSnapshot Buff(uint id, float remaining) => new(id, Provider, 0, remaining) { StatusSlot = 0 };
    private static DamageStatusApplication Dot(DamageActorIdentity player, uint status, double potency, double at) =>
        new(Enemy, player, status, "DoT", 0, 100, "Apply", At(at), 0, true, false, false)
        { PeriodicPotency = potency, CriticalRateLowByte = 14, HasSourceStatusSnapshot = true };
    private static DamageStatusApplication Confirm(DamageStatusApplication pending, double at) => pending with
    { SeenAtUtc = At(at), ActionId = 0, DurationSeconds = 45, BaseDamageLowByte = null, CriticalRateLowByte = null, PeriodicPotency = null };
    private static IReadOnlyList<ParsedDamageEvent> Tick(DamageParsingModule module, double at, uint status = 0, DamageActorIdentity? source = null)
    {
        module.ProcessPeriodicTick(new((long)(at*1000), At(at), Enemy, status, "", 0, 600, source));
        return module.FlushPendingPeriodicTicks(At(at), true);
    }
}
