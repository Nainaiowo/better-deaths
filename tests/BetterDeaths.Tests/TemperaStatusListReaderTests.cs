using BetterDeaths.PartyCooldowns;
using System.Buffers.Binary;
using System.Text.Json;

namespace BetterDeaths.Tests;

public sealed class TemperaStatusListReaderTests
{
    private const uint Target = 0x10000001;

    [Theory]
    [InlineData(0, false)]
    [InlineData(29, false)]
    [InlineData(30, true)]
    [InlineData(59, true)]
    public void BothNativeListLayoutsCaptureOwnShieldsAtTheirActualSlots(int index, bool doubled)
    {
        var first = new byte[360];
        var second = doubled ? new byte[360] : [];
        WriteStatus(index < 30 ? first : second, index % 30, 3686, Target, -10);
        var expected = new TemperaStatusSlot[60];
        var events = new TemperaEvent[120];
        Assert.True(TemperaStatusListReader.TryRead(first, second, Target, 1, expected, events, out var count, out var changed));
        Assert.Equal(1, count);
        Assert.Equal(TemperaEventKind.Applied, events[0].Kind);
        Assert.Equal(10, events[0].Remaining);
        Assert.Equal(3686u, expected[index].Id);
        Assert.Equal((1ul << (doubled ? 60 : 30)) - 1, changed);
        var native = expected.ToArray();
        Assert.True(TemperaResultReader.MatchesAppliedSlots(expected, native, changed));
        native[index] = default;
        Assert.False(TemperaResultReader.MatchesAppliedSlots(expected, native, changed));
    }

    [Fact]
    public void ThirtySlotUpdatesDoNotEraseTheOtherHalfOfTheNativeList()
    {
        var slots = new TemperaStatusSlot[60];
        slots[30] = new(3687, Target, 6);
        var events = new TemperaEvent[120];
        Assert.True(TemperaStatusListReader.TryRead(new byte[360], [], Target, 4, slots, events, out var count, out var changed));
        Assert.Equal(0, count);
        Assert.Equal(3687u, slots[30].Id);
        Assert.Equal((1ul << 30) - 1, changed);
    }

    [Fact]
    public void OtherCastersAndGrassaRecipientsCannotProduceOwnerEvents()
    {
        var first = new byte[360];
        WriteStatus(first, 2, 3687, 2, 10);
        var events = new TemperaEvent[120];
        Assert.True(TemperaStatusListReader.TryRead(first, [], Target, 4, new TemperaStatusSlot[60], events, out var count, out _));
        Assert.Equal(0, count);
    }

    [Fact]
    public void MovingTheSameShieldToAnotherNativeSlotDoesNotReportARemoval()
    {
        var slots = new TemperaStatusSlot[60];
        slots[30] = new(3686, Target, 6);
        var second = new byte[360];
        WriteStatus(second, 1, 3686, Target, 6);
        var events = new TemperaEvent[120];
        Assert.True(TemperaStatusListReader.TryRead(new byte[360], second, Target, 4, slots, events, out var count, out _));
        Assert.Equal(1, count);
        Assert.Equal(TemperaEventKind.Applied, events[0].Kind);
        Assert.Equal(0u, slots[30].Id);
        Assert.Equal(3686u, slots[31].Id);
    }

    [Fact]
    public void StatusListRemovalNeverClaimsDamageCausality()
    {
        var slots = new TemperaStatusSlot[60];
        slots[30] = new(3686, Target, 6);
        var events = new TemperaEvent[120];
        Assert.True(TemperaStatusListReader.TryRead(new byte[360], new byte[360], Target, 4,
            slots, events, out var count, out _));
        Assert.Equal(1, count);
        Assert.Equal(TemperaEventKind.Removed, events[0].Kind);
        Assert.False(events[0].HasHp);
        Assert.Equal(0u, events[0].Sequence);
        var tracker = new TemperaCooldownTracker();
        tracker.Cast(1, 0);
        tracker.Observe(1, new(TemperaEventKind.Damage, 3) { Sequence = 42 });
        tracker.Observe(1, events[0]);
        Assert.False(tracker.TryReduce(1, 4.21, out _));
    }

