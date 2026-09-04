using BetterDeaths.DamageParsing;
using BetterDeaths.WtfDig;

namespace BetterDeaths.Tests;

public sealed class DamageParityComparerTests
{
    private static readonly DateTime StartedAtUtc = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    [Fact]
    public void Compare_UsesOnlyFinalDamageEventsAndSeparatesPeriodicDamage()
    {
        var local = CreateLocalEncounter(1_100, 300);
        var report = CreateReport(
            new FflogsActor { Id = 1, Name = "Test Player", Type = "Player", SubType = "Machinist" },
            new FflogsActor { Id = 2, Name = "Automaton Queen", Type = "Pet", SubType = "Pet" });
        var fight = Assert.Single(report.Fights);
        var events = new FflogsEvent[]
        {
            new() { Type = "calculateddamage", SourceID = 1, AbilityGameID = 10, Amount = 700 },
            new() { Type = "damage", SourceID = 1, AbilityGameID = 10, Amount = 700 },
            new() { Type = "damage", SourceID = 1, AbilityGameID = 11, Amount = 300, Tick = true, Simulated = true },
            new() { Type = "damage", SourceID = 2, AbilityGameID = 12, Amount = 100 },
            new() { Type = "heal", SourceID = 1, AbilityGameID = 13, Amount = 9_999 },
        };

        var comparison = DamageParityComparer.Compare(local, report, fight, events);

        Assert.Equal(1_100, comparison.ReferenceDamage);
        Assert.Equal(800, comparison.ReferenceDirectDamage);
        Assert.Equal(300, comparison.ReferencePeriodicDamage);
        Assert.Equal(3, comparison.ReferenceEventCount);
        Assert.Equal(1, comparison.ReferenceSimulatedPeriodicEventCount);
        Assert.Equal(300, comparison.ReferenceSimulatedPeriodicDamage);
        Assert.Equal(0, comparison.ReferenceUnassignedPetDamage);
        var player = Assert.Single(comparison.Players);
        Assert.Equal("Test Player", player.PlayerName);
        Assert.Equal(1_100, player.ReferenceDamage);
        Assert.Equal(0, player.Difference);
    }

    [Fact]
    public void Compare_LeavesPetUnassignedWhenMoreThanOneOwnerMatches()
    {
        var local = CreateLocalEncounter(1_000, 300);
        var report = CreateReport(
            new FflogsActor { Id = 1, Name = "Test Player", Type = "Player", SubType = "Machinist" },
            new FflogsActor { Id = 3, Name = "Other Player", Type = "Player", SubType = "Machinist" },
            new FflogsActor { Id = 2, Name = "Automaton Queen", Type = "Pet", SubType = "Pet" });
        var comparison = DamageParityComparer.Compare(
            local,
            report,
            Assert.Single(report.Fights),
            [new FflogsEvent { Type = "damage", SourceID = 2, AbilityGameID = 12, Amount = 100 }]);

        Assert.Equal(100, comparison.ReferenceUnassignedPetDamage);
        Assert.Contains(comparison.Players, row => row.PlayerName == "Automaton Queen (unassigned pet)");
    }

    [Theory]
    [InlineData(1_001, 1_000, 0.1)]
    [InlineData(999, 1_000, -0.1)]
    public void DifferencePercent_ReturnsPercentagePoints(double local, double reference, double expected)
    {
        Assert.Equal(expected, DamageParityComparer.DifferencePercent(local, reference)!.Value, 6);
    }

    private static DamageEncounterSnapshot CreateLocalEncounter(ulong damage, ulong periodicDamage)
    {
        var actor = new DamageActorIdentity(0x1001, "Test Player", 0, string.Empty, true, 31)
        {
            IsPartyMember = true,
        };
        var actions = new DamageActionSummary[]
        {
            CreateAction(10, "Direct Hit", damage - periodicDamage, 0),
            CreateAction(11, "Damage over time", periodicDamage, periodicDamage),
        };
        var source = new DamageSourceSummary(actor, damage, 3, 3, 0, 0, 0, 0, 0, 0, 0, 0, actions)
        {
            MeterDamage = damage,
            PeriodicDamage = periodicDamage,
        };
        return new DamageEncounterSnapshot(
            StartedAtUtc,
            StartedAtUtc.AddSeconds(10),
            StartedAtUtc.AddSeconds(10),
            "Test",
            damage,
            3,
            0,
            [],
            [source],
            [])
        {
            MeterDamage = damage,
        };
    }

