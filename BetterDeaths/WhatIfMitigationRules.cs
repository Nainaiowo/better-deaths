using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterDeaths;

internal sealed record MitigationTotalDisplay(double AllReduction, double PhysicalReduction, double MagicReduction,
    bool HasTypedReduction, bool AllVariable, bool PhysicalVariable, bool MagicVariable);

internal static class WhatIfMitigationRules
{
    internal static bool HasMixedSourceCoverage(IReadOnlyList<CombatEventRecord> events, IReadOnlyList<PossibleMitigationSnapshot> options) =>
        options.Any(option => option.Scope == PossibleMitigationScope.Boss &&
            CombatEventComponents.Expand(events).Any(hit => PossibleMitigationTimingPolicy.AppliesToHit(option, hit)) &&
            CombatEventComponents.Expand(events).Any(hit => !PossibleMitigationTimingPolicy.AppliesToHit(option, hit)));

    internal static ulong DamageWithOptions(IEnumerable<CombatEventRecord> events, IReadOnlyList<PossibleMitigationSnapshot> options)
    {
        var total = 0UL;
        foreach (var hit in CombatEventComponents.Expand(events).Where(hit => hit.Kind == DeathEventKind.Damage && hit.Amount > 0))
        {
            var statuses = options.Where(option => PossibleMitigationTimingPolicy.AppliesToHit(option, hit))
                .SelectMany(option => option.Statuses);
            total += (ulong)Math.Ceiling(hit.Amount * DamageMultiplier(statuses, hit.DamageType));
        }
        return total;
    }

    internal static MitigationTotalDisplay? CalculateTotal(IEnumerable<StatusSnapshot> statuses)
    {
        var all = 1.0;
        var physical = 1.0;
        var magic = 1.0;
        var any = false;
        var typed = false;
        var allVariable = false;
        var physicalVariable = false;
        var magicVariable = false;
        foreach (var status in DeduplicateStatuses(statuses))
        {
            var info = Plugin.GetMitigationDisplayInfo(status);
            foreach (var part in info.MitigationPercents)
            {
                if (part.Percent <= 0)
                    continue;
                var remaining = 1 - Math.Clamp(part.Percent, 0, 100) / 100.0;
                any = true;
                switch (part.Scope)
                {
                    case Plugin.MitigationPercentScope.Physical:
                        physical *= remaining;
                        typed = true;
                        physicalVariable |= info.HasVariableMitigationPercent;
                        break;
                    case Plugin.MitigationPercentScope.Magic:
                        magic *= remaining;
                        typed = true;
                        magicVariable |= info.HasVariableMitigationPercent;
                        break;
                    default:
                        all *= remaining;
                        allVariable |= info.HasVariableMitigationPercent;
                        break;
                }
            }
        }
        return any ? new(1 - all, 1 - all * physical, 1 - all * magic, typed,
            allVariable, allVariable || physicalVariable, allVariable || magicVariable) : null;
    }

    internal static double DamageMultiplier(IEnumerable<StatusSnapshot> statuses, DamageType type)
    {
        var total = CalculateTotal(statuses);
        if (total is null)
            return 1;
        return 1 - (type switch
        {
            DamageType.Slashing or DamageType.Piercing or DamageType.Blunt or DamageType.Shot or DamageType.Physical => total.PhysicalReduction,
            DamageType.Magic or DamageType.Breath => total.MagicReduction,
            _ => total.AllReduction,
        });
    }

    internal static IReadOnlyList<StatusSnapshot> DeduplicateStatuses(IEnumerable<StatusSnapshot> statuses)
    {
        var deduplicated = new List<StatusSnapshot>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var status in statuses)
        {
            if (seen.Add(GetStatusIdentity(status)))
            {
                deduplicated.Add(status);
            }
        }

        return deduplicated;
    }

    internal static bool ShareStatus(
        IReadOnlyList<StatusSnapshot> leftStatuses,
        IReadOnlyList<StatusSnapshot> rightStatuses)
    {
        var leftIdentities = leftStatuses
            .Select(GetStatusIdentity)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return rightStatuses.Any(status => leftIdentities.Contains(GetStatusIdentity(status)));
    }

    private static string GetStatusIdentity(StatusSnapshot status)
    {
        return status.Id == 0
            ? $"name:{status.Name}"
            : $"id:{status.Id}";
    }
}
