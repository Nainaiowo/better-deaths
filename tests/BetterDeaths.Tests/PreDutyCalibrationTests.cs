namespace BetterDeaths.Tests;

using BetterDeaths.DamageParsing;

public sealed class PreDutyCalibrationTests
{
    private static readonly DateTime Start = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DamageActorIdentity Healer = new(0x1001, "Healer", 0, "", true, 24) { Level = 100 };
    private static readonly DamageActorIdentity Enemy = new(0x40000001, "Enemy", 0, "", false, 0);

    public static IEnumerable<object[]> HealingProfiles => Enumerable.Range(1, ushort.MaxValue)
        .Where(id => PeriodicCalibrationPolicy.HealingPotency((uint)id) is > 0)
        .Select(id => new object[] { (uint)id });

    [Theory]
    [MemberData(nameof(HealingProfiles))]
    public void EveryKnownHealingProfileSurvivesDutyStartWithoutCreatingAnEncounter(uint actionId)
    {
        var module = new DamageParsingModule();
        var heal = Healing(actionId);
        module.SetCombatActive(false, Start);
        Assert.Empty(module.Process(heal, allowAutomaticEncounterStart: false));
        Assert.Empty(module.Process(heal, allowAutomaticEncounterStart: false));
        module.SetCombatActive(false, Start.AddSeconds(30));
        Assert.Null(module.GetCurrentEncounter(Start.AddSeconds(30)));
        Assert.Null(module.LastEncounter);
        Assert.Null(module.EndEncounter(Start.AddSeconds(31), "Duty reset"));

        // Re-delivery across the boundary must not count seven heals twice.
        Assert.Empty(module.Process(heal, allowAutomaticEncounterStart: false));
        Assert.Null(module.GetCurrentEncounter(Start.AddSeconds(31)));
        var firstDamage = Damage(2, 40, critical: true);
        Assert.Single(module.Process(firstDamage, allowAutomaticEncounterStart: false));
        Assert.Null(module.GetCurrentEncounter(Start.AddSeconds(40)));
        module.SetCombatActive(true, Start.AddSeconds(40));
        var encounter = module.GetCurrentEncounter(Start.AddSeconds(40))!;
        Assert.Equal(firstDamage.SeenAtUtc, encounter.MeterStartedAtUtc);
        Assert.Equal(1500, encounter.RawMeterDamage);
        Assert.Equal(1, Assert.Single(encounter.Sources).Hits);
        var inputs = TickInputs(module, 41);
        Assert.Equal(8, inputs.CriticalSampleCount);
        Assert.Equal(1, inputs.CalibrationSampleCount);
        Assert.Equal(10, inputs.DamagePerPotency, 9);
    }

    [Fact]
    public void CapturedSevenTargetHealChangesLaterCriticalNormalizationWithoutAddingDamage()
    {
        var module = new DamageParsingModule();
        module.Process(Healing(37010), allowAutomaticEncounterStart: false);
        module.EndEncounter(Start.AddSeconds(31), "Duty reset");
        module.SetCombatActive(true, Start.AddSeconds(40));
        for (var i = 0; i < 4; i++) module.Process(Damage((uint)i + 2, 40 + i, critical: true), false);
        var inputs = TickInputs(module, 44);
        Assert.Equal(11, inputs.CriticalSampleCount);
        Assert.Equal(4, inputs.CalibrationSampleCount);
        Assert.Equal(8.0 / 11, inputs.CalibrationBaseRates!.Critical, 9);
        // Four pre-pull critical heals and four critical hits: eight out of eleven.
        // The first three hits still use the small-sample default; only hit four changes.
        Assert.Equal(10, inputs.DamagePerPotency, 9);
        var completed = module.EndEncounter(Start.AddSeconds(48), "Complete")!;
        Assert.Equal(6000, completed.Events.Where(e => !e.IsPeriodic).Sum(e => (long)e.Amount));
    }

    [Fact]
    public void WipeKeepsCalibrationButTerritoryResetRemovesIt()
    {
        var module = new DamageParsingModule();
        module.Process(Healing(37010), false);
        module.EndEncounter(Start.AddSeconds(31), "Duty reset");
        module.SetCombatActive(true, Start.AddSeconds(40));
        module.Process(Damage(2, 40), false);
        Assert.Equal(8, TickInputs(module, 41).CriticalSampleCount);
        var finished = module.EndEncounter(Start.AddSeconds(50), "Wipe")!;
        module.Process(Damage(3, 60), false);
        module.SetCombatActive(true, Start.AddSeconds(60));
        Assert.Equal(9, TickInputs(module, 61).CriticalSampleCount);
        module.EndEncounter(Start.AddSeconds(70), "Left territory");
        module.ResetCalibration();
        module.Process(Damage(4, 80), false);
        module.SetCombatActive(true, Start.AddSeconds(80));
        Assert.Equal(1, TickInputs(module, 81).CriticalSampleCount);
        Assert.Equal(1500, finished.Events.Single(e => !e.IsPeriodic).RawMeterAmount);
    }

    [Theory]
    [InlineData(28, 100)]
    [InlineData(24, 90)]
    public void ChangedJobOrLevelDoesNotInheritPrePullHealing(uint job, byte level)
    {
        var module = new DamageParsingModule();
        module.Process(Healing(37010), false);
        module.EndEncounter(Start.AddSeconds(31), "Duty reset");
        var source = Healer with { ClassJobId = job, Level = level };
        module.Process(Damage(2, 40) with { Source = source }, false);
        module.SetCombatActive(true, Start.AddSeconds(40));
        Assert.Equal(1, TickInputs(module, 41, source).CriticalSampleCount);
    }

    private static DamageActionPacket Healing(uint actionId) => new(1, Start.AddSeconds(1), 1, Healer,
        actionId, "Pre-pull heal", Enumerable.Range(0, 7).Select(index => new DamageActionTarget(index,
            Healer with { EntityId = Healer.EntityId + (uint)index },
            [new(0, 4, 0, index < 4 ? (byte)0x20 : (byte)0, 0, 0, 1500)])
            { HasTargetStatusSnapshot = true }).ToArray())
    {
        ActionCategoryId = 2, HealingPotency = PeriodicCalibrationPolicy.HealingPotency(actionId),
        HasSourceStatusSnapshot = true,
    };

    private static DamageActionPacket Damage(uint sequence, int seconds, bool critical = false) =>
        new(sequence, Start.AddSeconds(seconds), sequence, Healer, 50000, "Calibration hit",
            [new(0, Enemy, [new(0, 3, critical ? (byte)0x20 : (byte)0, 0, 0, 0, 1500)])
                { HasTargetStatusSnapshot = true }])
        { ActionCategoryId = 2, DirectPotency = 100, CanCalibratePotency = true, HasSourceStatusSnapshot = true };

    private static PeriodicDamageEstimateInputs TickInputs(DamageParsingModule module, int seconds,
        DamageActorIdentity? source = null)
    {
        module.ObserveStatus(new(Enemy, source ?? Healer, 50000, "DoT", 0, 50000, "DoT",
            Start.AddSeconds(seconds), 30, true, false, false) { PeriodicPotency = 60 });
        module.ProcessPeriodicTick(new(seconds, Start.AddSeconds(seconds + 3), Enemy, 0, "", 0, 1000, null));
        return Assert.Single(module.FlushPendingPeriodicTicks(Start.AddSeconds(seconds + 3), true))
            .PeriodicCompatibilityEstimate!.Inputs!;
    }
}
