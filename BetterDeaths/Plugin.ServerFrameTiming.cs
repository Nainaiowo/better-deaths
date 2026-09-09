namespace BetterDeaths;

using BetterDeaths.DamageParsing;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Network;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

public sealed partial class Plugin
{
    private const string GenericZoneDownSignature = "E8 ?? ?? ?? ?? 4C 8B 4F 10 8B 47 1C 45";
    private const int ZoneDownMatchIndex = 2;

    [ThreadStatic]
    private static ServerFrameTimestampCapture? currentServerFrameTiming;
    private static ServerFrameTimestampCapture? CurrentServerFrameTiming => currentServerFrameTiming;
    private sealed record ServerFrameTimestampCapture(DateTime SeenAtUtc);

    private Hook<ExtractZonePacketDelegate>? serverFrameHook;
    private Hook<PacketDispatcher.Delegates.OnReceivePacket>? packetTimingDispatchHook;
    private readonly DamagePacketTimingHandoff packetTimingHandoff = new();
    private readonly DamagePacketTimingDiagnostics packetTimingDiagnostics = new();
    private long timingExtractionCalls, timingInvalidPackets, timingDispatchCalls, timingCaptureErrors;
    private long timingActionCallbacks, timingActionsWithoutTimestamp, timingStatusCallbacks, timingStatusesWithoutTimestamp;
    private DateTime nextTimingHealthAtUtc;

    private unsafe delegate nint ExtractZonePacketDelegate(byte* state, nint allocator, byte* packetInfo,
        nint decompressionBuffer, nuint decompressionSize, nint context, nint callback, nint codec);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern nint RtlLookupFunctionEntry(ulong controlPc, out ulong imageBase, nint historyTable);

    private unsafe void TryInitializeServerFrameTiming()
    {
        try
        {
            var matches = SigScanner.ScanAllText(GenericZoneDownSignature, CancellationToken.None)
                .Take(ZoneDownMatchIndex + 1).ToArray();
            if (matches.Length <= ZoneDownMatchIndex)
                throw new InvalidOperationException("The zone packet extraction call site is unavailable.");

            // The call target only decompresses. Its containing function extracts one IPC
            // into the game's owned buffer; callbacks run later, after that function returns.
            var callSite = matches[ZoneDownMatchIndex];
            var function = RtlLookupFunctionEntry((ulong)callSite, out var imageBase, 0);
            if (function == 0)
                throw new InvalidOperationException("Zone packet extraction unwind metadata is unavailable.");
            var address = (nint)(imageBase + (uint)Marshal.ReadInt32(function));
            var end = (nint)(imageBase + (uint)Marshal.ReadInt32(function, 4));
            var textStart = SigScanner.Module.BaseAddress + (int)SigScanner.TextSectionOffset;
            var textEnd = textStart + SigScanner.TextSectionSize;
            if (address < textStart || end > textEnd || callSite < address || callSite >= end)
                throw new InvalidOperationException("Invalid zone packet extraction function bounds.");
            var dispatch = PacketDispatcher.StaticVirtualTablePointer;
            if (dispatch == null || dispatch->OnReceivePacket == null)
                throw new InvalidOperationException("The zone packet dispatcher is unavailable.");

            serverFrameHook = GameInteropProvider.HookFromAddress<ExtractZonePacketDelegate>(address, OnExtractZonePacket);
            packetTimingDispatchHook = GameInteropProvider.HookFromAddress<PacketDispatcher.Delegates.OnReceivePacket>(
                (nint)dispatch->OnReceivePacket, OnDispatchTimedPacket);
            packetTimingDispatchHook.Enable();
            serverFrameHook.Enable();
            Log.Information("Better Deaths packet timing handoff enabled.");
        }
        catch (Exception ex)
        {
            serverFrameHook?.Dispose();
            packetTimingDispatchHook?.Dispose();
            serverFrameHook = null;
            packetTimingDispatchHook = null;
            Log.Warning(ex, "Better Deaths packet timing handoff is unavailable; the damage meter will use local receipt time.");
        }
    }