    [Fact]
    public void CombinedTicksAreExcludedButRealLimitBreakIsRetained()
    {
        var report = CreateReport(
            new FflogsActor { Id = 1, Name = "Test Player", Type = "Player", SubType = "Machinist" },
            new FflogsActor { Id = 13, Name = "Multiple Players", Type = "Player", SubType = "LimitBreak" },
            new FflogsActor { Id = 33, Name = "Limit Break", Type = "Player", SubType = "LimitBreak" });
        var comparison = DamageParityComparer.Compare(CreateLocalEncounter(1_000, 300), report, report.Fights[0],
        [
            new() { Type = "damage", SourceID = 13, AbilityGameID = 500000, Tick = true, Amount = 300 },
            new() { Type = "damage", SourceID = 1, AbilityGameID = 1000011, Tick = true, Simulated = true, Amount = 300, FinalizedAmount = 321.5 },
            new() { Type = "damage", SourceID = 1, AbilityGameID = 10, Amount = 700 },
            new() { Type = "damage", SourceID = 33, AbilityGameID = 2000, Amount = 100 },
        ]);
        Assert.Equal(1_100, comparison.ReferenceDamage);
        Assert.Equal(3, comparison.ReferenceEventCount);
        Assert.Equal(300, comparison.ReferenceCombinedPeriodicDamageExcluded);
        Assert.Equal(321.5, comparison.ReferenceFinalizedPeriodicDamage);
        Assert.DoesNotContain(comparison.Players, player => player.PlayerName == "Multiple Players");
        Assert.Contains(comparison.Abilities, ability => ability.LocalPeriodicDamage == 300 && ability.ReferencePeriodicDamage == 300);
    }

    [Fact]
    public void UploadedSampleKeepsAllocatedAndFinalizedTicksDistinct()
    {
        var report = CreateReport(
            new FflogsActor { Id = 9, Name = "SGE", Type = "Player", SubType = "Sage" },
            new FflogsActor { Id = 10, Name = "WHM", Type = "Player", SubType = "WhiteMage" },
            new FflogsActor { Id = 13, Name = "Multiple Players", Type = "Player", SubType = "LimitBreak" });
        var comparison = DamageParityComparer.Compare(CreateLocalEncounter(26_205, 26_205), report, report.Fights[0],
        [
            new() { Type = "damage", SourceID = 13, AbilityGameID = 500000, Tick = true, Amount = 26205 },
            new() { Type = "damage", SourceID = 9, AbilityGameID = 1002616, Tick = true, Simulated = true, Amount = 13191, FinalizedAmount = 12564.126545964913 },
            new() { Type = "damage", SourceID = 10, AbilityGameID = 1001871, Tick = true, Simulated = true, Amount = 13014, FinalizedAmount = 12395.743532 },
        ]);
        Assert.Equal(26205, comparison.ReferenceDamage);
        Assert.Equal(24959.870077964915, comparison.ReferenceFinalizedPeriodicDamage, 6);
        Assert.Equal(0, comparison.ReferenceMissingFinalizedTicks);
    }

    [Fact]
    public void MissingFinalizedTickIsReportedInsteadOfSubstitutingAllocation()
    {
        var report = CreateReport(new FflogsActor { Id = 1, Name = "Test Player", Type = "Player" });
        var comparison = DamageParityComparer.Compare(CreateLocalEncounter(300, 300), report, report.Fights[0],
            [new() { Type = "damage", SourceID = 1, AbilityGameID = 11, Amount = 300, Tick = true, Simulated = true }]);
        Assert.Equal(1, comparison.ReferenceMissingFinalizedTicks);
        Assert.Equal(0, comparison.ReferenceFinalizedPeriodicDamage);
    }

    [Fact]
    public void AmbiguousPlayerNamesAreRejected()
    {
        var report = CreateReport(
            new FflogsActor { Id = 1, Name = "Duplicate", Type = "Player" },
            new FflogsActor { Id = 2, Name = "Duplicate", Type = "Player" });
        Assert.Throws<InvalidOperationException>(() => DamageParityComparer.Compare(CreateLocalEncounter(1, 0), report, report.Fights[0], []));
    }

