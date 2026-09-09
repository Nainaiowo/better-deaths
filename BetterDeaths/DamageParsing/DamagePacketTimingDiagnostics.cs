namespace BetterDeaths.DamageParsing;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading;

internal sealed class DamagePacketTimingDiagnostics
{
    public const int ExamplesPerReason = 3;
    private static readonly int ReasonCount = Enum.GetValues<DamagePacketTimingRejection>().Length;
    private readonly object gate = new();
    private readonly long[] counts = new long[ReasonCount];
    private readonly int[] exampleCounts = new int[ReasonCount];
    private readonly Example?[,] examples = new Example?[ReasonCount, ExamplesPerReason];

    private sealed record Example(DateTime ReceivedAtUtc, bool HasState, bool HasIpcBuffer,
        ulong IpcLength, uint MessageSource, uint MessageDestination, byte[] Frame, byte[] Element);

    public void Record(DamagePacketTimingRejection reason, bool captureExample,
        ReadOnlySpan<byte> frame, ReadOnlySpan<byte> element, ulong ipcLength,
        uint source, uint destination, DateTime receivedAtUtc, bool hasState, bool hasIpcBuffer)
    {
        int index = (int)reason;
        Interlocked.Increment(ref counts[index]);
        if (!captureExample || Volatile.Read(ref exampleCounts[index]) >= ExamplesPerReason)
            return;

        lock (gate)
        {
            int count = exampleCounts[index];
            if (count >= ExamplesPerReason)
                return;
            // Only bounded header copies are made here. No IPC body, native pointer,
            // formatting, or file I/O is retained on the packet thread.
            examples[index, count] = new(receivedAtUtc, hasState, hasIpcBuffer, ipcLength, source, destination,
                frame[..Math.Min(frame.Length, 40)].ToArray(), element[..Math.Min(element.Length, 16)].ToArray());
            Volatile.Write(ref exampleCounts[index], count + 1);
        }
    }

    // Called by the existing ten-second framework health report, never the packet hook.
    // Retain examples in that report so debug-file trimming cannot strand the reason counts.
    public DamagePacketTimingReasonSummary[] Snapshot()
    {
        var summaries = new List<DamagePacketTimingReasonSummary>();
        lock (gate)
        {
            for (int index = 0; index < ReasonCount; index++)
            {
                long count = Interlocked.Read(ref counts[index]);
                if (count == 0)
                    continue;
                var saved = new DamagePacketTimingExample[exampleCounts[index]];
                for (int i = 0; i < saved.Length; i++)
                {
                    var example = examples[index, i]!;
                    ReadOnlySpan<byte> frame = example.Frame;
                    ReadOnlySpan<byte> element = example.Element;
                    saved[i] = new(example.ReceivedAtUtc, example.HasState, example.HasIpcBuffer,
                        example.IpcLength, example.MessageSource, example.MessageDestination,
                        Convert.ToHexString(frame), Convert.ToHexString(element),
                        frame.Length < 40 ? null : new(
                            BinaryPrimitives.ReadUInt64LittleEndian(frame[16..]),
                            BinaryPrimitives.ReadUInt32LittleEndian(frame[24..]),
                            BinaryPrimitives.ReadUInt16LittleEndian(frame[28..]),
                            BinaryPrimitives.ReadUInt16LittleEndian(frame[30..]), frame[32], frame[33],
                            BinaryPrimitives.ReadUInt32LittleEndian(frame[36..])),
                        element.Length < 16 ? null : new(
                            BinaryPrimitives.ReadUInt32LittleEndian(element),
                            BinaryPrimitives.ReadUInt32LittleEndian(element[4..]),
                            BinaryPrimitives.ReadUInt32LittleEndian(element[8..]),
                            BinaryPrimitives.ReadUInt16LittleEndian(element[12..])));
                }
                summaries.Add(new(((DamagePacketTimingRejection)index).ToString(), count, saved));
            }
        }
        return [.. summaries];
    }
}

internal sealed record DamagePacketTimingReasonSummary(string Reason, long Count, DamagePacketTimingExample[] Examples);

internal sealed record DamagePacketTimingExample(DateTime ReceivedAtUtc, bool HasState, bool HasIpcBuffer,
    ulong IpcLength, uint MessageSource, uint MessageDestination, string FrameHeaderHex, string ElementHeaderHex,
    DamagePacketTimingFrameFields? Frame, DamagePacketTimingElementFields? Element);

internal sealed record DamagePacketTimingFrameFields(ulong UnixMilliseconds, uint TotalSize, ushort Protocol,
    ushort PacketCount, byte Version, byte Compression, uint DecompressedLength);

internal sealed record DamagePacketTimingElementFields(uint Size, uint Source, uint Destination, ushort Type);
