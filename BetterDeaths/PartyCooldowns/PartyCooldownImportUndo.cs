using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterDeaths.PartyCooldowns;

// A local checkpoint, never part of a share code. Inherited defaults and explicit empty selections are distinct.
public sealed class PartyCooldownImportUndo
{
    public sealed record JobState(uint JobId, uint[] Actions, bool InheritsActions, uint[][]? Rows, bool RowsEnabled);
    public sealed record LayoutState(PartyCooldownSide Side, PartyCooldownPlacements? Placements, PartyCooldownPlacement Legacy);

    public JobState[] Jobs { get; init; } = [];
    public LayoutState? Layout { get; init; }
    internal uint? SingleJobId => Jobs is [{ } job] ? job.JobId : null;

    internal static PartyCooldownImportUndo Capture(Configuration current, PartyCooldownCatalog catalog, uint? targetJobId, bool includeLayout) => new()
    {
        Jobs = PartyCooldownCatalog.Jobs.Where(job => targetJobId is null || job.Id == targetJobId).Select(job =>
        {
            var rows = current.PartyCooldownRowLayouts.GetValueOrDefault(job.Id);
            var actions = current.PartyCooldownActions.GetValueOrDefault(job.Id);
            var savedRows = rows is null ? null : rows.Rows?.Select(row => row?.ToArray()!).ToArray() ?? [];
            return new JobState(job.Id, (actions ?? catalog.DefaultSelection(job.Id)).ToArray(),
                actions is null, savedRows, rows?.Enabled ?? false);
        }).ToArray(),
        Layout = includeLayout ? new(current.PartyCooldownSide, CopyPlacements(current.PartyCooldownPlacements),
            current.GetLegacyCooldownPlacement()) : null,
    };

    internal bool CanRestoreExactly(PartyCooldownCatalog catalog)
    {
        if (Jobs is not { Length: > 0 } || Jobs.Length > PartyCooldownCatalog.Jobs.Count ||
            Layout is { } layout && (!Enum.IsDefined(layout.Side) || !ValidPlacement(layout.Legacy) ||
                layout.Placements is not { } sides || !ValidPlacement(sides.Left) || !ValidPlacement(sides.Right)))
            return false;

        var seen = new HashSet<uint>();
        foreach (var job in Jobs)
        {
            if (job is null || !seen.Add(job.JobId) || !PartyCooldownCatalog.Jobs.Any(entry => entry.Id == job.JobId))
                return false;
            var actions = job.Actions;
            if (actions is null || actions.Length > PartyCooldownRows.MaximumAbilities ||
                !catalog.NormalizeSelection(job.JobId, actions).SequenceEqual(actions) ||
                job.InheritsActions && !catalog.DefaultSelection(job.JobId).SequenceEqual(actions))
                return false;
            if (job.Rows is { } rows)
            {
                if (rows.Length is 0 or > PartyCooldownRows.MaximumRows ||
                    rows.Any(row => row is null || row.Length > PartyCooldownRows.MaximumAbilities))
                    return false;
                var ordered = rows.SelectMany(row => row).ToArray();
                if (ordered.Length != actions.Length || !ordered.ToHashSet().SetEquals(actions))
                    return false;
            }
            else if (job.RowsEnabled)
                return false;
        }
        return true;
    }

    internal void RestoreTo(Configuration candidate)
    {
        foreach (var job in Jobs)
        {
            if (job.Rows is { } rows)
                candidate.PartyCooldownRowLayouts[job.JobId] = new() { Enabled = job.RowsEnabled, Rows = rows.Select(row => row.ToList()).ToList() };
            else
                candidate.PartyCooldownRowLayouts.Remove(job.JobId);
            if (job.InheritsActions)
                candidate.PartyCooldownActions.Remove(job.JobId);
            else
                candidate.PartyCooldownActions[job.JobId] = [.. job.Actions];
        }
        if (Layout is { } previous)
            candidate.SetCooldownPlacement(previous.Side, CopyPlacements(previous.Placements), previous.Legacy);
    }

    private static bool ValidPlacement(PartyCooldownPlacement? value) => value is not null && !(value with { }).Normalize();

    private static PartyCooldownPlacements? CopyPlacements(PartyCooldownPlacements? source) => source is null ? null : new()
    {
        Left = source.Left is { } left ? left with { } : null,
        Right = source.Right is { } right ? right with { } : null,
    };
}
