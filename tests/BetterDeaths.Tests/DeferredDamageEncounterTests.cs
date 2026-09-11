using BetterDeaths.DamageParsing;
using System.Text.Json;

namespace BetterDeaths.Tests;

public sealed class DeferredDamageEncounterTests
{
    private static readonly DateTime Start = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DamageActorIdentity Player = new(0x1001, "Player", 0, "", true, 31);
    private static readonly DamageActorIdentity Enemy = new(0x40000001, "Enemy", 0, "", false, 0);

    [Fact]
    public void DetachingClearsLiveStateWithoutCalculatingOrPublishingAFinalSnapshot()
    {
        var module = new DamageParsingModule();
        module.Process(Packet(1));
        var detached = module.DetachEncounter(Start.AddSeconds(2), "Combat ended")!;
        Assert.Null(module.GetCurrentEncounter());
        Assert.Null(module.GetLiveEncounter());
        Assert.Null(module.LastEncounter);
        Assert.Single(detached.Input.Events);
        Assert.Same(DamageEncounterDiagnostics.Empty, detached.Input.Diagnostics);
        Assert.Equal(Start.AddSeconds(1), detached.Input.EndedAtUtc);
        Assert.Equal("Combat ended", detached.Input.EndReason);
    }

    [Fact]
    public async Task FullDeferredResultMatchesSynchronousEndIncludingAllDiagnostics()
    {
        var synchronous = new DamageParsingModule();
        var deferred = new DamageParsingModule();
        foreach (var module in new[] { synchronous, deferred })
        {
            SeedWithPendingTick(module);
            module.RecordDeath(Player);
        }
        var end = Start.AddSeconds(3);
        var expected = synchronous.EndEncounter(end, "Wipe")!;
        var detached = deferred.DetachEncounter(end, "Wipe")!;
        var actual = await Task.Run(() => DamageParsingModule.CompleteDetachedEncounter(detached));
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
        Assert.NotEmpty(actual.Events);
        Assert.NotSame(DamageEncounterDiagnostics.Empty, actual.Diagnostics);
        deferred.PublishCompletedEncounter(detached, actual);
        Assert.Same(actual, deferred.LastEncounter);
    }

    [Fact]
    public void PendingTicksAreFlushedExactlyOnceBeforeDetachment()
    {
        var module = new DamageParsingModule();
        SeedWithPendingTick(module);
        var callbacks = new List<ParsedDamageEvent>();
        module.PeriodicEventsResolved = parsed => callbacks.AddRange(parsed);
        var detached = module.DetachEncounter(Start.AddSeconds(3), "Reset")!;
        Assert.Single(callbacks);
        Assert.Equal(2, detached.Input.Events.Count);
        Assert.Null(module.DetachEncounter(Start.AddSeconds(4), "Reset again"));
        Assert.Single(callbacks);
        var completed = DamageParsingModule.CompleteDetachedEncounter(detached);
        Assert.Equal(1600ul, completed.TotalDamage);
        Assert.Equal(2, completed.Events.Count);
    }

    [Fact]
    public async Task DetachedInputsStayUnchangedWhileTheNextPullRecordsAndRefreshes()
    {
        var module = new DamageParsingModule();
        module.Process(Packet(1));
        module.Process(Packet(2));
        var detached = module.DetachEncounter(Start.AddSeconds(3), "First")!;
        var frozen = JsonSerializer.Serialize(detached);
        module.Process(Packet(10));
        module.Process(Packet(11));
        module.Process(Packet(12));
        module.RecordDeath(Player);
        var completed = await Task.Run(() => DamageParsingModule.CompleteDetachedEncounter(detached));
        module.PublishCompletedEncounter(detached, completed);
        Assert.Equal(frozen, JsonSerializer.Serialize(detached));
        Assert.Equal(2000ul, module.LastEncounter!.TotalDamage);
        Assert.Equal(3000ul, module.GetCurrentEncounter()!.TotalDamage);
        Assert.Equal(0, module.LastEncounter.Sources.Single().Deaths);
        Assert.Equal(1, module.GetCurrentEncounter()!.Sources.Single().Deaths);
    }

    [Fact]
    public void LateOrRepeatedPublicationCannotReplaceANewerCompletedEncounter()
    {
        var module = new DamageParsingModule();
        module.Process(Packet(1));
        var first = module.DetachEncounter(Start.AddSeconds(2), "First")!;
        module.Process(Packet(3));
        module.Process(Packet(4));
        var second = module.DetachEncounter(Start.AddSeconds(5), "Second")!;
        var firstResult = DamageParsingModule.CompleteDetachedEncounter(first);
        var secondResult = DamageParsingModule.CompleteDetachedEncounter(second);
        module.PublishCompletedEncounter(second, secondResult);
        module.PublishCompletedEncounter(first, firstResult);
        module.PublishCompletedEncounter(second, firstResult);
        Assert.Same(secondResult, module.LastEncounter);
    }

    [Fact]
    public async Task AnOldLiveWorkerCannotPublishIntoTheNextEncounter()
    {
        var module = new DamageParsingModule();
        module.Process(Packet(1));
        module.GetLiveEncounter();
        module.RefreshLiveEncounter(Start.AddSeconds(1));
        var oldLive = module.PendingLiveSnapshot!;
        var detached = module.DetachEncounter(Start.AddSeconds(2), "First")!;
        module.Process(Packet(3));
        await oldLive;
        module.RefreshLiveEncounter(Start.AddSeconds(3));
        Assert.Null(module.GetLiveEncounter());
        module.PublishCompletedEncounter(detached, DamageParsingModule.CompleteDetachedEncounter(detached));
        Assert.Null(module.GetLiveEncounter());
        Assert.Equal(Start.AddSeconds(3), module.GetCurrentEncounter()!.StartedAtUtc);
    }

    private static void SeedWithPendingTick(DamageParsingModule module)
    {
        module.Process(Packet(1) with { DirectPotency = 100, CanCalibratePotency = true });
        module.ObserveStatus(new DamageStatusApplication(
            Enemy, Player, 0x500, "Periodic", 0, 100, "Action", Start.AddSeconds(1.1), 30, true, false, false)
        {
            PeriodicPotency = 20,
            SourceStatuses = [], TargetStatuses = [], HasSourceStatusSnapshot = true, HasTargetStatusSnapshot = true,
        });
        Assert.Empty(module.ProcessPeriodicTick(new PeriodicDamageTick(100, Start.AddSeconds(3), Enemy, 0, "", 0, 600, null)));
    }

    private static DamageActionPacket Packet(long sequence) => new(
        sequence, Start.AddSeconds(sequence), (uint)sequence, Player, 100, "Action",
        [new DamageActionTarget(0, Enemy, [new DamageActionEffect(0, 3, 0, 0, 0, 0, 1000)])]);
}
