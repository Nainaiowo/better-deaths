using System;
using System.Buffers.Binary;

namespace BetterDeaths.PartyCooldowns;

internal static class TemperaStatusListReader
{
    public const int HalfCount = 30, StatusSize = 12, HalfSize = HalfCount * StatusSize;
    public const int MaximumEvents = TemperaResultReader.SlotCount * 2;

    public static bool TryRead(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second, uint target, double now,
        Span<TemperaStatusSlot> slots, Span<TemperaEvent> destination, out int count, out ulong changedSlots)
    {
        count = 0;
        changedSlots = 0;
        var slotCount = second.IsEmpty ? HalfCount : HalfCount * 2;
        if (first.Length != HalfSize || !second.IsEmpty && second.Length != HalfSize ||
            target is 0 or 0xE0000000 || !double.IsFinite(now) || slots.Length < slotCount || destination.Length < slotCount * 2)
            return false;
        var present = 0;
        for (var index = 0; index < slotCount; index++)
        {
            var update = index < HalfCount ? first.Slice(index * StatusSize, StatusSize)
                : second.Slice((index - HalfCount) * StatusSize, StatusSize);
            var id = BinaryPrimitives.ReadUInt16LittleEndian(update);
            var source = BinaryPrimitives.ReadUInt32LittleEndian(update[8..]);
            if (TemperaCooldownTracker.IsOwnShield(target, source, id))
                present |= id == TemperaCooldownTracker.CoatStatus ? 1 : 2;
        }
        for (var index = 0; index < slotCount; index++)
        {
            var update = index < HalfCount ? first.Slice(index * StatusSize, StatusSize)
                : second.Slice((index - HalfCount) * StatusSize, StatusSize);
            var id = BinaryPrimitives.ReadUInt16LittleEndian(update);
            var remaining = BinaryPrimitives.ReadSingleLittleEndian(update[4..]);
            var source = BinaryPrimitives.ReadUInt32LittleEndian(update[8..]);
            var previous = slots[index];
            if (TemperaCooldownTracker.IsOwnShield(target, previous.Source, previous.Id) &&
                (previous.Id != id || previous.Source != source) &&
                (present & (previous.Id == TemperaCooldownTracker.CoatStatus ? 1 : 2)) == 0)
                destination[count++] = new(TemperaEventKind.Removed, now)
                {
                    StatusId = previous.Id,
                    Remaining = float.IsFinite(previous.Remaining) ? Math.Abs(previous.Remaining) : double.NaN,
                };
            if (TemperaCooldownTracker.IsOwnShield(target, source, id))
                destination[count++] = new(TemperaEventKind.Applied, now)
                {
                    StatusId = id,
                    Remaining = float.IsFinite(remaining) ? Math.Abs(remaining) : double.NaN,
                };
            slots[index] = new(id, source, remaining);
        }
        changedSlots = (1ul << slotCount) - 1;
        return true;
    }
}
