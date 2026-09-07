namespace BetterDeaths.Tests;

using System.Text.Json;
using BetterDeaths.DamageParsing;

public sealed class PeriodicPotencyCompatibilityTests
{
    private static readonly DateTime Start = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DamageActorIdentity Source = new(0x1001, "Caster", 0, "", true, 24) { Level = 100 };
    private static readonly DamageActorIdentity Target = new(0x40000001, "Enemy", 0, "", false, 0);

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(10, 0.0)]
    [InlineData(11, 0.10)]
    public void DiagnosticCriticalHistoryHasAnExplicitWarmUpBoundary(int samples, double expectedRate)
    {
        var tracker = new PeriodicDamageTracker();
        var rates = new DamageBaseRateSnapshot(0.15, 0.05);
        for (var i = 0; i < samples; i++) Hit(tracker, 1000, rates: rates);
        tracker.Observe(Application() with
        {
            CriticalRateLowByte = null,
            SourceBaseRates = rates,
            SourceStatuses = [new(0x312, Source, 0, 30)],
        });
        var tick = Tick(tracker);
        Assert.Equal(expectedRate, tick.PeriodicCompatibilityEstimate!.Inputs!.CriticalRate, 9);
        if (samples > 0) Assert.Equal(0.25, tick.PeriodicEstimateInputs!.CriticalRate, 9);
        Assert.Equal(600u, tick.Amount);
    }

    [Theory]
    [InlineData(0x2Bu, 0.75)]
    [InlineData(0x2Cu, 0.50)]
    [InlineData(0x31u, 1.15)]
    public void AttributeStatusUsesDiagnosticMultiplierWithoutChangingObservedDamage(uint status, double multiplier)
    {
        var tracker = new PeriodicDamageTracker();
        Hit(tracker, 1000);
        var snapshot = new DamageStatusSnapshot(status, Source, 0, 100);
        tracker.Observe(Application() with { SourceStatuses = [snapshot, snapshot] });
        var tick = Tick(tracker);
        var estimate = Assert.IsType<PeriodicCompatibilityEstimate>(tick.PeriodicCompatibilityEstimate);
        Assert.Equal(multiplier, estimate.Inputs!.DamageMultiplier, 10);
        Assert.Equal(10, estimate.Inputs.DamagePerPotency);
        Assert.Null(tick.SimulatedPeriodicAmount);
        Assert.Equal("Attribute-changing status", tick.PeriodicEstimateUnavailableReason);
        Assert.Equal(600u, tick.Amount);
        Assert.Equal(estimate.EstimatedDamage, tick.EffectiveMeterAmount);
    }

    [Theory]
    [InlineData(0x2Bu, 750u)]
    [InlineData(0x2Cu, 500u)]
    [InlineData(0x31u, 1150u)]
    public void AttributeCalibrationIsAllowedOnlyDuringTheInitialHistoryWindow(uint status, uint amount)
    {
        var tracker = new PeriodicDamageTracker();
        for (var index = 0; index < 11; index++) Hit(tracker, amount, status);
        Hit(tracker, 100000, status);
        tracker.Observe(Application());
        var inputs = Tick(tracker).PeriodicCompatibilityEstimate!.Inputs!;
        Assert.Equal(11, inputs.CalibrationSampleCount);
        Assert.Equal(10.0, inputs.DamagePerPotency, 8);
    }

    [Fact]
    public void UncalibratableHitsStillAdvanceTheWarmUpWindow()
    {
        var tracker = new PeriodicDamageTracker();
        for (var index = 0; index < 11; index++) Hit(tracker, 1000, canCalibrate: false);
        Hit(tracker, 750, 0x2B);
        tracker.Observe(Application());
        Assert.True(Tick(tracker).PeriodicCompatibilityEstimate!.UsedUnitCalibration);
    }

    [Fact]
    public void InvalidSamplesAgeTheBoundedHistoryAndIndexWraps()
    {
        var tracker = new PeriodicDamageTracker();
        Hit(tracker, 1000);
        for (var index = 0; index < 1000; index++) Hit(tracker, 1000, canCalibrate: false);
        tracker.Observe(Application());
        Assert.True(Tick(tracker).PeriodicCompatibilityEstimate!.UsedUnitCalibration);
        Hit(tracker, 750, 0x2B);
        tracker.Observe(Application() with { SeenAtUtc = Start.AddSeconds(5) });
        Assert.Equal(10.0, Tick(tracker, 8).PeriodicCompatibilityEstimate!.Inputs!.DamagePerPotency, 10);
    }

    [Fact]
    public void DiagnosticStackingAndCalibrationUseAdditionWhilePhysicalInputsKeepMultiplication()
    {
        var tracker = new PeriodicDamageTracker();
        DamageStatusSnapshot[] buffs = [new(0x4A1, Source, 0, 30), new(0x511, Source, 0, 30)];
        Hit(tracker, 1100, statuses: buffs);
        tracker.Observe(Application() with { SourceStatuses = buffs });
        var tick = Tick(tracker);
        Assert.Equal(1.1, tick.PeriodicCompatibilityEstimate!.Inputs!.DamageMultiplier, 10);
        Assert.Equal(10.0, tick.PeriodicCompatibilityEstimate.Inputs.DamagePerPotency, 10);
        Assert.Equal(1.1025, tick.PeriodicEstimateInputs!.DamageMultiplier, 10);
        Assert.Equal(11 / 1.1025, tick.PeriodicEstimateInputs.DamagePerPotency, 10);
    }

    [Fact]
    public void OpeningSageDiagnosticPreservesTheVersionedProfileAndLabelsTheUnitFallback()
    {
        var tracker = new PeriodicDamageTracker();
        var application = Application() with
        {
            StatusId = 0xA38,
            PeriodicPotency = 90,
            BaseDamageLowByte = 128,
            CriticalRateLowByte = 251,
            SourceBaseRates = new(0.251, 0.106),
        };
        tracker.Observe(application);
        var estimate = Tick(tracker).PeriodicCompatibilityEstimate!;
        Assert.Equal(90, estimate.CapturedPotency);
        Assert.Equal(85, estimate.Inputs!.Potency);
        Assert.Equal(85, estimate.Inputs.BaseDamage);
        Assert.Equal(99.0, Math.Floor(estimate.EstimatedDamage));
        Assert.True(estimate.UsedUnitCalibration);
        Hit(tracker, 1000, rates: application.SourceBaseRates);
        Assert.Equal(estimate, Tick(tracker, 6).PeriodicCompatibilityEstimate);
    }

    [Theory]
    [InlineData(0x2Bu, 94.37963185274108, 4, 0xA27u, 0u, 0.78, 6404)]
    [InlineData(0x2Cu, 94.11408492600579, 163, 0x4A1u, 0x511u, 0.60, 5027)]
    public void CapturedDeathPenaltySnapshotsReconstructTheAuditedBaseAmount(uint penalty, double scale,
        byte lowByte, uint buff, uint secondBuff, double multiplier, double expectedBase)
    {
        var tracker = new PeriodicDamageTracker();
        // Seed the captured scale without rounding it to integer damage per potency.
        var scaled = scale * 100;
        Hit(tracker, (uint)Math.Floor(scaled), potency: Math.Floor(scaled) / scale);
        DamageStatusSnapshot[] statuses = secondBuff == 0
            ? [new(penalty, Source, 0, 100), new(buff, Source, 0, 30)]
            : [new(penalty, Source, 0, 100), new(buff, Source, 0, 30), new(secondBuff, Source, 0, 30)];
        tracker.Observe(Application() with
        {
            PeriodicPotency = 85,
            BaseDamageLowByte = lowByte,
            CriticalRateLowByte = 219,
            SourceStatuses = statuses,
        });
        var inputs = Tick(tracker).PeriodicCompatibilityEstimate!.Inputs!;
        Assert.Equal(multiplier, inputs.DamageMultiplier, 10);
        Assert.Equal(expectedBase, inputs.BaseDamage);
        Assert.Equal(0.219, inputs.CriticalRate, 10);
        Assert.Equal(1.569, inputs.CriticalMultiplier, 10);
    }

    [Fact]
    public void ConfirmationFreezesItsOwnCalibrationAndDuplicateAcknowledgementsDoNotResample()
    {
        var tracker = new PeriodicDamageTracker();
        var pending = Application() with { ActionId = 100, DurationSeconds = 0 };
        tracker.Observe(pending);
        Hit(tracker, 1000);
        var confirmation = pending with { ActionId = 0, SeenAtUtc = Start.AddSeconds(2), DurationSeconds = 30 };
        tracker.Observe(confirmation);
        Hit(tracker, 9000);
        tracker.Observe(confirmation with { SeenAtUtc = Start.AddSeconds(2.1) });
        var tick = Tick(tracker);
        Assert.Equal(10.0, tick.PeriodicCompatibilityEstimate!.Inputs!.DamagePerPotency);
        Assert.Null(tick.PeriodicEstimateInputs);
        Assert.False(tick.PeriodicCompatibilityEstimate.UsedUnitCalibration);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfirmationFreezesItsOwnBuffContextWithoutChangingThePhysicalSnapshot(bool expires)
    {
        var tracker = new PeriodicDamageTracker();
        Hit(tracker, 1000);
        DamageStatusSnapshot[] buffs = [new(0x4A1, Source, 0, 30)];
        var pending = Application() with
        {
            ActionId = 100,
            DurationSeconds = 0,
            SourceStatuses = expires ? buffs : [],
        };
        tracker.Observe(pending);
        var confirmation = pending with
        {
            ActionId = 0,
            SeenAtUtc = Start.AddSeconds(2),
            DurationSeconds = 30,
            SourceStatuses = expires ? [] : buffs,
        };
        if (!expires)
        {
            tracker.Observe(new(Source, Source, 0x4A1, "Buff", 0, 0, "", Start.AddSeconds(1.5), 30, false, false, false));
        }
        tracker.Observe(confirmation);
        tracker.Observe(confirmation with { SeenAtUtc = Start.AddSeconds(2.1), SourceStatuses = [] });
        var tick = Tick(tracker);
        Assert.Equal(expires ? 1 : 1.05, tick.PeriodicCompatibilityEstimate!.Inputs!.DamageMultiplier, 10);
        Assert.Equal(expires ? 1.05 : 1, tick.PeriodicEstimateInputs!.DamageMultiplier, 10);
    }

    [Fact]
    public void BrinkReplacementDoesNotAlsoApplyStaleTrackedWeakness()
    {
        var tracker = new PeriodicDamageTracker();
        Hit(tracker, 1000);
        tracker.Observe(Application() with
        {
            SourceStatuses = [new(0x2B, Source, 0, 80), new(0x2C, Source, 0, 100),
                new(0x4A1, Source, 0, 20), new(0x511, Source, 0, 20)],
        });
        Assert.Equal(0.6, Tick(tracker).PeriodicCompatibilityEstimate!.Inputs!.DamageMultiplier, 10);
    }

    [Fact]
    public void UnknownDamageDownStrengthStillPreventsDiagnosticEstimation()
    {
        var tracker = new PeriodicDamageTracker();
        Hit(tracker, 1000);
        tracker.Observe(Application() with { SourceStatuses = [new(0xB5F, Source, 0, 30)] });
        var tick = Tick(tracker);
        Assert.Null(tick.PeriodicCompatibilityEstimate);
        Assert.Equal(600u, tick.Amount);
    }

    [Fact]
    public void RefreshHistoryRetainsItsCompatibilitySnapshot()
    {
        var tracker = new PeriodicDamageTracker();
        Hit(tracker, 1000);
        tracker.Observe(Application());
        var old = Tick(tracker).PeriodicCompatibilityEstimate;
        Hit(tracker, 9000);
        tracker.Observe(Application() with { ActionId = 100, SeenAtUtc = Start.AddSeconds(6), DurationSeconds = 0 });
        Assert.Equal(old, Tick(tracker, 7).PeriodicCompatibilityEstimate);
        tracker.Observe(Application() with { SeenAtUtc = Start.AddSeconds(8) });
        Assert.Equal(50.0, Tick(tracker, 11).PeriodicCompatibilityEstimate!.Inputs!.DamagePerPotency);
    }

    [Fact]
    public void ExplicitResetAndActorContextChangeClearCompatibilityCalibration()
    {
        var tracker = new PeriodicDamageTracker();
        Hit(tracker, 1000);
        tracker.Clear(preserveCalibration: true);
        tracker.Observe(Application());
        Assert.False(Tick(tracker).PeriodicCompatibilityEstimate!.UsedUnitCalibration);
        tracker.ClearCalibration();
        tracker.Observe(Application() with { SeenAtUtc = Start.AddSeconds(5) });
        Assert.True(Tick(tracker, 8).PeriodicCompatibilityEstimate!.UsedUnitCalibration);
        Hit(tracker, 1000);
        tracker.Observe(Application() with { SeenAtUtc = Start.AddSeconds(9), Source = Source with { Level = 90 } });
        Assert.True(Tick(tracker, 12).PeriodicCompatibilityEstimate!.UsedUnitCalibration);
    }

    [Fact]
    public void FallbackCoverageSurvivesCompactEncounterSerialization()
    {
        var module = new DamageParsingModule();
        module.ObserveStatus(Application());
        module.ProcessPeriodicTick(new(1, Start.AddSeconds(3), Target, 0, "", 0, 600, null));
        module.FlushPendingPeriodicTicks(Start.AddSeconds(3.1));
        var saved = module.EndEncounter(Start.AddSeconds(4), "Test")! with { Events = [] };
        var restored = JsonSerializer.Deserialize<DamageEncounterSnapshot>(JsonSerializer.Serialize(saved))!;
        var diagnostic = Assert.Single(Assert.Single(restored.Diagnostics.PeriodicAllocations).IndependentEstimates);
        Assert.Equal(1, diagnostic.CompatibilityFallbackTickCount);
        Assert.Equal(1, diagnostic.CompatibilityTickCount);
        Assert.Equal(600.0, restored.EffectiveMeterDamage);
    }

    private static void Hit(PeriodicDamageTracker tracker, uint amount, uint status = 0, bool canCalibrate = true,
        DamageStatusSnapshot[]? statuses = null, double potency = 100, DamageBaseRateSnapshot? rates = null)
    {
        var packet = new DamageActionPacket(1, Start, 1, Source, 100, "Spell",
            [new(0, Target, [new(0, 3, 0, 0, 0, 0, amount)])])
        {
            DirectPotency = potency,
            CanCalibratePotency = canCalibrate,
            ActionCategoryId = 2,
            SourceBaseRates = rates ?? new(0.219, 0.05),
            SourceStatuses = statuses ?? (status == 0 ? [] : [new(status, Source, 0, 100)]),
        };
        tracker.ObserveDirectDamage(new DirectDamageParser().Parse(packet));
    }

    private static DamageStatusApplication Application() =>
        new(Target, Source, 1871, "Dia", 0, 0, "", Start.AddSeconds(1), 30, true, false, false)
        { PeriodicPotency = 50, ActionCategoryId = 2, DamageType = 5, HasSourceStatusSnapshot = true };

    private static ParsedDamageEvent Tick(PeriodicDamageTracker tracker, double seconds = 3) =>
        Assert.Single(tracker.Process(new(1, Start.AddSeconds(seconds), Target, 0, "", 0, 600, null)));
}