    [Theory]
    [InlineData(359, 0, 60, 120)]
    [InlineData(361, 0, 60, 120)]
    [InlineData(360, 359, 60, 120)]
    [InlineData(360, 361, 60, 120)]
    [InlineData(360, 360, 59, 120)]
    [InlineData(360, 360, 60, 119)]
    public void InvalidBuffersCannotPartiallyChangeTheSlotMirror(int first, int second, int slots, int capacity)
    {
        var mirror = new TemperaStatusSlot[slots];
        mirror[0] = new(3686, Target, 6);
        Assert.False(TemperaStatusListReader.TryRead(new byte[first], new byte[second], Target, 4,
            mirror, new TemperaEvent[capacity], out var count, out var changed));
        Assert.Equal(0, count);
        Assert.Equal(0ul, changed);
        Assert.Equal(3686u, mirror[0].Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0xE0000000)]
    public void InvalidTargetsAreRejected(uint target) =>
        Assert.False(TemperaStatusListReader.TryRead(new byte[360], [], target, 4,
            new TemperaStatusSlot[60], new TemperaEvent[120], out _, out _));

    [Fact]
    public void DecoderDoesNotAllocateManagedMemoryAfterWarmup()
    {
        var first = new byte[360];
        var second = new byte[360];
        WriteStatus(second, 0, 3686, Target, 10);
        var slots = new TemperaStatusSlot[60];
        var events = new TemperaEvent[120];
        for (var index = 0; index < 100; index++)
            TemperaStatusListReader.TryRead(first, second, Target, 4, slots, events, out _, out _);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var accepted = true;
        for (var index = 0; index < 1000; index++)
            accepted &= TemperaStatusListReader.TryRead(first, second, Target, 4, slots, events, out _, out _);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(accepted);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void CapturedUseStatusListDamageAndClearSequencesReachTheActualTimerDisplay()
    {
        var captures = JsonSerializer.Deserialize<Capture[]>(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "TemperaStatusUpdatesCaptured.json")))!;
        Assert.Equal(2, captures.Length);
        foreach (var capture in captures)
        {
            var tracker = new TemperaCooldownTracker();
            var cooldowns = new PartyCooldownTracker();
            var profile = new PartyCooldownProfile(34685, 22, 120, 1);
            var action = new PartyCooldownAction(34685, "Tempera Coat", 1, 10, 22, 120, 1, [42]);
            var display = new PartyCooldownDisplayRow([action]);
            var displayed = new PartyCooldownDisplayEntry[1];
            var slots = new TemperaStatusSlot[60];
            var events = new TemperaEvent[120];
            var previousAt = 0d;
            foreach (var step in capture.Steps)
            {
                for (var index = 0; index < slots.Length; index++)
                    slots[index] = slots[index] with { Remaining = Math.Max(0, Math.Abs(slots[index].Remaining) - (float)(step.At - previousAt)) };
                previousAt = step.At;
                var count = 0;
                switch (step.Kind)
                {
                    case "Use" when step.ActionId == 34685:
                        Assert.True(cooldowns.Observe(1, profile, step.At, 1));
                        tracker.Cast(1, step.At);
                        var status = cooldowns.Get(1, profile, step.At);
                        Assert.False(tracker.IsUncertain(1, step.At));
                        display.Update(0, status);
                        display.CopyTo(displayed);
                        Assert.True(displayed[0].Status.IsOnCooldown);
                        Assert.Equal("120", displayed[0].TimerLabel);
                        break;
                    case "Use":
                        Assert.Equal(34686u, step.ActionId);
                        tracker.Observe(1, new(TemperaEventKind.Conversion, step.At));
                        break;
                    case "StatusList":
                        var halves = new byte[step.SlotCount * 12];
                        foreach (var update in step.Updates)
                        {
                            var packed = Hex(update.Packed);
                            WriteStatus(halves, update.Slot, (ushort)packed, Hex(update.Source),
                                BitConverter.UInt32BitsToSingle(Hex(update.Duration)), (ushort)(packed >> 16));
                        }
                        Assert.True(TemperaStatusListReader.TryRead(halves.AsSpan(0, 360), halves.AsSpan(360),
                            Target, step.At, slots, events, out count, out _));
                        break;
                    case "Damage":
                        var word0 = Hex(step.DamageWord0);
                        var word1 = Hex(step.DamageWord1);
                        Assert.True(TemperaCooldownTracker.IsTargetDamage((byte)word0, (byte)(word1 >> 8)));
                        tracker.Observe(1, new(TemperaEventKind.Damage, step.At) { Sequence = Hex(step.Sequence) });
                        break;
                    case "Result":
                        var packet = Result(step);
                        Assert.True(TemperaResultReader.TryRead(packet, Target, step.At, slots, events, out count, out _));
                        break;
                    default:
                        Assert.Fail($"Unknown captured step: {step.Kind}");
                        break;
                }
                for (var index = 0; index < count; index++)
                    tracker.Observe(1, events[index]);
                Assert.False(tracker.TryReduce(1, step.At, out _));
            }
            var now = capture.Steps[^1].At + .21;
            Assert.True(tracker.TryReduce(1, now, out var reduction), capture.Name);
            Assert.Equal(capture.ExpectedReduction, reduction.Seconds);
            Assert.True(cooldowns.Reduce(1, profile, reduction, now));
            Assert.False(tracker.TryReduce(1, now + 1, out _));
            Assert.False(tracker.IsUncertain(1, now));
            var final = cooldowns.Get(1, profile, now);
            display.Update(0, final);
            display.CopyTo(displayed);
            Assert.Equal(120 - capture.ExpectedReduction - now, displayed[0].Status.RemainingSeconds, 4);
            Assert.Equal(PartyCooldownLabels.Timer(final), displayed[0].TimerLabel);
        }
    }

    private static uint Hex(string value) => Convert.ToUInt32(value, 16);

    private static void WriteStatus(Span<byte> bytes, int index, ushort id, uint source, float duration, ushort extra = 0)
    {
        var status = bytes.Slice(index * 12, 12);
        BinaryPrimitives.WriteUInt16LittleEndian(status, id);
        BinaryPrimitives.WriteUInt16LittleEndian(status[2..], extra);
        BinaryPrimitives.WriteSingleLittleEndian(status[4..], duration);
        BinaryPrimitives.WriteUInt32LittleEndian(status[8..], source);
    }

    private static byte[] Result(Step step)
    {
        var bytes = new byte[TemperaResultReader.HeaderSize + TemperaResultReader.EntrySize];
        bytes[0] = 1;
        var record = bytes.AsSpan(TemperaResultReader.HeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(record, Hex(step.Sequence));
        BinaryPrimitives.WriteUInt32LittleEndian(record[4..], Target);
        BinaryPrimitives.WriteUInt32LittleEndian(record[8..], step.Hp);
        record[21] = (byte)step.Updates.Length;
        for (var index = 0; index < step.Updates.Length; index++)
        {
            var update = step.Updates[index];
            var packed = Hex(update.Packed);
            var status = record.Slice(24 + index * 16, 16);
            status[0] = (byte)(packed >> 24);
            BinaryPrimitives.WriteUInt16LittleEndian(status[2..], (ushort)packed);
            BinaryPrimitives.WriteUInt32LittleEndian(status[8..], Hex(update.Duration));
            BinaryPrimitives.WriteUInt32LittleEndian(status[12..], Hex(update.Source));
        }
        return bytes;
    }

    private sealed record Capture(string Name, string SourceWindow, double ExpectedReduction, Step[] Steps);
    private sealed record Step(string Kind, double At, uint ActionId, int SlotCount, Update[] Updates,
        string Sequence, uint Hp, string DamageWord0, string DamageWord1);
    private sealed record Update(int Slot, string Packed, string Duration, string Source);
}
