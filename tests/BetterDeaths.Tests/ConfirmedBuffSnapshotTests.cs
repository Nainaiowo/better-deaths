namespace BetterDeaths.Tests;

using BetterDeaths.DamageParsing;

public sealed class ConfirmedBuffSnapshotTests
{
    private static readonly DateTime Start = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DamageActorIdentity Player = new(0x1001, "Player", 0, "", true, 24);
    private static readonly DamageActorIdentity Provider = Player with { EntityId = 0x1002 };
    private static readonly DamageActorIdentity Enemy = new(0x40000001, "Enemy", 0, "", false, 0);

    [Theory]
    [InlineData(2216u)]
    [InlineData(2217u)]
    [InlineData(2218u)]
    [InlineData(786u)]
    [InlineData(1825u)]
    [InlineData(1878u)]
    [InlineData(2599u)]
    public void CapturedBuffWithoutAGainEventSurvivesUntilConfirmation(uint statusId)
    {
        var tracker = new RaidBuffTracker(confirmedOnly: true);
        tracker.ObserveSnapshots(Application(0) with
        {
            HasSourceStatusSnapshot = true,
            SourceStatuses = [new(statusId, Provider, 0, 3)],
        });
        var buff = Assert.Single(tracker.ApplyConfirmed(Application(1)).SourceStatuses);
        Assert.Equal(statusId, buff.StatusId);
        Assert.Equal(Provider, buff.Source);
        Assert.Equal(2, buff.RemainingTime);
        Assert.Empty(tracker.ApplyConfirmed(Application(5)).SourceStatuses);
    }

