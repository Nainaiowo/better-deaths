using BetterDeaths.PartyCooldowns;
using BetterDeaths.DamageParsing;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using System;
using System.Threading;

namespace BetterDeaths;

public sealed partial class Plugin
{
    private readonly TemperaCooldownTracker temperaCooldownTracker = new();
    [ThreadStatic]
    private static PartyCooldownLanding? currentPartyCooldownLanding;
    private readonly record struct PartyCooldownLanding(uint Target, uint Sequence, uint Hp);

    private void EnqueueTemperaEvent(uint target, TemperaEvent observation)
    {
        if (!Configuration.ShowPartyCooldowns || disposing ||
            !Volatile.Read(ref partyCooldownActors).TryGetValue(target, out var actor) || actor.JobId != 42)
            return;
        EnqueuePartyCooldownObservation(new(target, 0, 0, observation.SeenAt)
        {
            MemberKey = actor.Key,
            TerritoryId = ClientState.TerritoryType,
            Tempera = observation,
        });
    }

    private unsafe void CaptureTemperaStatus(StatusManager* manager, int index, uint statusId,
        uint sourceId, float remaining, bool removal)
    {
        if (!Configuration.ShowPartyCooldowns || manager is null || manager->Owner is null ||
            index is < 0 or >= 60 || !removal && CurrentServerFrameTiming is null)
            return;
        try
        {
            var target = manager->Owner->EntityId;
            if (removal)
            {
                var status = manager->Status[index];
                statusId = status.StatusId;
                sourceId = status.SourceObject.ObjectId;
                remaining = status.RemainingTime;
            }
            if (!TemperaCooldownTracker.IsOwnShield(target, sourceId, statusId))
                return;
            var landing = currentPartyCooldownLanding is { } context && context.Target == target
                ? context : (PartyCooldownLanding?)null;
            EnqueueTemperaEvent(target, new(removal ? TemperaEventKind.Removed : TemperaEventKind.Applied, PartyCooldownNow)
            {
                StatusId = statusId,
                ServerFrame = CurrentServerFrameTiming?.SeenAtUtc.Ticks ?? 0,
                Remaining = float.IsFinite(remaining) ? DamageStatusTiming.DecodeRemaining(remaining) : double.NaN,
                Sequence = landing?.Sequence ?? 0,
                Hp = landing?.Hp ?? 0,
                HasHp = landing is not null,
            });
        }
        catch (Exception error)
        {
            Log.Debug(error, "Could not capture a party Tempera status update.");
        }
    }

    private unsafe void CaptureTemperaDamage(ActionEffectHandler.Header* header,
        ActionEffectHandler.TargetEffects* effects, GameObjectId* targets)
    {
        if (effects is null || targets is null)
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

    private unsafe PartyCooldownLanding? ReadPartyCooldownLanding(uint target, IntPtr data)
    {
        if (!Configuration.ShowPartyCooldowns || data == IntPtr.Zero)
            return null;
        var packet = (EffectResultPacket*)data;
        target = target != 0 ? target : packet->ActorId;
        return Volatile.Read(ref partyCooldownActors).TryGetValue(target, out var actor) && actor.JobId == 42
            ? new(target, packet->RelatedActionSequence, packet->CurrentHp) : null;
    }

    private void CapturePartyCooldownLanding()
    {
        if (currentPartyCooldownLanding is { Hp: 0 } landing && CurrentServerFrameTiming is { } timing)
            EnqueueTemperaEvent(landing.Target, new(TemperaEventKind.Landing, PartyCooldownNow)
            {
                Sequence = landing.Sequence,
                ServerFrame = timing.SeenAtUtc.Ticks,
                Hp = landing.Hp,
                HasHp = true,
            });
    }

    private void CapturePartyCooldownDeath(uint target)
    {
        try
        {
            EnqueueTemperaEvent(target, new(TemperaEventKind.Death, PartyCooldownNow));
        }
        catch (Exception error)
        {
            Log.Debug(error, "Could not capture a party cooldown death notification.");
        }
    }

    private void ResolveTemperaCooldowns(double now)
    {
        foreach (var pair in partyCooldownActors)
        {
            var actor = pair.Value;
            if (actor.JobId != 42 || !actor.Profiles.TryGetValue(TemperaCooldownTracker.CoatAction, out var profile))
                continue;
            if (ObjectTable.SearchByEntityId(pair.Key) is Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter player &&
                (player.IsDead || player.CurrentHp == 0))
                temperaCooldownTracker.Observe(actor.Key, new(TemperaEventKind.Death, now));
            if (temperaCooldownTracker.TryReduce(actor.Key, now, out var reduction) &&
                partyCooldownTracker.Reduce(actor.Key, profile, reduction, now))
                nextPartyCooldownDisplayAt = 0;
        }
    }

}
