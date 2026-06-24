namespace BetterDeaths;

using System;
using System.Collections.Generic;
using System.Linq;

public sealed record DeathDisplaySelection(
    DateTime AnchorSeenAtUtc,
    HpHistorySnapshot? Snapshot,
    IReadOnlyList<CombatEventRecord> Events);

public static class DeathDisplaySelector
{
    private const float LeadUpHistorySeconds = 10.0f;
    private static readonly TimeSpan MaxHeadlineHpSampleAge = TimeSpan.FromMilliseconds(1500);

    public static DeathDisplaySelection Select(PartyDeathRecord death)
    {
        var anchorSeenAtUtc = GetLeadUpAnchorSeenAtUtc(death);
        var events = GetStoredLikelyCauseEvents(death);
        var snapshot = GetMathematicallyFittingSnapshot(death, anchorSeenAtUtc, events) ??
            GetReliablePriorSnapshot(death, anchorSeenAtUtc, events) ??
            GetFallbackSnapshot(death, anchorSeenAtUtc, events);

        return new DeathDisplaySelection(anchorSeenAtUtc, snapshot, events);
    }

    public static IReadOnlyList<CombatEventRecord> GetLeadUpEvents(PartyDeathRecord death)
    {
        var displayAnchorSeenAtUtc = death.SeenAtUtc;
        var cutoff = displayAnchorSeenAtUtc - TimeSpan.FromSeconds(LeadUpHistorySeconds);
        var events = death.RecentEvents.AsEnumerable();
        if (death.FatalSequence is { Events.Count: > 0 } sequence)
        {
            events = events.Concat(sequence.Events);
        }

        if (death.LikelyCause is { } likelyCause)
        {
            events = events.Append(likelyCause);
        }

        var orderedEvents = events
            .Where(combatEvent => combatEvent.SeenAtUtc >= cutoff && combatEvent.SeenAtUtc <= displayAnchorSeenAtUtc)
            .Where(IsDeathRelevantLeadUpEvent)
            .OrderBy(combatEvent => combatEvent.SeenAtUtc)
            .ToList();

        return DeduplicateStoredEvents(orderedEvents);
    }

    public static bool IsLikelyDeathCauseEvent(CombatEventRecord combatEvent)
    {
        return combatEvent.Kind == DeathEventKind.Status ||
            (combatEvent.Kind == DeathEventKind.Damage && combatEvent.Amount > 0);
    }

    private static HpHistorySnapshot? GetReliablePriorSnapshot(
        PartyDeathRecord death,
        DateTime anchorSeenAtUtc,
        IReadOnlyList<CombatEventRecord> events)
    {
        var latestAllowedAtUtc = events.Count > 0
            ? events.OrderBy(combatEvent => combatEvent.SeenAtUtc).First().SeenAtUtc
            : anchorSeenAtUtc;

        return GetLeadUpHpHistory(death, anchorSeenAtUtc)
            .Where(snapshot => snapshot.SeenAtUtc <= latestAllowedAtUtc)
            .Where(snapshot => latestAllowedAtUtc - snapshot.SeenAtUtc <= MaxHeadlineHpSampleAge)
            .LastOrDefault();
    }

    private static HpHistorySnapshot? GetMathematicallyFittingSnapshot(
        PartyDeathRecord death,
        DateTime anchorSeenAtUtc,
        IReadOnlyList<CombatEventRecord> events)
    {
        if (events.Count == 0 || GetIncomingDamageAmount(events) is not { } incomingDamage)
        {
            return null;
        }

        var firstCauseSeenAtUtc = events
            .OrderBy(combatEvent => combatEvent.SeenAtUtc)
            .First()
            .SeenAtUtc;

        return GetLeadUpHpHistory(death, anchorSeenAtUtc)
            .Where(snapshot => snapshot.SeenAtUtc <= firstCauseSeenAtUtc)
            .LastOrDefault(snapshot => SnapshotHpAndShields(snapshot) <= incomingDamage);
    }

