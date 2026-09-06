using BetterDeaths.DamageParsing;
using System.Text.Json;

namespace BetterDeaths.Tests;

public sealed class DamageLiveSnapshotTests
{
    private static readonly DateTime Start = new(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DamageActorIdentity Player = new(0x1001, "Player", 0, "", true, 31);
    private static readonly DamageActorIdentity Enemy = new(0x40000001, "Enemy", 0, "", false, 0);

    [Fact]
    public async Task DrawingDoesNotComputeOrRefreshSnapshots()
    {
        var module = new DamageParsingModule();
        module.Process(Packet(1));
        module.RefreshLiveEncounter(Start); // No view requested a result.
        Assert.Null(module.GetLiveEncounter());
        await Refresh(module, Start);
        var first = Assert.IsType<DamageEncounterSnapshot>(module.GetLiveEncounter());
        module.Process(Packet(2));
        for (var index = 0; index < 1000; index++)
            Assert.Same(first, module.GetLiveEncounter());
        Assert.Equal(1000ul, first.TotalDamage);
    }

    [Fact]
    public async Task NewEventsCannotBypassTheRefreshInterval()
    {
        var module = new DamageParsingModule();
        module.Process(Packet(1));
        module.GetLiveEncounter();
        await Refresh(module, Start);
        var first = module.GetLiveEncounter();
        for (var index = 1; index < 500; index++)
        {
            module.Process(Packet(index + 1));
            module.GetLiveEncounter();
            module.RefreshLiveEncounter(Start.AddMilliseconds(index));
            Assert.Same(first, module.GetLiveEncounter());
        }

        await Refresh(module, Start + DamageParsingModule.LiveSnapshotRefreshInterval);
        var refreshed = Assert.IsType<DamageEncounterSnapshot>(module.GetLiveEncounter());
        Assert.NotSame(first, refreshed);
        Assert.Equal(500000ul, refreshed.TotalDamage);
        Assert.Equal(module.GetCurrentEncounter()!.DurationSeconds, refreshed.DurationSeconds);
        module.RefreshLiveEncounter(Start.AddMinutes(5));
        Assert.Same(refreshed, module.GetLiveEncounter());
    }

    [Fact]
    public async Task ClosingTheViewStopsRefreshWork()
    {
        var module = new DamageParsingModule();
        module.Process(Packet(1));
        module.GetLiveEncounter();
        await Refresh(module, Start);
        module.Process(Packet(2));
        await Refresh(module, Start.AddSeconds(1));
        Assert.Equal(1000ul, module.GetLiveEncounter()!.TotalDamage);
        await Refresh(module, Start.AddSeconds(1));
        Assert.Equal(2000ul, module.GetLiveEncounter()!.TotalDamage);
    }

    [Fact]
    public async Task LiveResultsMatchFullResultsWithoutDiagnosticHistories()
    {
        var module = new DamageParsingModule();
        module.Process(Packet(1));
        module.Process(Packet(2));
        module.GetLiveEncounter();
        await Refresh(module, Start);
        var live = module.GetLiveEncounter()!;
        var full = module.GetCurrentEncounter()!;
        Assert.Same(DamageEncounterDiagnostics.Empty, live.Diagnostics);
        Assert.NotSame(DamageEncounterDiagnostics.Empty, full.Diagnostics);
        Assert.Equal(JsonSerializer.Serialize(full with { Diagnostics = DamageEncounterDiagnostics.Empty }),
            JsonSerializer.Serialize(live));
    }

    [Fact]
    public async Task EncounterEndFlushesEventsRegardlessOfDisplayThrottleAndClearsTheCache()
    {
        var module = new DamageParsingModule();
        module.Process(Packet(1));
        module.GetLiveEncounter();
        await Refresh(module, Start);
        module.Process(Packet(2));
        var ended = module.EndEncounter(Start.AddMilliseconds(100), "Reset")!;
        Assert.Equal(2000ul, ended.TotalDamage);
        Assert.Equal(2, ended.Events.Count);
        Assert.NotSame(DamageEncounterDiagnostics.Empty, ended.Diagnostics);
        Assert.Null(module.GetLiveEncounter());
        module.Process(Packet(3));
        await Refresh(module, Start.AddMilliseconds(101));
        Assert.Equal(1000ul, module.GetLiveEncounter()!.TotalDamage);
    }

    [Fact]
    public async Task ClockMovingBackwardsDoesNotFreezeTheDisplay()
    {
        var module = new DamageParsingModule();
        module.Process(Packet(1));
        module.GetLiveEncounter();
        await Refresh(module, Start);
        module.Process(Packet(2));
        module.GetLiveEncounter();
        await Refresh(module, Start.AddSeconds(-1));
        Assert.Equal(2000ul, module.GetLiveEncounter()!.TotalDamage);
    }

    [Fact]
    public async Task ResetRejectsTheOldWorkerAndKeepsOneBuildInFlight()
    {
        var module = new DamageParsingModule();
        module.Process(Packet(1));
        module.GetLiveEncounter();
        module.RefreshLiveEncounter(Start);
        var oldWork = module.PendingLiveSnapshot;
        Assert.NotNull(oldWork);
        module.Process(Packet(2));
        module.EndEncounter(Start.AddMilliseconds(5), "Reset");
        module.Process(Packet(3));
        Assert.Null(module.GetLiveEncounter());
        Assert.Same(oldWork, module.PendingLiveSnapshot);
        var oldSnapshot = await (Task<DamageEncounterSnapshot>)oldWork;
        Assert.Equal(1000ul, oldSnapshot.TotalDamage);
        await Refresh(module, Start.AddMilliseconds(6));
        Assert.Equal(1000ul, module.GetLiveEncounter()!.TotalDamage);
        Assert.Equal(Start.AddMilliseconds(3), module.GetLiveEncounter()!.StartedAtUtc);
    }

    private static async Task Refresh(DamageParsingModule module, DateTime now)
    {
        module.RefreshLiveEncounter(now);
        if (module.PendingLiveSnapshot is { } work)
            await work;
        module.RefreshLiveEncounter(now);
    }

    private static DamageActionPacket Packet(long sequence) => new(
        sequence, Start.AddMilliseconds(sequence), (uint)sequence, Player, 100, "Action",
        [new DamageActionTarget(0, Enemy, [new DamageActionEffect(0, 3, 0, 0, 0, 0, 1000)])]);
}
