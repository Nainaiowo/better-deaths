namespace BetterDeaths.DamageParsing;

using System.Text.Json;

public sealed class DamageModelSerializationTests
{
    [Fact]
    public void OlderEncounterDataFallsBackToRawDamageAndEventTiming()
    {
        const string json = """
            {
              "StartedAtUtc":"2026-08-27T12:00:00Z",
              "SnapshotAtUtc":"2026-08-27T12:00:05Z",
              "EndedAtUtc":"2026-08-27T12:00:05Z",
              "EndReason":"Combat ended",
              "TotalDamage":1000,
              "PacketCount":1,
              "DuplicateEventCount":0,
              "Events":[],
              "Sources":[],
              "Targets":[],
              "RaidAdjustedDamage":900
            }
            """;

        var snapshot = Assert.IsType<DamageEncounterSnapshot>(
            JsonSerializer.Deserialize<DamageEncounterSnapshot>(json));

        Assert.Equal(1000.0, snapshot.EffectiveMeterDamage);
        Assert.Equal(1000.0, snapshot.ObservedMeterDamage);
        Assert.Equal(200.0, snapshot.DamagePerSecond);
        Assert.Equal(900.0, snapshot.EffectiveMeterRaidAdjustedDamage);
        Assert.Equal(5.0, snapshot.DurationSeconds);
    }

    [Fact]
    public void NewEncounterDataKeepsMeterValuesAcrossSerialization()
    {
        var startedAtUtc = new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);
        var snapshot = new DamageEncounterSnapshot(
            startedAtUtc,
            startedAtUtc.AddSeconds(5),
            startedAtUtc.AddSeconds(5),
            "Combat ended",
            1000,
            1,
            0,
            [],
            [],
            [])
        {
            MeterStartedAtUtc = startedAtUtc.AddMilliseconds(100),
            MeterEndedAtUtc = startedAtUtc.AddSeconds(5.2),
            MeterDamage = 950,
            RaidAdjustedDamage = 900,
            MeterRaidAdjustedDamage = 875,
            Diagnostics = new DamageEncounterDiagnostics(
                1,
                1000,
                950,
                700,
                650,
                300,
                300,
                [new DamageResolutionDiagnostic(DamageResolutionQuality.Resolved, 1, 1000, 950)],
                [new DamageEligibilityDiagnostic(DamageMeterEligibility.Eligible, 1, 1000, 950)],
                [],
                [new PeriodicTickDiagnostic(
                    PeriodicAllocationBasis.SingleCandidate,
                    1,
                    1,
                    300,
                    300)]),
        };

        var restored = Assert.IsType<DamageEncounterSnapshot>(
            JsonSerializer.Deserialize<DamageEncounterSnapshot>(JsonSerializer.Serialize(snapshot)));

