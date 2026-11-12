using System.Collections.Generic;
using System.Linq;

namespace BetterDeaths.PartyCooldowns;

public sealed class PartyCooldownRows
{
    internal const int MaximumRows = 3;
    internal const int MaximumAbilities = 24;

    public bool Enabled { get; set; }
    public List<List<uint>> Rows { get; set; } = [[]];

    internal bool Normalize(IReadOnlyList<uint> selected)
    {
        var allowed = selected.Where(id => id != 0).Distinct().Take(MaximumAbilities).ToHashSet();
        var seen = new HashSet<uint>();
        var normalized = new List<List<uint>>();
        if (Rows is not null)
            foreach (var row in Rows.Take(MaximumRows))
                normalized.Add(row?.Where(id => allowed.Contains(id) && seen.Add(id)).ToList() ?? []);
        if (normalized.Count == 0)
            normalized.Add([]);
        foreach (var id in selected)
            if (allowed.Contains(id) && seen.Add(id))
                normalized[0].Add(id);
        if (Rows is not null && Rows.Count == normalized.Count &&
            Rows.Select((row, index) => row is not null && row.SequenceEqual(normalized[index])).All(equal => equal))
            return false;
        Rows = normalized;
        return true;
    }

    internal List<uint> Flatten() => Rows.SelectMany(row => row).Distinct().ToList();

    internal byte RowFor(uint actionId)
    {
        if (Enabled)
            for (byte index = 0; index < Rows.Count && index < MaximumRows; index++)
                if (Rows[index].Contains(actionId))
                    return index;
        return 0;
    }

    internal bool AddRow()
    {
        if (Rows.Count >= MaximumRows)
            return false;
        Rows.Add([]);
        return true;
    }

    internal bool RemoveEmptyRow(int index)
    {
        if (index <= 0 || index >= Rows.Count || Rows[index].Count != 0)
            return false;
        Rows.RemoveAt(index);
        return true;
    }

    internal bool Remove(uint actionId)
    {
        var removed = false;
        foreach (var row in Rows)
            removed |= row.Remove(actionId);
        return removed;
    }

    internal bool Place(uint actionId, int rowIndex, uint? target = null, bool after = false)
    {
        if (actionId == 0 || rowIndex < 0 || rowIndex >= Rows.Count || rowIndex >= MaximumRows || actionId == target ||
            target is { } anchor && !Rows[rowIndex].Contains(anchor))
            return false;
        var source = Rows.FindIndex(row => row.Contains(actionId));
        if (source < 0 && Rows.Sum(row => row.Count) >= MaximumAbilities)
            return false;
        if (source >= 0 && source != rowIndex)
            Rows[source].Remove(actionId);
        return TileOrderPolicy.Place(Rows[rowIndex], actionId, target, after);
    }
}
