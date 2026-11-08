using BetterDeaths.PartyCooldowns;
using System.Buffers.Binary;

namespace BetterDeaths.Tests;

public sealed class TemperaResultReaderTests
{
    private const uint Target = 1;

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(16, 16)]
    [InlineData(17, 0)]
    [InlineData(255, 0)]
    public void NativeBatchCountIsBounded(byte count, int expected) =>
        Assert.Equal(expected, TemperaResultReader.GetEntryCount(new byte[] { count, 0, 0, 0 }));

    [Fact]
    public void MissingOrTruncatedBuffersCannotProduceObservations()
    {
        Assert.Equal(0, TemperaResultReader.GetEntryCount(new byte[3]));
        var packet = Packet(new Result(42, 5000, new Update(4, 0, 0, 0)));
        var slots = Slots();
        var events = Events(1);
        Assert.False(TemperaResultReader.TryRead(packet.AsSpan(0, packet.Length - 1), Target, 4,
            slots, events, out var count, out var changed));
        Assert.Equal(0, count);
        Assert.Equal(0ul, changed);
        Assert.Equal(TemperaCooldownTracker.CoatStatus, slots[4].Id);
    }

    [Theory]
    [InlineData(60, 1)]
    [InlineData(255, 1)]
    [InlineData(4, 5)]
    public void InvalidLaterEntriesRejectTheEntireBatchBeforeChangingTheMirror(byte index, byte effectCount)
    {
        var packet = Packet(new Result(42, 5000, new Update(4, 0, 0, 0)), new Result(43, 5000, new Update(index, 0, 0, 0)));
        packet[TemperaResultReader.HeaderSize + TemperaResultReader.EntrySize + 21] = effectCount;
        var slots = Slots();
        Assert.False(TemperaResultReader.TryRead(packet, Target, 4, slots, Events(2), out var count, out var changed));
        Assert.Equal(0, count);
        Assert.Equal(0ul, changed);
        Assert.Equal(TemperaCooldownTracker.CoatStatus, slots[4].Id);
    }

    [Fact]
    public void InsufficientSlotOrEventStorageIsRejected()
    {
        var packet = Packet(new Result(42, 5000));
        Assert.False(TemperaResultReader.TryRead(packet, Target, 4, new TemperaStatusSlot[59], Events(1), out _, out _));
        Assert.False(TemperaResultReader.TryRead(packet, Target, 4, Slots(), new TemperaEvent[8], out _, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0xE0000000)]
    public void InvalidTargetCannotProduceObservations(uint target) =>
        Assert.False(TemperaResultReader.TryRead(Packet(new Result(42, 5000)), target, 4, Slots(), Events(1), out _, out _));

    [Fact]
    public void ApplicationsRetainOwnShieldIdentityAndDecodeSignedRemainingTime()
    {
        var packet = Packet(new Result(42, 5000, new Update(4, 3686, Target, -10), new Update(59, 3687, 2, 10)));
        var slots = new TemperaStatusSlot[60];
        var events = Events(1);
        Assert.True(TemperaResultReader.TryRead(packet, Target, 0, slots, events, out var count, out var changed));
        Assert.Equal(1, count);
        Assert.Equal(TemperaEventKind.Applied, events[0].Kind);
        Assert.Equal(10, events[0].Remaining);
        Assert.Equal((1ul << 4) | (1ul << 59), changed);
        Assert.Equal(2u, slots[59].Source);
    }

    [Fact]
    public void ExplicitSlotClearCarriesTheExactNonlethalDamageSequence()
    {
        var slots = Slots();
        var events = Events(1);
        Assert.True(TemperaResultReader.TryRead(Packet(new Result(42, 5000, new Update(4, 0, 0, 0))), Target, 4,
            slots, events, out var count, out var changed));
        Assert.Equal(1, count);
        Assert.Equal(1ul << 4, changed);
        Assert.Equal(TemperaEventKind.Removed, events[0].Kind);
        Assert.Equal(3686u, events[0].StatusId);
        Assert.Equal(42u, events[0].Sequence);
        Assert.True(events[0].HasHp);
        Assert.Equal(5000u, events[0].Hp);
        Assert.Equal(6, events[0].Remaining);
        Assert.Equal(0u, slots[4].Id);
        var tracker = Started();
        tracker.Observe(1, events[0]);
        Assert.True(tracker.TryReduce(1, 4.21, out var reduction));
        Assert.Equal(60, reduction.Seconds);
        Assert.False(tracker.TryReduce(1, 5, out _));
    }

    [Theory]
    [InlineData(3687, 1)]
    [InlineData(3686, 2)]
    [InlineData(100, 2)]
    public void NonzeroReplacementNeverBecomesBreakEvidence(ushort status, uint source)
    {
        var slots = Slots();
        var events = Events(1);
        Assert.True(TemperaResultReader.TryRead(Packet(new Result(42, 5000, new Update(4, status, source, 10))), Target, 4,
            slots, events, out var count, out _));
        Assert.Equal(TemperaEventKind.Removed, events[0].Kind);
        Assert.False(events[0].HasHp);
        Assert.Equal(0u, events[0].Sequence);
        var tracker = Started();
        for (var index = 0; index < count; index++)
            tracker.Observe(1, events[index]);
        Assert.False(tracker.TryReduce(1, 4.21, out _));
    }

    [Fact]
    public void BatchConversionThenGrassaBreakUsesItsOwnSequenceAndThirtySecondReduction()
    {
        var packet = Packet(new Result(40, 5000, new Update(4, 3687, Target, 10)), new Result(42, 5000, new Update(4, 0, 0, 0)));
        var events = Events(2);
        Assert.True(TemperaResultReader.TryRead(packet, Target, 4, Slots(), events, out var count, out _));
        Assert.Equal(3, count);
        var tracker = Started();
        for (var index = 0; index < count; index++)
            tracker.Observe(1, events[index]);
        Assert.True(tracker.TryReduce(1, 4.21, out var reduction));
        Assert.Equal(30, reduction.Seconds);
    }

    [Fact]
    public void LaterBatchEntryIsNotLostAndCannotBorrowTheFirstSequence()
    {
        var packet = Packet(new Result(42, 5000), new Result(999, 5000, new Update(4, 0, 0, 0)));
        var events = Events(2);
        Assert.True(TemperaResultReader.TryRead(packet, Target, 4, Slots(), events, out var count, out _));
        Assert.Equal(1, count);
        Assert.Equal(999u, events[0].Sequence);
        var tracker = Started();
        tracker.Observe(1, events[0]);
        Assert.False(tracker.TryReduce(1, 4.21, out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LethalCurrentOrLaterBatchEntryVetoesTheRefund(bool lethalLater)
    {
        var packet = lethalLater
            ? Packet(new Result(42, 5000, new Update(4, 0, 0, 0)), new Result(43, 0))
            : Packet(new Result(42, 0, new Update(4, 0, 0, 0)));
        var events = Events(lethalLater ? 2 : 1);
        Assert.True(TemperaResultReader.TryRead(packet, Target, 4, Slots(), events, out var count, out _));
        var tracker = Started();
        for (var index = 0; index < count; index++)
            tracker.Observe(1, events[index]);
        Assert.False(tracker.TryReduce(1, 4.21, out _));
    }

    [Fact]
    public void ExpiredShieldCannotRefundEvenWhenAClearContainsAMatchingDamageSequence()
    {
        var slots = Slots();
        slots[4] = slots[4] with { Remaining = 0 };
        var events = Events(1);
        Assert.True(TemperaResultReader.TryRead(Packet(new Result(42, 5000, new Update(4, 0, 0, 0))), Target, 10.1,
            slots, events, out var count, out _));
        var tracker = Started();
        tracker.Observe(1, new(TemperaEventKind.Damage, 9.5) { Sequence = 42 });
        Assert.Equal(1, count);
        tracker.Observe(1, events[0]);
        Assert.False(tracker.TryReduce(1, 10.5, out _));
        Assert.False(tracker.IsUncertain(1, 11));
    }

    [Fact]
    public void DuplicateClearDoesNotGenerateASecondRemoval()
    {
        var events = Events(2);
        Assert.True(TemperaResultReader.TryRead(Packet(new Result(42, 5000, new Update(4, 0, 0, 0)), new Result(42, 5000, new Update(4, 0, 0, 0))),
            Target, 4, Slots(), events, out var count, out _));
        Assert.Equal(1, count);
    }

    [Fact]
    public void UnknownNativeSlotCannotInventARemoval()
    {
        var events = Events(1);
        Assert.True(TemperaResultReader.TryRead(Packet(new Result(42, 5000, new Update(4, 0, 0, 0))), Target, 4,
            new TemperaStatusSlot[60], events, out var count, out _));
        Assert.Equal(0, count);
    }

    [Fact]
    public void NaNRemainingDoesNotBecomeEarlyBreakEvidence()
    {
        var slots = Slots();
        slots[4] = slots[4] with { Remaining = float.NaN };
        var events = Events(1);
        Assert.True(TemperaResultReader.TryRead(Packet(new Result(42, 5000, new Update(4, 0, 0, 0))), Target, 4,
            slots, events, out _, out _));
        var tracker = Started();
        tracker.Observe(1, events[0]);
        Assert.False(tracker.TryReduce(1, 4.21, out _));
    }

    [Fact]
    public void NativePostStateMustConfirmEveryUpdatedSlot()
    {
        var expected = Slots();
        var actual = Slots();
        expected[4] = default;
        Assert.False(TemperaResultReader.MatchesAppliedSlots(expected, actual, 1ul << 4));
        actual[4] = new(0, 2, 0);
        Assert.True(TemperaResultReader.MatchesAppliedSlots(expected, actual, 1ul << 4));
        expected[59] = new(3687, Target, 10);
        actual[59] = new(3687, 2, 10);
        Assert.False(TemperaResultReader.MatchesAppliedSlots(expected, actual, (1ul << 4) | (1ul << 59)));
        actual[59] = expected[59] with { Remaining = 9.999f };
        Assert.True(TemperaResultReader.MatchesAppliedSlots(expected, actual, (1ul << 4) | (1ul << 59)));
        Assert.False(TemperaResultReader.MatchesAppliedSlots(expected, actual, 1ul << 60));
        Assert.False(TemperaResultReader.MatchesAppliedSlots(expected.AsSpan(0, 59), actual, 0));
    }

    [Fact]
    public void MaximumBatchFitsTheDeclaredStackBufferBound()
    {
        var results = new Result[16];
        var slots = new TemperaStatusSlot[60];
        for (var index = 0; index < results.Length; index++)
            results[index] = new(42, 0,
                new(0, (ushort)(index % 2 == 0 ? 3686 : 3687), Target, 10),
                new(1, (ushort)(index % 2 == 0 ? 3686 : 3687), Target, 10),
                new(2, (ushort)(index % 2 == 0 ? 3686 : 3687), Target, 10),
                new(3, (ushort)(index % 2 == 0 ? 3686 : 3687), Target, 10));
        var events = Events(16);
        Assert.True(TemperaResultReader.TryRead(Packet(results), Target, 4, slots, events, out var count, out _));
        Assert.Equal(140, count);
        Assert.True(count <= events.Length);
    }

    [Fact]
    public void DecoderUsesNoManagedAllocationsAfterWarmup()
    {
        var packet = Packet(new Result(42, 5000, new Update(4, 0, 0, 0)));
        var slots = Slots();
        var events = Events(1);
        for (var index = 0; index < 100; index++)
        {
            slots[4] = new(3686, Target, 6);
            TemperaResultReader.TryRead(packet, Target, 4, slots, events, out _, out _);
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        var accepted = true;
        for (var index = 0; index < 1000; index++)
        {
            slots[4] = new(3686, Target, 6);
            accepted &= TemperaResultReader.TryRead(packet, Target, 4, slots, events, out _, out _);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(accepted);
        Assert.Equal(0, allocated);
    }

    private static TemperaCooldownTracker Started()
    {
        var tracker = new TemperaCooldownTracker();
        tracker.Cast(1, 0);
        tracker.Observe(1, new(TemperaEventKind.Applied, 0) { StatusId = 3686, Remaining = 10 });
        tracker.Observe(1, new(TemperaEventKind.Damage, 3) { Sequence = 42 });
        return tracker;
    }

    private static TemperaStatusSlot[] Slots()
    {
        var slots = new TemperaStatusSlot[60];
        slots[4] = new(3686, Target, 6);
        return slots;
    }

    private static TemperaEvent[] Events(int entries) => new TemperaEvent[entries * TemperaResultReader.MaximumEventsPerEntry];

    private readonly record struct Update(byte Slot, ushort Id, uint Source, float Remaining);
    private sealed record Result(uint Sequence, uint Hp, params Update[] Updates);

    private static byte[] Packet(params Result[] results)
    {
        var packet = new byte[TemperaResultReader.HeaderSize + results.Length * TemperaResultReader.EntrySize];
        packet[0] = (byte)results.Length;
        for (var index = 0; index < results.Length; index++)
        {
            var result = results[index];
            var entry = packet.AsSpan(TemperaResultReader.HeaderSize + index * TemperaResultReader.EntrySize, TemperaResultReader.EntrySize);
            BinaryPrimitives.WriteUInt32LittleEndian(entry, result.Sequence);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[4..], Target);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], result.Hp);
            entry[21] = (byte)result.Updates.Length;
            for (var effect = 0; effect < result.Updates.Length; effect++)
            {
                var update = result.Updates[effect];
                var bytes = entry.Slice(24 + effect * 16, 16);
                bytes[0] = update.Slot;
                BinaryPrimitives.WriteUInt16LittleEndian(bytes[2..], update.Id);
                BinaryPrimitives.WriteSingleLittleEndian(bytes[8..], update.Remaining);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes[12..], update.Source);
            }
        }
        return packet;
    }
}
