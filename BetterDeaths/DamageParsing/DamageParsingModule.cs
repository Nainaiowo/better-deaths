namespace BetterDeaths.DamageParsing;

using System;
using System.Collections.Generic;
using System.Linq;

internal sealed class DamageParsingModule
{
    private static readonly TimeSpan DeferredPeriodicTickDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan PreEncounterRetention = TimeSpan.FromSeconds(2);
    private readonly object syncRoot = new();
    private readonly DirectDamageParser parser = new();
    private readonly PeriodicDamageTracker periodicDamageTracker = new();
    private readonly List<ParsedDamageEvent> events = [];
    private readonly HashSet<string> eventIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MutableDamageSource> sources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MutableDamageTarget> targets = new(StringComparer.Ordinal);
    private readonly List<PendingPeriodicTick> pendingPeriodicTicks = [];
    private readonly List<StagedDamageBatch> stagedDamageBatches = [];
    private DateTime? startedAtUtc;
    private DateTime? combatStartedAtUtc;
    private DateTime? latestEventAtUtc;
    private DateTime? latestPreEncounterActivityAtUtc;
    private bool combatActive;
    private bool usesExplicitCombatLifecycle;
    private int packetCount;
    private int duplicateEventCount;
    private DamageEncounterSnapshot? lastEncounter;
    private DamageEncounterSnapshot? cachedCurrentEncounter;
    private long mutationRevision;
    private long cachedCurrentEncounterRevision = -1;
    private long cachedCurrentEncounterTimeBucket = -1;

    public Action<IReadOnlyList<ParsedDamageEvent>>? PeriodicEventsResolved { get; set; }

    public DamageEncounterSnapshot? LastEncounter
    {
        get
        {
            lock (syncRoot)
            {
                return lastEncounter;
            }
        }
    }

    public IReadOnlyList<ParsedDamageEvent> Process(
        DamageActionPacket packet,
        bool allowAutomaticEncounterStart = true)
    {
        lock (syncRoot)
        {
            usesExplicitCombatLifecycle |= !allowAutomaticEncounterStart;
            FlushPendingPeriodicTicksCore(packet.SeenAtUtc, force: false);
            PrunePreEncounterState(packet.SeenAtUtc);
            var parsed = parser.Parse(packet)
                .Select(periodicDamageTracker.ResolveReactiveDamage)
                .ToList();
            if (parsed.Count > 0)
            {
                RecordParsedEvents(parsed, 1, allowAutomaticEncounterStart);
            }

            foreach (var application in packet.StatusApplications)
            {
                ObserveStatusCore(application);
            }

            return parsed;
        }
    }

    public void ObserveStatus(DamageStatusApplication application)
    {
        lock (syncRoot)
        {
            FlushPendingPeriodicTicksCore(application.SeenAtUtc, force: false);
            PrunePreEncounterState(application.SeenAtUtc);
            ObserveStatusCore(application);
        }
    }

    public void RefreshStatus(uint targetEntityId, uint statusId, DateTime seenAtUtc)
    {
        lock (syncRoot)
        {
            FlushPendingPeriodicTicksCore(seenAtUtc, force: false);
            PrunePreEncounterState(seenAtUtc);
            periodicDamageTracker.Refresh(targetEntityId, statusId, seenAtUtc);
            MarkPreEncounterActivity(seenAtUtc);
        }
    }

    public IReadOnlyList<ParsedDamageEvent> ProcessPeriodicTick(
        PeriodicDamageTick tick,
        bool allowAutomaticEncounterStart = true)
    {
        lock (syncRoot)
        {
            usesExplicitCombatLifecycle |= !allowAutomaticEncounterStart;
            FlushPendingPeriodicTicksCore(tick.SeenAtUtc, force: false);
            PrunePreEncounterState(tick.SeenAtUtc);
            if (tick.Amount == 0)
            {
                return [];
            }

            if (tick.StatusId != 0)
            {
                periodicDamageTracker.ConfirmGroundDamageStatus(tick.StatusId);
            }

            // Periodic packets can arrive beside the status update needed to resolve them.
            var normalizedTick = tick.StatusId == 0 ? tick with { Source = null } : tick;
            pendingPeriodicTicks.Add(new PendingPeriodicTick(
                normalizedTick,
                allowAutomaticEncounterStart));
            MarkPreEncounterActivity(tick.SeenAtUtc);
            return [];
        }
    }

