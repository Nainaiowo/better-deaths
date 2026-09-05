namespace BetterDeaths.Tests;

using System.Text.Json;
using BetterDeaths.DamageParsing;

public sealed class PeriodicDirectHitCompatibilityTests
{
    private static readonly DateTime Start = new(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DamageActorIdentity Source = new(0x1001, "Caster", 0, "", true, 24) { Level = 100 };
    private static readonly DamageActorIdentity Target = new(0x40000001, "Enemy", 0, "", false, 0);
    private static readonly DamageActorIdentity Bard = new(0x1002, "Bard", 0, "", true, 23);

    [Theory]
    [InlineData(10, 2, 0.05)]
    [InlineData(11, 2, 2.5 / 11)]
    [InlineData(20, 4, 0.25)]
    [InlineData(20, 20, 1.0)]
    public void WeightedFactorIsNotAnObservedProbability(int count, int hits, double expected)
    {
        var tracker = new PeriodicDirectHitCompatibility();
        for (var i = 0; i < count; i++) tracker.Observe(Hit(i + 1, i < hits));
        var snapshot = tracker.Capture(Application(), Start.AddSeconds(30));
        Assert.Equal(count, snapshot.Samples);
        Assert.Equal(hits, snapshot.DirectHits);
        Assert.Equal(expected, snapshot.Factor, 10);
        if (count == 20 && hits == 4)
        {
            Assert.Equal(0.2, DamageBaseRatePolicy.EstimateObserved(hits, count, critical: false), 10);
        }
    }

    [Theory]
    [InlineData(0x8Du, 0.20)]
    [InlineData(0x8AAu, 0.03)]
    [InlineData(0x721u, 0.20)]
    [InlineData(0x84Du, 0.20)]
    public void ChanceBuffsAreExcludedFromHistoryAndAddedToTheSnapshot(uint statusId, double bonus)
    {
        var tracker = Warm();
        var status = new DamageStatusSnapshot(statusId, Bard, 0, 30);
        tracker.Observe(Hit(100, true) with { SourceStatuses = [status] });
        var application = Application() with { SourceStatuses = [status, status] };
        var snapshot = tracker.Capture(application, application.SeenAtUtc.AddSeconds(1));
        Assert.Equal(20, snapshot.Samples);
        Assert.Equal(bonus, snapshot.BuffRate, 10);
        Assert.Equal(0.25 + bonus, snapshot.Factor, 10);
        Assert.True(DamageStatusCapturePolicy.IsRelevant(statusId));
    }

    [Theory]
    [InlineData(0.999f, 21)]
    [InlineData(1.0f, 20)]
    [InlineData(1.001f, 20)]
    public void CompatibilityUsesItsOwnExpiryBoundary(float remaining, int samples)
    {
        var tracker = Warm();
        tracker.Observe(Hit(100, true) with { SourceStatuses = [new(0x8D, Bard, 0, remaining)] });
        Assert.Equal(samples, tracker.Capture(Application(), Start.AddSeconds(30)).Samples);
    }

    [Fact]
    public void BuffRemainingTimeIsMeasuredAtConfirmation()
    {
        var tracker = Warm();
        var application = Application() with { SourceStatuses = [new(0x8D, Bard, 0, 1.5f)] };
        Assert.Equal(0.45, tracker.Capture(application, application.SeenAtUtc).Factor, 10);
        Assert.Equal(0.25, tracker.Capture(application, application.SeenAtUtc.AddSeconds(0.6)).Factor, 10);
    }

    [Fact]
    public void ShortHistoryFallbackDoesNotPretendToKnowAPlayersStats()
    {
        var tracker = new PeriodicDirectHitCompatibility();
        tracker.ObserveContext(Source, new(0.3, 0.6));
        var application = Application() with { SourceStatuses = [new(0x8D, Bard, 0, 20)] };
        var snapshot = tracker.Capture(application, application.SeenAtUtc);
        Assert.Equal(0, snapshot.Samples);
        Assert.Equal(0.05, snapshot.Factor, 10);
    }

    [Theory]
    [InlineData(0x4051u)]
    [InlineData(0x404Fu)]
    [InlineData(0x6499u)]
    [InlineData(0x64C0u)]
    [InlineData(0x8776u)]
    [InlineData(0x8777u)]
    [InlineData(0x8778u)]
    [InlineData(0x903Du)]
    [InlineData(0x9076u)]
    [InlineData(0x8C6u)]
    public void GuaranteedActionsNeverIncreaseTheNaturalHitHistory(uint action)
    {
        var tracker = Warm();
        tracker.Observe(Hit(100, true) with { ActionId = action });
        Assert.Equal(20, tracker.Capture(Application(), Start.AddSeconds(30)).Samples);
    }

    [Theory]
    [InlineData(0x353u, 100u)]
    [InlineData(0x56u, 100u)]
    [InlineData(0x499u, 0xDDDu)]
    [InlineData(0x499u, 0xDDEu)]
    public void GuaranteedWeaponskillsAreExcludedWithoutExcludingAutoAttacks(uint status, uint action)
    {
        var tracker = Warm();
        var hit = Hit(100, true) with { ActionId = action, ActionCategoryId = 3, SourceStatuses = [new(status, Source, 0, 5)] };
        tracker.Observe(hit);
        Assert.Equal(20, tracker.Capture(Application(), Start.AddSeconds(30)).Samples);
        tracker.Observe(hit with { ActionId = 7, ActionCategoryId = 1, IsAutoAttack = true });
        Assert.Equal(21, tracker.Capture(Application(), Start.AddSeconds(30)).Samples);
    }

    [Fact]
    public void RawSourceHistoryDoesNotReattributeLimitBreakDamage()
    {
        var module = WarmModule();
        module.Process(Packet(21, 21, false) with { ActionCategoryId = 9 });
        module.ObserveStatus(Application() with { DurationSeconds = 30 });
        var tick = Tick(module, 33);
        Assert.Equal(21, tick.PeriodicCompatibilityEstimate!.DirectHit.Samples);
        Assert.Equal(20, tick.PeriodicEstimateInputs!.DirectHitSampleCount);
        Assert.Equal(0.096, tick.PeriodicEstimateInputs.DirectHitRate, 10);
        Assert.Equal(600u, tick.Amount);
        var saved = module.EndEncounter(Start.AddSeconds(34), "test")!;
        Assert.Equal(1000ul, Assert.Single(saved.Sources, s => s.Source.IsLimitBreak).TotalDamage);
        var compact = saved with { Events = [] };
        var restored = JsonSerializer.Deserialize<DamageEncounterSnapshot>(JsonSerializer.Serialize(compact))!;
        var coverage = Assert.Single(Assert.Single(restored.Diagnostics.PeriodicAllocations).IndependentEstimates);
        Assert.Equal(1, coverage.CompatibilityTickCount);
        Assert.Equal(tick.PeriodicCompatibilityEstimate.EstimatedDamage, coverage.CompatibilityDamage);
    }

    [Fact]
    public void CompatibilityFreezesAtConfirmationWithoutChangingThePhysicalSnapshot()
    {
        var module = WarmModule();
        var pending = Application();
        module.ObserveStatus(pending);
        module.Process(Packet(21, 30.5, true));
        var confirmation = pending with
        {
            ActionId = 0,
            SeenAtUtc = Start.AddSeconds(31),
            DurationSeconds = 30,
            BaseDamageLowByte = null,
            CriticalRateLowByte = null,
            SourceBaseRates = null
        };
        module.ObserveStatus(confirmation);
        module.Process(Packet(22, 31.1, false));
        module.ObserveStatus(confirmation with { SeenAtUtc = Start.AddSeconds(31.2) });
        var tick = Tick(module, 34);
        var compatibility = tick.PeriodicCompatibilityEstimate!;
        Assert.Equal(21, compatibility.DirectHit.Samples);
        Assert.Equal(5, compatibility.DirectHit.DirectHits);
        Assert.Equal(6.25 / 21, compatibility.DirectHit.Factor, 10);
        Assert.Equal(Start.AddSeconds(31), compatibility.DirectHit.SeenAtUtc);
        Assert.Equal(20, tick.PeriodicEstimateInputs!.DirectHitSampleCount);
        Assert.Equal(0.096, tick.PeriodicEstimateInputs.DirectHitRate, 10);
        Assert.Equal(pending.SourceBaseRates, tick.SourceBaseRates);
        Assert.Equal(600u, tick.Amount);
        Assert.Equal(10.0, tick.PeriodicEstimateInputs.DamagePerPotency, 10);
        Assert.Equal(compatibility, JsonSerializer.Deserialize<ParsedDamageEvent>(JsonSerializer.Serialize(tick))!.PeriodicCompatibilityEstimate);
    }

    [Fact]
    public void PetSamplesStaySeparateFromTheOwnersCompatibilityHistory()
    {
        var tracker = Warm();
        var pet = Source with { EntityId = 0x40000002, IsPlayer = false, IsPet = true, OwnerEntityId = Source.EntityId };
        tracker.Observe(Hit(100, true) with { Source = pet, AttributedSource = Source });
        Assert.Equal(20, tracker.Capture(Application(), Start.AddSeconds(30)).Samples);
        Assert.Equal(1, tracker.Capture(Application() with { Source = pet }, Start.AddSeconds(30)).Samples);
    }

    [Fact]
    public void ContextChangesAndClearsDropHistoryButUnknownMetadataDoesNot()
    {
        var tracker = Warm();
        tracker.ObserveContext(Source with { ClassJobId = 0, Level = 0 }, null);
        Assert.Equal(20, tracker.Capture(Application(), Start.AddSeconds(30)).Samples);
        tracker.ObserveContext(Source with { Level = 90 }, null);
        Assert.Equal(0, tracker.Capture(Application(), Start.AddSeconds(30)).Samples);
        tracker.Observe(Hit(100, true));
        tracker.ObserveContext(Source with { ClassJobId = 25 }, null);
        Assert.Equal(0, tracker.Capture(Application(), Start.AddSeconds(30)).Samples);
        tracker.Observe(Hit(101, true) with { SourceBaseRates = new(0.3, 0.4) });
        tracker.ObserveContext(Source, new(0.3, 0.5));
        Assert.Equal(0, tracker.Capture(Application(), Start.AddSeconds(30)).Samples);
        tracker.Observe(Hit(102, true));
        tracker.Clear();
        Assert.Equal(0, tracker.Capture(Application(), Start.AddSeconds(30)).Samples);
    }

    [Fact]
    public void EncounterResetRetainsHistoryButExplicitCalibrationResetDoesNot()
    {
        var module = WarmModule();
        module.EndEncounter(Start.AddSeconds(25), "Combat ended");
        module.ObserveStatus(Application() with { DurationSeconds = 30 });
        Assert.Equal(20, Tick(module, 33).PeriodicCompatibilityEstimate!.DirectHit.Samples);
        module.EndEncounter(Start.AddSeconds(34), "Left duty");
        module.ResetCalibration();
        module.Process(Packet(21, 40, false));
        module.ObserveStatus(Application() with { SeenAtUtc = Start.AddSeconds(41), DurationSeconds = 30 });
        Assert.Equal(1, Tick(module, 44).PeriodicCompatibilityEstimate!.DirectHit.Samples);
    }

    [Fact]
    public void MissingCalibrationDoesNotProduceACompatibilityDamageEstimate()
    {
        var module = new DamageParsingModule();
        module.ObserveStatus(Application() with { DurationSeconds = 30 });
        var tick = Tick(module, 33);
        Assert.Equal(600u, tick.Amount);
        Assert.Null(tick.PeriodicEstimateInputs);
        Assert.Null(tick.PeriodicCompatibilityEstimate);
    }

    [Fact]
    public void ObservedSourceSpecificTicksAreNotReplacedWithEstimates()
    {
        var module = WarmModule();
        module.ObserveStatus(Application() with { DurationSeconds = 30 });
        var tick = new PeriodicDamageTick(1000, Start.AddSeconds(33), Target, 1871, "Dia", 0, 600, Source);
        var immediate = module.ProcessPeriodicTick(tick);
        var parsed = Assert.Single(immediate.Count > 0 ? immediate : module.FlushPendingPeriodicTicks(tick.SeenAtUtc.AddMilliseconds(50)));
        Assert.Equal(600u, parsed.Amount);
        Assert.Null(parsed.PeriodicEstimateInputs);
        Assert.Null(parsed.PeriodicCompatibilityEstimate);
        Assert.Equal("Observed source-specific tick", parsed.PeriodicEstimateUnavailableReason);
    }

    private static PeriodicDirectHitCompatibility Warm()
    {
        var tracker = new PeriodicDirectHitCompatibility();
        for (var i = 0; i < 20; i++) tracker.Observe(Hit(i + 1, i < 4));
        return tracker;
    }

    private static DamageParsingModule WarmModule()
    {
        var module = new DamageParsingModule();
        for (var i = 0; i < 20; i++) module.Process(Packet(i + 1, i, i < 4));
        return module;
    }

    private static ParsedDamageEvent Hit(long sequence, bool direct) =>
        Assert.Single(new DirectDamageParser().Parse(Packet(sequence, sequence, direct)));

    private static DamageActionPacket Packet(long sequence, double seconds, bool direct) =>
        new(sequence, Start.AddSeconds(seconds), (uint)sequence, Source, 100, "Spell",
            [new DamageActionTarget(0, Target, [new(0, 3, direct ? (byte)0x40 : (byte)0, 0, 0, 0, direct ? 1250u : 1000u)])])
        { DirectPotency = 100, CanCalibratePotency = true, SourceBaseRates = new(0.251, 0.096), HasSourceStatusSnapshot = true };

    private static DamageStatusApplication Application() => new(Target, Source, 1871, "Dia", 0, 16532, "Dia",
        Start.AddSeconds(30), 0, true, false, false)
    {
        PeriodicPotency = 85,
        BaseDamageLowByte = 80,
        CriticalRateLowByte = 251,
        SourceBaseRates = new(0.251, 0.096),
        HasSourceStatusSnapshot = true
    };

    private static ParsedDamageEvent Tick(DamageParsingModule module, double seconds)
    {
        var tick = new PeriodicDamageTick(1000, Start.AddSeconds(seconds), Target, 0, "", 0, 600, null);
        var immediate = module.ProcessPeriodicTick(tick);
        return Assert.Single(immediate.Count > 0 ? immediate : module.FlushPendingPeriodicTicks(tick.SeenAtUtc.AddMilliseconds(50)));
    }
}