    private unsafe nint OnExtractZonePacket(byte* state, nint allocator, byte* packetInfo,
        nint decompressionBuffer, nuint decompressionSize, nint context, nint callback, nint codec)
    {
        var result = serverFrameHook!.Original(state, allocator, packetInfo, decompressionBuffer,
            decompressionSize, context, callback, codec);
        Interlocked.Increment(ref timingExtractionCalls);
        if (result == 0)
            return result;
        try
        {
            // Native extraction copies the IPC into message+0x38 and publishes its length
            // at +0x30. packetInfo+8 is the accompanying 16-byte element header.
            var buffer = *(byte**)(result + 0x38);
            var length = *(ulong*)(result + 0x30);
            var frame = state != null ? *(byte**)(state + 16) : null;
            var source = *(uint*)(result + 0x20);
            var destination = *(uint*)(result + 0x24);
            var receivedAtUtc = DateTime.UtcNow;
            ReadOnlySpan<byte> frameHeader = frame != null ? new(frame, 40) : [];
            ReadOnlySpan<byte> elementHeader = packetInfo != null ? new(packetInfo + 8, 16) : [];
            DateTime? timestamp = null;
            DamagePacketTimingRejection rejection;
            if (state == null)
                rejection = DamagePacketTimingRejection.MissingState;
            else if (frame == null)
                rejection = DamagePacketTimingRejection.MissingFrame;
            else if (packetInfo == null)
                rejection = DamagePacketTimingRejection.MissingElementHeader;
            else if (buffer == null)
                rejection = DamagePacketTimingRejection.MissingIpcBuffer;
            else if (DamagePacketTimingReader.TryRead(frameHeader, elementHeader, length,
                source, destination, receivedAtUtc, out var time, out rejection))
                timestamp = time;
            if (!timestamp.HasValue)
                Interlocked.Increment(ref timingInvalidPackets);
            packetTimingHandoff.Publish((nint)buffer, timestamp.HasValue ? *(ulong*)buffer : 0,
                timestamp.HasValue ? *(ulong*)(buffer + 8) : 0, timestamp);
            packetTimingDiagnostics.Record(rejection,
                ShouldSaveDamageMeterDebug(DamageMeterDebugTraceCategory.StatusChanges) &&
                    Volatile.Read(ref contentCaptureState) is { IsDungeon: false },
                frameHeader, elementHeader, length, source, destination, receivedAtUtc, state != null, buffer != null);
        }
        catch (Exception)
        {
            Interlocked.Increment(ref timingCaptureErrors);
            packetTimingDiagnostics.Record(DamagePacketTimingRejection.CaptureException, false,
                [], [], 0, 0, 0, DateTime.UtcNow, false, false);
        }
        return result;
    }

    private unsafe void OnDispatchTimedPacket(PacketDispatcher* dispatcher, uint targetId, nint packet)
    {
        Interlocked.Increment(ref timingDispatchCalls);
        var previous = currentServerFrameTiming;
        currentServerFrameTiming = null;
        try
        {
            if (packet != 0)
            {
                var timestamp = packetTimingHandoff.Take(packet, *(ulong*)packet, *(ulong*)(packet + 8));
                if (timestamp is { } time)
                    currentServerFrameTiming = new(time);
            }
        }
        catch (Exception)
        {
            Interlocked.Increment(ref timingCaptureErrors);
        }
        try
        {
            // Keep one packet's nested status/action callbacks in a single queue snapshot.
            lock (rawCombatQueueLock)
                packetTimingDispatchHook!.Original(dispatcher, targetId, packet);
        }
        finally
        {
            currentServerFrameTiming = previous;
        }
    }

    private void RecordTimingHealth(DateTime now)
    {
        if (now < nextTimingHealthAtUtc || !ShouldSaveDamageMeterDebug(DamageMeterDebugTraceCategory.StatusChanges))
            return;
        nextTimingHealthAtUtc = now.AddSeconds(10);
        QueueDebugCaptureRecord("DamageMeterTimingHealth", new
        {
            ExtractionHookEnabled = serverFrameHook?.IsEnabled == true,
            DispatchHookEnabled = packetTimingDispatchHook?.IsEnabled == true,
            StatusHookEnabled = statusTimingSetHook?.IsEnabled == true && statusTimingRemoveHook?.IsEnabled == true,
            ExtractionCalls = Interlocked.Read(ref timingExtractionCalls),
            InvalidPackets = Interlocked.Read(ref timingInvalidPackets),
            DispatchCalls = Interlocked.Read(ref timingDispatchCalls),
            CaptureErrors = Interlocked.Read(ref timingCaptureErrors),
            ActionCallbacks = Interlocked.Read(ref timingActionCallbacks),
            ActionsWithoutTimestamp = Interlocked.Read(ref timingActionsWithoutTimestamp),
            StatusCallbacks = Interlocked.Read(ref timingStatusCallbacks),
            StatusCallbacksWithoutTimestamp = Interlocked.Read(ref timingStatusesWithoutTimestamp),
            Handoff = packetTimingHandoff.Snapshot(),
            Validation = packetTimingDiagnostics.Snapshot(),
        });
    }
}
