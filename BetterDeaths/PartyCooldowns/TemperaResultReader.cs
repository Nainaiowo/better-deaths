using System;
using System.Buffers.Binary;
using System.Numerics;

namespace BetterDeaths.PartyCooldowns;

internal readonly record struct TemperaStatusSlot(uint Id, uint Source, float Remaining);

internal static class TemperaResultReader
{
    public const int HeaderSize = 4, EntrySize = 0x58, MaximumEntries = 16, SlotCount = 60;
    public const int MaximumEventsPerEntry = 9;

    public static int GetEntryCount(ReadOnlySpan<byte> header) => header.Length >= HeaderSize && header[0] is > 0 and <= MaximumEntries
        ? header[0] : 0;

    public static bool TryGetUpdatedSlots(ReadOnlySpan<byte> packet, out ulong changedSlots)
    {
        changedSlots = 0;
        var entries = GetEntryCount(packet);
        if (entries == 0 || packet.Length < HeaderSize + entries * EntrySize)
            return false;
        ulong updatedSlots = 0;
        for (var entry = 0; entry < entries; entry++)
        {
            var result = packet.Slice(HeaderSize + entry * EntrySize, EntrySize);
            if (result[21] > 4)
                return false;
            for (var effect = 0; effect < result[21]; effect++)
            {
                var index = result[24 + effect * 16];
                if (index >= SlotCount)
                    return false;
                updatedSlots |= 1ul << index;
            }
        }
        changedSlots = updatedSlots;
        return true;
    }

    public static bool TryRead(ReadOnlySpan<byte> packet, uint target, double now, Span<TemperaStatusSlot> slots,
        Span<TemperaEvent> destination, out int count, out ulong changedSlots)
    {
        count = 0;
        changedSlots = 0;
        var entries = GetEntryCount(packet);
        if (entries == 0 || packet.Length < HeaderSize + entries * EntrySize || slots.Length < SlotCount ||
            destination.Length < entries * MaximumEventsPerEntry || target is 0 or 0xE0000000 || !double.IsFinite(now))
            return false;
        // Validate the complete batch before changing the local slot mirror.
        if (!TryGetUpdatedSlots(packet, out var updatedSlots))
            return false;
        changedSlots = updatedSlots;
        for (var entry = 0; entry < entries; entry++)
        {
            var result = packet.Slice(HeaderSize + entry * EntrySize, EntrySize);
            var sequence = BinaryPrimitives.ReadUInt32LittleEndian(result);
            var hp = BinaryPrimitives.ReadUInt32LittleEndian(result[8..]);
            if (hp == 0)
                destination[count++] = new(TemperaEventKind.Landing, now) { Sequence = sequence, Hp = 0, HasHp = true };
            for (var effect = 0; effect < result[21]; effect++)
            {
                var update = result.Slice(24 + effect * 16, 16);
                var index = update[0];
                var id = BinaryPrimitives.ReadUInt16LittleEndian(update[2..]);
                var remaining = BinaryPrimitives.ReadSingleLittleEndian(update[8..]);
                var source = BinaryPrimitives.ReadUInt32LittleEndian(update[12..]);
                var previous = slots[index];
                if (TemperaCooldownTracker.IsOwnShield(target, previous.Source, previous.Id) &&
                    (previous.Id != id || previous.Source != source))
                {
                    // A nonzero replacement is an overwrite, never proof of shield consumption.
                    destination[count++] = new(TemperaEventKind.Removed, now)
                    {
                        StatusId = previous.Id,
                        Remaining = float.IsFinite(previous.Remaining) ? Math.Abs(previous.Remaining) : double.NaN,
                        Sequence = id == 0 ? sequence : 0,
                        Hp = hp,
                        HasHp = id == 0,
                    };
                }
                if (TemperaCooldownTracker.IsOwnShield(target, source, id))
                    destination[count++] = new(TemperaEventKind.Applied, now)
                    {
                        StatusId = id,
                        Remaining = float.IsFinite(remaining) ? Math.Abs(remaining) : double.NaN,
                    };
                slots[index] = new(id, source, remaining);
            }
        }
        return true;
    }

    public static bool MatchesAppliedSlots(ReadOnlySpan<TemperaStatusSlot> expected,
        ReadOnlySpan<TemperaStatusSlot> actual, ulong changedSlots)
    {
        if (expected.Length < SlotCount || actual.Length < SlotCount || changedSlots >> SlotCount != 0)
            return false;
        while (changedSlots != 0)
        {
            var index = BitOperations.TrailingZeroCount(changedSlots);
            if (expected[index].Id != actual[index].Id ||
                expected[index].Id != 0 && expected[index].Source != actual[index].Source)
                return false;
            changedSlots &= changedSlots - 1;
        }
        return true;
    }
}
