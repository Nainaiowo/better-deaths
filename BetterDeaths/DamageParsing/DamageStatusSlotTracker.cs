namespace BetterDeaths.DamageParsing;

using System;
using System.Collections.Generic;
using System.Linq;

// A slot is authoritative replacement evidence, not a rule that equal status IDs cannot coexist.
internal sealed class DamageStatusSlotTracker
{
    private readonly Dictionary<(uint Target, byte Slot), DamageStatusApplication> occupants = [];

    public DamageStatusApplication? Observe(DamageStatusApplication application)
    {
        if (application.StatusSlot is not { } slot || application.Target.EntityId == 0 ||
            application.ObservationKind == DamageStatusObservationKind.Announcement ||
            application.IsRemoval || !float.IsFinite(application.DurationSeconds) || application.DurationSeconds <= 0)
            return null;

        var key = (application.Target.EntityId, slot);
        if (!occupants.TryGetValue(key, out var previous))
        {
            occupants[key] = application;
            return null;
        }

        var sameIdentity = previous.StatusId == application.StatusId &&
            (previous.Source.EntityId == application.Source.EntityId ||
                previous.Source.EntityId == 0 || application.Source.EntityId == 0);
        if (application.SeenAtUtc < previous.SeenAtUtc)
            return sameIdentity ? null : application with { IsRemoval = true, SeenAtUtc = previous.SeenAtUtc };

        occupants[key] = application;
        return sameIdentity ? null : previous with { IsRemoval = true, SeenAtUtc = application.SeenAtUtc };
    }

    public void Prune(DateTime now)
    {
        foreach (var key in occupants.Where(entry =>
                     entry.Value.SeenAtUtc.AddSeconds(entry.Value.DurationSeconds + 35) < now)
                     .Select(entry => entry.Key).ToArray())
            occupants.Remove(key);
    }

    public void Clear() => occupants.Clear();
}