        Assert.Equal(950.0, restored.EffectiveMeterDamage);
        Assert.Equal(875.0, restored.EffectiveMeterRaidAdjustedDamage);
        Assert.Equal(5.1, restored.DurationSeconds, 3);
        Assert.Equal(300.0, restored.Diagnostics.PeriodicRawMeterDamage);
        Assert.Equal(
            PeriodicAllocationBasis.SingleCandidate,
            Assert.Single(restored.Diagnostics.PeriodicTicks).Basis);
    }

    [Fact]
    public void EffectiveDamageEvidenceSurvivesEncounterSerialization()
    {
        var seenAtUtc = new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);
        var actor = new DamageActorIdentity(1, "Source", 0, string.Empty, true, 21);
        var target = new DamageActorIdentity(2, "Target", 0, string.Empty, false, 0);
        var before = new DamageHpSnapshot(905, 0, 10000);
        var after = new DamageHpSnapshot(0, 0, 10000);
        var damageEvent = new ParsedDamageEvent(
            "event",
            1,
            seenAtUtc,
            700,
            actor,
            target,
            100,
            "Action",
            0,
            0,
            DamageEventOutcome.Damage,
            6262,
            0,
            false,
            false,
            false,
            false,
            3,
            0,
            0,
            0,
            0)
        {
            MeterAmount = 6262,
            CalculatedAmount = 905,
            OverkillDamage = 5357,
            TargetHpBefore = before,
            TargetHpAfter = after,
            ResolutionQuality = DamageResolutionQuality.Resolved,
            SourceBaseRates = new DamageBaseRateSnapshot(0.208, 0.235),
        };
        var snapshot = new DamageEncounterSnapshot(
            seenAtUtc,
            seenAtUtc,
            seenAtUtc,
            "test",
            6262,
            1,
            0,
            [damageEvent],
            [],
            []);

        var restored = Assert.IsType<DamageEncounterSnapshot>(
            JsonSerializer.Deserialize<DamageEncounterSnapshot>(JsonSerializer.Serialize(snapshot)));
        var restoredEvent = Assert.Single(restored.Events);

        Assert.Equal(905.0, restoredEvent.EffectiveMeterAmount);
        Assert.Equal(5357.0, restoredEvent.OverkillDamage);
        Assert.Equal(before, restoredEvent.TargetHpBefore);
        Assert.Equal(after, restoredEvent.TargetHpAfter);
        Assert.Equal(DamageResolutionQuality.Resolved, restoredEvent.ResolutionQuality);
        Assert.Equal(new DamageBaseRateSnapshot(0.208, 0.235), restoredEvent.SourceBaseRates);
    }

    [Fact]
    public void NeutralAndAdjustedDamageUseTheirSeparateBuffRemovalRules()
    {
        var actor = new DamageActorIdentity(1, "Source", 0, string.Empty, true, 21);
        var source = new DamageSourceSummary(actor, 1000, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, [])
        {
            MeterDamage = 950,
            ExternalBuffDamageReceived = 100,
            MeterExternalBuffDamageReceived = 90,
            SingleTargetBuffDamageReceived = 40,
            MeterSingleTargetBuffDamageReceived = 35,
        };

        Assert.Equal(900.0, source.NeutralDamage);
        Assert.Equal(860.0, source.EffectiveMeterNeutralDamage);
        Assert.Equal(960.0, source.AdjustedDamage);
        Assert.Equal(915.0, source.EffectiveMeterAdjustedDamage);
    }

    [Fact]
    public void StandardMeterUsesRawDamageAndKeepsHpAdjustmentsSeparate()
    {
        var start = new DateTime(2026, 9, 4, 0, 0, 0, DateTimeKind.Utc);
        var snapshot = new DamageEncounterSnapshot(start, start.AddSeconds(10), start.AddSeconds(10),
            "test", 1500, 2, 0, [], [], [])
        {
            RawMeterDamage = 1000,
            MeterDamage = 750,
            RaidAdjustedDamage = 1000,
            MeterRaidAdjustedDamage = 750,
        };
        var restored = JsonSerializer.Deserialize<DamageEncounterSnapshot>(JsonSerializer.Serialize(snapshot))!;
        Assert.Equal(1000, restored.ObservedMeterDamage);
        Assert.Equal(750, restored.EffectiveMeterDamage);
        Assert.Equal(100, restored.DamagePerSecond);
        Assert.Equal(100, restored.RaidDamagePerSecond);
    }

    [Fact]
    public void PersonalDurationSurvivesSourceSerialization()
    {
        var seenAtUtc = new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);
        var actor = new DamageActorIdentity(1, "Source", 0, string.Empty, true, 21);
        var source = new DamageSourceSummary(actor, 1000, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, [])
        {
            ActiveStartedAtUtc = seenAtUtc,
            ActiveEndedAtUtc = seenAtUtc.AddSeconds(4.5),
            ActiveDurationSeconds = 4.5,
        };

        var restored = Assert.IsType<DamageSourceSummary>(
            JsonSerializer.Deserialize<DamageSourceSummary>(JsonSerializer.Serialize(source)));

        Assert.Equal(seenAtUtc, restored.ActiveStartedAtUtc);
        Assert.Equal(seenAtUtc.AddSeconds(4.5), restored.ActiveEndedAtUtc);
        Assert.Equal(4.5, restored.ActiveDurationSeconds.GetValueOrDefault(), 6);
    }
}
