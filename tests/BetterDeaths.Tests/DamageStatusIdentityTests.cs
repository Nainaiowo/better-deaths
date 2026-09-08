namespace BetterDeaths.Tests;

using BetterDeaths.DamageParsing;
using System.Text.Json;

public sealed class DamageStatusIdentityTests
{
    private sealed record Snapshot(uint StatusId, uint SourceId, float RemainingTime = 30) : IDamageStatusIdentity;
    private static readonly DateTime Start = new(2026, 9, 8, 2, 30, 0, DateTimeKind.Utc);
    private static readonly DamageActorIdentity First = new(0x1001, "First", 0, "", true, 24);
    private static readonly DamageActorIdentity Second = First with { EntityId = 0x1002, Name = "Second" };
    private static readonly DamageActorIdentity Target = new(0x40000001, "Target", 0, "", false, 0);

    public static IEnumerable<object[]> PeriodicStatuses
    {
        get
        {
            using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
                "Fixtures", "PeriodicApplicationGolden.json")));
            return fixture.RootElement.GetProperty("Cases").EnumerateArray()
                .Where(c => !c.GetProperty("Ground").GetBoolean())
                .Select(c => c.GetProperty("Status").GetUInt32()).Distinct()
                .Select(id => new object[] { id }).ToArray();
        }
    }

    [Fact]
    public void EveryStatusIdUsesThePacketCasterNotTheLongestRemainingEffect()
    {
        for (uint id = 1; id <= ushort.MaxValue; id++)
        {
            Snapshot[] statuses = [new(id, Second.EntityId, 29), new(id, First.EntityId, 1)];
            foreach (var removal in new[] { false, true })
            {
                Assert.True(DamageStatusIdentityPolicy.TryResolve(statuses, id, First.EntityId, removal, out var source, out var selected));
                Assert.Equal(First.EntityId, source);
                Assert.Same(statuses[1], selected);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AbsentPacketCasterDoesNotBorrowAnotherCastersMetadata(bool removal)
    {
        Snapshot[] statuses = [new(1871, Second.EntityId, 23.339697f)];
        Assert.True(DamageStatusIdentityPolicy.TryResolve(statuses, 1871, First.EntityId, removal, out var source, out var selected));
        Assert.Equal(First.EntityId, source);
        Assert.Null(selected);
        Assert.True(DamageStatusIdentityPolicy.TryResolve<Snapshot>(null, 1871, First.EntityId, removal, out source, out selected));
        Assert.Equal(First.EntityId, source);
        Assert.Null(selected);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(0xE0000000u)]
    [InlineData(uint.MaxValue)]
    public void UnknownRemovalSourceNeverDeletesTheSurvivingCaster(uint unknown)
    {
        Snapshot[] statuses = [new(1871, Second.EntityId)];
        Assert.False(DamageStatusIdentityPolicy.TryResolve(statuses, 1871, unknown, true, out _, out _));
    }

    [Fact]
    public void SourceLessUpdatesRequireExactlyOneMatchingIdentity()
    {
        Snapshot[] statuses = [new(1871, First.EntityId), new(1871, Second.EntityId), new(999, Second.EntityId)];
        Assert.False(DamageStatusIdentityPolicy.TryResolve(statuses, 1871, 0, false, out _, out _));
        Assert.True(DamageStatusIdentityPolicy.TryResolve(statuses, 999, 0, false, out var source, out var selected));
        Assert.Equal(Second.EntityId, source);
        Assert.Same(statuses[2], selected);
        Assert.False(DamageStatusIdentityPolicy.TryResolve(statuses, 1000, 0, false, out _, out _));
        Assert.False(DamageStatusIdentityPolicy.TryResolve<Snapshot>(null, 999, 0, false, out _, out _));
    }

    [Fact]
    public void DuplicateSnapshotEntriesCannotChooseArbitraryParameters()
    {
        Snapshot[] statuses = [new(1871, First.EntityId, 2), new(1871, First.EntityId, 25)];
        Assert.True(DamageStatusIdentityPolicy.TryResolve(statuses, 1871, First.EntityId, false, out var source, out var selected));
        Assert.Equal(First.EntityId, source);
        Assert.Null(selected);
        Assert.False(DamageStatusIdentityPolicy.TryResolve(statuses, 1871, 0, false, out _, out _));
    }

    [Theory]
    [MemberData(nameof(PeriodicStatuses))]
    public void EveryCatalogTargetDotKeepsTheOtherCasterAndOtherTargetAfterRemoval(uint id)
    {
        var module = new DamageParsingModule();
        var otherTarget = Target with { EntityId = 0x40000002 };
        module.ObserveStatus(Application(id, First, Target, 0));
        module.ObserveStatus(Application(id, Second, Target, 0));
        module.ObserveStatus(Application(id, First, otherTarget, 0));
        Snapshot[] statuses = [new(id, Second.EntityId, 23)];
        Assert.True(DamageStatusIdentityPolicy.TryResolve(statuses, id, First.EntityId, true, out var source, out _));
        module.ObserveStatus(Application(id, First with { EntityId = source }, Target, 2) with { IsRemoval = true });
        Assert.Equal(Second.EntityId, Assert.Single(Tick(module, Target, 3)).Source.EntityId);
        Assert.Equal(First.EntityId, Assert.Single(Tick(module, otherTarget, 3)).Source.EntityId);
        // A later refresh of the retired caster must not overwrite the survivor.
        module.ObserveStatus(Application(id, First, Target, 4));
        Assert.Equal(new[] { First.EntityId, Second.EntityId }, Tick(module, Target, 6).Select(e => e.Source.EntityId).Order());
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public void BufferedCombinedTickHonorsExactRemovalBoundary(long offset, bool firstSurvives)
    {
        var module = new DamageParsingModule();
        module.ObserveStatus(Application(1871, First, Target, 0));
        module.ObserveStatus(Application(1871, Second, Target, 0));
        var tickAt = Start.AddSeconds(3).AddTicks(offset);
        module.ProcessPeriodicTick(new(1, tickAt, Target, 0, "", 0, 600, null));
        module.ObserveStatus(Application(1871, First, Target, 3) with { IsRemoval = true });
        var ticks = module.FlushPendingPeriodicTicks(Start.AddSeconds(3.1), true);
        Assert.Equal(firstSurvives, ticks.Any(t => t.Source.EntityId == First.EntityId));
        Assert.Contains(ticks, t => t.Source.EntityId == Second.EntityId);
        Assert.Equal(600u, (uint)ticks.Sum(t => t.Amount));
    }

    [Fact]
    public void FinalCombinedPacketDoesNotAddRemovedStatusesToPlayerDamage()
    {
        var module = new DamageParsingModule();
        module.SetCombatActive(true, Start);
        module.Process(new DamageActionPacket(1, Start, 1, First, 100, "Direct",
            [new(0, Target, [new(0, 3, 0, 0, 0, 0, 1500)])]), false);
        module.ObserveStatus(Application(1871, First, Target, 0));
        module.ProcessPeriodicTick(new(2, Start.AddSeconds(3), Target, 0, "", 0, 600, null), false);
        module.ObserveStatus(Application(1871, First, Target, 3) with { IsRemoval = true });
        var encounter = module.EndEncounter(Start.AddSeconds(3), "Left duty")!;
        Assert.Equal(1500, Assert.Single(encounter.Sources, s => s.Source.EntityId == First.EntityId).ObservedMeterDamage);
        Assert.Contains(encounter.Events, e => e.IsPeriodic && e.Amount == 600 && e.AttributionQuality == DamageAttributionQuality.Unattributed);
    }

    [Theory]
    [InlineData(0x8AAu)]
    [InlineData(0x8A8u)]
    [InlineData(0x8A9u)]
    [InlineData(0x727u)]
    public void BuffRemovalAlsoKeepsTheOtherProvider(uint id)
    {
        var tracker = new RaidBuffTracker();
        tracker.Observe(Application(id, First, Target, 0));
        tracker.Observe(Application(id, Second, Target, 0));
        Snapshot[] statuses = [new(id, Second.EntityId)];
        Assert.True(DamageStatusIdentityPolicy.TryResolve(statuses, id, First.EntityId, true, out var source, out _));
        tracker.Observe(Application(id, First with { EntityId = source }, Target, 2) with { IsRemoval = true });
        var active = tracker.ApplyFallback(Application(65000, Target, Target, 3)).SourceStatuses;
        Assert.Equal(Second.EntityId, Assert.Single(active, s => s.StatusId == id).Source.EntityId);
    }

    private static DamageStatusApplication Application(uint id, DamageActorIdentity source, DamageActorIdentity target, double seconds) =>
        new(target, source, id, "Effect", 0, 0, "", Start.AddSeconds(seconds), 30, true, false, false) { PeriodicPotency = 60 };

    [Theory]
    [InlineData(false, 53, 75)]
    [InlineData(true, 49, 88)]
    public void CapturedOverlappingEffectsRestoreTheCorrectCastersTicks(bool corrected, int firstTicks, int secondTicks)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "CapturedStatusOwnership.json")));
        var tracker = new PeriodicDamageTracker();
        var counts = new Dictionary<uint, int>();
        long sequence = 0;
        foreach (var row in fixture.RootElement.GetProperty("Rows").EnumerateArray())
        {
            var at = row.GetProperty("Time").GetDateTime();
            if (row.GetProperty("Kind").GetString() == "Status")
            {
                var id = row.GetProperty(corrected ? "Source" : "OldSource").GetUInt32();
                var source = First with { EntityId = id };
                if (corrected && row.GetProperty("Removed").GetBoolean())
                {
                    Snapshot[] remaining = [new(1871, id == First.EntityId ? Second.EntityId : First.EntityId)];
                    Assert.True(DamageStatusIdentityPolicy.TryResolve(remaining, 1871, id, true, out id, out _));
                    source = source with { EntityId = id };
                }
                tracker.Observe(new(Target, source, 1871, "Effect", 0, 0, "", at,
                    row.GetProperty("Duration").GetSingle(), true, false, row.GetProperty("Removed").GetBoolean())
                    { PeriodicPotency = 85 });
            }
            else
            {
                foreach (var tick in tracker.Process(new(++sequence, at, Target, 0, "", 0, row.GetProperty("Amount").GetUInt32(), null)))
                    counts[tick.Source.EntityId] = counts.GetValueOrDefault(tick.Source.EntityId) + 1;
            }
        }
        Assert.Equal(firstTicks, counts.GetValueOrDefault(First.EntityId));
        Assert.Equal(secondTicks, counts.GetValueOrDefault(Second.EntityId));
    }

    private static IReadOnlyList<ParsedDamageEvent> Tick(DamageParsingModule module, DamageActorIdentity target, double seconds)
    {
        module.ProcessPeriodicTick(new((long)seconds + target.EntityId, Start.AddSeconds(seconds), target, 0, "", 0, 600, null));
        return module.FlushPendingPeriodicTicks(Start.AddSeconds(seconds).AddMilliseconds(50));
    }
}
