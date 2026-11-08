using System;
using System.Collections.Generic;
using System.Numerics;

namespace BetterDeaths.PartyCooldowns;

public enum PartyCooldownSide
{
    Left,
    Right,
}

internal enum PartyCooldownRole
{
    Tank,
    Healer,
    Melee,
    Ranged,
}

internal sealed record PartyCooldownJob(uint Id, string Name, string Abbreviation, PartyCooldownRole Role);

internal sealed record PartyCooldownAction(
    uint Id, string Name, uint IconId, byte Level, byte Group, float RecastSeconds,
    byte MaxCharges, IReadOnlyList<uint> Jobs, bool DefaultSelected = false,
    bool TargetsOtherPlayers = false, string? UnsupportedReason = null, bool CaptureOnly = false);

internal readonly record struct PartyCooldownProfile(uint ActionId, byte Group, double RecastSeconds, int MaxCharges);

internal readonly record struct PartyCooldownStatus(int Charges, int MaxCharges, double RemainingSeconds, bool IsKnown)
{
    public bool IsReady => IsKnown && Charges > 0;
    public bool IsOnCooldown => IsKnown && Charges == 0 && RemainingSeconds > 0;

    public static PartyCooldownStatus Preview(int maximum, double recastSeconds, int index)
    {
        maximum = Math.Clamp(maximum, 1, 5);
        var remaining = double.IsFinite(recastSeconds) ? Math.Clamp(recastSeconds * .4, 1, 38) : 1;
        return (index % 3) switch
        {
            0 => new(0, maximum, remaining, true),
            1 => new(maximum - 1, maximum, remaining, true),
            _ => new(maximum, maximum, 0, true),
        };
    }

    public static PartyCooldownStatus FromNative(int charges, int maximum, double elapsed, double total)
    {
        maximum = Math.Clamp(maximum, 1, 5);
        charges = Math.Clamp(charges, 0, maximum);
        if (!double.IsFinite(elapsed) || !double.IsFinite(total) || total <= 0)
            return new(charges, maximum, 0, true);
        var remaining = charges == maximum ? 0 : Math.Clamp(total - elapsed - (maximum - 1 - charges) * total / maximum, 0, total / maximum);
        return new(charges, maximum, remaining, true);
    }
}

internal readonly record struct PartyCooldownObservation(uint EntityId, uint ActionId, uint Sequence, double SeenAt)
{
    public ulong MemberKey { get; init; }
    public uint TerritoryId { get; init; }
    public TemperaEvent? Tempera { get; init; }
}

internal readonly record struct PartyCooldownRow(uint EntityId, int DisplayRow, Vector2 NameMin, Vector2 NameMax, float Scale);
internal readonly record struct PartyCooldownHudMember(uint EntityId, int Index);

internal static class PartyCooldownLayout
{
    public const float MinimumIconSize = 16, MaximumIconSize = 72;
    public static float IconSize(float value) => float.IsFinite(value) ? Math.Clamp(value, MinimumIconSize, MaximumIconSize) : 24;
    public static float Offset(float value) => float.IsFinite(value) ? Math.Clamp(value, -300, 300) : 0;
    public static float Gap(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 12) : 3;

    public static int FindUniqueHudMember(ReadOnlySpan<PartyCooldownHudMember> members, int row)
    {
        if (row is < 0 or > 7)
            return -1;
        var result = -1;
        for (var index = 0; index < members.Length; index++)
            if (members[index].EntityId is > 0 and not 0xE0000000 && members[index].Index == row)
            {
                if (result >= 0)
                    return -1;
                result = index;
            }
        if (result >= 0)
            for (var index = 0; index < members.Length; index++)
                if (index != result && members[index].EntityId == members[result].EntityId)
                    return -1;
        return result;
    }

    public static Vector2 GetOrigin(PartyCooldownRow row, int count, PartyCooldownSide side, float size, float gap, Vector2 offset)
    {
        var scale = float.IsFinite(row.Scale) && row.Scale > 0 ? row.Scale : 1;
        var width = Math.Max(0, count) * IconSize(size) + Math.Max(0, count - 1) * Gap(gap);
        var x = side == PartyCooldownSide.Left ? row.NameMin.X - (width + 6) * scale : row.NameMax.X + 6 * scale;
        return new Vector2(x, (row.NameMin.Y + row.NameMax.Y - IconSize(size) * scale) * .5f) + offset * scale;
    }
}
