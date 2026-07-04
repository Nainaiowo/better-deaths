namespace BetterDeaths;

using System;
using System.Collections.Generic;
using System.Linq;

public sealed record DeathDisplaySelection(
    DateTime AnchorSeenAtUtc,
    HpHistorySnapshot? Snapshot,
    IReadOnlyList<FatalEventGroup> FatalEvents)
{
    public IReadOnlyList<CombatEventRecord> Events { get; } = FatalEvents
        .Select(group => group.DisplayEvent)
        .ToList();
}

public sealed record FatalEventGroup(IReadOnlyList<CombatEventRecord> Events)
{
    public CombatEventRecord DisplayEvent { get; } = Events.Count == 1
        ? Events[0]
        : CreateDisplayEvent(Events);

    public ulong Amount { get; } = Events
        .Where(combatEvent => combatEvent.Kind == DeathEventKind.Damage && combatEvent.Amount > 0)
        .Aggregate(0UL, (sum, combatEvent) => sum + combatEvent.Amount);

    private static CombatEventRecord CreateDisplayEvent(IReadOnlyList<CombatEventRecord> events)
    {
        var orderedEvents = events
            .OrderBy(combatEvent => combatEvent.SeenAtUtc)
            .ThenBy(combatEvent => combatEvent.EventOrdinal)
            .ToList();
        var first = orderedEvents[0];
        var last = orderedEvents[^1];
        var total = orderedEvents.Aggregate(0UL, (sum, combatEvent) => sum + combatEvent.Amount);
        return first with
        {
            Amount = total > uint.MaxValue ? uint.MaxValue : (uint)total,
            Statuses = MergeStatuses(orderedEvents.SelectMany(combatEvent => combatEvent.Statuses)),
            SourceStatuses = MergeStatuses(orderedEvents.SelectMany(combatEvent => combatEvent.SourceStatuses)),
            ResultSeenAtUtc = last.ResultSeenAtUtc,
            ResultCurrentHp = last.ResultCurrentHp,
            ResultShieldHp = last.ResultShieldHp,
            ResultMaxHp = last.ResultMaxHp,
            ResultStatuses = last.ResultStatuses,
            EventIdentity = $"fatal-group:{first.EventIdentity ?? first.SeenAtUtc.Ticks.ToString()}:{last.EventIdentity ?? last.SeenAtUtc.Ticks.ToString()}",
        };
    }

    private static IReadOnlyList<StatusSnapshot> MergeStatuses(IEnumerable<StatusSnapshot> statuses)
    {
        return statuses
            .GroupBy(status => (status.Id, status.SourceId))
            .Select(group => group.OrderBy(status => status.RemainingTime).First())
            .OrderBy(status => status.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(status => status.Id)
            .ToList();
    }
}

public static class DeathDisplaySelector
{
    private const float LeadUpHistorySeconds = 10.0f;
    private static readonly TimeSpan MaxHeadlineHpSampleAge = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan FatalTailLookback = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan FatalTailForwardBuffer = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan CombatLogActionEffectMatchWindow = TimeSpan.FromSeconds(2);

    public static DeathDisplaySelection Select(PartyDeathRecord death)
    {
        var anchorSeenAtUtc = GetLeadUpAnchorSeenAtUtc(death);
        var fatalEvents = GetStoredFatalEventGroups(death);
        var events = fatalEvents.Select(group => group.DisplayEvent).ToList();
        var snapshot = GetDirectEventSnapshot(events) ??
            GetFatalTailSnapshot(death, events) ??
            GetMathematicallyFittingSnapshot(death, anchorSeenAtUtc, events) ??
            GetReliablePriorSnapshot(death, anchorSeenAtUtc, events) ??
            GetFallbackSnapshot(death, anchorSeenAtUtc, events);

        return new DeathDisplaySelection(anchorSeenAtUtc, snapshot, fatalEvents);
    }

