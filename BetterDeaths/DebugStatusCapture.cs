using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterDeaths;

internal sealed class DebugStatusCapture
{
    private readonly Dictionary<string, DebugStatusSnapshot> current = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<(uint Id, uint SourceId), StatusSnapshot>> history = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> signatures = new(StringComparer.Ordinal);

    public int Count => current.Count;

    public IReadOnlyList<DebugStatusSnapshot> GetHistory() => current.Values
        .Select(snapshot => snapshot with { Statuses = history[snapshot.MemberKey].Values.ToArray() })
        .OrderBy(snapshot => snapshot.PartyIndex)
        .ThenBy(snapshot => snapshot.MemberName, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public DebugStatusSnapshot? Observe(DebugStatusSnapshot snapshot)
    {
        snapshot = snapshot with { Statuses = snapshot.Statuses.ToArray() };
        current[snapshot.MemberKey] = snapshot;
        if (!history.TryGetValue(snapshot.MemberKey, out var statuses))
        {
            statuses = new();
            history[snapshot.MemberKey] = statuses;
        }

        foreach (var status in snapshot.Statuses)
        {
            statuses[(status.Id, status.SourceId)] = status;
        }

        var signature = string.Join("|", snapshot.IsDead, snapshot.HasWorldObject, snapshot.WorldObjectIsDead,
            snapshot.CurrentHp, snapshot.ShieldHp, snapshot.MaxHp,
            string.Join(";", snapshot.Statuses.OrderBy(status => status.Id).ThenBy(status => status.SourceId)
                .Select(status => $"{status.Id}:{status.SourceId}:{status.StackCount}")));
        if (signatures.TryGetValue(snapshot.MemberKey, out var previous) && previous == signature)
        {
            return null;
        }

        signatures[snapshot.MemberKey] = signature;
        // Persist the current state, not the accumulated history used by the Debug UI.
        return snapshot;
    }

    public void Clear()
    {
        current.Clear();
        history.Clear();
        signatures.Clear();
    }
}
