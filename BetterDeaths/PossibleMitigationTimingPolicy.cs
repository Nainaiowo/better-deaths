using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterDeaths;

internal static class PossibleMitigationTimingPolicy
{
    private sealed record Context(PartyDeathRecord Death, CombatEventRecord[] Hits,
        DeathMitigationContext Active, DeathMitigationResolver Resolver);

    private static Context For(PartyDeathRecord death)
    {
        var hits = CombatEventComponents.Expand(DeathDisplaySelector.Select(death).FatalEvents.SelectMany(value => value.Events))
            .Where(hit => hit.Kind == DeathEventKind.Damage && hit.Amount > 0).ToArray();
        var resolver = DeathMitigationResolver.For(death);
        return new(death, hits, resolver.Resolve(), resolver);
    }

    public static IReadOnlyList<PossibleMitigationSnapshot> ResolveAll(IEnumerable<PossibleMitigationSnapshot> candidates,
        PartyDeathRecord death, MitigationUseTracker tracker, DateTime evidenceEndAtUtc, int maximum)
    {
        var context = For(death);
        return candidates.Select(candidate => Resolve(candidate, context, tracker, evidenceEndAtUtc))
            .OfType<PossibleMitigationSnapshot>().Take(maximum).ToArray();
    }

    public static PossibleMitigationSnapshot? Resolve(PossibleMitigationSnapshot candidate, PartyDeathRecord death,
        MitigationUseTracker tracker, DateTime evidenceEndAtUtc) => Resolve(candidate, For(death), tracker, evidenceEndAtUtc);

    private static PossibleMitigationSnapshot? Resolve(PossibleMitigationSnapshot candidate, Context context,
        MitigationUseTracker tracker, DateTime evidenceEndAtUtc)
    {
        var death = context.Death;
        var hits = context.Hits;
        var uses = tracker.UsesFor(candidate.Key).Where(use => use.SeenAtUtc <= evidenceEndAtUtc).ToArray();
        var active = context.Active;
        if (candidate.Scope != PossibleMitigationScope.Boss && IsAlreadyActive(candidate, active.PlayerStatuses))
            return null;
        var unprotected = candidate.Scope == PossibleMitigationScope.Boss
            ? hits.Where(hit => !IsAlreadyActive(candidate, context.Resolver.ForHit(hit).Select(value => value.Status))).ToArray() : hits;
        if (hits.Length > 0 && unprotected.Length == 0)
            return null;
        var availableSources = unprotected.Select(hit => hit.SourceEntityId).Where(IsValidEntityId).Distinct().ToArray();
        var at = unprotected.Length == 0 ? death.SeenAtUtc : unprotected.Min(hit => hit.SeenAtUtc);
        var prior = uses.LastOrDefault(use => use.SeenAtUtc <= at);
        var ready = tracker.IsReadyAt(candidate, at);

        if (hits.Length > 0)
        {
            var sourceIds = availableSources;
            foreach (var use in uses.Reverse())
            {
                var applications = tracker.ApplicationsFor(use)
                    .Where(value => value.SeenAtUtc >= use.SeenAtUtc && value.SeenAtUtc <= evidenceEndAtUtc &&
                        MatchesStatus(candidate, value.Status) && MatchesTarget(candidate.Scope, death.EntityId, sourceIds, value.TargetEntityId))
                    .ToArray();
                var targets = use.TargetEntityIds.Where(target => MatchesTarget(candidate.Scope, death.EntityId, sourceIds, target))
                    .Concat(applications.Select(value => value.TargetEntityId)).Distinct().ToArray();
                if (targets.Length == 0)
                    continue;
                var affectedHits = candidate.Scope == PossibleMitigationScope.Boss
                    ? unprotected.Where(hit => targets.Contains(hit.SourceEntityId)).ToArray() : unprotected;
                var hitAt = affectedHits.Min(hit => hit.SeenAtUtc);
                if (use.SeenAtUtc > hitAt)
                {
                    // A later use must not hide an earlier use that was already on cooldown at the hit.
                    if (!tracker.IsReadyAt(candidate, hitAt))
                        continue;
                    return WithUse(candidate, use, true, candidate.Scope == PossibleMitigationScope.Boss ? targets : [], affectedHits);
                }
                if (applications.Length == 0 || applications.Min(value => value.SeenAtUtc) <= hitAt ||
                    !tracker.IsReadyAt(candidate, hitAt, use.CaptureId))
                    continue;
                if (!use.TargetEntityIds.Any(target => MatchesTarget(candidate.Scope, death.EntityId, sourceIds, target)))
                    continue;
                if (!HasKnownSnapshotAtHit(candidate.Scope, death, affectedHits))
                    continue;
                // Matching action sequence, provider and recipient prove a delayed application, not just an absent icon.
                return WithUse(candidate, use, true, candidate.Scope == PossibleMitigationScope.Boss ? targets : [], affectedHits);
            }
        }
        return ready ? WithUse(candidate, prior, false, candidate.Scope == PossibleMitigationScope.Boss ? availableSources : [], unprotected) : null;
    }

