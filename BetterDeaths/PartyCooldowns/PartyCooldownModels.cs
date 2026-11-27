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
internal readonly record struct PartyCooldownNumberLayout(Vector2 Min, Vector2 Max, Vector2 TextOrigin, float FontSize, float Outline);

internal static class PartyCooldownLayout
{
    public const float MinimumIconSize = 16, MaximumIconSize = 1440, MaximumOffset = 300, MaximumGap = 12;
    public const int MinimumFontPercent = 50, DefaultFontPercent = 100, MaximumFontPercent = 200;
    public const int MaximumRows = PartyCooldownRows.MaximumRows;
    public static float IconSize(float value) => float.IsFinite(value) ? Math.Clamp(value, MinimumIconSize, MaximumIconSize) : 24;
    public static float Offset(float value) => float.IsFinite(value) ? Math.Clamp(value, -MaximumOffset, MaximumOffset) : 0;
    public static float Gap(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, MaximumGap) : 3;
    public static int FontPercent(int value) => Math.Clamp(value, MinimumFontPercent, MaximumFontPercent);
    public static float TimerTextSize(float iconSize, float previewScale = 1) => MathF.Max(13 * previewScale, iconSize * .68f);
    public static float ChargeTextSize(float iconSize, float previewScale = 1) => MathF.Max(10 * previewScale, iconSize * .42f);
    public static float TextOutlineWidth(float fontSize, float previewScale = 1) => MathF.Max(previewScale, fontSize / 24);

    public static PartyCooldownNumberLayout FitNumber(float size, float previewScale, (Vector2 Min, Vector2 Max) ink,
        bool isCharge, bool paired, int fontSizePercent = DefaultFontPercent)
    {
        var pad = MathF.Min(size * .05f, MathF.Max(.6f * previewScale, size / 32f));
        // A recharging action can show two numbers. Keep their backgrounds and outlines apart.
        var bandHeight = paired ? (isCharge ? size * .38f : size * .62f - pad) : size;
        var target = isCharge ? ChargeTextSize(size, previewScale) : TimerTextSize(size, previewScale);
        var inkSize = Vector2.Max(ink.Max - ink.Min, new Vector2(.001f));
        var limit = MathF.Min((size - 2 * pad) / inkSize.X, (bandHeight - 2 * pad) / inkSize.Y);
        var baseline = MathF.Min(target, limit);
        var fontSize = MathF.Min(limit, baseline * (FontPercent(fontSizePercent) / 100f));
        var extent = inkSize * fontSize + new Vector2(2 * pad);
        var min = isCharge ? new Vector2(size) - extent : (new Vector2(size, bandHeight) - extent) * .5f;
        return new(min, min + extent, min + new Vector2(pad) - ink.Min * fontSize,
            fontSize, MathF.Min(pad, TextOutlineWidth(fontSize, previewScale)));
    }

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

    public static Vector2 GetSize(int count, float size, float gap) => GetSize([count], size, gap);

    public static Vector2 GetSize(ReadOnlySpan<int> rowCounts, float size, float gap)
    {
        var columns = 0;
        var rows = 0;
        for (var index = 0; index < rowCounts.Length && index < MaximumRows; index++)
            if (rowCounts[index] > 0)
            {
                columns = Math.Max(columns, rowCounts[index]);
                rows = index + 1;
            }
        return rows == 0 ? Vector2.Zero : new(columns * IconSize(size) + (columns - 1) * Gap(gap),
            rows * IconSize(size) + (rows - 1) * Gap(gap));
    }

    public static Vector2 GetIconOffset(int rowIndex, int columnIndex, ReadOnlySpan<int> rowCounts,
        PartyCooldownSide side, float size, float gap)
    {
        if (rowIndex < 0 || rowIndex >= rowCounts.Length || rowIndex >= MaximumRows ||
            columnIndex < 0 || columnIndex >= rowCounts[rowIndex])
            return Vector2.Zero;
        if (side == PartyCooldownSide.Left)
        {
            var columns = 0;
            for (var index = 0; index < rowCounts.Length && index < MaximumRows; index++)
                columns = Math.Max(columns, rowCounts[index]);
            columnIndex += columns - rowCounts[rowIndex];
        }
        var step = IconSize(size) + Gap(gap);
        return new(columnIndex * step, rowIndex * step);
    }

    public static Vector2 GetOrigin(PartyCooldownRow row, int count, PartyCooldownSide side, float size, float gap, Vector2 offset) =>
        GetOrigin(row, [count], side, size, gap, offset);

    public static Vector2 GetOrigin(PartyCooldownRow row, ReadOnlySpan<int> rowCounts, PartyCooldownSide side,
        float size, float gap, Vector2 offset)
    {
        var scale = float.IsFinite(row.Scale) && row.Scale > 0 ? row.Scale : 1;
        var width = GetSize(rowCounts, size, gap).X;
        var x = side == PartyCooldownSide.Left ? row.NameMin.X - (width + 6) * scale : row.NameMax.X + 6 * scale;
        return new Vector2(x, (row.NameMin.Y + row.NameMax.Y - IconSize(size) * scale) * .5f) + offset * scale;
    }
}
