using BetterDeaths.PartyCooldowns;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using System;
using System.Numerics;
using System.Threading;

namespace BetterDeaths;

public sealed partial class Plugin
{
    private readonly TemperaCooldownTracker temperaCooldownTracker = new();
    private double nextTemperaResolutionAt = double.PositiveInfinity;

    private void EnqueueTemperaEvent(uint target, TemperaEvent observation, ulong expectedMember = 0)
    {
        if (!Configuration.ShowPartyCooldowns || disposing ||
            !Volatile.Read(ref partyCooldownActors).TryGetValue(target, out var actor) || actor.JobId != 42 ||
            expectedMember != 0 && actor.Key != expectedMember)
            return;
        EnqueuePartyCooldownObservation(new(target, 0, 0, observation.SeenAt)
        {
            MemberKey = actor.Key,
            TerritoryId = ClientState.TerritoryType,
            Tempera = observation,
        });
    }

    private unsafe bool ProcessTemperaResult(uint target, IntPtr data, byte isReplay)
    {
        if (!Configuration.ShowPartyCooldowns || disposing || isReplay != 0 || data == IntPtr.Zero ||
            !Volatile.Read(ref partyCooldownHasTempera) ||
            !Volatile.Read(ref partyCooldownActors).TryGetValue(target, out var actor) || actor.JobId != 42)
            return false;
        var entries = TemperaResultReader.GetEntryCount(new ReadOnlySpan<byte>((void*)data, TemperaResultReader.HeaderSize));
        if (entries == 0)
            return false;
        Span<TemperaStatusSlot> expected = stackalloc TemperaStatusSlot[TemperaResultReader.SlotCount];
        Span<TemperaStatusSlot> actual = stackalloc TemperaStatusSlot[TemperaResultReader.SlotCount];
        Span<TemperaEvent> observations = stackalloc TemperaEvent[entries * TemperaResultReader.MaximumEventsPerEntry];
        var count = 0;
        ulong changedSlots = 0;
        var prepared = false;
        try
        {
            using var sample = MeasurePartyCooldown(PartyCooldownPhase.Capture);
            var packet = new ReadOnlySpan<byte>((void*)data, TemperaResultReader.HeaderSize + entries * TemperaResultReader.EntrySize);
            prepared = TemperaResultReader.TryGetUpdatedSlots(packet, out changedSlots) &&
                ReadTemperaSlots(target, actor, changedSlots, expected) &&
                TemperaResultReader.TryRead(packet, target, PartyCooldownNow, expected, observations, out count, out changedSlots);
        }
        catch (Exception error)
        {
            Log.Debug(error, "Could not read a party Tempera result.");
        }

        // Run native code exactly once, then verify that it applied the expected slot updates.
        effectResultHook?.Original(target, data, isReplay);
        try
        {
            using var sample = MeasurePartyCooldown(PartyCooldownPhase.Capture);
            if (prepared && count > 0 && ReadTemperaSlots(target, actor, changedSlots, actual) &&
                TemperaResultReader.MatchesAppliedSlots(expected, actual, changedSlots))
                for (var index = 0; index < count; index++)
                    EnqueueTemperaEvent(target, observations[index], actor.Key);
        }
        catch (Exception error)
        {
            Log.Debug(error, "Could not capture a party Tempera result.");
        }
        return true;
    }

    private unsafe StatusManager* GetTemperaStatusManager(uint target, PartyCooldownActor actor)
    {
        var objects = GameObjectManager.Instance();
        var entity = objects is not null ? objects->Objects.GetObjectByEntityId(target) : null;
        if (entity is null || entity->EntityId != target || entity->ObjectKind != ObjectKind.Pc)
            return null;
        var character = (Character*)entity;
        if (character->ClassJob != 42 ||
            actor.Key != (0xF000000000000000ul | target) && character->ContentId != actor.Key)
            return null;
        var manager = character->GetStatusManager();
        return manager is not null && manager->Owner == character ? manager : null;
    }

    private unsafe bool ReadTemperaSlots(uint target, PartyCooldownActor actor, ulong changedSlots, Span<TemperaStatusSlot> slots)
    {
        if (changedSlots == 0)
            return true;
        var manager = GetTemperaStatusManager(target, actor);
        if (manager is null)
            return false;
        while (changedSlots != 0)
        {
            var index = BitOperations.TrailingZeroCount(changedSlots);
            var status = manager->Status[index];
            slots[index] = new(status.StatusId, status.SourceObject.ObjectId, status.RemainingTime);
            changedSlots &= changedSlots - 1;
        }
        return true;
    }