    private static bool HasKnownSnapshotAtHit(PossibleMitigationScope scope, PartyDeathRecord death, IEnumerable<CombatEventRecord> hits) =>
        hits.All(hit => scope == PossibleMitigationScope.Boss
            ? hit.HasSourceStatusSnapshot || death.SourceMitigationHistory.Any(value => value.SourceEntityId == hit.SourceEntityId &&
                value.SeenAtUtc <= hit.SeenAtUtc && value.IsComplete)
            : hit.HasPlayerStatusSnapshot || hit.HpSource == CombatEventHpSource.DirectCombatEventSnapshot ||
                death.PlayerStatusHistory.Any(value => value.SeenAtUtc <= hit.SeenAtUtc && value.IsComplete));

    private static bool MatchesTarget(PossibleMitigationScope scope, uint recipient, IReadOnlyList<uint> sources, uint target) =>
        IsValidEntityId(target) && (scope == PossibleMitigationScope.Boss ? sources.Contains(target) : target == recipient);

    private static bool IsValidEntityId(uint id) => id is not (0 or 0xE0000000 or uint.MaxValue);

    private static PossibleMitigationSnapshot WithUse(PossibleMitigationSnapshot candidate, MitigationUseEvidence? use,
        bool late, IReadOnlyList<uint> sources, IReadOnlyList<CombatEventRecord> hits) => candidate with
        {
            ActionId = use?.ActionId ?? candidate.ActionId,
            ActionName = use?.ActionName ?? candidate.ActionName,
            ActionIconId = use?.ActionIconId ?? candidate.ActionIconId,
            LastUsedAtUtc = use?.SeenAtUtc,
            LastUsedPullElapsedSeconds = use?.PullElapsedSeconds,
            Availability = late ? "Used too late" : "Ready",
            WasUsedTooLate = late,
            SimulationSourceEntityIds = sources,
            SimulationHits = candidate.Scope == PossibleMitigationScope.Boss ? hits
                .Where(hit => sources.Contains(hit.SourceEntityId)).Select(SimulationIdentity).ToArray() : [],
        };

    public static MitigationSimulationHit SimulationIdentity(CombatEventRecord hit) =>
        new(hit.SeenAtUtc, hit.SourceEntityId, hit.ActionId, hit.ActionSequence, hit.EventOrdinal, hit.EventIdentity);

    public static bool AppliesToHit(PossibleMitigationSnapshot option, CombatEventRecord hit) =>
        (option.Scope != PossibleMitigationScope.Boss || option.SimulationSourceEntityIds.Count == 0 || option.SimulationSourceEntityIds.Contains(hit.SourceEntityId)) &&
        (option.SimulationHits.Count == 0 || option.SimulationHits.Contains(SimulationIdentity(hit)));

    public static bool MatchesStatus(PossibleMitigationSnapshot option, StatusSnapshot status) =>
        option.EffectStatusIds.Count > 0 ? option.EffectStatusIds.Contains(status.Id) : option.Statuses.Any(expected => expected.Id != 0
            ? expected.Id == status.Id : string.Equals(expected.Name, status.Name, StringComparison.OrdinalIgnoreCase));

    public static bool IsAlreadyActive(PossibleMitigationSnapshot option, IEnumerable<StatusSnapshot> statuses) =>
        statuses.Any(status => MatchesStatus(option, status));

    public static bool SameOptions(IReadOnlyList<PossibleMitigationSnapshot> left, IReadOnlyList<PossibleMitigationSnapshot> right) =>
        left.Count == right.Count && left.Zip(right).All(pair => pair.First.Key == pair.Second.Key &&
            pair.First.ActionId == pair.Second.ActionId && pair.First.LastUsedAtUtc == pair.Second.LastUsedAtUtc &&
            pair.First.WasUsedTooLate == pair.Second.WasUsedTooLate &&
            pair.First.SimulationSourceEntityIds.SequenceEqual(pair.Second.SimulationSourceEntityIds) &&
            pair.First.SimulationHits.SequenceEqual(pair.Second.SimulationHits));
}