    [Fact]
    public void LocalPeriodicBreakdownUsesEffectiveActionAmounts()
    {
        var local = CreateLocalEncounter(1000, 300);
        var original = local.Sources[0];
        var source = original with
        {
            MeterDamage = 850,
            Actions = [original.Actions[0], original.Actions[1] with { MeterDamage = 150 }],
        };
        var report = CreateReport(new FflogsActor { Id = 1, Name = "Test Player", Type = "Player" });
        var comparison = DamageParityComparer.Compare(local with { Sources = [source] }, report, report.Fights[0], []);
        Assert.Equal(850, comparison.LocalDamage);
        Assert.Equal(700, comparison.LocalDirectDamage);
        Assert.Equal(150, comparison.LocalPeriodicDamage);
    }

    [Fact]
    public void RetainedEventsKeepRawEffectiveAndIndependentAmountsSeparate()
    {
        var local = CreateLocalEncounter(1000, 300);
        var actor = local.Sources[0].Source;
        var target = new DamageActorIdentity(0x40000001, "Target", 0, string.Empty, false, 0);
        var periodic = new ParsedDamageEvent("periodic", 1, StartedAtUtc, 0, actor, target,
            11, "Damage over time", 0, 0, DamageEventOutcome.Damage, 300, 0,
            false, false, false, false, 0, 0, 0, 0, 0)
        {
            IsPeriodic = true,
            MeterAmount = 299.75,
            CalculatedAmount = 150,
            SimulatedPeriodicAmount = 160.25,
        };
        var direct = periodic with
        {
            EventId = "direct", IsPeriodic = false, ActionId = 10, ActionName = "Direct Hit",
            Amount = 700, MeterAmount = 700, CalculatedAmount = null, SimulatedPeriodicAmount = null,
        };
        local = local with
        {
            Sources = [local.Sources[0] with { MeterDamage = 850 }],
            Events = [periodic, direct, direct with { MeterEligibility = DamageMeterEligibility.FriendlyTarget }],
        };
        var report = CreateReport(new FflogsActor { Id = 1, Name = "Test Player", Type = "Player" });
        var result = DamageParityComparer.Compare(local, report, report.Fights[0], []);
        Assert.Equal(850, result.LocalDamage);
        Assert.Equal(999.75, result.LocalRawDamage);
        Assert.Equal(150, result.LocalPeriodicDamage);
        Assert.Equal(700, result.LocalDirectDamage);
        Assert.Equal(160.25, result.LocalSimulatedPeriodicDamage);
        Assert.Equal(1, result.LocalSimulatedPeriodicTicks);
        Assert.Equal(2, result.LocalEventCount);
        Assert.DoesNotContain(result.Warnings, warning => warning.Contains("incomplete"));
    }

    [Fact]
    public void FightSelectionUsesAbsoluteTimeNotLastFightInReport()
    {
        var local = CreateLocalEncounter(1_000, 0);
        var report = new FflogsReportSummary
        {
            Code = "aBcDeFgHiJkLmNoP",
            StartTime = new DateTimeOffset(StartedAtUtc).ToUnixTimeMilliseconds(),
            Fights = [new() { Id = 1, StartTime = 0, EndTime = 10_000 }, new() { Id = 2, StartTime = 60_000, EndTime = 70_000 }],
        };
        Assert.Equal(1, DamageParityComparer.SelectMatchingFight(local, report, null).Id);
        Assert.Throws<InvalidOperationException>(() => DamageParityComparer.SelectMatchingFight(local, report, 2));
    }

    private static DamageActionSummary CreateAction(uint id, string name, ulong damage, ulong periodicDamage)
    {
        return new DamageActionSummary(id, name, damage, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0)
        {
            MeterDamage = damage,
            PeriodicDamage = periodicDamage,
        };
    }

    private static FflogsReportSummary CreateReport(params FflogsActor[] actors)
    {
        return new FflogsReportSummary
        {
            Code = "aBcDeFgHiJkLmNoP",
            Title = "Test report",
            Actors = actors,
            Abilities =
            [
                new FflogsAbility { GameID = 10, Name = "Direct Hit" },
                new FflogsAbility { GameID = 11, Name = "Damage over time" },
                new FflogsAbility { GameID = 12, Name = "Arm Punch" },
            ],
            Fights =
            [
                new FflogsFight
                {
                    Id = 7,
                    Name = "Test fight",
                    StartTime = 100,
                    EndTime = 10_100,
                    FriendlyPlayers = actors.Where(actor => actor.Type == "Player").Select(actor => actor.Id).ToList(),
                },
            ],
        };
    }
}