    private static IReadOnlyList<CombatEventRecord> DeduplicateStoredEvents(IReadOnlyList<CombatEventRecord> orderedEvents)
    {
        if (orderedEvents.Count == 0)
        {
            return [];
        }

        var seenIdentities = new HashSet<string>(StringComparer.Ordinal);
        var seenLegacyKeys = new HashSet<(DateTime SeenAtUtc, string MemberKey, uint SourceEntityId, uint ActionId, DeathEventKind Kind, uint Amount)>();
        var deduplicated = new List<CombatEventRecord>(orderedEvents.Count);
        foreach (var combatEvent in orderedEvents)
        {
            if (!string.IsNullOrWhiteSpace(combatEvent.EventIdentity))
            {
                if (seenIdentities.Add(combatEvent.EventIdentity))
                {
                    deduplicated.Add(combatEvent);
                }

                continue;
            }

            var legacyKey = (
                combatEvent.SeenAtUtc,
                combatEvent.MemberKey,
                combatEvent.SourceEntityId,
                combatEvent.ActionId,
                combatEvent.Kind,
                combatEvent.Amount);
            if (seenLegacyKeys.Add(legacyKey))
            {
                deduplicated.Add(combatEvent);
            }
        }

        return deduplicated;
    }

    private static HpHistorySnapshot? GetFallbackSnapshot(
        PartyDeathRecord death,
        DateTime anchorSeenAtUtc,
        IReadOnlyList<CombatEventRecord> events)
    {
        var latestAllowedAtUtc = events.Count > 0
            ? events.OrderBy(combatEvent => combatEvent.SeenAtUtc).First().SeenAtUtc
            : anchorSeenAtUtc;

        return GetLeadUpHpHistory(death, anchorSeenAtUtc)
            .Where(snapshot => snapshot.SeenAtUtc <= latestAllowedAtUtc)
            .LastOrDefault();
    }

    private static IReadOnlyList<HpHistorySnapshot> GetLeadUpHpHistory(PartyDeathRecord death, DateTime anchorSeenAtUtc)
    {
        var cutoff = anchorSeenAtUtc - TimeSpan.FromSeconds(LeadUpHistorySeconds);
        return death.HpHistory
            .Where(snapshot => snapshot.SeenAtUtc >= cutoff && snapshot.SeenAtUtc <= anchorSeenAtUtc)
            .Where(snapshot => snapshot.CurrentHp > 0 || snapshot.ShieldHp > 0)
            .OrderBy(snapshot => snapshot.SeenAtUtc)
            .ToList();
    }

    private static IReadOnlyList<CombatEventRecord> GetStoredLikelyCauseEvents(PartyDeathRecord death)
    {
        if (death.LikelyCause is { Kind: DeathEventKind.Status } statusCause)
        {
            return [statusCause];
        }

        if (death.FatalSequence is { Events.Count: > 0 } sequence)
        {
            var sequenceEvents = sequence.Events
                .Where(IsLikelyDeathCauseEvent)
                .OrderBy(combatEvent => combatEvent.SeenAtUtc)
                .ToList();
            if (sequenceEvents.Count > 0)
            {
                return DeduplicateStoredEvents(sequenceEvents);
            }
        }

        return death.LikelyCause is { } likelyCause && IsLikelyDeathCauseEvent(likelyCause)
            ? [likelyCause]
            : [];
    }

    private static DateTime GetLeadUpAnchorSeenAtUtc(PartyDeathRecord death)
    {
        return death.FatalSequence?.EndAtUtc ?? GetDisplayCause(death)?.SeenAtUtc ?? death.SeenAtUtc;
    }

    private static CombatEventRecord? GetDisplayCause(PartyDeathRecord death)
    {
        var likelyCause = death.LikelyCause is { } storedCause && IsLikelyDeathCauseEvent(storedCause)
            ? storedCause
            : null;

        return death.FatalSequence is { } sequence
            ? sequence.Events
                .Where(IsLikelyDeathCauseEvent)
                .OrderByDescending(combatEvent => combatEvent.SeenAtUtc)
                .ThenByDescending(combatEvent => combatEvent.Kind == DeathEventKind.Damage)
                .FirstOrDefault() ?? likelyCause
            : likelyCause;
    }

    private static bool IsDeathRelevantLeadUpEvent(CombatEventRecord combatEvent)
    {
        return combatEvent.Kind switch
        {
            DeathEventKind.Damage => combatEvent.Amount > 0,
            DeathEventKind.Miss or DeathEventKind.Invulnerable or DeathEventKind.Status => true,
            _ => false,
        };
    }

    private static ulong? GetIncomingDamageAmount(IReadOnlyList<CombatEventRecord> events)
    {
        var total = events
            .Where(combatEvent => combatEvent.Kind == DeathEventKind.Damage && combatEvent.Amount > 0)
            .Aggregate(0UL, (sum, combatEvent) => sum + combatEvent.Amount);
        return total == 0 ? null : total;
    }

    private static ulong SnapshotHpAndShields(HpHistorySnapshot snapshot)
    {
        return snapshot.CurrentHp + (ulong)snapshot.ShieldHp;
    }
}