    private unsafe void CaptureTemperaLoss(uint target, uint statusId, uint source)
    {
        if (!Configuration.ShowPartyCooldowns || disposing || !Volatile.Read(ref partyCooldownHasTempera) ||
            !TemperaCooldownTracker.IsOwnShield(target, source, statusId) ||
            !Volatile.Read(ref partyCooldownActors).TryGetValue(target, out var actor) || actor.JobId != 42)
            return;
        using var sample = MeasurePartyCooldown(PartyCooldownPhase.Capture);
        try
        {
            var manager = GetTemperaStatusManager(target, actor);
            if (manager is null)
                return;
            foreach (var status in manager->Status)
                if (status.StatusId == statusId && status.SourceObject.ObjectId == source)
                {
                    // A loss notice can finish expiry, but cannot prove damage consumption.
                    EnqueueTemperaEvent(target, new(TemperaEventKind.Removed, PartyCooldownNow)
                    {
                        StatusId = statusId,
                        Remaining = float.IsFinite(status.RemainingTime) ? Math.Abs(status.RemainingTime) : double.NaN,
                    }, actor.Key);
                    return;
                }
        }
        catch (Exception error)
        {
            Log.Debug(error, "Could not capture a party Tempera loss.");
        }
    }

    private unsafe void CaptureTemperaDamage(ActionEffectHandler.Header* header,
        ActionEffectHandler.TargetEffects* effects, GameObjectId* targets)
    {
        if (!Volatile.Read(ref partyCooldownHasTempera) || effects is null || targets is null)
            return;
        var actors = Volatile.Read(ref partyCooldownActors);
        for (var index = 0; index < Math.Min((int)header->NumTargets, MaxActionEffectTargets); index++)
        {
            var target = targets[index].ObjectId;
            if (!actors.TryGetValue(target, out var actor) || actor.JobId != 42)
                continue;
            var damage = false;
            var stripsShield = false;
            for (var effectIndex = 0; effectIndex < 8; effectIndex++)
            {
                ref var effect = ref effects[index].Effects[effectIndex];
                damage |= TemperaCooldownTracker.IsTargetDamage(effect.Type, effect.Param4);
                stripsShield |= effect.Type is 17 or 18 &&
                    (uint)effect.Value is TemperaCooldownTracker.CoatStatus or TemperaCooldownTracker.GrassaStatus;
            }
            if (damage && !stripsShield)
                EnqueueTemperaEvent(target, new(TemperaEventKind.Damage, PartyCooldownNow) { Sequence = header->GlobalSequence });
        }
    }

    private void CapturePartyCooldownDeath(uint target)
    {
        using var sample = MeasurePartyCooldown(PartyCooldownPhase.Capture);
        try
        {
            EnqueueTemperaEvent(target, new(TemperaEventKind.Death, PartyCooldownNow));
        }
        catch (Exception error)
        {
            Log.Debug(error, "Could not capture a party cooldown death notification.");
        }
    }

    private void ScheduleTemperaCooldownCheck(ulong member) =>
        nextTemperaResolutionAt = Math.Min(nextTemperaResolutionAt, temperaCooldownTracker.GetNextReductionCheckAt(member));

    private void ResolveTemperaCooldowns(double now)
    {
        if (now < nextTemperaResolutionAt)
            return;
        nextTemperaResolutionAt = double.PositiveInfinity;
        foreach (var pair in partyCooldownActors)
        {
            var actor = pair.Value;
            if (actor.JobId != 42 || !actor.Profiles.TryGetValue(TemperaCooldownTracker.CoatAction, out var profile))
                continue;
            var dueAt = temperaCooldownTracker.GetNextReductionCheckAt(actor.Key);
            if (now < dueAt)
            {
                nextTemperaResolutionAt = Math.Min(nextTemperaResolutionAt, dueAt);
                continue;
            }
            if (ObjectTable.SearchByEntityId(pair.Key) is Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter player &&
                (player.IsDead || player.CurrentHp == 0))
                temperaCooldownTracker.Observe(actor.Key, new(TemperaEventKind.Death, now));
            if (temperaCooldownTracker.TryReduce(actor.Key, now, out var reduction) &&
                partyCooldownTracker.Reduce(actor.Key, profile, reduction, now))
                RefreshPartyCooldownDisplay(pair.Key, actor, now, TemperaCooldownTracker.CoatAction);
        }
    }

}
