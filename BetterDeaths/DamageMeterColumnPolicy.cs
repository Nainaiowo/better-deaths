namespace BetterDeaths;

using System;
using System.Collections.Generic;

public enum DamageMeterColumn
{
    Rank,
    JobIcon,
    PlayerName,
    DamagePercent,
    DamagePerSecond,
    RaidDamagePerSecond,
    CriticalHitPercent,
    DirectHitPercent,
    CriticalDirectHitPercent,
    MaxHitAmount,
    MaxHitName,
    TotalDamage,
    Deaths,
    HitCount,
    NeutralDamagePerSecond,
    AdjustedDamagePerSecond,
}

internal static class DamageMeterColumnPolicy
{
    private static readonly DamageMeterColumn[] DefaultColumns =
    [
        DamageMeterColumn.Rank,
        DamageMeterColumn.JobIcon,
        DamageMeterColumn.PlayerName,
        DamageMeterColumn.TotalDamage,
        DamageMeterColumn.DamagePerSecond,
        DamageMeterColumn.RaidDamagePerSecond,
        DamageMeterColumn.DamagePercent,
    ];

    public static List<DamageMeterColumn> CreateDefault()
    {
        return [.. DefaultColumns];
    }

    public static List<DamageMeterColumn> Normalize(IEnumerable<DamageMeterColumn>? columns)
    {
        if (columns is null)
        {
            return CreateDefault();
        }

        var normalized = new List<DamageMeterColumn>();
        var seen = new HashSet<DamageMeterColumn>();
        foreach (var column in columns)
        {
            if (Enum.IsDefined(column) && seen.Add(column))
            {
                normalized.Add(column);
            }
        }

        return normalized.Count > 0 ? normalized : CreateDefault();
    }

    public static bool Move(IList<DamageMeterColumn> columns, DamageMeterColumn source, DamageMeterColumn target)
    {
        var sourceIndex = columns.IndexOf(source);
        var targetIndex = columns.IndexOf(target);
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
        {
            return false;
        }

        columns.RemoveAt(sourceIndex);
        columns.Insert(Math.Min(targetIndex, columns.Count), source);
        return true;
    }

    public static bool PlaceBefore(IList<DamageMeterColumn> columns, DamageMeterColumn source, DamageMeterColumn target)
    {
        var targetIndex = columns.IndexOf(target);
        if (targetIndex < 0)
        {
            return false;
        }

        var sourceIndex = columns.IndexOf(source);
        if (sourceIndex >= 0)
        {
            return Move(columns, source, target);
        }

        columns.Insert(targetIndex, source);
        return true;
    }
}
