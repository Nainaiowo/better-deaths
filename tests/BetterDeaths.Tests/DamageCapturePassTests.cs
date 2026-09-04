namespace BetterDeaths.Tests;

using BetterDeaths.DamageParsing;

public sealed class DamageCapturePassTests
{
    private static readonly DateTime Start = new(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DamageActorIdentity Player = new(0x1001, "Player", 0, "", true, 31);
    private static readonly DamageActorIdentity Enemy = new(0x40000001, "Enemy", 0, "", false, 0);

    [Theory]
    [InlineData(3, 65535, 0, 0, 0, 0x15, 65535, false, false, false)]
    [InlineData(3, 0, 1, 0x40, 0x20, 0x24, 65536, true, false, false)]
    [InlineData(3, 5678, 2, 0x40, 0x60, 0x35, 136750, true, true, false)]
    [InlineData(3, 5678, 2, 0x80, 0x40, 0x44, 5678, false, true, true)]
    [InlineData(5, 125, 3, 0x40, 0x60, 0xB5, 196733, true, true, false)]
    [InlineData(6, 999, 255, 0, 0, 0x61, 999, false, false, false)]
    [InlineData(3, 65535, 255, 0xC0, 0x60, 0xFF, 16777215, true, true, true)]
    public void DecodesDamageFieldBoundaries(
        byte type, uint value, byte high, byte flags, byte hit, byte kind,
        uint amount, bool critical, bool directHit, bool sourceEntry)
    {
        var packet = Packet(1) with
        {
            Targets = [new DamageActionTarget(0, Enemy, [new DamageActionEffect(0, type, hit, kind, high, flags, value)])],
        };
        var parsed = Assert.Single(new DirectDamageParser().Parse(packet));

        Assert.Equal(amount, parsed.Amount);
        Assert.Equal(critical, parsed.Critical);
        Assert.Equal(directHit, parsed.DirectHit);
        Assert.Equal(sourceEntry, parsed.IsSourceEntry);
        Assert.Equal(sourceEntry ? Player : Enemy, parsed.Target);
        Assert.Equal(Enemy, parsed.PacketTarget);
        Assert.Equal(type == 5, parsed.Blocked);
        Assert.Equal(type == 6, parsed.Parried);
        Assert.Equal(kind & 0x0F, parsed.DamageType);
        Assert.Equal(kind >> 4, parsed.ElementType);
    }

    [Fact]
    public void EarlierDamageArrivingLateExtendsTheStartNotJustTheTotal()
    {
        var module = new DamageParsingModule();
        module.Process(Packet(1, seconds: 2));
        module.Process(Packet(2, seconds: 0));
        module.Process(Packet(3, seconds: 5));

        var snapshot = module.GetCurrentEncounter()!;
        Assert.Equal(Start, snapshot.MeterStartedAtUtc);
        Assert.Equal(5.0, snapshot.DurationSeconds);
        Assert.Equal(600.0, snapshot.DamagePerSecond);
        Assert.Equal(Start, module.EndEncounter(Start.AddSeconds(10), "test")!.MeterStartedAtUtc);
    }

    [Fact]
    public void DeferredPeriodicDamageCanExtendTheEncounterStart()
    {
        var module = new DamageParsingModule();
        module.Process(Packet(1, seconds: 2));
        PeriodicTick(module, seconds: 0.5);
        module.Process(Packet(2, seconds: 5));

        Assert.Equal(Start.AddSeconds(0.5), module.GetCurrentEncounter()!.MeterStartedAtUtc);
        Assert.Equal(4.5, module.GetCurrentEncounter()!.DurationSeconds);
    }

    [Fact]
    public void LateDamageDoesNotDiscardAnEarlierCastStart()
    {
        var module = new DamageParsingModule();
        module.ObserveOffensiveCast(Start, Start.AddMilliseconds(100));
        module.Process(Packet(1, seconds: 2));
        module.Process(Packet(2, seconds: 1));

        Assert.Equal(Start, module.GetCurrentEncounter()!.MeterStartedAtUtc);
    }

    [Fact]
    public void LateNonAlliedDamageDoesNotMoveTheMeterStart()
    {
        var module = new DamageParsingModule();
        module.Process(Packet(1, seconds: 2));
        module.Process(Packet(2, seconds: 0) with { Source = Enemy, Targets = [Target(Player)] });
        module.Process(Packet(3, seconds: 5));

        Assert.Equal(Start.AddSeconds(2), module.GetCurrentEncounter()!.MeterStartedAtUtc);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DuplicatedDamageDoesNotBecomeAnotherCalibrationSample(bool automaticStart)
    {
        var module = new DamageParsingModule();
        var first = Packet(1, amount: 1000);
        module.Process(first, automaticStart);
        module.Process(Packet(2, seconds: 0.1, amount: 3000), automaticStart);
        module.Process(Packet(3, seconds: 0.2, amount: 5000), automaticStart);
        var duplicate = module.Process(first with { PacketSequence = 99, SeenAtUtc = Start.AddSeconds(0.3) }, automaticStart);
        Assert.Empty(duplicate);
        module.ObserveStatus(new DamageStatusApplication(Enemy, Player, 0x500, "Periodic", 0, 100, "Action",
            Start.AddSeconds(0.4), 30, true, false, false)
        {
            PeriodicPotency = 20,
            CriticalRateLowByte = 0,
            HasSourceStatusSnapshot = true,
        });
        module.SetCombatActive(true, Start.AddSeconds(0.5));

        var tick = PeriodicTick(module, seconds: 3);
        Assert.Equal(30.0, tick.PeriodicEstimateInputs!.DamagePerPotency);
        Assert.Equal(600.0, tick.SimulatedPeriodicAmount);
        Assert.Equal(1, module.GetCurrentEncounter()!.DuplicateEventCount);
        Assert.Equal(9600ul, module.GetCurrentEncounter()!.TotalDamage);
    }

    [Fact]
    public void DuplicatePacketCannotRestoreAnOldHpSnapshot()
    {
        var module = new DamageParsingModule();
        var packet = Packet(1, amount: 6000) with
        {
            Targets = [Target(Enemy, 6000) with { TargetHp = new DamageHpSnapshot(100, 0, 1000) }],
        };
        module.Process(packet);
        module.ObserveEffectResult(new DamageEffectResult(Start.AddSeconds(0.1), 1, Enemy, new DamageHpSnapshot(0, 0, 1000)));
        module.Process(packet with { PacketSequence = 99, SeenAtUtc = Start.AddSeconds(0.2) });

        var tick = PeriodicTick(module, seconds: 0.5);
        Assert.Equal(0.0, tick.EffectiveMeterAmount);
        Assert.Equal(DamageResolutionQuality.KnownZeroHp, tick.ResolutionQuality);
        Assert.Equal(600.0, tick.RawMeterAmount);
    }

    [Fact]
    public void ARepeatedTargetDoesNotDropANewTargetInTheSameAction()
    {
        var module = new DamageParsingModule();
        var first = Packet(1);
        module.Process(first);
        var otherTarget = Enemy with { EntityId = 0x40000002 };
        var second = first with
        {
            PacketSequence = 2,
            Targets = [Target(Enemy), Target(otherTarget) with { TargetIndex = 1 }],
        };
        var newEvents = module.Process(second);

        Assert.Equal(otherTarget.EntityId, Assert.Single(newEvents).Target.EntityId);
        Assert.Equal(2000ul, module.GetCurrentEncounter()!.TotalDamage);
        Assert.Equal(1, module.GetCurrentEncounter()!.DuplicateEventCount);
    }

    [Fact]
    public void StatusOnlyPacketWithTheSameActionSequenceStillApplies()
    {
        var module = new DamageParsingModule();
        module.Process(Packet(1));
        var application = new DamageStatusApplication(Enemy, Player, 0x500, "Periodic", 0, 100, "Action",
            Start.AddSeconds(0.1), 30, true, false, false)
        {
            PeriodicPotency = 20,
            CriticalRateLowByte = 0,
            HasSourceStatusSnapshot = true,
        };
        Assert.Empty(module.Process(Packet(1, seconds: 0.1) with
        {
            PacketSequence = 2,
            Targets = [new DamageActionTarget(0, Enemy, [new DamageActionEffect(0, 14, 0, 0, 0, 0, 0x500)])],
            StatusApplications = [application],
        }));

        var tick = PeriodicTick(module, seconds: 3);
        Assert.Equal(0x500u, tick.StatusId);
        Assert.Equal(200.0, tick.SimulatedPeriodicAmount);
        Assert.Equal(0, module.GetCurrentEncounter()!.DuplicateEventCount);
    }

    [Fact]
    public void EventIdentityCanBeReusedAfterAnEncounterEnds()
    {
        var module = new DamageParsingModule();
        module.Process(Packet(1));
        module.EndEncounter(Start.AddSeconds(10), "test");
        Assert.Single(module.Process(Packet(1, seconds: 20)));

        var snapshot = module.GetCurrentEncounter()!;
        Assert.Equal(1000ul, snapshot.TotalDamage);
        Assert.Equal(0, snapshot.DuplicateEventCount);
        Assert.Equal(Start.AddSeconds(20), snapshot.MeterStartedAtUtc);
    }

    [Fact]
    public void NearbyAlliancePlayersContinueAfterLocalDeathAndCombatFlagClear()
    {
        var module = new DamageParsingModule();
        for (var index = 0; index < 24; index++)
        {
            var source = Player with
            {
                EntityId = (uint)(0x1001 + index),
                Name = index < 2 ? "Same name" : $"Player {index}",
                IsPartyMember = index < 8,
            };
            module.Process(Packet(index + 1, seconds: index) with { Source = source });
            if (index == 0)
            {
                module.RecordDeath(source);
                module.SetCombatActive(false, Start.AddSeconds(0.5));
            }
        }

        var snapshot = module.GetCurrentEncounter()!;
        Assert.Equal(24, snapshot.Sources.Count);
        Assert.All(snapshot.Sources, source => Assert.True(DamageMeterCombatantPolicy.ShouldDisplay(source.Source)));
        Assert.Equal(24000.0, snapshot.ObservedMeterDamage);
        Assert.Equal(23.0, snapshot.DurationSeconds);
        Assert.Equal(1, snapshot.Sources.Sum(source => source.Deaths));
    }

    [Fact]
    public void SameJobPetsKeepDistinctOwnersAfterAnIdentityDisappears()
    {
        var module = new DamageParsingModule();
        var secondOwner = Player with { EntityId = 0x1002, Name = "Second owner" };
        var firstPet = new DamageActorIdentity(0x3001, "Pet", Player.EntityId, Player.Name, false, 0) { IsPet = true };
        var secondPet = firstPet with { EntityId = 0x3002, OwnerEntityId = secondOwner.EntityId, OwnerName = secondOwner.Name };
        module.Process(Packet(1) with { Source = firstPet, SourceOwner = Player });
        module.Process(Packet(2, seconds: 1) with { Source = secondPet, SourceOwner = secondOwner });
        module.Process(Packet(3, seconds: 2) with
        {
            Source = new DamageActorIdentity(firstPet.EntityId, "Entity 00003001", 0, "", false, 0),
        });

        var sources = module.GetCurrentEncounter()!.Sources;
        Assert.Equal(2, sources.Count);
        Assert.Equal(2000.0, sources.Single(source => source.Source.EntityId == Player.EntityId).ObservedMeterDamage);
        Assert.Equal(1000.0, sources.Single(source => source.Source.EntityId == secondOwner.EntityId).ObservedMeterDamage);
    }

    private static DamageActionPacket Packet(long sequence, double seconds = 0, uint amount = 1000) => new(
        sequence, Start.AddSeconds(seconds), (uint)sequence, Player, 100, "Action", [Target(Enemy, amount)])
    {
        DirectPotency = 100,
        CanCalibratePotency = true,
        SourceBaseRates = new DamageBaseRateSnapshot(0.05, 0),
        HasSourceStatusSnapshot = true,
    };

    private static DamageActionTarget Target(DamageActorIdentity target, uint amount = 1000) =>
        new(0, target, [new DamageActionEffect(0, 3, 0, 0, 0, 0, amount)]);

    private static ParsedDamageEvent PeriodicTick(DamageParsingModule module, double seconds)
    {
        var time = Start.AddSeconds(seconds);
        module.ProcessPeriodicTick(new PeriodicDamageTick(100, time, Enemy, 0, "", 0, 600, null));
        return Assert.Single(module.FlushPendingPeriodicTicks(time.AddMilliseconds(50)));
    }
}
