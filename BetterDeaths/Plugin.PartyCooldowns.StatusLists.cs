using BetterDeaths.PartyCooldowns;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using System;
using System.Threading;

namespace BetterDeaths;

public sealed partial class Plugin
{
    private const string TemperaStatusListSignature =
        "48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 48 83 EC 40 40 ?? F6 C6 81 ?? ?? ?? ?? 1E";
    private const string TemperaDoubleStatusListSignature =
        "48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 48 89 7C 24 ?? 41 56 48 83 EC 40 40 ?? FF C6 81 ?? ?? ?? ?? 3C";
    private unsafe delegate void TemperaStatusListDelegate(StatusManager* manager, IntPtr statuses);
    private unsafe delegate void TemperaDoubleStatusListDelegate(StatusManager* manager, IntPtr first, IntPtr second);
    private Hook<TemperaStatusListDelegate>? temperaStatusListHook;
    private Hook<TemperaDoubleStatusListDelegate>? temperaDoubleStatusListHook;

    private unsafe void TryInitializeTemperaStatusLists()
    {
        try
        {
            temperaStatusListHook = GameInteropProvider.HookFromSignature<TemperaStatusListDelegate>(
                TemperaStatusListSignature, OnTemperaStatusList);
            temperaStatusListHook.Enable();
        }
        catch (Exception error)
        {
            temperaStatusListHook?.Dispose();
            temperaStatusListHook = null;
            Log.Warning(error, "Tempera 30-slot shield updates are unavailable.");
        }
        try
        {
            temperaDoubleStatusListHook = GameInteropProvider.HookFromSignature<TemperaDoubleStatusListDelegate>(
                TemperaDoubleStatusListSignature, OnTemperaDoubleStatusList);
            temperaDoubleStatusListHook.Enable();
        }
        catch (Exception error)
        {
            temperaDoubleStatusListHook?.Dispose();
            temperaDoubleStatusListHook = null;
            Log.Warning(error, "Tempera 60-slot shield updates are unavailable.");
        }
    }

    private unsafe void OnTemperaStatusList(StatusManager* manager, IntPtr statuses) =>
        ProcessTemperaStatusList(manager, statuses, IntPtr.Zero, false);

    private unsafe void OnTemperaDoubleStatusList(StatusManager* manager, IntPtr first, IntPtr second) =>
        ProcessTemperaStatusList(manager, first, second, true);

    private unsafe void CallOriginalTemperaStatusList(StatusManager* manager, IntPtr first, IntPtr second, bool doubled)
    {
        if (doubled)
            temperaDoubleStatusListHook!.Original(manager, first, second);
        else
            temperaStatusListHook!.Original(manager, first);
    }

    private unsafe bool TryGetTemperaStatusListActor(StatusManager* manager, out uint target, out PartyCooldownActor? actor)
    {
        target = 0;
        actor = null;
        if (!Configuration.ShowPartyCooldowns || disposing || !Volatile.Read(ref partyCooldownHasTempera) ||
            manager is null || manager->Owner is null || manager->Owner->ObjectKind != ObjectKind.Pc || manager->Owner->ClassJob != 42)
            return false;
        target = manager->Owner->EntityId;
        if (!Volatile.Read(ref partyCooldownActors).TryGetValue(target, out actor) || actor.JobId != 42)
            return false;
        var content = manager->Owner->ContentId;
        return content == 0 || actor.Key == content || actor.Key == (0xF000000000000000ul | target);
    }

    private static unsafe void CopyTemperaStatusListSlots(StatusManager* manager, int count, Span<TemperaStatusSlot> slots)
    {
        for (var index = 0; index < count; index++)
        {
            var status = manager->Status[index];
            slots[index] = new(status.StatusId, status.SourceObject.ObjectId, status.RemainingTime);
        }
    }

    private unsafe void ProcessTemperaStatusList(StatusManager* manager, IntPtr first, IntPtr second, bool doubled)
    {
        if (first == IntPtr.Zero || doubled && second == IntPtr.Zero ||
            !TryGetTemperaStatusListActor(manager, out var target, out var actor))
        {
            CallOriginalTemperaStatusList(manager, first, second, doubled);
            return;
        }
        var slotCount = doubled ? 60 : 30;
        Span<TemperaStatusSlot> expected = stackalloc TemperaStatusSlot[60];
        Span<TemperaStatusSlot> actual = stackalloc TemperaStatusSlot[60];
        Span<TemperaEvent> observations = stackalloc TemperaEvent[TemperaStatusListReader.MaximumEvents];
        var prepared = false;
        var count = 0;
        ulong changedSlots = 0;
        try
        {
            using var sample = MeasurePartyCooldown(PartyCooldownPhase.Capture);
            CopyTemperaStatusListSlots(manager, slotCount, expected);
            var firstHalf = new ReadOnlySpan<byte>((void*)first, TemperaStatusListReader.HalfSize);
            var secondHalf = doubled ? new ReadOnlySpan<byte>((void*)second, TemperaStatusListReader.HalfSize) : default;
            prepared = TemperaStatusListReader.TryRead(firstHalf, secondHalf, target, PartyCooldownNow,
                expected, observations, out count, out changedSlots);
        }
        catch (Exception error)
        {
            Log.Debug(error, "Could not read a Tempera status list.");
        }

        CallOriginalTemperaStatusList(manager, first, second, doubled);
        try
        {
            using var sample = MeasurePartyCooldown(PartyCooldownPhase.Capture);
            if (!prepared || count == 0 || !TryGetTemperaStatusListActor(manager, out var currentTarget, out var currentActor) ||
                currentTarget != target || currentActor!.Key != actor!.Key)
                return;
            CopyTemperaStatusListSlots(manager, slotCount, actual);
            if (TemperaResultReader.MatchesAppliedSlots(expected, actual, changedSlots))
                for (var index = 0; index < count; index++)
                    EnqueueTemperaEvent(target, observations[index], actor.Key);
        }
        catch (Exception error)
        {
            Log.Debug(error, "Could not capture a Tempera status list.");
        }
    }
}
