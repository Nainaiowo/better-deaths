using System.Collections.Generic;
using System.Linq;

namespace BetterDeaths;

internal static class DeathMitigationDisplayFormatter
{
    public static IReadOnlyList<StatusSnapshot> Combine(
        IEnumerable<StatusSnapshot> playerStatuses,
        IEnumerable<StatusSnapshot> bossStatuses)
    {
        return playerStatuses
            .Concat(bossStatuses.Where(Plugin.IsBossMitigationStatus))
            .GroupBy(status => (status.Id, status.IconId, status.SourceId, status.Name))
            .Select(group => group
                .OrderBy(status => status.RemainingTime <= 0.0f ? float.MaxValue : status.RemainingTime)
                .ThenBy(status => status.StackCount)
                .First())
            .ToList();
    }
}
