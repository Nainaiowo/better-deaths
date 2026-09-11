using BetterDeaths.DamageParsing;

namespace BetterDeaths.Tests;

public sealed class OverworldDamageEncounterTests
{
    private static readonly DateTime Start = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DamageActorIdentity Player = new(0x1001, "Player", 0, "", true, 31);
    private static readonly DamageActorIdentity OtherPlayer = new(0x1002, "Nearby player", 0, "", true, 20);
    private static readonly DamageActorIdentity Enemy = new(0x40000001, "Enemy", 0, "", false, 0);

    [Fact]
    public void EndingRequiresThreeSecondsWithoutCombatOrNewDamage()
    {
        var state = new OverworldDamageEncounterState();
        Assert.False(state.ShouldEnd(true, true, Start, Start));
        Assert.False(state.ShouldEnd(true, false, Start, Start.AddSeconds(1)));
        Assert.False(state.ShouldEnd(true, false, Start, Start.AddSeconds(3.99)));
        Assert.True(state.ShouldEnd(true, false, Start, Start.AddSeconds(4)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ResumedCombatOrDamageCancelsTheEndCountdown(bool combatFlag)
    {
        var state = new OverworldDamageEncounterState();
        state.ShouldEnd(true, true, Start, Start);
        state.ShouldEnd(true, false, Start, Start.AddSeconds(1));
        var latest = combatFlag ? Start : Start.AddSeconds(3);
        Assert.False(state.ShouldEnd(true, combatFlag, latest, Start.AddSeconds(3)));
        Assert.False(state.ShouldEnd(true, false, latest, Start.AddSeconds(4)));
        Assert.False(state.ShouldEnd(true, false, latest, Start.AddSeconds(6.99)));
        Assert.True(state.ShouldEnd(true, false, latest, Start.AddSeconds(7)));
    }

    [Fact]
    public void NoEncounterOrResetCannotReuseAnOldCountdown()
    {
        var state = new OverworldDamageEncounterState();
        state.ShouldEnd(true, true, Start, Start);
        state.ShouldEnd(true, false, Start, Start.AddSeconds(1));
        Assert.False(state.ShouldEnd(false, false, null, Start.AddSeconds(10)));
        Assert.False(state.ShouldEnd(true, false, Start.AddSeconds(11), Start.AddSeconds(11)));
        state.Reset();
        Assert.False(state.ShouldEnd(true, false, Start.AddSeconds(11), Start.AddSeconds(20)));
    }

    [Fact]
    public void NonPartyPlayersAndPetOwnersAreParticipantsButBystandersAreNot()
    {
        var module = CreateModule();
        module.Process(Packet(1));
        var pet = new DamageActorIdentity(0x40000002, "Pet", OtherPlayer.EntityId, OtherPlayer.Name, false, 0)
        { IsPet = true };
        module.Process(Packet(2) with { Source = pet, SourceOwner = OtherPlayer });
        module.RecordDeath(Player);
        Assert.True(module.IsEncounterPlayer(Player.EntityId));
        Assert.True(module.IsEncounterPlayer(OtherPlayer.EntityId));
        Assert.False(module.IsEncounterPlayer(0x1003));
        Assert.False(module.IsEncounterPlayer(pet.EntityId));
        Assert.False(module.IsEncounterPlayer(Enemy.EntityId));
        var state = new OverworldDamageEncounterState();
        var activity = module.GetEncounterActivity();
        Assert.False(state.ShouldEnd(activity.HasEncounter, true, activity.LatestDamageAtUtc, Start.AddSeconds(30)));
    }

    [Fact]
    public void OverworldDamageStartsWithoutLocalCombatAndWaitingDoesNotInflateDpsDuration()
    {
        var module = CreateModule();
        module.SetCombatActive(false, Start);
        module.Process(Packet(1));
        module.SetCombatActive(false, Start.AddSeconds(2));
        module.Process(Packet(11));
        var detached = module.DetachEncounter(Start.AddSeconds(15), "Overworld combat ended", true)!;
        var result = DamageParsingModule.CompleteDetachedEncounter(detached);
        Assert.Equal(10, result.DurationSeconds);
        Assert.Equal(2000ul, result.TotalDamage);
        module.PublishCompletedEncounter(detached, result);
        Assert.False(module.GetEncounterActivity().HasEncounter);
        Assert.Same(result, module.LastEncounter);
        Assert.Null(module.DetachEncounter(Start.AddSeconds(16), "Repeated reset", true));
        module.Process(Packet(20));
        Assert.Equal(1000ul, module.GetCurrentEncounter(Start.AddSeconds(20))!.TotalDamage);
        Assert.Same(result, module.LastEncounter);
    }

    [Fact]
    public void BuffAndNonPlayerDamageAloneDoNotStartAnEncounter()
    {
        var module = CreateModule();
        module.SetCombatActive(false, Start);
        module.ObserveStatus(PeriodicStatus());
        module.Process(Packet(1) with { Source = Enemy });
        Assert.False(module.GetEncounterActivity().HasEncounter);
    }

    [Fact]
    public void ManualResetPreservesActiveDotsEvenWhileWaitingForTheNextTick()
    {
        var module = CreateModule();
        module.SetCombatActive(false, Start);
        module.Process(Packet(1) with { DirectPotency = 100, CanCalibratePotency = true });
        module.ObserveStatus(PeriodicStatus());
        var first = module.DetachEncounter(Start.AddSeconds(2), "Manual new encounter", true)!;
        // Exercise pre-encounter pruning, including more than its two-second retention window.
        module.SetCombatActive(false, Start.AddSeconds(5));
        module.RefreshStatus(Enemy.EntityId, 0x500, Start.AddSeconds(5));
        module.SetCombatActive(false, Start.AddSeconds(8));
        module.ProcessPeriodicTick(Tick(9));
        module.FlushPendingPeriodicTicks(Start.AddSeconds(9.1));
        var second = module.DetachEncounter(Start.AddSeconds(10), "Next", true)!;
        var result = DamageParsingModule.CompleteDetachedEncounter(second);
        Assert.Equal(1000ul, DamageParsingModule.CompleteDetachedEncounter(first).TotalDamage);
        var tick = Assert.Single(result.Events);
        Assert.True(tick.IsPeriodic);
        Assert.Equal(Player.EntityId, (tick.AttributedSource ?? tick.Source).EntityId);
        Assert.Equal(600ul, result.TotalDamage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreservedDotsStillExpireOrAreRemoved(bool explicitRemoval)
    {
        var module = CreateModule();
        module.Process(Packet(1));
        module.ObserveStatus(PeriodicStatus());
        module.DetachEncounter(Start.AddSeconds(2), "Manual", true);
        if (explicitRemoval)
            module.ObserveStatus(PeriodicStatus() with { IsRemoval = true, SeenAtUtc = Start.AddSeconds(3) });
        var tickTime = explicitRemoval ? 10 : 60;
        module.ProcessPeriodicTick(Tick(tickTime));
        module.FlushPendingPeriodicTicks(Start.AddSeconds(tickTime + 0.1));
        Assert.False(module.GetEncounterActivity().HasEncounter);
    }

    [Fact]
    public void ManualResetRetainsBuffsButTerritoryOrDutyResetClearsThem()
    {
        var module = CreateModule();
        module.ObserveStatus(new DamageStatusApplication(Player, OtherPlayer, 0x4A1, "Brotherhood",
            0, 0, "", Start, 20, false, false, false));
        module.Process(Packet(1));
        module.DetachEncounter(Start.AddSeconds(2), "Manual", true);
        module.Process(Packet(3));
        var second = module.DetachEncounter(Start.AddSeconds(4), "Left territory")!;
        var buffed = DamageParsingModule.CompleteDetachedEncounter(second);
        Assert.Contains(buffed.Sources, source => source.Source.EntityId == OtherPlayer.EntityId && source.RaidBuffDamageGiven > 0);
        module.Process(Packet(5));
        var unbuffed = module.GetCurrentEncounter(Start.AddSeconds(5))!;
        Assert.Single(unbuffed.Sources);
        Assert.Equal(0, unbuffed.Sources[0].ExternalBuffDamageReceived);
    }

    [Fact]
    public void DutyPacketsStillWaitForExplicitCombatActivation()
    {
        var module = CreateModule();
        module.SetCombatActive(false, Start);
        module.Process(Packet(1), allowAutomaticEncounterStart: false);
        Assert.False(module.GetEncounterActivity().HasEncounter);
        module.SetCombatActive(true, Start.AddSeconds(1.1));
        Assert.True(module.GetEncounterActivity().HasEncounter);
        module.DetachEncounter(Start.AddSeconds(2), "Duty reset");
        Assert.False(module.IsEncounterPlayer(Player.EntityId));
    }

    [Fact]
    public void CastInProgressAtManualResetCannotBackdateTheNextMeterIntoThePreviousEncounter()
    {
        var module = CreateModule();
        module.Process(Packet(1));
        module.DetachEncounter(Start.AddSeconds(2), "Manual", true);
        module.ObserveOffensiveCast(Start.AddSeconds(1.5), Start.AddSeconds(2.5));
        module.Process(Packet(3) with { CapturedAtUtc = Start.AddSeconds(3) });
        var result = module.GetCurrentEncounter(Start.AddSeconds(3))!;
        Assert.Equal(Start.AddSeconds(2), result.MeterStartedAtUtc);
        Assert.Equal(1, result.DurationSeconds);
    }

    private static DamageParsingModule CreateModule() => new() { RequireKnownPlayerForAutomaticStart = true };

    private static DamageStatusApplication PeriodicStatus() => new(
        Enemy, Player, 0x500, "Periodic", 0, 100, "Action", Start.AddSeconds(1.1), 30, true, false, false)
    {
        PeriodicPotency = 20,
        SourceStatuses = [], TargetStatuses = [], HasSourceStatusSnapshot = true, HasTargetStatusSnapshot = true,
    };

    private static PeriodicDamageTick Tick(int seconds) => new(seconds, Start.AddSeconds(seconds), Enemy, 0, "", 0, 600, null);

    private static DamageActionPacket Packet(long sequence) => new(
        sequence, Start.AddSeconds(sequence), (uint)sequence, Player, 100, "Action",
        [new DamageActionTarget(0, Enemy, [new DamageActionEffect(0, 3, 0, 0, 0, 0, 1000)])]);
}
