namespace BetterDeaths.Tests;

using BetterDeaths.DamageParsing;

public sealed class PeriodicRemovalTests
{
    private static readonly DateTime Start = new(2026, 9, 5, 3, 9, 0, DateTimeKind.Utc);
    private static readonly DamageActorIdentity Source = new(0x1001, "Caster", 0, "", true, 25) { Level = 100 };
    private static readonly DamageActorIdentity Target = new(0x40000001, "Enemy", 0, "", false, 0);
    private static readonly DamageActorIdentity Bard = new(0x1002, "Bard", 0, "", true, 23);

    [Theory]
    [InlineData(2.1132585, 32.0758926, 32.0765444, 32.8336868, (byte)252, false)]
    [InlineData(62.2587076, 91.5143317, 92.2853740, 92.2858048, (byte)75, true)]
    public void ExpiringPreviousApplicationPreservesCapturedReplacement(
        double oldConfirmedSeconds, double castSeconds, double removalSeconds,
        double confirmationSeconds, byte damageByte, bool hasSong)
    {
        var module = CreateModule();
        var old = Application(oldConfirmedSeconds - 0.7);
        module.ObserveStatus(old);
        module.ObserveStatus(Confirmation(old, oldConfirmedSeconds));
        var pending = Application(castSeconds) with
        {
            BaseDamageLowByte = damageByte,
            SourceStatuses = hasSong ? [new DamageStatusSnapshot(0x8A9, Bard, 0, 30)] : [],
        };
        module.ObserveStatus(pending);
        module.ObserveStatus(Removal(pending, removalSeconds));
        Calibrate(module, 2, removalSeconds + 0.0001, 99900);
        module.ObserveStatus(Confirmation(pending, confirmationSeconds));

        var tick = Tick(module, confirmationSeconds + 1);
        var inputs = Assert.IsType<PeriodicDamageEstimateInputs>(tick.PeriodicEstimateInputs);
        Assert.Equal(Source.EntityId, tick.Source.EntityId);
        Assert.Equal(damageByte, inputs.DamageLowByte);
        Assert.Equal((byte)12, tick.CriticalRateLowByte);
        Assert.Equal(131.04, inputs.DamagePerPotency, 6);
        Assert.Equal(1, inputs.CalibrationSampleCount);
        Assert.Equal(0.267, inputs.CriticalRate, 6);
        Assert.Equal(0.3, inputs.DirectHitRate, 6);
        Assert.Equal(hasSong ? 1.01 : 1.0, inputs.DamageMultiplier, 6);
        Assert.Equal(hasSong ? 8267.0 : 8188.0, inputs.BaseDamage);
        Assert.Equal(pending.SourceStatuses, tick.SourceStatuses);
        Assert.Equal(600u, tick.Amount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovedOldEffectCannotTickWhileReplacementWaitsForConfirmation(bool unknownRemovalSource)
    {
        var module = CreateModule();
        var old = Application(0.1);
        module.ObserveStatus(old);
        module.ObserveStatus(Confirmation(old, 1));
        var pending = Application(10) with { BaseDamageLowByte = 75 };
        module.ObserveStatus(pending);
        var removal = Removal(pending, 11);
        if (unknownRemovalSource)
        {
            removal = removal with { Source = Source with { EntityId = 0 } };
        }
        module.ObserveStatus(removal);

        Assert.Equal(DamageAttributionQuality.Unattributed, Tick(module, 12).AttributionQuality);
        module.ObserveStatus(Confirmation(pending, 13));
        Assert.Equal((byte)75, Tick(module, 15, 101).PeriodicEstimateInputs!.DamageLowByte);
    }

    [Fact]
    public void UnconfirmedReplacementNeverBecomesAnActiveDot()
    {
        var module = CreateModule();
        var old = Application(0.1);
        module.ObserveStatus(old);
        module.ObserveStatus(Confirmation(old, 1));
        module.ObserveStatus(Application(10));
        module.ObserveStatus(Removal(old, 11));

        Assert.Equal(DamageAttributionQuality.Unattributed, Tick(module, 12).AttributionQuality);
        Assert.Equal(DamageAttributionQuality.Unattributed, Tick(module, 80, 101).AttributionQuality);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovalAfterConfirmationStillRetiresTheNewEffect(bool unknownRemovalSource)
    {
        var module = CreateModule();
        var old = Application(0.1);
        module.ObserveStatus(old);
        module.ObserveStatus(Confirmation(old, 1));
        var pending = Application(10);
        module.ObserveStatus(pending);
        module.ObserveStatus(Removal(old, 10.5));
        module.ObserveStatus(Confirmation(pending, 11));
        Assert.Equal(Source.EntityId, Tick(module, 12).Source.EntityId);
        var removal = Removal(pending, 12.01);
        if (unknownRemovalSource)
        {
            removal = removal with { Source = Source with { EntityId = 0 } };
        }
        module.ObserveStatus(removal);

        Assert.Equal(DamageAttributionQuality.Unattributed, Tick(module, 15, 101).AttributionQuality);
    }

    [Fact]
    public void RemovalForAnotherSourceDoesNotRetireTheConfirmedEffect()
    {
        var module = CreateModule();
        var application = Application(0.1);
        module.ObserveStatus(application);
        module.ObserveStatus(Confirmation(application, 1));
        module.ObserveStatus(Removal(application, 2) with { Source = Source with { EntityId = 0x1003 } });

        Assert.Equal(Source.EntityId, Tick(module, 3).Source.EntityId);
    }

    [Theory]
    [InlineData(0x01F5u)]
    [InlineData(0x1234u)]
    public void GroundEffectsKeepTheirExistingRemovalBehavior(uint statusId)
    {
        var tracker = new PeriodicDamageTracker();
        var application = Application(0.1) with { StatusId = statusId, Target = Source };
        tracker.Observe(application);
        var tick = new PeriodicDamageTick(100, Start.AddSeconds(3), Target, statusId, "Ground effect", 0, 600, null);
        Assert.Equal(Source.EntityId, Assert.Single(tracker.Process(tick)).Source.EntityId);
        tracker.Observe(Removal(application, 4));

        var later = Assert.Single(tracker.Process(tick with { PacketSequence = 101, SeenAtUtc = Start.AddSeconds(6) }));
        Assert.Equal(DamageAttributionQuality.Unattributed, later.AttributionQuality);
    }

    private static DamageParsingModule CreateModule()
    {
        var module = new DamageParsingModule();
        Calibrate(module, 1, 0, 13104);
        return module;
    }

    private static void Calibrate(DamageParsingModule module, long sequence, double seconds, uint amount) =>
        module.Process(new DamageActionPacket(sequence, Start.AddSeconds(seconds), (uint)sequence,
            Source, 100, "Spell", [new DamageActionTarget(0, Target, [new(0, 3, 0, 0, 0, 0, amount)])])
        {
            DirectPotency = 100,
            CanCalibratePotency = true,
            HasSourceStatusSnapshot = true,
            SourceBaseRates = new(0.267, 0.3),
        });

    private static DamageStatusApplication Application(double seconds) =>
        new(Target, Source, 3871, "High Thunder", 0, 36986, "High Thunder",
            Start.AddSeconds(seconds), 0, true, false, false)
        {
            PeriodicPotency = 60,
            BaseDamageLowByte = 252,
            CriticalRateLowByte = 12,
            HasSourceStatusSnapshot = true,
            HasTargetStatusSnapshot = true,
        };

    private static DamageStatusApplication Confirmation(DamageStatusApplication pending, double seconds) => pending with
    {
        ActionId = 0,
        SeenAtUtc = Start.AddSeconds(seconds),
        DurationSeconds = 30,
        BaseDamageLowByte = null,
        CriticalRateLowByte = null,
        PeriodicPotency = null,
        SourceStatuses = [],
        HasSourceStatusSnapshot = false,
        HasTargetStatusSnapshot = false,
    };

    private static DamageStatusApplication Removal(DamageStatusApplication application, double seconds) =>
        Confirmation(application, seconds) with { DurationSeconds = 0, IsRemoval = true };

    private static ParsedDamageEvent Tick(DamageParsingModule module, double seconds, long sequence = 100)
    {
        var tick = new PeriodicDamageTick(sequence, Start.AddSeconds(seconds), Target, 0, "", 0, 600, null);
        module.ProcessPeriodicTick(tick);
        return Assert.Single(module.FlushPendingPeriodicTicks(tick.SeenAtUtc.AddMilliseconds(50)));
    }
}