    [Fact]
    public void FallbackListsAndPendingAnnouncementsAreNotConfirmation()
    {
        var tracker = new RaidBuffTracker(confirmedOnly: true);
        tracker.ObserveSnapshots(Application(0) with { SourceStatuses = [new(2218, Provider, 0, 30)] });
        tracker.Observe(Buff(1) with { ActionId = 100, DurationSeconds = 0 });
        Assert.Empty(tracker.ApplyConfirmed(Application(2)).SourceStatuses);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitRemovalOrAnEmptySnapshotRetiresCapturedBuffs(bool emptySnapshot)
    {
        var tracker = new RaidBuffTracker(confirmedOnly: true);
        tracker.ObserveSnapshots(Application(0) with
        {
            HasSourceStatusSnapshot = true, SourceStatuses = [new(2218, Provider, 0, 30)],
        });
        if (emptySnapshot)
            tracker.ObserveSnapshots(Application(1) with { HasSourceStatusSnapshot = true });
        else
            tracker.Observe(Buff(1) with { IsRemoval = true, Source = Provider with { EntityId = 0 } });
        Assert.Empty(tracker.ApplyConfirmed(Application(2)).SourceStatuses);
        Assert.Single(tracker.ApplyConfirmed(Application(0.5)).SourceStatuses);
    }

    [Fact]
    public void SourceAndTargetSnapshotsStaySeparateAndRefreshDoesNotStack()
    {
        var tracker = new RaidBuffTracker(confirmedOnly: true);
        var application = Application(0) with
        {
            HasSourceStatusSnapshot = true, SourceStatuses = [new(2218, Provider, 0, 3)],
            HasTargetStatusSnapshot = true, TargetStatuses = [new(0x4C5, Provider, 0, 10)],
        };
        tracker.ObserveSnapshots(application);
        tracker.ObserveSnapshots(application with { SeenAtUtc = Start.AddSeconds(1) });
        tracker.Observe(Buff(1) with { DurationSeconds = 20 });
        var confirmed = tracker.ApplyConfirmed(Application(2));
        Assert.Equal(19, Assert.Single(confirmed.SourceStatuses).RemainingTime);
        Assert.Equal(0x4C5u, Assert.Single(confirmed.TargetStatuses).StatusId);
        Assert.Empty(tracker.ApplyConfirmed(Application(-1)).SourceStatuses);
    }

    [Fact]
    public void SnapshotEnrichmentPreservesKnownProviderWithoutAddingAnotherBuff()
    {
        var tracker = new RaidBuffTracker(confirmedOnly: true);
        tracker.Observe(Buff(0) with { Parameter = 3 });
        tracker.ObserveSnapshots(Application(1) with
        {
            HasSourceStatusSnapshot = true,
            SourceStatuses = [new(2218, Provider with { EntityId = 0 }, 0, 29)],
        });
        var buff = Assert.Single(tracker.ApplyConfirmed(Application(2)).SourceStatuses);
        Assert.Equal(Provider, buff.Source);
        Assert.Equal(3, buff.Parameter);
        tracker.Clear();
        Assert.Empty(tracker.ApplyConfirmed(Application(3)).SourceStatuses);
    }

    [Fact]
    public void DelayedSnapshotCannotResurrectARemovedBuff()
    {
        var tracker = new RaidBuffTracker(confirmedOnly: true);
        tracker.Observe(Buff(0));
        tracker.Observe(Buff(2) with { IsRemoval = true });
        tracker.ObserveSnapshots(Application(1) with
        {
            HasSourceStatusSnapshot = true, SourceStatuses = [new(2218, Provider, 0, 29)],
        });
        Assert.Single(tracker.ApplyConfirmed(Application(1.5)).SourceStatuses);
        Assert.Empty(tracker.ApplyConfirmed(Application(3)).SourceStatuses);
    }

    [Fact]
    public void EarlierEmptySnapshotCannotRetireALaterGain()
    {
        var tracker = new RaidBuffTracker(confirmedOnly: true);
        tracker.Observe(Buff(0));
        tracker.Observe(Buff(2));
        tracker.ObserveSnapshots(Application(1) with { HasSourceStatusSnapshot = true });
        Assert.Single(tracker.ApplyConfirmed(Application(0.5)).SourceStatuses);
        Assert.Empty(tracker.ApplyConfirmed(Application(1.5)).SourceStatuses);
        Assert.Single(tracker.ApplyConfirmed(Application(3)).SourceStatuses);
    }

    [Fact]
    public void CapturedParameterChangesReplaceRatherThanStackAndProvidersStaySeparate()
    {
        var tracker = new RaidBuffTracker(confirmedOnly: true);
        tracker.Observe(Buff(0) with { Parameter = 2 });
        tracker.Observe(Buff(0) with { Source = Provider with { EntityId = 0x1003 } });
        tracker.ObserveSnapshots(Application(1) with
        {
            HasSourceStatusSnapshot = true, SourceStatuses = [new(2218, Provider, 6, 29)],
        });
        var buff = Assert.Single(tracker.ApplyConfirmed(Application(2)).SourceStatuses);
        Assert.Equal(6, buff.Parameter);
        Assert.Equal(Provider, buff.Source);
        Assert.Equal(2, tracker.ApplyConfirmed(Application(0.5)).SourceStatuses.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void InvalidOrExpiredSnapshotDurationsDoNotBecomeDefaultDurationBuffs(float remaining)
    {
        var tracker = new RaidBuffTracker(confirmedOnly: true);
        tracker.ObserveSnapshots(Application(0) with
        {
            HasSourceStatusSnapshot = true, SourceStatuses = [new(2218, Provider, 0, remaining)],
        });
        Assert.Empty(tracker.ApplyConfirmed(Application(0)).SourceStatuses);
    }

    [Fact]
    public void PetSnapshotDoesNotReplaceItsOwnersBuffHistory()
    {
        var tracker = new RaidBuffTracker(confirmedOnly: true);
        tracker.Observe(Buff(0));
        tracker.ObserveSnapshots(new DamageActionPacket(1, Start.AddSeconds(1), 1,
            Player with { EntityId = 0x40000002, OwnerEntityId = Player.EntityId, IsPet = true, IsPlayer = false },
            100, "Pet hit", []) { HasSourceStatusSnapshot = true, SourceOwner = Player });
        Assert.Single(tracker.ApplyConfirmed(Application(2)).SourceStatuses);
    }

    [Fact]
    public void OwnerAttributedApplicationReadsItsRawPetsSnapshotWithoutChangingTheOwner()
    {
        var tracker = new RaidBuffTracker(confirmedOnly: true);
        tracker.Observe(Buff(0));
        var application = Application(1) with
        {
            SourceStatusActorId = 0x40000002, HasSourceStatusSnapshot = true,
            SourceStatuses = [new(786, Provider, 0, 3)],
        };
        tracker.ObserveSnapshots(application);
        Assert.Equal(786u, Assert.Single(tracker.ApplyConfirmed(application with { SeenAtUtc = Start.AddSeconds(2) }).SourceStatuses).StatusId);
        Assert.Equal(2218u, Assert.Single(tracker.ApplyConfirmed(Application(2)).SourceStatuses).StatusId);
    }

    [Fact]
    public void PendingStrongerBuffCannotOverwriteTheActualCapturedStrength()
    {
        var module = new DamageParsingModule();
        module.Process(new(1, Start, 1, Player, 100, "Hit", [new(0, Enemy, [new(0, 3, 0, 0, 0, 0, 1000)])])
        { DirectPotency = 100, CanCalibratePotency = true });
        module.ObserveStatus(Buff(0.5) with { StatusId = 0xB94, ActionId = 100, DurationSeconds = 0, Parameter = 6 });
        var dot = Application(1) with
        {
            PeriodicPotency = 20, HasSourceStatusSnapshot = true, SourceStatuses = [new(0xB94, Provider, 2, 3)],
        };
        module.ObserveStatus(dot);
        module.ObserveStatus(dot with
        {
            SeenAtUtc = Start.AddSeconds(2), ActionId = 0, DurationSeconds = 30,
            HasSourceStatusSnapshot = false, SourceStatuses = [],
        });
        module.ProcessPeriodicTick(new(100, Start.AddSeconds(3), Enemy, 0, "", 0, 600, null));
        var tick = Assert.Single(module.FlushPendingPeriodicTicks(Start.AddSeconds(3), true));
        Assert.Equal(1.02, tick.PeriodicCompatibilityEstimate!.Inputs!.DamageMultiplier, 6);
    }

    [Fact]
    public void HealingOnlyPacketsAlsoContributeRealBuffSnapshots()
    {
        var module = new DamageParsingModule();
        for (var i = 0; i < 20; i++)
            module.Process(new(i + 1, Start.AddMilliseconds(i), (uint)(i + 1), Player, 100, "Hit",
                [new(0, Enemy, [new(0, 3, 0, 0, 0, 0, 1000)])]) { DirectPotency = 100, CanCalibratePotency = true });
        module.Process(new(21, Start.AddSeconds(1), 21, Player, 100, "Heal", [new(0, Player, [new(0, 4, 0, 0, 0, 0, 1000)])])
        { HasSourceStatusSnapshot = true, SourceStatuses = [new(2218, Provider, 0, 3)] });
        var dot = Application(1.1) with { PeriodicPotency = 20 };
        module.ObserveStatus(dot);
        module.ObserveStatus(dot with { SeenAtUtc = Start.AddSeconds(2), ActionId = 0, DurationSeconds = 30 });
        module.ProcessPeriodicTick(new(100, Start.AddSeconds(3), Enemy, 0, "", 0, 600, null));
        var tick = Assert.Single(module.FlushPendingPeriodicTicks(Start.AddSeconds(3), true));
        Assert.Equal(0.03, tick.PeriodicCompatibilityEstimate!.DirectHit.BuffRate, 6);
    }

    [Theory]
    [InlineData(0xB94u, 2)]
    [InlineData(0x71Eu, 4)]
    [InlineData(0x839u, 2)]
    [InlineData(0x75Au, 3)]
    [InlineData(0xB5Fu, 236)]
    public void LandedBuffRecoversAnnouncedStrengthWhenStatusListReportsZero(uint status, ushort parameter)
    {
        var tracker = new RaidBuffTracker(confirmedOnly: true);
        var announced = Buff(0) with { StatusId = status, ActionId = 100, DurationSeconds = 0, Parameter = parameter };
        tracker.Observe(announced);
        Assert.Empty(tracker.ApplyConfirmed(Application(0.5)).SourceStatuses);
        tracker.Observe(announced with { SeenAtUtc = Start.AddSeconds(1), ActionId = 0, DurationSeconds = 20, Parameter = 0 });
        tracker.ObserveSnapshots(Application(2) with
        {
            HasSourceStatusSnapshot = true, SourceStatuses = [new(status, Provider, 0, 19)],
        });
        Assert.Equal(parameter, Assert.Single(tracker.ApplyConfirmed(Application(3)).SourceStatuses).Parameter);
    }

    [Fact]
    public void ConfirmedNonzeroStrengthWinsOverAnnouncedStrength()
    {
        var tracker = new RaidBuffTracker(confirmedOnly: true);
        tracker.Observe(Buff(0) with { StatusId = 0xB94, ActionId = 100, DurationSeconds = 0, Parameter = 6 });
        tracker.Observe(Buff(1) with { StatusId = 0xB94, Parameter = 2 });
        Assert.Equal(2, Assert.Single(tracker.ApplyConfirmed(Application(2)).SourceStatuses).Parameter);
    }

    [Fact]
    public void PendingParameterDoesNotUpgradeAnExistingBuffFromAnUnknownSnapshot()
    {
        var tracker = new RaidBuffTracker(confirmedOnly: true);
        tracker.Observe(Buff(0) with { StatusId = 0xB94, Parameter = 2 });
        tracker.Observe(Buff(1) with { StatusId = 0xB94, ActionId = 100, DurationSeconds = 0, Parameter = 6 });
        tracker.ObserveSnapshots(Application(1.5) with
        {
            HasSourceStatusSnapshot = true, SourceStatuses = [new(0xB94, Provider, 0, 18)],
        });
        Assert.Equal(2, Assert.Single(tracker.ApplyConfirmed(Application(1.6)).SourceStatuses).Parameter);
        tracker.Observe(Buff(2) with { StatusId = 0xB94, Parameter = 0 });
        Assert.Equal(6, Assert.Single(tracker.ApplyConfirmed(Application(3)).SourceStatuses).Parameter);
    }

    [Fact]
    public void CapturedRadiantFinaleConfirmationUsesTwoPercentInsteadOfTheUnknownDefault()
    {
        var module = new DamageParsingModule();
        module.Process(new(1, Start, 1, Player, 100, "Hit", [new(0, Enemy, [new(0, 3, 0, 0, 0, 0, 1000)])])
        { DirectPotency = 100, CanCalibratePotency = true });
        module.ObserveStatus(Buff(0.1) with { StatusId = 0xB94, ActionId = 100, DurationSeconds = 0, Parameter = 2 });
        module.ObserveStatus(Buff(0.2) with { StatusId = 0xB94, Parameter = 0 });
        var dot = Application(1) with
        {
            PeriodicPotency = 20, HasSourceStatusSnapshot = true,
            SourceStatuses = [new(0xB94, Provider, 0, 19)],
        };
        module.ObserveStatus(dot);
        module.ObserveStatus(dot with
        {
            SeenAtUtc = Start.AddSeconds(2), ActionId = 0, DurationSeconds = 30,
            HasSourceStatusSnapshot = false, SourceStatuses = [],
        });
        module.ProcessPeriodicTick(new(100, Start.AddSeconds(3), Enemy, 0, "", 0, 600, null));
        var tick = Assert.Single(module.FlushPendingPeriodicTicks(Start.AddSeconds(3), true));
        Assert.Equal(1.02, tick.PeriodicCompatibilityEstimate!.Inputs!.DamageMultiplier, 6);
    }

    [Theory]
    [InlineData(2218u, 2216u, (byte)15, 1.0, 1.60)]
    [InlineData(2216u, 2217u, (byte)35, 1.01, 1.64)]
    public void LandingSnapshotReplacesThePreviousSongForTheWholeApplication(
        uint oldSong, uint newSong, byte lowCrit, double damageMultiplier, double criticalMultiplier)
    {
        var module = new DamageParsingModule();
        for (var i = 0; i < 20; i++)
            module.Process(new(i + 1, Start.AddMilliseconds(i), (uint)(i + 1), Player, 100, "Hit",
                [new(0, Enemy, [new(0, 3, i < 5 ? (byte)0x20 : (byte)0, 0, 0, 0, 1000)])])
            { DirectPotency = 100, CanCalibratePotency = true, HasSourceStatusSnapshot = true });
        var dot = Application(1) with
        {
            PeriodicPotency = 20, CriticalRateLowByte = lowCrit,
            HasSourceStatusSnapshot = true, SourceStatuses = [new(oldSong, Provider, 0, 3)],
        };
        module.ObserveStatus(dot);
        module.ObserveStatus(dot with
        {
            ActionId = 0, SeenAtUtc = Start.AddSeconds(2), DurationSeconds = 30,
            SourceStatuses = [new(newSong, Provider, 0, 5)],
            HasTargetStatusSnapshot = true, TargetStatuses = [],
            BaseDamageLowByte = null, CriticalRateLowByte = null,
        });
        module.ProcessPeriodicTick(new(100, Start.AddSeconds(3), Enemy, 0, "", 0, 600, null));
        var first = Assert.Single(module.FlushPendingPeriodicTicks(Start.AddSeconds(3), true));
        Assert.Equal(damageMultiplier, first.PeriodicCompatibilityEstimate!.Inputs!.DamageMultiplier, 6);
        Assert.Equal(criticalMultiplier, first.PeriodicCompatibilityEstimate.Inputs.CriticalMultiplier, 6);
        Assert.Equal(0, first.PeriodicCompatibilityEstimate.DirectHit.BuffRate);
        module.ObserveStatus(Buff(4) with { StatusId = newSong, IsRemoval = true });
        module.ProcessPeriodicTick(new(101, Start.AddSeconds(6), Enemy, 0, "", 0, 600, null));
        var later = Assert.Single(module.FlushPendingPeriodicTicks(Start.AddSeconds(6), true));
        Assert.Equal(first.PeriodicCompatibilityEstimate, later.PeriodicCompatibilityEstimate);
    }

    [Theory]
    [InlineData("other-source")]
    [InlineData("other-target")]
    [InlineData("future")]
    [InlineData("expired")]
    [InlineData("removed")]
    [InlineData("source-less-removal")]
    [InlineData("superseded")]
    [InlineData("cleared")]
    public void UnrelatedOrInvalidatedAnnouncementsCannotSupplyStrength(string scenario)
    {
        var tracker = new RaidBuffTracker(confirmedOnly: true);
        var announced = Buff(scenario == "future" ? 2 : 0) with
        { StatusId = 0xB94, ActionId = 100, DurationSeconds = 0, Parameter = 2 };
        if (scenario == "other-source") announced = announced with { Source = Provider with { EntityId = 0x1003 } };
        if (scenario == "other-target") announced = announced with { Target = Enemy };
        tracker.Observe(announced);
        if (scenario == "cleared") tracker.Clear();
        if (scenario == "removed") tracker.Observe(Buff(0.5) with { StatusId = 0xB94, IsRemoval = true });
        if (scenario == "source-less-removal") tracker.Observe(Buff(0.5) with
        { StatusId = 0xB94, IsRemoval = true, Source = Provider with { EntityId = 0 } });
        if (scenario == "superseded") tracker.Observe(announced with { SeenAtUtc = Start.AddSeconds(0.5), Parameter = 0 });
        var at = scenario == "expired" ? 36 : 1;
        tracker.Observe(Buff(at) with { StatusId = 0xB94, Parameter = 0 });
        Assert.Equal(0, Assert.Single(tracker.ApplyConfirmed(Application(at)).SourceStatuses).Parameter);
    }

    [Theory]
    [MemberData(nameof(DamageJobRegressionMatrixTests.CurrentPeriodicDamageMatrix), MemberType = typeof(DamageJobRegressionMatrixTests))]
    public void EveryPeriodicWorkflowUsesTheSameConfirmedBuffPath(string _, uint job, uint status, double potency, bool ground)
    {
        var captured = Run(capturedBuffs: true);
        var explicitGain = Run(capturedBuffs: false);
        Assert.Equal(explicitGain.RawMeterAmount, captured.RawMeterAmount, 6);
        Assert.Equal(explicitGain.AttributedSource!.EntityId, captured.AttributedSource!.EntityId);
        if (ground)
        {
            Assert.False(captured.PeriodicMeterUsesEstimate);
            Assert.Equal(600, captured.RawMeterAmount);
        }
        else
        {
            Assert.True(captured.PeriodicMeterUsesEstimate);
            Assert.Equal(0.03, captured.PeriodicCompatibilityEstimate!.DirectHit.BuffRate, 6);
            Assert.Equal(explicitGain.PeriodicCompatibilityEstimate!.Inputs, captured.PeriodicCompatibilityEstimate.Inputs);
        }

        ParsedDamageEvent Run(bool capturedBuffs)
        {
            var module = new DamageParsingModule();
            var player = Player with { ClassJobId = job };
            for (var i = 0; i < 20; i++)
                module.Process(new(i + 1, Start.AddMilliseconds(i), (uint)(i + 1), player, 100, "Direct",
                    [new(0, Enemy, [new(0, 3, i < 8 ? (byte)0x20 : (byte)0, 0, 0, 0, 1000)])])
                { DirectPotency = 100, CanCalibratePotency = true, HasSourceStatusSnapshot = true });
            if (!capturedBuffs)
            {
                module.ObserveStatus(Buff(1) with { Target = player, DurationSeconds = 3 });
                module.ObserveStatus(Buff(1) with { Target = player, StatusId = 2216, DurationSeconds = 3 });
            }
            var application = Application(1) with
            {
                Source = player, StatusId = status, PeriodicPotency = potency, CriticalRateLowByte = 150,
                HasSourceStatusSnapshot = capturedBuffs,
                SourceStatuses = capturedBuffs ? [new(2218, Provider, 0, 3), new(2216, Provider, 0, 3)] : [],
            };
            module.ObserveStatus(application);
            module.ObserveStatus(application with
            {
                SeenAtUtc = Start.AddSeconds(2), ActionId = 0, DurationSeconds = 30,
                PeriodicPotency = null, CriticalRateLowByte = null,
                HasSourceStatusSnapshot = false, SourceStatuses = [],
            });
            // Later removal must not alter the already-confirmed DoT's strength.
            module.ObserveStatus(Buff(2.5) with { Target = player, IsRemoval = true });
            module.ProcessPeriodicTick(new(100, Start.AddSeconds(3), Enemy, ground ? status : 0, "", 0, 600,
                ground ? player : null));
            return Assert.Single(module.FlushPendingPeriodicTicks(Start.AddSeconds(3), true));
        }
    }

    [Fact]
    public void DirectPacketSnapshotFeedsConfirmationWhenTheApplicationSnapshotIsUnavailable()
    {
        var tracker = new PeriodicDamageTracker();
        var parser = new DirectDamageParser();
        for (var i = 0; i < 20; i++)
            tracker.ObserveDirectDamage(parser.Parse(new DamageActionPacket(i + 1, Start.AddMilliseconds(i), (uint)(i + 1),
                Player, 100, "Hit", [new(0, Enemy, [new(0, 3, 0, 0, 0, 0, 1000)])])
            { DirectPotency = 100, CanCalibratePotency = true }));
        tracker.ObserveDirectDamage(parser.Parse(new DamageActionPacket(21, Start.AddSeconds(1), 21, Player, 100, "Hit",
            [new(0, Enemy, [new(0, 3, 0, 0, 0, 0, 1030)])])
        { HasSourceStatusSnapshot = true, SourceStatuses = [new(2218, Provider, 0, 3)] }));
        var dot = Application(1.1) with { PeriodicPotency = 20 };
        tracker.Observe(dot);
        tracker.Observe(dot with { SeenAtUtc = Start.AddSeconds(2), ActionId = 0, DurationSeconds = 30 });
        var tick = Assert.Single(tracker.Process(new(100, Start.AddSeconds(3), Enemy, 0, "", 0, 600, null)));
        Assert.Equal(0.03, tick.PeriodicCompatibilityEstimate!.DirectHit.BuffRate, 6);
    }

    [Theory]
    [MemberData(nameof(DamageJobRegressionMatrixTests.CurrentPeriodicDamageMatrix), MemberType = typeof(DamageJobRegressionMatrixTests))]
    public void EveryPeriodicWorkflowUsesFreshSourceAndTargetSnapshotsAtLanding(string _, uint job, uint status, double potency, bool ground)
    {
        var module = new DamageParsingModule();
        var player = Player with { ClassJobId = job };
        for (var i = 0; i < 20; i++)
            module.Process(new(i + 1, Start.AddMilliseconds(i), (uint)(i + 1), player, 100, "Hit",
                [new(0, Enemy, [new(0, 3, 0, 0, 0, 0, 1000)])])
            { DirectPotency = 100, CanCalibratePotency = true, HasSourceStatusSnapshot = true });
        var dot = Application(1) with
        {
            Source = player, StatusId = status, PeriodicPotency = potency,
            HasSourceStatusSnapshot = true, SourceStatuses = [new(2218, Provider, 0, 5)],
        };
        module.ObserveStatus(dot);
        module.ObserveStatus(dot with
        {
            ActionId = 0, SeenAtUtc = Start.AddSeconds(2), DurationSeconds = 30,
            SourceStatuses = [new(2216, Provider, 0, 5)],
            HasTargetStatusSnapshot = true, TargetStatuses = [new(0x4C5, Provider, 0, 5)],
        });
        module.ProcessPeriodicTick(new(100, Start.AddSeconds(3), Enemy, ground ? status : 0, "", 0, 600,
            ground ? player : null));
        var tick = Assert.Single(module.FlushPendingPeriodicTicks(Start.AddSeconds(3), true));
        if (ground)
        {
            Assert.False(tick.PeriodicMeterUsesEstimate);
            Assert.Equal(600, tick.RawMeterAmount);
            return;
        }
        var estimate = Assert.IsType<PeriodicCompatibilityEstimate>(tick.PeriodicCompatibilityEstimate);
        Assert.Equal(0, estimate.DirectHit.BuffRate);
        Assert.Equal(0.12, estimate.Inputs!.CriticalRate, 6);
        Assert.Equal(1.4, estimate.Inputs.CriticalMultiplier, 6);
    }

    private static DamageStatusApplication Application(double seconds) => new(Enemy, Player, 50000,
        "DoT", 0, 100, "Application", Start.AddSeconds(seconds), 0, true, false, false);

    private static DamageStatusApplication Buff(double seconds) => new(Player, Provider, 2218,
        "Buff", 0, 0, "", Start.AddSeconds(seconds), 30, false, false, false);
}
