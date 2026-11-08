using System;
using System.Globalization;
using System.Numerics;

namespace BetterDeaths.PartyCooldowns;

internal readonly record struct PartyCooldownDisplayEntry(PartyCooldownAction Action, PartyCooldownStatus Status,
    string TimerLabel, string ChargeLabel);

internal sealed class PartyCooldownDisplayRow
{
    private readonly object sync = new();
    private readonly PartyCooldownDisplayEntry[] entries;

    public PartyCooldownDisplayRow(PartyCooldownAction[] actions)
    {
        entries = new PartyCooldownDisplayEntry[actions.Length];
        for (var index = 0; index < actions.Length; index++)
            entries[index] = new(actions[index], default, "?", string.Empty);
    }

    public void Update(int index, PartyCooldownStatus status)
    {
        lock (sync)
        {
            var entry = entries[index];
            entries[index] = entry with
            {
                Status = status,
                TimerLabel = PartyCooldownLabels.Timer(status),
                ChargeLabel = PartyCooldownLabels.Charges(status),
            };
        }
    }

    public int CopyTo(Span<PartyCooldownDisplayEntry> destination)
    {
        lock (sync)
        {
            var count = Math.Min(entries.Length, destination.Length);
            entries.AsSpan(0, count).CopyTo(destination);
            return count;
        }
    }
}

internal static class PartyCooldownLabels
{
    private static readonly string[] Numbers = BuildNumbers();

    private static string[] BuildNumbers()
    {
        var result = new string[601];
        for (var index = 0; index < result.Length; index++)
            result[index] = index.ToString(CultureInfo.InvariantCulture);
        return result;
    }

    public static string Timer(PartyCooldownStatus status) => !status.IsKnown ? "?" :
        status.RemainingSeconds > 0 && status.Charges < status.MaxCharges ? Number(Math.Ceiling(status.RemainingSeconds)) : string.Empty;

    public static string Charges(PartyCooldownStatus status) => status.IsKnown && status.MaxCharges > 1
        ? Number(status.Charges) : string.Empty;

    private static string Number(double value) => double.IsFinite(value) && value >= 0 && value < Numbers.Length
        ? Numbers[(int)value] : value.ToString("0", CultureInfo.InvariantCulture);
}

internal readonly record struct PartyCooldownRosterMember(ulong Key, uint EntityId, uint Job, byte Level);

internal readonly record struct PartyCooldownNameKey(nint Node, ushort Width, ushort Height, byte FontSize,
    byte Font, byte CharacterSpacing, byte LineSpacing, ushort Flags, ushort FontCache);

internal sealed class PartyCooldownNameMeasurement
{
    private PartyCooldownNameKey key;
    private byte[] text = new byte[128];
    private int length = -1;
    private Vector2 measured;

    public bool TryGet(PartyCooldownNameKey candidate, ReadOnlySpan<byte> value, out Vector2 size)
    {
        size = measured;
        return candidate == key && value.Length == length && value.SequenceEqual(text.AsSpan(0, Math.Max(0, length)));
    }

    public void Store(PartyCooldownNameKey candidate, ReadOnlySpan<byte> value, Vector2 size)
    {
        if (text.Length < value.Length)
            text = new byte[value.Length];
        value.CopyTo(text);
        length = value.Length;
        key = candidate;
        measured = size;
    }
}
