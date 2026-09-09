namespace BetterDeaths;

using System;
using System.Collections.Generic;
using System.Threading;
using BetterDeaths.DamageParsing;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

public sealed partial class Plugin
{
    private Hook<StatusManager.Delegates.SetStatus>? statusTimingSetHook;
    private Hook<StatusManager.Delegates.RemoveStatus>? statusTimingRemoveHook;
    private readonly Queue<RawStatusTimingUpdate> rawStatusTimingUpdates = [];
    private readonly HashSet<(uint Actor, int Slot)> capturedTimingSlots = [];
    private long droppedStatusTimingUpdates;
    [ThreadStatic]
    private static bool statusTimingMissingInformation;
    [ThreadStatic]
    private static int statusTimingSetDepth;

    private sealed record RawStatusTimingUpdate(long DamageCaptureOrder, DateTime SeenAtUtc,
        ServerFrameTimestampCapture Timing, uint TargetId, byte Slot, uint StatusId,
        uint SourceId, ushort Parameter, float Duration, bool MissingInformation);

    private unsafe void TryInitializeStatusTiming()
    {
        try
        {
            if (serverFrameHook is null)
                throw new InvalidOperationException("Server-frame timing is required for status-update capture.");
            if (StatusManager.Addresses.SetStatus.Value == IntPtr.Zero ||
                StatusManager.Addresses.RemoveStatus.Value == IntPtr.Zero)
                throw new InvalidOperationException("Status update addresses are unavailable.");
            statusTimingSetHook = GameInteropProvider.HookFromAddress<StatusManager.Delegates.SetStatus>(
                StatusManager.Addresses.SetStatus.Value, OnSetStatusTiming);
            statusTimingRemoveHook = GameInteropProvider.HookFromAddress<StatusManager.Delegates.RemoveStatus>(
                StatusManager.Addresses.RemoveStatus.Value, OnRemoveStatusTiming);
            statusTimingSetHook.Enable();
            statusTimingRemoveHook.Enable();
            Log.Information("Better Deaths damage meter status-update timing enabled.");
        }
        catch (Exception ex)
        {
            statusTimingSetHook?.Dispose();
            statusTimingRemoveHook?.Dispose();
            statusTimingSetHook = null;
            statusTimingRemoveHook = null;
            Log.Warning(ex, "Better Deaths status-update timing is unavailable; captured countdown timing remains in use.");
        }
    }

    private unsafe bool OnSetStatusTiming(StatusManager* manager, int index, ushort statusId,
        float remaining, ushort parameter, GameObjectId source, bool refreshFlags)
    {
        CaptureStatusTiming(manager, index, statusId, source.ObjectId, parameter, remaining);
        statusTimingSetDepth++;
        try
        {
            return statusTimingSetHook!.Original(manager, index, statusId, remaining, parameter, source, refreshFlags);
        }
        finally
        {
            statusTimingSetDepth--;
        }
    }

    private unsafe void OnRemoveStatusTiming(StatusManager* manager, int index, byte flags)
    {
        // Replacing a slot is atomic; its internal clear is not a later server removal.
        if (statusTimingSetDepth == 0)
            CaptureStatusTiming(manager, index, 0, 0, 0, 0);
        statusTimingRemoveHook!.Original(manager, index, flags);
    }

    private unsafe void CaptureStatusTiming(StatusManager* manager, int index, uint statusId,
        uint sourceId, ushort parameter, float duration)
    {
        // Local countdown/expiry calls are not server status updates.
        Interlocked.Increment(ref timingStatusCallbacks);
        if (CurrentServerFrameTiming is null)
            Interlocked.Increment(ref timingStatusesWithoutTimestamp);
        if (CurrentServerFrameTiming is not { } timing || manager is null || manager->Owner is null ||
            index is < 0 or >= 60)
            return;
        try
        {
            var targetId = manager->Owner->EntityId;
            lock (rawCombatQueueLock)
            {
                var key = (targetId, index);
                var relevant = DamageStatusCapturePolicy.IsRelevant(statusId);
                if (!relevant && !DamageStatusCapturePolicy.IsRelevant(manager->Status[index].StatusId) &&
                    !capturedTimingSlots.Contains(key))
                    return;
                var now = DateTime.UtcNow;
                if (!ShouldAcceptDamageParserCapture(now))
                    return;
                if (rawStatusTimingUpdates.Count >= 16384)
                {
                    droppedStatusTimingUpdates++;
                    return;
                }
                if (relevant)
                    capturedTimingSlots.Add(key);
                else
                    capturedTimingSlots.Remove(key);
                rawStatusTimingUpdates.Enqueue(new(nextDamageCaptureOrder++, now, timing,
                    targetId, (byte)index, statusId, sourceId, parameter, duration,
                    statusId != 0 && statusTimingMissingInformation));
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not capture Better Deaths status-update timing.");
        }
    }

    private void ObserveStatusTiming(RawStatusTimingUpdate packet)
    {
        var seenAtUtc = packet.Timing.SeenAtUtc;
        var update = new DamageStatusTimingUpdate(NormalizeActorEntityId(packet.TargetId), packet.Slot,
            new(packet.StatusId, CaptureDamageActorIdentity(packet.SourceId, string.Empty), packet.Parameter,
                packet.Duration)
            {
                StatusSlot = packet.Slot,
                HasParameter = !RaidBuffPolicy.UsesApplicationParameter(packet.StatusId) && packet.Parameter != 0,
            }, seenAtUtc, packet.MissingInformation);
        var accepted = damageParsingModule.ObserveStatusTiming(update);
        if (ShouldSaveDamageMeterDebug(DamageMeterDebugTraceCategory.StatusChanges))
            QueueDebugCaptureRecord("DamageMeterStatusTiming", new
            {
                packet.DamageCaptureOrder,
                CapturedAtUtc = packet.SeenAtUtc,
                ServerSeenAtUtc = packet.Timing.SeenAtUtc,
                Update = update,
                Accepted = accepted,
            });
    }
}
