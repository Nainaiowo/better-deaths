namespace BetterDeaths;

using BetterDeaths.DamageParsing;
using Dalamud.Hooking;
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

    private Hook<ProcessZoneDownDelegate>? serverFrameHook;

    private static ServerFrameTimestampCapture? CurrentServerFrameTiming => currentServerFrameTiming;

    private sealed class ServerFrameTimestampCapture
    {
        public DateTime? SeenAtUtc { get; set; }
    }

    private unsafe delegate nuint ProcessZoneDownDelegate(
        byte* data,
        byte* unknown,
        nuint value3,
        nuint value4,
        nuint value5);

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private unsafe struct ServerFrameHeader
    {
        public fixed byte Prefix[16];
        public ulong TimeValue;
        public uint TotalSize;
        public ushort Protocol;
        public ushort Count;
        public byte Version;
        public byte Compression;
        public ushort Unknown;
        public uint DecompressedLength;
    }

    private unsafe void TryInitializeServerFrameTiming()
    {
        try
        {
            var matches = SigScanner
                .ScanAllText(GenericZoneDownSignature, CancellationToken.None)
                .Take(ZoneDownMatchIndex + 1)
                .ToArray();
            if (matches.Length <= ZoneDownMatchIndex)
            {
                throw new InvalidOperationException(
                    $"Expected at least {ZoneDownMatchIndex + 1} server receive matches, found {matches.Length}.");
            }

            var address = ResolveCallTarget(matches[ZoneDownMatchIndex]);
            var textStart = SigScanner.Module.BaseAddress + (int)SigScanner.TextSectionOffset;
            var textEnd = textStart + SigScanner.TextSectionSize;
            if (address < textStart || address >= textEnd)
            {
                throw new InvalidOperationException("The server receive hook resolved outside the game text section.");
            }

            serverFrameHook = GameInteropProvider.HookFromAddress<ProcessZoneDownDelegate>(
                address,
                OnProcessZoneDown);
            serverFrameHook.Enable();
            Log.Information("Better Deaths damage meter server-frame timing enabled.");
        }
        catch (Exception ex)
        {
            serverFrameHook?.Dispose();
            serverFrameHook = null;
            Log.Warning(ex, "Better Deaths server-frame timing could not be enabled; the damage meter will use local receipt time.");
        }
    }

    private static IntPtr ResolveCallTarget(IntPtr address)
    {
        return Marshal.ReadByte(address) == 0xE8
            ? address + 5 + Marshal.ReadInt32(address, 1)
            : address;
    }

    private unsafe nuint OnProcessZoneDown(
        byte* data,
        byte* unknown,
        nuint value3,
        nuint value4,
        nuint value5)
    {
        var previous = currentServerFrameTiming;
        var capture = new ServerFrameTimestampCapture();
        currentServerFrameTiming = capture;

        try
        {
            // Raw packet callbacks run inside this call. Holding the queue lock keeps the
            // framework thread from consuming their shared timing token before it is filled.
            lock (rawCombatQueueLock)
            {
                var result = serverFrameHook!.Original(data, unknown, value3, value4, value5);
                if (TryReadServerFrameTimestamp(data, out var serverSeenAtUtc))
                {
                    capture.SeenAtUtc = serverSeenAtUtc;
                }

                return result;
            }
        }
        finally
        {
            currentServerFrameTiming = previous;
        }
    }

    private static unsafe bool TryReadServerFrameTimestamp(byte* data, out DateTime serverSeenAtUtc)
    {
        serverSeenAtUtc = default;
        if (data is null)
        {
            return false;
        }

        var packetOffset = *(uint*)(data + 28);
        if (packetOffset != 0)
        {
            return false;
        }

        var frame = *(ServerFrameHeader**)(data + 16);
        if (frame is null ||
            frame->TotalSize < sizeof(ServerFrameHeader) ||
            frame->TotalSize > 16 * 1024 * 1024 ||
            frame->Protocol != 1 ||
            frame->Count == 0 ||
            frame->Compression > 2)
        {
            return false;
        }

        return ServerFrameTimestampPolicy.TryConvert(
            frame->TimeValue,
            DateTime.UtcNow,
            out serverSeenAtUtc);
    }
}