    public void SetCombatActive(bool active, DateTime seenAtUtc)
    {
        lock (syncRoot)
        {
            usesExplicitCombatLifecycle = true;
            if (active)
            {
                PrunePreEncounterState(seenAtUtc);
                if (!combatActive)
                {
                    combatStartedAtUtc = seenAtUtc;
                    cachedCurrentEncounterTimeBucket = -1;
                }

                combatActive = true;
                ActivateStagedEncounter();
                FlushPendingPeriodicTicksCore(seenAtUtc, force: false);
                return;
            }

            if (combatActive)
            {
                cachedCurrentEncounterTimeBucket = -1;
            }

            combatActive = false;
            if (startedAtUtc is null)
            {
                combatStartedAtUtc = null;
            }

            PrunePreEncounterState(seenAtUtc);
        }
    }

    public IReadOnlyList<ParsedDamageEvent> FlushPendingPeriodicTicks(DateTime nowUtc, bool force = false)
    {
        lock (syncRoot)
        {
            return FlushPendingPeriodicTicksCore(nowUtc, force);
        }
    }

    public DamageEncounterSnapshot? GetCurrentEncounter()
    {
        return GetCurrentEncounter(null);
    }

    internal DamageEncounterSnapshot? GetCurrentEncounter(DateTime? nowUtc)
    {
        lock (syncRoot)
        {
            var currentTime = nowUtc ?? DateTime.UtcNow;
            FlushPendingPeriodicTicksCore(currentTime, force: false);
            if (startedAtUtc is null || latestEventAtUtc is null)
            {
                return null;
            }

            var snapshotAtUtc = usesExplicitCombatLifecycle && combatActive
                ? currentTime > latestEventAtUtc.Value ? currentTime : latestEventAtUtc.Value
                : latestEventAtUtc.Value;
            var timeBucket = snapshotAtUtc.Ticks / TimeSpan.TicksPerSecond;
            if (cachedCurrentEncounter is not null &&
                cachedCurrentEncounterRevision == mutationRevision &&
                cachedCurrentEncounterTimeBucket == timeBucket)
            {
                return cachedCurrentEncounter;
            }

            cachedCurrentEncounter = BuildSnapshot(snapshotAtUtc, null, string.Empty, includeEvents: false);
            cachedCurrentEncounterRevision = mutationRevision;
            cachedCurrentEncounterTimeBucket = timeBucket;
            return cachedCurrentEncounter;
        }
    }

    public DamageEncounterSnapshot? EndEncounter(DateTime endedAtUtc, string reason)
    {
        lock (syncRoot)
        {
            FlushPendingPeriodicTicksCore(endedAtUtc, force: true);
            if (startedAtUtc is null || latestEventAtUtc is null)
            {
                ClearCurrentEncounter();
                return null;
            }

            var effectiveEnd = endedAtUtc > latestEventAtUtc.Value
                ? endedAtUtc
                : latestEventAtUtc.Value;
            lastEncounter = BuildSnapshot(effectiveEnd, effectiveEnd, reason, includeEvents: true);
            ClearCurrentEncounter();
            return lastEncounter;
        }
    }

