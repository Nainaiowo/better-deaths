namespace BetterDeaths;

using System;
using System.Collections.Generic;

public enum DamageMeterColumn
{
    JobIcon = 1,
    PlayerName = 2,
    DamagePercent = 3,
    DamagePerSecond = 4,
    RaidDamagePerSecond = 5,
    CriticalHitPercent = 6,
    DirectHitPercent = 7,
    CriticalDirectHitPercent = 8,
    MaxHitAmount = 9,
    MaxHitName = 10,
    TotalDamage = 11,
    Deaths = 12,
    HitCount = 13,
    NeutralDamagePerSecond = 14,
    AdjustedDamagePerSecond = 15,
    EncounterDamagePerSecond = 16,
}

internal static class DamageMeterColumnPolicy
{
    private static readonly DamageMeterColumn[] DefaultColumns =
    [
        DamageMeterColumn.PlayerName,
        DamageMeterColumn.TotalDamage,
        DamageMeterColumn.DamagePerSecond,
        DamageMeterColumn.DamagePercent,
    ];

    public static List<DamageMeterColumn> CreateDefault()
    {
        return [.. DefaultColumns];
    }

    public static bool IsEnabled(DamageMeterColumn column)
    {
        return Enum.IsDefined(column) && column is not (
            DamageMeterColumn.JobIcon or DamageMeterColumn.EncounterDamagePerSecond or
            DamageMeterColumn.RaidDamagePerSecond or DamageMeterColumn.NeutralDamagePerSecond or
            DamageMeterColumn.AdjustedDamagePerSecond);
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
            var canonical = column switch
            {
                DamageMeterColumn.JobIcon => DamageMeterColumn.PlayerName,
                DamageMeterColumn.EncounterDamagePerSecond => DamageMeterColumn.DamagePerSecond,
                _ => column,
            };
            if (IsEnabled(canonical) && seen.Add(canonical))
            {
                normalized.Add(canonical);
            }
        }

        return normalized.Count > 0 ? normalized : CreateDefault();
    }

    public static bool Move(IList<DamageMeterColumn> columns, DamageMeterColumn source, DamageMeterColumn target)
    {
        if (!IsEnabled(source) || !IsEnabled(target))
        {
            return false;
        }

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

    public static bool PlaceBefore(IList<DamageMeterColumn> columns, DamageMeterColumn source, DamageMeterColumn? target) =>
        Place(columns, source, target, false);

    public static bool PlaceAfter(IList<DamageMeterColumn> columns, DamageMeterColumn source, DamageMeterColumn target) =>
        Place(columns, source, target, true);

    private static bool Place(IList<DamageMeterColumn> columns, DamageMeterColumn source, DamageMeterColumn? target, bool after) =>
        IsEnabled(source) && (target is null || IsEnabled(target.Value)) && TileOrderPolicy.Place(columns, source, target, after);
}
