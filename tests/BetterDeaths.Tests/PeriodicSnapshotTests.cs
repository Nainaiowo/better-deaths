namespace BetterDeaths.Tests;

using BetterDeaths.DamageParsing;

public sealed class PeriodicSnapshotTests
{
    private static readonly DateTime Start = new(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DamageActorIdentity Source = new(0x1001, "Source", 0, "", true, 22);
    private static readonly DamageActorIdentity Target = new(0x40000001, "Target", 0, "", false, 0);

    [Fact]
    public void EnrichedSnapshotUsesCapturedBytesWithoutResamplingCalibration()
    {
        var module = new DamageParsingModule();
        Calibrate(module, 1000);
        var application = Application();
        module.ObserveStatus(application);
        Calibrate(module, 9000, sequence: 2, seconds: 0.2);
        module.ObserveStatus(application with
        {
            SeenAtUtc = Start.AddSeconds(0.3),
            BaseDamageLowByte = 17,
            CriticalRateLowByte = 200,
        });

        var tick = Assert.Single(Tick(module));
        Assert.Equal(10.0, tick.PeriodicEstimateInputs!.DamagePerPotency);
        Assert.Equal(273.0, tick.PeriodicEstimateInputs.BaseDamage);
        Assert.Equal(0.2, tick.PeriodicEstimateInputs.CriticalRate, 6);
        Assert.Equal(600u, tick.Amount);
    }

    [Fact]
    public void LatePotencyMetadataUsesTheOriginalCalibration()
    {
        var module = new DamageParsingModule();
        Calibrate(module, 1000);
        var application = Application() with { PeriodicPotency = null };
        module.ObserveStatus(application);
        Calibrate(module, 9000, sequence: 2, seconds: 0.2);
        module.ObserveStatus(application with { SeenAtUtc = Start.AddSeconds(0.3), PeriodicPotency = 20 });

        var tick = Assert.Single(Tick(module));
        Assert.Equal(10.0, tick.PeriodicEstimateInputs!.DamagePerPotency);
        Assert.Equal(200.0, tick.PeriodicEstimateInputs.BaseDamage);
    }

    [Fact]
    public void DuplicateObservationCannotHideMissingApplicationTimeCalibration()
    {
        var module = new DamageParsingModule();
        var application = Application();
        module.ObserveStatus(application);
        Calibrate(module, 1000, seconds: 0.2);
        module.ObserveStatus(application with { SeenAtUtc = Start.AddSeconds(0.3), BaseDamageLowByte = 17 });

        var tick = Assert.Single(Tick(module));
        Assert.Null(tick.SimulatedPeriodicAmount);
        Assert.Equal("Missing application-time calibration", tick.PeriodicEstimateUnavailableReason);
        Assert.Equal(600u, tick.Amount);
    }

    [Fact]
    public void EnrichmentRetainsApplicationTimeHitRates()
    {
        var module = new DamageParsingModule();
        Calibrate(module, 1000, rates: new(0.2, 0.3));
        var application = Application();
        module.ObserveStatus(application);
        Calibrate(module, 9000, sequence: 2, seconds: 0.2, rates: new(0.5, 0.7));
        module.ObserveStatus(application with { SeenAtUtc = Start.AddSeconds(0.3), BaseDamageLowByte = 17 });

        var inputs = Assert.Single(Tick(module)).PeriodicEstimateInputs!;
        Assert.Equal(273.0, inputs.BaseDamage);
        Assert.Equal(0.2, inputs.CriticalRate, 6);
        Assert.Equal(0.3, inputs.DirectHitRate, 6);
    }

    [Fact]
    public void EnrichmentPreservesExplicitApplicationAttributes()
    {
        var module = new DamageParsingModule();
        Calibrate(module, 1000);
        var application = Application() with { SourceBaseRates = new(0.2, 0.3) };
        module.ObserveStatus(application);
        module.ObserveStatus(application with
        {
            SeenAtUtc = Start.AddSeconds(0.3), BaseDamageLowByte = 17, SourceBaseRates = new(0.5, 0.7),
        });

        var tick = Assert.Single(Tick(module));
        Assert.Equal(new DamageBaseRateSnapshot(0.2, 0.3), tick.SourceBaseRates);
        Assert.Equal(0.2, tick.PeriodicEstimateInputs!.CriticalRate, 6);
        Assert.Equal(0.3, tick.PeriodicEstimateInputs.DirectHitRate, 6);
    }

    [Fact]
    public void ExplicitRefreshTakesNewCalibrationAndKeepsTheEnrichedOldSnapshot()
    {
        var module = new DamageParsingModule();
        Calibrate(module, 1000);
        var application = Application();
        module.ObserveStatus(application);
        module.ObserveStatus(application with { SeenAtUtc = Start.AddSeconds(0.3), BaseDamageLowByte = 17 });
        Calibrate(module, 9000, sequence: 2, seconds: 4);
        module.ObserveStatus(application with
        {
            ActionId = PeriodicDamageRefreshPolicy.IronJawsActionId, SeenAtUtc = Start.AddSeconds(4.1),
        });

        Assert.Equal(273.0, Assert.Single(Tick(module, seconds: 3)).PeriodicEstimateInputs!.BaseDamage);
        var refreshed = Assert.Single(Tick(module, seconds: 6, sequence: 101));
        Assert.Equal(50.0, refreshed.PeriodicEstimateInputs!.DamagePerPotency);
        Assert.Equal(1000.0, refreshed.PeriodicEstimateInputs.BaseDamage);
    }

    [Theory]
    [InlineData(100u)]
    [InlineData(900u)]
    [InlineData(9000u)]
    public void IndependentEstimatesDoNotNormalizeToTheCombinedTick(uint combined)
    {
        var module = new DamageParsingModule();
        Calibrate(module, 1000, rates: new(0.05, 0));
        var application = Application() with { PeriodicPotency = 10, CriticalRateLowByte = 0 };
        module.ObserveStatus(application);
        module.ObserveStatus(application with { StatusId = 0x501, PeriodicPotency = 20 });

        var ticks = Tick(module, amount: combined).OrderBy(entry => entry.StatusId).ToList();
        Assert.Equal(new double?[] { 100.0, 200.0 }, ticks.Select(entry => entry.SimulatedPeriodicAmount));
        Assert.Equal((double)combined, ticks.Sum(entry => entry.RawMeterAmount));
    }

    [Fact]
    public void LateBuffMetadataUsesTheOriginalDamageCalibration()
    {
        var module = new DamageParsingModule();
        Calibrate(module, 1000);
        var application = Application() with { HasSourceStatusSnapshot = false };
        module.ObserveStatus(application);
        Calibrate(module, 9000, sequence: 2, seconds: 0.2);
        module.ObserveStatus(application with
        {
            SeenAtUtc = Start.AddSeconds(0.3), HasSourceStatusSnapshot = true,
            SourceStatuses = [new DamageStatusSnapshot(0x748, Source, 0, 15)],
        });

        var inputs = Assert.Single(Tick(module)).PeriodicEstimateInputs!;
        Assert.Equal(10.0, inputs.DamagePerPotency);
        Assert.Equal(1.1, inputs.DamageMultiplier, 6);
        Assert.Equal(220.0, inputs.BaseDamage);
    }

    [Fact]
    public void CoverageRetainsMissingAndObservedTicksInCompactHistory()
    {
        var module = new DamageParsingModule();
        Calibrate(module, 1000);
        module.ObserveStatus(Application());
        module.ObserveStatus(Application() with { StatusId = 0x501, PeriodicPotency = null });
        Tick(module);
        Tick(module, statusId: 0x502, sequence: 101, seconds: 4);

        var snapshot = module.EndEncounter(Start.AddSeconds(5), "test")! with { Events = [] };
        var restored = System.Text.Json.JsonSerializer.Deserialize<DamageEncounterSnapshot>(
            System.Text.Json.JsonSerializer.Serialize(snapshot))!;
        var diagnostics = restored.Diagnostics.PeriodicAllocations;
        var estimated = Assert.Single(diagnostics.Single(entry => entry.StatusId == 0x500).IndependentEstimates);
        Assert.Null(estimated.UnavailableReason);
        Assert.Equal(1, estimated.TickCount);
        Assert.Equal(300.0, estimated.AllocatedDamage);
        Assert.Equal(217.6875, estimated.EstimatedDamage!.Value, 6);
        var missing = Assert.Single(diagnostics.Single(entry => entry.StatusId == 0x501).IndependentEstimates);
        Assert.Equal("Missing potency", missing.UnavailableReason);
        Assert.Equal(300.0, missing.AllocatedDamage);
        Assert.Null(missing.EstimatedDamage);
        var observed = Assert.Single(diagnostics.Single(entry => entry.StatusId == 0x502).IndependentEstimates);
        Assert.Equal("Observed source-specific tick", observed.UnavailableReason);
        Assert.Equal(600.0, observed.AllocatedDamage);
        Assert.Null(observed.EstimatedDamage);
        Assert.Empty(restored.Events);
    }

    [Fact]
    public void ZeroEstimateIsNotClassifiedAsMissing()
    {
        var module = new DamageParsingModule();
        Calibrate(module, 1000);
        module.ObserveStatus(Application() with { PeriodicPotency = 10, BaseDamageLowByte = 0 });
        var tick = Assert.Single(Tick(module));
        Assert.Equal(0.0, tick.SimulatedPeriodicAmount);
        var diagnostic = Assert.Single(Assert.Single(module.GetCurrentEncounter()!
            .Diagnostics.PeriodicAllocations).IndependentEstimates);
        Assert.Null(diagnostic.UnavailableReason);
        Assert.Equal(0.0, diagnostic.EstimatedDamage);
        Assert.Equal(600.0, diagnostic.AllocatedDamage);
    }

    [Fact]
    public void SourceSpecificTickUsesItsObservedAmount()
    {
        var module = new DamageParsingModule();
        Calibrate(module, 1000);
        module.ObserveStatus(Application());
        var tick = Assert.Single(Tick(module, statusId: 0x500));
        Assert.Equal(600u, tick.Amount);
        Assert.Null(tick.SimulatedPeriodicAmount);
        Assert.Equal("Observed source-specific tick", tick.PeriodicEstimateUnavailableReason);
    }

    private static DamageStatusApplication Application() => new(
        Target, Source, 0x500, "Periodic", 0, 100, "Action", Start.AddSeconds(0.1), 30, true, false, false)
    {
        PeriodicPotency = 20,
        SourceStatuses = [],
        TargetStatuses = [],
        HasSourceStatusSnapshot = true,
        HasTargetStatusSnapshot = true,
    };

    private static void Calibrate(DamageParsingModule module, uint amount, long sequence = 1,
        double seconds = 0, DamageBaseRateSnapshot? rates = null)
    {
        module.Process(new DamageActionPacket(sequence, Start.AddSeconds(seconds), (uint)sequence,
            Source, 100, "Action", [new DamageActionTarget(0, Target, [new(0, 3, 0, 0, 0, 0, amount)])])
        {
            DirectPotency = 100,
            CanCalibratePotency = true,
            SourceBaseRates = rates ?? new DamageBaseRateSnapshot(0.15, 0.05),
            HasSourceStatusSnapshot = true,
        });
    }

    private static IReadOnlyList<ParsedDamageEvent> Tick(DamageParsingModule module, uint amount = 600,
        double seconds = 3, long sequence = 100, uint statusId = 0)
    {
        var tick = new PeriodicDamageTick(sequence, Start.AddSeconds(seconds), Target, statusId, "", 0,
            amount, statusId != 0 ? Source : null);
        var immediate = module.ProcessPeriodicTick(tick);
        return immediate.Count > 0 ? immediate : module.FlushPendingPeriodicTicks(tick.SeenAtUtc.AddMilliseconds(50));
    }
}