    private DamageEncounterSnapshot BuildSnapshot(
        DateTime snapshotAtUtc,
        DateTime? endedAtUtc,
        string reason,
        bool includeEvents)
    {
        IReadOnlyList<ParsedDamageEvent> snapshotEvents = includeEvents
            ? events
                .OrderBy(entry => entry.SeenAtUtc)
                .ThenBy(entry => entry.PacketSequence)
                .ThenBy(entry => entry.TargetIndex)
                .ThenBy(entry => entry.EffectIndex)
                .ToList()
            : [];
        var sourceSnapshots = sources.Values
            .Select(source => source.ToSnapshot())
            .OrderByDescending(source => source.TotalDamage)
            .ThenBy(source => source.Source.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var targetSnapshots = targets.Values
            .Select(target => target.ToSnapshot())
            .OrderByDescending(target => target.TotalDamage)
            .ThenBy(target => target.Target.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var snapshot = new DamageEncounterSnapshot(
            startedAtUtc!.Value,
            snapshotAtUtc,
            endedAtUtc,
            reason,
            sourceSnapshots.Aggregate(0UL, (total, source) => total + source.TotalDamage),
            packetCount,
            duplicateEventCount,
            snapshotEvents,
            sourceSnapshots,
            targetSnapshots);
        return snapshot with
        {
            EstimatedDamage = sourceSnapshots.Aggregate(0UL, (total, source) => total + source.EstimatedDamage),
            UnattributedDamage = sourceSnapshots.Aggregate(0UL, (total, source) => total + source.UnattributedDamage),
            ExactDamage = sourceSnapshots.Aggregate(0UL, (total, source) =>
                total + source.TotalDamage - source.EstimatedDamage - source.UnattributedDamage),
        };
    }

    private void ClearCurrentEncounter()
    {
        events.Clear();
        eventIds.Clear();
        sources.Clear();
        targets.Clear();
        pendingPeriodicTicks.Clear();
        stagedDamageBatches.Clear();
        periodicDamageTracker.Clear();
        startedAtUtc = null;
        combatStartedAtUtc = null;
        latestEventAtUtc = null;
        latestPreEncounterActivityAtUtc = null;
        combatActive = false;
        packetCount = 0;
        duplicateEventCount = 0;
        cachedCurrentEncounter = null;
        cachedCurrentEncounterRevision = -1;
        cachedCurrentEncounterTimeBucket = -1;
    }

    private IReadOnlyList<ParsedDamageEvent> FlushPendingPeriodicTicksCore(
        DateTime nowUtc,
        bool force)
    {
        if (pendingPeriodicTicks.Count == 0)
        {
            periodicDamageTracker.Advance(nowUtc);
            PrunePreEncounterState(nowUtc);
            return [];
        }

        var ready = pendingPeriodicTicks
            .Where(entry => force ||
                nowUtc - entry.Tick.SeenAtUtc >= DeferredPeriodicTickDelay)
            .OrderBy(entry => entry.Tick.SeenAtUtc)
            .ThenBy(entry => entry.Tick.PacketSequence)
            .ToList();
        if (ready.Count == 0)
        {
            return [];
        }

        foreach (var entry in ready)
        {
            pendingPeriodicTicks.Remove(entry);
        }

        var flushed = new List<ParsedDamageEvent>();
        foreach (var entry in ready)
        {
            flushed.AddRange(ProcessPeriodicTickCore(entry));
        }

        if (pendingPeriodicTicks.Count == 0)
        {
            periodicDamageTracker.Advance(nowUtc);
        }

        PrunePreEncounterState(nowUtc);
        return flushed;
    }

    private IReadOnlyList<ParsedDamageEvent> ProcessPeriodicTickCore(PendingPeriodicTick entry)
    {
        var parsed = periodicDamageTracker.Process(entry.Tick);
        if (parsed.Count == 0)
        {
            return parsed;
        }

        RecordParsedEvents(parsed, 1, entry.AllowAutomaticEncounterStart);
        PeriodicEventsResolved?.Invoke(parsed);
        return parsed;
    }

    private void ObserveStatusCore(DamageStatusApplication application)
    {
        periodicDamageTracker.Observe(application);
        MarkPreEncounterActivity(application.SeenAtUtc);
    }

    private void RecordParsedEvents(
        IReadOnlyList<ParsedDamageEvent> parsed,
        int parsedPacketCount,
        bool allowAutomaticEncounterStart)
    {
        if (startedAtUtc is not null || combatActive)
        {
            packetCount += parsedPacketCount;
            AddEvents(parsed);
            return;
        }

        stagedDamageBatches.Add(new StagedDamageBatch(
            parsed.Min(entry => entry.SeenAtUtc),
            parsedPacketCount,
            parsed.ToList()));
        MarkPreEncounterActivity(parsed.Max(entry => entry.SeenAtUtc));
        if (allowAutomaticEncounterStart && parsed.Any(IsEncounterStartingDamage))
        {
            combatActive = true;
            ActivateStagedEncounter();
        }
    }

    private void ActivateStagedEncounter()
    {
        if (stagedDamageBatches.Count == 0)
        {
            return;
        }

        foreach (var batch in stagedDamageBatches.OrderBy(batch => batch.SeenAtUtc))
        {
            packetCount += batch.PacketCount;
            AddEvents(batch.Events);
        }

        stagedDamageBatches.Clear();
        latestPreEncounterActivityAtUtc = null;
    }

    private void PrunePreEncounterState(DateTime nowUtc)
    {
        if (!usesExplicitCombatLifecycle || startedAtUtc is not null || combatActive)
        {
            return;
        }

        var cutoff = nowUtc - PreEncounterRetention;
        stagedDamageBatches.RemoveAll(batch => batch.SeenAtUtc < cutoff);
        if (latestPreEncounterActivityAtUtc is not { } latestActivity ||
            latestActivity >= cutoff)
        {
            return;
        }

        pendingPeriodicTicks.RemoveAll(entry => entry.Tick.SeenAtUtc < cutoff);
        stagedDamageBatches.Clear();
        periodicDamageTracker.Clear();
        latestPreEncounterActivityAtUtc = null;
    }

    private void MarkPreEncounterActivity(DateTime seenAtUtc)
    {
        if (startedAtUtc is not null || combatActive)
        {
            return;
        }

        latestPreEncounterActivityAtUtc = latestPreEncounterActivityAtUtc is null ||
            seenAtUtc > latestPreEncounterActivityAtUtc
                ? seenAtUtc
                : latestPreEncounterActivityAtUtc;
    }

    private static bool IsEncounterStartingDamage(ParsedDamageEvent damageEvent)
    {
        if (damageEvent.Outcome != DamageEventOutcome.Damage || damageEvent.Amount == 0)
        {
            return false;
        }

        var attributedSource = damageEvent.AttributedSource ?? damageEvent.Source;
        return attributedSource.IsPartyMember ||
            attributedSource.IsPlayer ||
            attributedSource.IsLimitBreak ||
            damageEvent.Target.IsPartyMember;
    }

    private void AddEvents(IEnumerable<ParsedDamageEvent> parsed)
    {
        foreach (var damageEvent in parsed)
        {
            if (!eventIds.Add(damageEvent.EventId))
            {
                duplicateEventCount++;
                mutationRevision++;
                continue;
            }

            var eventStartAtUtc = combatStartedAtUtc is { } combatStart && combatStart < damageEvent.SeenAtUtc
                ? combatStart
                : damageEvent.SeenAtUtc;
            startedAtUtc = startedAtUtc is null || eventStartAtUtc < startedAtUtc
                ? eventStartAtUtc
                : startedAtUtc;
            latestEventAtUtc = latestEventAtUtc is null || damageEvent.SeenAtUtc > latestEventAtUtc
                ? damageEvent.SeenAtUtc
                : latestEventAtUtc;
            events.Add(damageEvent);

            var attributedSource = damageEvent.AttributedSource ?? damageEvent.Source;
            var sourceKey = GetActorKey(attributedSource);
            if (!sources.TryGetValue(sourceKey, out var source))
            {
                source = new MutableDamageSource(attributedSource);
                sources[sourceKey] = source;
            }

            source.Add(damageEvent);

            var targetKey = GetActorKey(damageEvent.Target);
            if (!targets.TryGetValue(targetKey, out var target))
            {
                target = new MutableDamageTarget(damageEvent.Target);
                targets[targetKey] = target;
            }

            target.Add(damageEvent);
            mutationRevision++;
        }
    }

    private static string GetActorKey(DamageActorIdentity actor)
    {
        return actor.EntityId != 0
            ? $"entity:{actor.EntityId:X8}"
            : $"name:{actor.Name}";
    }

    private sealed record PendingPeriodicTick(
        PeriodicDamageTick Tick,
        bool AllowAutomaticEncounterStart);

    private sealed record StagedDamageBatch(
        DateTime SeenAtUtc,
        int PacketCount,
        IReadOnlyList<ParsedDamageEvent> Events);

    private sealed class MutableDamageSource
    {
        private readonly Dictionary<(uint ActionId, string ActionName), MutableDamageTotals> actions = [];
        private readonly MutableDamageTotals totals = new();
        private DamageActorIdentity source;

        public MutableDamageSource(DamageActorIdentity source)
        {
            this.source = source;
        }

        public void Add(ParsedDamageEvent damageEvent)
        {
            var candidateSource = damageEvent.AttributedSource ?? damageEvent.Source;
            if (GetIdentityQuality(candidateSource) > GetIdentityQuality(source))
            {
                source = candidateSource;
            }

            totals.Add(damageEvent);
            var actionKey = (damageEvent.ActionId, damageEvent.ActionName);
            if (!actions.TryGetValue(actionKey, out var action))
            {
                action = new MutableDamageTotals();
                actions[actionKey] = action;
            }

            action.Add(damageEvent);
        }

        public DamageSourceSummary ToSnapshot()
        {
            var actionSnapshots = actions
                .Select(entry => entry.Value.ToActionSnapshot(entry.Key.ActionId, entry.Key.ActionName))
                .OrderByDescending(action => action.TotalDamage)
                .ThenBy(action => action.ActionName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return totals.ToSourceSnapshot(source, actionSnapshots);
        }
    }

    private static int GetIdentityQuality(DamageActorIdentity actor)
    {
        var score = actor.IsLimitBreak ? 32 : 0;
        score += actor.IsPartyMember ? 16 : 0;
        score += actor.IsPlayer ? 8 : 0;
        score += actor.ClassJobId != 0 ? 4 : 0;
        score += actor.BaseId != 0 ? 2 : 0;
        score += IsGenericActorName(actor.Name) ? 0 : 1;
        return score;
    }

    private static bool IsGenericActorName(string name)
    {
        return string.IsNullOrWhiteSpace(name) ||
            name.StartsWith("Entity ", StringComparison.Ordinal) ||
            name.StartsWith("Unknown ", StringComparison.Ordinal);
    }

    private sealed class MutableDamageTarget
    {
        private readonly MutableDamageTotals totals = new();
        private DamageActorIdentity target;

        public MutableDamageTarget(DamageActorIdentity target)
        {
            this.target = target;
        }

        public void Add(ParsedDamageEvent damageEvent)
        {
            if (GetIdentityQuality(damageEvent.Target) > GetIdentityQuality(target))
            {
                target = damageEvent.Target;
            }

            totals.Add(damageEvent);
        }

        public DamageTargetSummary ToSnapshot()
        {
            return totals.ToTargetSnapshot(target);
        }
    }

    private sealed class MutableDamageTotals
    {
        private ulong totalDamage;
        private int swings;
        private int hits;
        private int misses;
        private int resists;
        private int invulnerableHits;
        private int criticalHits;
        private int directHits;
        private int criticalDirectHits;
        private int blockedHits;
        private int parriedHits;
        private ulong periodicDamage;
        private ulong estimatedDamage;
        private ulong unattributedDamage;
        private int periodicHits;
        private bool isAutoAttack;
        private uint actionCategoryId;

        public void Add(ParsedDamageEvent damageEvent)
        {
            swings++;
            isAutoAttack |= damageEvent.IsAutoAttack;
            actionCategoryId = actionCategoryId == 0 ? damageEvent.ActionCategoryId : actionCategoryId;
            switch (damageEvent.Outcome)
            {
                case DamageEventOutcome.Damage:
                    totalDamage += damageEvent.Amount;
                    periodicDamage += damageEvent.IsPeriodic ? damageEvent.Amount : 0;
                    periodicHits += damageEvent.IsPeriodic ? 1 : 0;
                    estimatedDamage += damageEvent.AttributionQuality == DamageAttributionQuality.Estimated
                        ? damageEvent.Amount
                        : 0;
                    unattributedDamage += damageEvent.AttributionQuality == DamageAttributionQuality.Unattributed
                        ? damageEvent.Amount
                        : 0;
                    hits++;
                    criticalHits += damageEvent.Critical ? 1 : 0;
                    directHits += damageEvent.DirectHit ? 1 : 0;
                    criticalDirectHits += damageEvent.Critical && damageEvent.DirectHit ? 1 : 0;
                    blockedHits += damageEvent.Blocked ? 1 : 0;
                    parriedHits += damageEvent.Parried ? 1 : 0;
                    break;
                case DamageEventOutcome.Miss:
                    misses++;
                    break;
                case DamageEventOutcome.Resisted:
                    resists++;
                    break;
                case DamageEventOutcome.Invulnerable:
                    invulnerableHits++;
                    break;
            }
        }

        public DamageActionSummary ToActionSnapshot(uint actionId, string actionName)
        {
            return new DamageActionSummary(
                actionId,
                actionName,
                totalDamage,
                swings,
                hits,
                misses,
                resists,
                invulnerableHits,
                criticalHits,
                directHits,
                criticalDirectHits,
                blockedHits,
                parriedHits)
            {
                IsAutoAttack = isAutoAttack,
                ActionCategoryId = actionCategoryId,
                PeriodicDamage = periodicDamage,
                EstimatedDamage = estimatedDamage,
                UnattributedDamage = unattributedDamage,
                PeriodicHits = periodicHits,
            };
        }

        public DamageSourceSummary ToSourceSnapshot(
            DamageActorIdentity source,
            IReadOnlyList<DamageActionSummary> actionSnapshots)
        {
            return new DamageSourceSummary(
                source,
                totalDamage,
                swings,
                hits,
                misses,
                resists,
                invulnerableHits,
                criticalHits,
                directHits,
                criticalDirectHits,
                blockedHits,
                parriedHits,
                actionSnapshots)
            {
                PeriodicDamage = periodicDamage,
                EstimatedDamage = estimatedDamage,
                UnattributedDamage = unattributedDamage,
                PeriodicHits = periodicHits,
            };
        }

        public DamageTargetSummary ToTargetSnapshot(DamageActorIdentity target)
        {
            return new DamageTargetSummary(
                target,
                totalDamage,
                swings,
                hits,
                misses,
                resists,
                invulnerableHits);
        }
    }
}