    public static IReadOnlyList<CombatEventRecord> GetLeadUpEvents(PartyDeathRecord death)
    {
        var displayAnchorSeenAtUtc = death.SeenAtUtc;
        var anchorSeenAtUtc = GetLeadUpAnchorSeenAtUtc(death);
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

        var fatalDisplayEvents = GetStoredFatalEventGroups(death)
            .SelectMany(group => group.Events);
        events = events.Concat(fatalDisplayEvents);

        var orderedEvents = events
            .Where(combatEvent => EventTouchesLeadUpWindow(combatEvent, cutoff, anchorSeenAtUtc))
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

    public static bool IsFatalEvent(CombatEventRecord combatEvent)
    {
        return IsLikelyDeathCauseEvent(combatEvent);
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
            .LastOrDefault(snapshot => SnapshotHpForDamageMath(snapshot) <= incomingDamage);
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

    private static HpHistorySnapshot? GetDirectEventSnapshot(IReadOnlyList<CombatEventRecord> events)
    {
        var combatEvent = events
            .Where(HasDirectEventHp)
            .OrderBy(combatEvent => combatEvent.SeenAtUtc)
            .ThenBy(combatEvent => combatEvent.EventOrdinal)
            .FirstOrDefault();
        return combatEvent is null
            ? null
            : new HpHistorySnapshot(
                combatEvent.SeenAtUtc,
                combatEvent.PullElapsedSeconds,
                combatEvent.CurrentHp,
                combatEvent.ShieldHp,
                combatEvent.MaxHp,
                combatEvent.Statuses);
    }

    private static bool HasDirectEventHp(CombatEventRecord combatEvent)
    {
        return combatEvent.HpSource == CombatEventHpSource.DirectCombatEventSnapshot &&
            combatEvent.MaxHp > 0 &&
            (combatEvent.CurrentHp > 0 || combatEvent.ShieldHp > 0);
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

    private static IReadOnlyList<FatalEventGroup> GetStoredFatalEventGroups(PartyDeathRecord death)
    {
        if (IsEnvironmentSourceDeath(death))
        {
            return [];
        }

        if (death.LikelyCause is { Kind: DeathEventKind.Status } statusCause)
        {
            return [new FatalEventGroup([statusCause])];
        }

        var lastAliveSnapshot = GetLastAliveSnapshot(death);
        if (death.FatalSequence is { Events.Count: > 0 } sequence)
        {
            var sequenceEvents = sequence.Events
                .Where(IsFatalEvent)
                .OrderBy(combatEvent => combatEvent.SeenAtUtc)
                .ToList();
            if (sequenceEvents.Count > 0)
            {
                if (sequenceEvents.Any(HasDirectEventHp))
                {
                    var directFatalTail = SelectFatalTail(sequenceEvents, lastAliveSnapshot);
                    if (directFatalTail.Count > 0)
                    {
                        return BuildFatalEventGroups(directFatalTail);
                    }
                }

                var combatLogGroups = GetFatalEventGroupsFromCombatLogTail(death, lastAliveSnapshot);
                if (combatLogGroups.Count > 0)
                {
                    return combatLogGroups;
                }

                var fatalTail = SelectFatalTail(sequenceEvents, lastAliveSnapshot);
                return BuildFatalEventGroups(fatalTail.Count > 0 ? fatalTail : DeduplicateStoredEvents(sequenceEvents));
            }
        }

        var fallbackCombatLogGroups = GetFatalEventGroupsFromCombatLogTail(death, lastAliveSnapshot);
        if (fallbackCombatLogGroups.Count > 0)
        {
            return fallbackCombatLogGroups;
        }

        var recentTailGroups = GetFatalEventGroupsFromRecentEvents(death, lastAliveSnapshot);
        if (recentTailGroups.Count > 0)
        {
            return recentTailGroups;
        }

        return death.LikelyCause is { } likelyCause && IsFatalEvent(likelyCause)
            ? [new FatalEventGroup([likelyCause])]
            : [];
    }

    private static DateTime GetLeadUpAnchorSeenAtUtc(PartyDeathRecord death)
    {
        if (IsEnvironmentSourceDeath(death))
        {
            return death.SeenAtUtc;
        }

        return death.FatalSequence?.EndAtUtc ?? GetDisplayCause(death)?.SeenAtUtc ?? death.SeenAtUtc;
    }

    private static CombatEventRecord? GetDisplayCause(PartyDeathRecord death)
    {
        if (IsEnvironmentSourceDeath(death))
        {
            return null;
        }

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

    private static bool IsEnvironmentSourceDeath(PartyDeathRecord death)
    {
        return death.EnvironmentalAssessment is { EnvironmentSourceDeath: true };
    }

    private static HpHistorySnapshot? GetFatalTailSnapshot(
        PartyDeathRecord death,
        IReadOnlyList<CombatEventRecord> events)
    {
        if (events.Count == 0 || GetIncomingDamageAmount(events) is not { } incomingDamage)
        {
            return null;
        }

        var lastAliveSnapshot = GetLastAliveSnapshot(death);
        if (lastAliveSnapshot is null)
        {
            return null;
        }

        return incomingDamage >= SnapshotHpForDamageMath(lastAliveSnapshot)
            ? lastAliveSnapshot
            : null;
    }

    private static HpHistorySnapshot? GetLastAliveSnapshot(PartyDeathRecord death)
    {
        return death.HpHistory
            .Where(snapshot => snapshot.SeenAtUtc <= death.SeenAtUtc)
            .Where(snapshot => snapshot.CurrentHp > 0 || snapshot.ShieldHp > 0)
            .OrderBy(snapshot => snapshot.SeenAtUtc)
            .LastOrDefault();
    }

    private static IReadOnlyList<FatalEventGroup> GetFatalEventGroupsFromCombatLogTail(
        PartyDeathRecord death,
        HpHistorySnapshot? lastAliveSnapshot)
    {
        if (death.FatalSequence is not { LogEvents.Count: > 0 } sequence)
        {
            return [];
        }

        var logEvents = sequence.LogEvents
            .Where(logEvent => logEvent.Amount > 0)
            .OrderBy(logEvent => logEvent.SeenAtUtc)
            .ToList();
        if (logEvents.Count == 0)
        {
            return [];
        }

        var matchableLogEvents = FilterCombatLogEventsWithMatchingActionEffects(death, logEvents);
        var fatalTail = SelectFatalTail(matchableLogEvents, lastAliveSnapshot);
        if (fatalTail.Count == 0)
        {
            return [];
        }

        return BuildFatalEventGroups(CreateCombatEventsFromLogTail(death, fatalTail));
    }

    private static IReadOnlyList<CombatLogEventRecord> FilterCombatLogEventsWithMatchingActionEffects(
        PartyDeathRecord death,
        IReadOnlyList<CombatLogEventRecord> logEvents)
    {
        var matchingLogs = logEvents
            .Where(logEvent => death.RecentEvents.Any(combatEvent => LogEventMatchesCombatEvent(logEvent, combatEvent)))
            .ToList();

        return matchingLogs.Count == 0 ? logEvents : matchingLogs;
    }

    private static bool LogEventMatchesCombatEvent(CombatLogEventRecord logEvent, CombatEventRecord combatEvent)
    {
        return combatEvent.Kind == DeathEventKind.Damage &&
            combatEvent.Amount == logEvent.Amount &&
            string.Equals(combatEvent.SourceName, logEvent.SourceName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(combatEvent.ActionName, logEvent.ActionName, StringComparison.OrdinalIgnoreCase) &&
            Math.Abs((combatEvent.SeenAtUtc - logEvent.SeenAtUtc).TotalSeconds) <= CombatLogActionEffectMatchWindow.TotalSeconds;
    }

    private static IReadOnlyList<CombatEventRecord> CreateCombatEventsFromLogTail(
        PartyDeathRecord death,
        IReadOnlyList<CombatLogEventRecord> logEvents)
    {
        var usedCombatEventIndexes = new HashSet<int>();
        var displayEvents = new List<CombatEventRecord>(logEvents.Count);
        for (var logIndex = 0; logIndex < logEvents.Count; logIndex++)
        {
            var logEvent = logEvents[logIndex];
            var matchedEvent = FindMatchingCombatEventForLogEvent(death, logEvent, usedCombatEventIndexes);
            displayEvents.Add(matchedEvent is null
                ? CreateSyntheticCombatEventFromLogEvent(death, logEvent, logIndex)
                : matchedEvent);
        }

        return displayEvents;
    }

    private static CombatEventRecord? FindMatchingCombatEventForLogEvent(
        PartyDeathRecord death,
        CombatLogEventRecord logEvent,
        ISet<int> usedCombatEventIndexes)
    {
        var bestIndex = -1;
        var bestDistance = TimeSpan.MaxValue;
        for (var index = 0; index < death.RecentEvents.Count; index++)
        {
            if (usedCombatEventIndexes.Contains(index))
            {
                continue;
            }

            var combatEvent = death.RecentEvents[index];
            if (!LogEventMatchesCombatEvent(logEvent, combatEvent))
            {
                continue;
            }

            var distance = (combatEvent.SeenAtUtc - logEvent.SeenAtUtc).Duration();
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = index;
            }
        }

        if (bestIndex < 0)
        {
            return null;
        }

        usedCombatEventIndexes.Add(bestIndex);
        return death.RecentEvents[bestIndex];
    }

    private static CombatEventRecord CreateSyntheticCombatEventFromLogEvent(
        PartyDeathRecord death,
        CombatLogEventRecord logEvent,
        int logIndex)
    {
        var template = death.RecentEvents
            .Where(combatEvent => string.Equals(combatEvent.SourceName, logEvent.SourceName, StringComparison.OrdinalIgnoreCase))
            .Where(combatEvent => string.Equals(combatEvent.ActionName, logEvent.ActionName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(combatEvent => Math.Abs((combatEvent.SeenAtUtc - logEvent.SeenAtUtc).TotalSeconds))
            .FirstOrDefault();

        return new CombatEventRecord(
            logEvent.SeenAtUtc,
            logEvent.PullElapsedSeconds,
            death.MemberKey,
            death.MemberName,
            death.PartyIndex,
            template?.SourceEntityId ?? 0,
            logEvent.SourceName,
            template?.ActionId ?? 0,
            logEvent.ActionName,
            template?.ActionIconId ?? 0,
            DeathEventKind.Damage,
            logEvent.Amount,
            0,
            0,
            death.MaxHp,
            template?.DamageType ?? DamageType.Unknown,
            template?.Critical ?? false,
            template?.DirectHit ?? false,
            template?.Blocked ?? false,
            template?.Parried ?? false,
            template?.Detail ?? "Combat log confirmation.",
            template?.Statuses ?? death.StatusesAtDeath,
            template?.SourceStatuses ?? [])
        {
            EventIdentity = $"log:{logEvent.SeenAtUtc.Ticks}:{logIndex}:{logEvent.ActionName}:{logEvent.Amount}",
            EventOrdinal = template?.EventOrdinal ?? 0,
            HpSource = CombatEventHpSource.NoPreHitSample,
        };
    }

    private static IReadOnlyList<FatalEventGroup> GetFatalEventGroupsFromRecentEvents(
        PartyDeathRecord death,
        HpHistorySnapshot? lastAliveSnapshot)
    {
        var candidates = death.RecentEvents
            .Where(IsFatalEvent)
            .Where(combatEvent => combatEvent.SeenAtUtc >= death.SeenAtUtc - FatalTailLookback)
            .Where(combatEvent => combatEvent.SeenAtUtc <= death.SeenAtUtc + FatalTailForwardBuffer)
            .OrderBy(combatEvent => combatEvent.SeenAtUtc)
            .ThenBy(combatEvent => combatEvent.EventOrdinal)
            .ToList();
        if (candidates.Count == 0)
        {
            return [];
        }

        var fatalTail = SelectFatalTail(candidates, lastAliveSnapshot);
        return fatalTail.Count == 0 ? [] : BuildFatalEventGroups(fatalTail);
    }

    private static IReadOnlyList<CombatEventRecord> SelectFatalTail(
        IReadOnlyList<CombatEventRecord> events,
        HpHistorySnapshot? lastAliveSnapshot)
    {
        if (lastAliveSnapshot is null && !events.Any(HasDirectEventHp))
        {
            return DeduplicateStoredEvents(events);
        }

        var fallbackHpBeforeHit = lastAliveSnapshot is null ? 0 : SnapshotHpForDamageMath(lastAliveSnapshot);
        if (fallbackHpBeforeHit == 0 && !events.Any(HasDirectEventHp))
        {
            return DeduplicateStoredEvents(events);
        }

        var orderedEvents = DeduplicateStoredEvents(events)
            .OrderBy(combatEvent => combatEvent.SeenAtUtc)
            .ThenBy(combatEvent => combatEvent.EventOrdinal)
            .ToList();
        var total = 0UL;
        for (var index = orderedEvents.Count - 1; index >= 0; index--)
        {
            var combatEvent = orderedEvents[index];
            if (combatEvent.Kind != DeathEventKind.Damage || combatEvent.Amount == 0)
            {
                continue;
            }

            total += combatEvent.Amount;
            var hpBeforeHit = HasDirectEventHp(combatEvent)
                ? combatEvent.CurrentHp
                : fallbackHpBeforeHit;
            if (hpBeforeHit > 0 && total >= hpBeforeHit)
            {
                return orderedEvents.Skip(index).ToList();
            }
        }

        return [];
    }

    private static IReadOnlyList<CombatLogEventRecord> SelectFatalTail(
        IReadOnlyList<CombatLogEventRecord> events,
        HpHistorySnapshot? lastAliveSnapshot)
    {
        if (lastAliveSnapshot is null)
        {
            return events;
        }

        var hpBeforeHit = SnapshotHpForDamageMath(lastAliveSnapshot);
        if (hpBeforeHit == 0)
        {
            return events;
        }

        var total = 0UL;
        for (var index = events.Count - 1; index >= 0; index--)
        {
            total += events[index].Amount;
            if (total >= hpBeforeHit)
            {
                return events.Skip(index).ToList();
            }
        }

        return [];
    }

    private static IReadOnlyList<FatalEventGroup> BuildFatalEventGroups(IReadOnlyList<CombatEventRecord> events)
    {
        return events
            .GroupBy(BuildFatalEventGroupKey)
            .Select(group => new FatalEventGroup(group
                .OrderBy(combatEvent => combatEvent.SeenAtUtc)
                .ThenBy(combatEvent => combatEvent.EventOrdinal)
                .ToList()))
            .OrderBy(group => group.DisplayEvent.SeenAtUtc)
            .ThenBy(group => group.DisplayEvent.EventOrdinal)
            .ToList();
    }

    private static (string SourceName, uint ActionId, string ActionName, DeathEventKind Kind) BuildFatalEventGroupKey(CombatEventRecord combatEvent)
    {
        return (
            combatEvent.SourceName,
            combatEvent.ActionId,
            combatEvent.ActionName,
            combatEvent.Kind);
    }

    private static bool IsDeathRelevantLeadUpEvent(CombatEventRecord combatEvent)
    {
        return combatEvent.Kind switch
        {
            DeathEventKind.Damage => combatEvent.Amount > 0,
            DeathEventKind.Heal => combatEvent.Amount > 0,
            DeathEventKind.Miss or DeathEventKind.Invulnerable or DeathEventKind.Status => true,
            _ => false,
        };
    }

    private static bool EventTouchesLeadUpWindow(
        CombatEventRecord combatEvent,
        DateTime cutoff,
        DateTime anchorSeenAtUtc)
    {
        return IsTimestampInsideLeadUpWindow(combatEvent.SeenAtUtc, cutoff, anchorSeenAtUtc) ||
            combatEvent.ResultSeenAtUtc is { } resultSeenAtUtc &&
            IsTimestampInsideLeadUpWindow(resultSeenAtUtc, cutoff, anchorSeenAtUtc);
    }

    private static bool IsTimestampInsideLeadUpWindow(DateTime seenAtUtc, DateTime cutoff, DateTime anchorSeenAtUtc)
    {
        return seenAtUtc >= cutoff && seenAtUtc <= anchorSeenAtUtc;
    }

    private static ulong? GetIncomingDamageAmount(IReadOnlyList<CombatEventRecord> events)
    {
        var total = events
            .Where(combatEvent => combatEvent.Kind == DeathEventKind.Damage && combatEvent.Amount > 0)
            .Aggregate(0UL, (sum, combatEvent) => sum + combatEvent.Amount);
        return total == 0 ? null : total;
    }

    private static ulong SnapshotHpForDamageMath(HpHistorySnapshot snapshot)
    {
        return snapshot.CurrentHp;
    }
}
