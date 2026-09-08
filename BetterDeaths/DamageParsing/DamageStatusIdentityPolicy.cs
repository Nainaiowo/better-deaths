namespace BetterDeaths.DamageParsing;

using System.Collections.Generic;

internal interface IDamageStatusIdentity
{
    uint StatusId { get; }
    uint SourceId { get; }
}

internal static class DamageStatusIdentityPolicy
{
    public static bool TryResolve<T>(IReadOnlyList<T>? snapshots, uint statusId,
        uint packetSourceId, bool isRemoval, out uint sourceId, out T? snapshot)
        where T : class, IDamageStatusIdentity
    {
        sourceId = IsActor(packetSourceId) ? packetSourceId : 0;
        snapshot = null;
        // A removed effect may already be absent from memory. Never substitute
        // another caster's surviving effect for the packet's source.
        if (sourceId == 0 && isRemoval)
            return false;

        var authoritativeSource = sourceId != 0;
        if (snapshots is not null)
        {
            foreach (var candidate in snapshots)
            {
                if (candidate.StatusId != statusId || !IsActor(candidate.SourceId) ||
                    authoritativeSource && candidate.SourceId != sourceId)
                    continue;

                if (snapshot is not null)
                {
                    // Source-less updates cannot distinguish overlapping effects.
                    // Even duplicate entries for one source have ambiguous metadata.
                    snapshot = null;
                    if (!authoritativeSource)
                    {
                        sourceId = 0;
                        return false;
                    }
                    return true;
                }
                sourceId = candidate.SourceId;
                snapshot = candidate;
            }
        }
        return sourceId != 0;
    }

    private static bool IsActor(uint id) => id != 0 && id != 0xE0000000 && id != uint.MaxValue;
}
