using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterDeaths;

internal static class DeathMitigationChatFormatter
{
    public static string ActiveMitigations(DeathMitigationContext context)
    {
        var parts = new List<string>();
        var defenses = context.PlayerStatuses.Where(Plugin.IsDefensiveStatus).ToArray();
        if (defenses.Length > 0)
            parts.Add(Names(defenses));
        var groups = context.BossStatuses.Where(value => Plugin.IsBossMitigationStatus(value.Status))
            .GroupBy(value => (value.EntityId, value.Name)).OrderBy(group => group.Key.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Key.EntityId).ToArray();
        foreach (var group in groups)
        {
            var label = group.Key.Name;
            var sameName = groups.Where(value => string.Equals(value.Key.Name, label, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (sameName.Length > 1)
                label += $" #{Array.IndexOf(sameName, group) + 1}";
            parts.Add($"{label}: {Names(group.Select(value => value.Status))}");
        }
        return parts.Count == 0 ? "none captured" : string.Join(" | ", parts);
    }

    private static string Names(IEnumerable<StatusSnapshot> statuses) => string.Join(", ", statuses
        .GroupBy(status => status.Id).Select(group => group.First()).OrderBy(status => status.Name, StringComparer.OrdinalIgnoreCase)
        .Select(status => status.StackCount == 0 ? status.Name : $"{status.Name} x{status.StackCount}"));
}
