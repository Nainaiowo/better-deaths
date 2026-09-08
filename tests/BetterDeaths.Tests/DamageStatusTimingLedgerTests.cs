namespace BetterDeaths.Tests;

using BetterDeaths.DamageParsing;

public sealed class DamageStatusTimingLedgerTests
{
    private static readonly DateTime Start = new(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DamageActorIdentity Player = new(0x10000001, "Player", 0, "", true, 24) { Level = 100 };
    private static readonly DamageActorIdentity Provider = new(0x10000002, "Provider", 0, "", true, 23);
    private static readonly DamageActorIdentity Enemy = new(0x40000001, "Enemy", 0, "", false, 0);

    [Theory]
    [InlineData(141u)]
    [InlineData(2218u)]
    [InlineData(1825u)]
    [InlineData(2125u)]
    [InlineData(3887u)]
    [InlineData(3889u)]
    public void CountdownReadsCannotMoveAcceptedExpiry(uint status)
    {
        var ledger = new DamageStatusTimingLedger();
        Assert.True(ledger.Observe(Update(0, 30, status)));
        Assert.False(ledger.Observe(Update(10, 21.9f, status)));
        for (var i = 0; i < 100; i++)
        {
            var captured = new[] { Update(0, 18, status).Status };
            Assert.Equal(0.5f, Assert.Single(ledger.Resolve(Player.EntityId, captured, At(29.5))).RemainingTime);
            Assert.Equal(18, captured[0].RemainingTime);
        }
        Assert.Equal(30, Assert.Single(ledger.Resolve(Player.EntityId, [], Start)).RemainingTime);
    }

    [Theory]
    [InlineData(22f, false)]
    [InlineData(22.01f, true)]
    [InlineData(18f, false)]
    [InlineData(17.99f, true)]
    public void FullUpdatesHaveAStrictTwoSecondExpiryThreshold(float duration, bool accepted)
    {
        var ledger = new DamageStatusTimingLedger();
        ledger.Observe(Update(0, 30));
        Assert.Equal(accepted, ledger.Observe(Update(10, duration)));
        Assert.Equal(accepted ? duration : 20, Assert.Single(ledger.Resolve(Player.EntityId, [], At(10))).RemainingTime, 3);
    }

    [Theory]
    [InlineData(20.199f, false)]
    [InlineData(20.201f, true)]
    public void ExplicitRefreshAcceptsASmallerMeaningfulChange(float duration, bool accepted)
    {
        var ledger = new DamageStatusTimingLedger();
        ledger.Observe(Update(0, 30));
        ledger.MarkRefresh(Player.EntityId, 2218);
        Assert.Equal(accepted, ledger.Observe(Update(10, duration)));
        if (accepted)
            Assert.False(ledger.Observe(Update(11, duration - 0.7f)));
    }

    [Fact]
    public void ParameterChangesReplaceTheExpiryAndPreserveEarlierQueries()
    {
        var ledger = new DamageStatusTimingLedger();
        ledger.Observe(Update(0, 30));
        var changed = Update(10, 21) with { Status = Update(10, 21).Status with { Parameter = 2 } };
        Assert.True(ledger.Observe(changed));
        Assert.Equal(25, Assert.Single(ledger.Resolve(Player.EntityId, [], At(5))).RemainingTime);
        Assert.Equal(21, Assert.Single(ledger.Resolve(Player.EntityId, [], At(10))).RemainingTime);
    }

    [Fact]
    public void PartialParameterUpdateKeepsNetworkDurationWithoutLearningAMemoryCountdown()
    {
        var ledger = new DamageStatusTimingLedger();
        ledger.Observe(Update(0, 30));
        Assert.True(ledger.Observe(Update(10, 0) with
        {
            MissingInformation = true,
            Status = Update(10, 0).Status with { Source = Provider with { EntityId = 0 }, Parameter = 2 },
        }));
        Assert.Equal(30, Assert.Single(ledger.Resolve(Player.EntityId, [], At(10))).RemainingTime);
        // Acceptance still compares against the original network expiry of 30s, not the emitted expiry of 40s.
        Assert.False(ledger.Observe(Update(11, 19) with { Status = Update(11, 19).Status with { Parameter = 2 } }));
        Assert.Equal(29, Assert.Single(ledger.Resolve(Player.EntityId, [], At(11))).RemainingTime);
    }

    [Fact]
    public void RemovalAndReplacementCannotBeUndoneByAnOlderMemoryList()
    {
        var ledger = new DamageStatusTimingLedger();
        var old = Update(0, 30);
        ledger.Observe(old);
        ledger.Observe(Update(10, 30, 141));
        Assert.Equal(141u, Assert.Single(ledger.Resolve(Player.EntityId, [old.Status], At(11))).StatusId);
        Assert.Equal(2218u, Assert.Single(ledger.Resolve(Player.EntityId, [old.Status], At(9))).StatusId);
        ledger.Observe(Update(12, 0, 0));
        Assert.Empty(ledger.Resolve(Player.EntityId, [old.Status], At(13)));
        Assert.False(ledger.Observe(Update(11, 30)));
        Assert.Empty(ledger.Resolve(Player.EntityId, [old.Status], At(14)));
        Assert.Equal(141u, Assert.Single(ledger.Resolve(Player.EntityId, [], At(11))).StatusId);
    }

    [Fact]
    public void UnknownReplacementRetiresTrackedBuffWithoutAddingUnrelatedStatus()
    {
        var ledger = new DamageStatusTimingLedger();
        ledger.Observe(Update(0, 30));
        ledger.Observe(Update(10, 30, ushort.MaxValue));
        Assert.Empty(ledger.Resolve(Player.EntityId, [Update(0, 30).Status], At(11)));
    }

    [Fact]
    public void TimerExpiryDoesNotForgetTheAcceptedNetworkSlot()
    {
        var ledger = new DamageStatusTimingLedger();
        ledger.Observe(Update(0, 30));
        Assert.Equal(-1, Assert.Single(ledger.Resolve(Player.EntityId, [], At(31))).RemainingTime);
        Assert.False(ledger.Observe(Update(31, 0.5f)));
        Assert.True(ledger.Observe(Update(32, 30)));
        Assert.Equal(30, Assert.Single(ledger.Resolve(Player.EntityId, [], At(32))).RemainingTime);
    }

    [Fact]
    public void ProvidersSlotsAndActorsStayIndependent()
    {
        var ledger = new DamageStatusTimingLedger();
        ledger.Observe(Update(0, 30));
        ledger.Observe(Update(0, 20) with { Slot = 31, Status = Update(0, 20).Status with { Source = Player } });
        ledger.Observe(Update(0, 40) with { TargetEntityId = Provider.EntityId });
        Assert.Equal(2, ledger.Resolve(Player.EntityId, [], At(10)).Count);
        Assert.Equal(30, Assert.Single(ledger.Resolve(Provider.EntityId, [], At(10))).RemainingTime);
        ledger.Observe(Update(10, 15) with { Status = Update(10, 15).Status with { Source = Provider with { EntityId = 3 } } });
        Assert.Contains(ledger.Resolve(Player.EntityId, [], At(10)), status => status.Source.EntityId == 3 && status.RemainingTime == 15);
        ledger.Clear();
        Assert.Empty(ledger.Resolve(Player.EntityId, [], At(10)));
    }

    [Theory]
    [InlineData(0f, 9999f)]
    [InlineData(10000f, 9999f)]
    [InlineData(30f, 30f)]
    public void ZeroAndLargeDurationsUseTheAcceptedDurationPolicy(float input, float expected)
    {
        var ledger = new DamageStatusTimingLedger();
        ledger.Observe(Update(0, input));
        Assert.Equal(expected, Assert.Single(ledger.Resolve(Player.EntityId, [], Start)).RemainingTime);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void InvalidInputDoesNotReplaceKnownTiming(float duration)
    {
        var ledger = new DamageStatusTimingLedger();
        ledger.Observe(Update(0, 30));
        Assert.False(ledger.Observe(Update(10, duration)));
        Assert.Equal(20, Assert.Single(ledger.Resolve(Player.EntityId, [], At(10))).RemainingTime);
    }

    [Theory]
    [InlineData(1.004f, 1f)]
    [InlineData(0.996f, 1f)]
    [InlineData(0.994f, 0.99f)]
    public void SamplingUsesTheReportedCentisecondDuration(float wire, float reported)
    {
        var ledger = new DamageStatusTimingLedger();
        ledger.Observe(Update(0, wire));
        Assert.Equal(reported, Assert.Single(ledger.Resolve(Player.EntityId, [], Start)).RemainingTime);
    }

    [Theory]
    [InlineData(false, false, 20)]
    [InlineData(true, false, 0)]
    [InlineData(false, true, 21)]
    public void EncounterAndCalibrationResetsDoNotEraseBuffsButExplicitContextResetDoes(
        bool resetCalibration, bool resetTiming, int expectedSamples)
    {
        var module = new DamageParsingModule();
        for (var i = 0; i < 20; i++) module.Process(Packet(i + 1, i));
        module.ObserveStatusTiming(Update(20, 30));
        module.EndEncounter(At(21), "Ended");
        if (resetCalibration) module.ResetCalibration();
        if (resetTiming) module.ResetStatusTiming();
        module.Process(Packet(21, 30));
        module.ObserveStatus(new(Enemy, Player, 1871, "Dia", 0, 16532, "Dia", At(31), 30, true, false, false)
        { PeriodicPotency = 85, SourceBaseRates = new(0.251, 0.096), HasSourceStatusSnapshot = true });
        module.ProcessPeriodicTick(new(1000, At(34), Enemy, 0, "", 0, 600, null));
        var tick = Assert.Single(module.FlushPendingPeriodicTicks(At(34.1)));
        Assert.Equal(expectedSamples, tick.PeriodicCompatibilityEstimate!.DirectHit.Samples);
    }

    [Theory]
    [InlineData(2218u, 11f, 2.5f, 21)]
    [InlineData(141u, 11.6f, 0.924f, 20)]
    public void WholeModuleUsesAcceptedTimingForDirectSamplesAndDotConfirmation(
        uint buff, float duration, float memoryRemaining, int samples)
    {
        var module = new DamageParsingModule();
        for (var i = 0; i < 20; i++) module.Process(Packet(i + 1, i));
        module.ObserveStatusTiming(Update(20, duration, buff));
        var captured = Update(30.5, memoryRemaining, buff).Status;
        var hit = Assert.Single(module.Process(Packet(21, 30.5) with { SourceStatuses = [captured] }));
        Assert.Equal(1000u, hit.Amount);
        Assert.Equal(memoryRemaining, Assert.Single(hit.SourceStatuses).RemainingTime);
        module.ObserveStatus(new(Enemy, Player, 1871, "Dia", 0, 16532, "Dia", At(30.6), 30, true, false, false)
        {
            PeriodicPotency = 85, SourceBaseRates = new(0.251, 0.096), HasSourceStatusSnapshot = true,
            SourceStatuses = [captured],
        });
        module.ProcessPeriodicTick(new(1000, At(34), Enemy, 0, "", 0, 600, null));
        var tick = Assert.Single(module.FlushPendingPeriodicTicks(At(34.1)));
        Assert.Equal(samples, tick.PeriodicCompatibilityEstimate!.DirectHit.Samples);
        Assert.Equal(buff == 141 ? 0.2 : 0, tick.PeriodicCompatibilityEstimate.DirectHit.BuffRate, 6);
    }

    [Fact]
    public void PetStatusContextDoesNotBorrowTheOwnersBuffTimer()
    {
        var ledger = new DamageStatusTimingLedger();
        var pet = Player with { EntityId = 0x40000002, IsPlayer = false, IsPet = true, OwnerEntityId = Player.EntityId };
        ledger.Observe(Update(0, 30));
        ledger.Observe(Update(0, 15) with { TargetEntityId = pet.EntityId });
        var application = new DamageStatusApplication(Enemy, Player, 1871, "Dia", 0, 0, "", At(10), 30, true, false, false)
        { SourceStatusActorId = pet.EntityId, SourceStatuses = [Update(0, 99).Status] };
        Assert.Equal(5, Assert.Single(ledger.Resolve(application).SourceStatuses).RemainingTime);
        Assert.Equal(20, Assert.Single(ledger.Resolve(application with { SourceStatusActorId = null }).SourceStatuses).RemainingTime);
    }

    [Fact]
    public void ChanceBuffTimingDoesNotChangeGuaranteedHitDetection()
    {
        var sampler = new PeriodicDirectHitCompatibility();
        var packet = Packet(1, 10) with
        {
            ActionCategoryId = 3,
            SourceStatuses = [new(0x353, Player, 0, 3)],
        };
        var hit = Assert.Single(new DirectDamageParser().Parse(packet));
        sampler.Observe(hit, []);
        var application = new DamageStatusApplication(Enemy, Player, 1871, "Dia", 0, 0, "", At(11), 30, true, false, false);
        Assert.Equal(0, sampler.Capture(application, At(11)).Samples);
    }

    private static DateTime At(double seconds) => Start.AddSeconds(seconds);
    private static DamageStatusTimingUpdate Update(double time, float duration, uint statusId = 2218) =>
        new(Player.EntityId, 30, new(statusId, Provider, 0, duration) { StatusSlot = 30 }, At(time));
    private static DamageActionPacket Packet(long sequence, double time) =>
        new(sequence, At(time), (uint)sequence, Player, 100, "Spell",
            [new(0, Enemy, [new(0, 3, 0, 0, 0, 0, 1000)])])
        { DirectPotency = 100, CanCalibratePotency = true, SourceBaseRates = new(0.251, 0.096), HasSourceStatusSnapshot = true };
}
