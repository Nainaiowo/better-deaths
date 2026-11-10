using System;
using System.Collections.Generic;

namespace BetterDeaths;

internal static class CombatEventComponents
{
    public static bool SameSource(CombatEventRecord left, CombatEventRecord right) =>
        IsKnownSource(left.SourceEntityId) && IsKnownSource(right.SourceEntityId)
            ? left.SourceEntityId == right.SourceEntityId
            : string.Equals(left.SourceName, right.SourceName, StringComparison.Ordinal);

    private static bool IsKnownSource(uint id) => id is not (0 or 0xE0000000 or uint.MaxValue);

    public static IEnumerable<CombatEventRecord> Expand(CombatEventRecord hit)
    {
        if (hit.ConstituentEvents.Count == 0)
            yield return hit;
        else
            foreach (var child in hit.ConstituentEvents)
                foreach (var component in Expand(child))
                    yield return component;
    }

    public static IEnumerable<CombatEventRecord> Expand(IEnumerable<CombatEventRecord> hits)
    {
        foreach (var hit in hits)
            foreach (var component in Expand(hit))
                yield return component;
    }
}
