namespace BetterDeaths.DamageParsing;

using System;
using System.Collections.Generic;
using System.Linq;

internal sealed class DamageParsingModule
{
    private static readonly TimeSpan DeferredPeriodicTickDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan PreEncounterRetention = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan OffensiveCastRetention = TimeSpan.FromSeconds(5);
    private readonly object syncRoot = new();
    private readonly DirectDamageParser parser = new();
    private readonly EffectiveDamageResolver effectiveDamageResolver = new();
    private readonly PeriodicDamageTracker periodicDamageTracker = new();
    private readonly RaidBuffTracker raidBuffTracker = new();
    private readonly List<ParsedDamageEvent> events = [];
    private readonly HashSet<string> eventIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> eventIndices = new(StringComparer.Ordinal);
    private readonly Dictionary<uint, DamageActorIdentity> knownActors = [];
    private readonly Dictionary<string, MutableDamageSource> sources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MutableActivityDuration> sourceActivities = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MutableDamageTarget> targets = new(StringComparer.Ordinal);
    private readonly List<PendingPeriodicTick> pendingPeriodicTicks = [];
    private readonly List<StagedDamageBatch> stagedDamageBatches = [];
    private DateTime? startedAtUtc;
    private DateTime? latestEventAtUtc;
    private DateTime? meterStartedAtUtc;
    private DateTime? latestMeterDamageAtUtc;
    private DateTime? pendingOffensiveCastStartedAtUtc;
    private DateTime? latestPreEncounterActivityAtUtc;
    private bool combatActive;
    private bool usesExplicitCombatLifecycle;
    private int activitySegmentId;
    private int packetCount;
    private int duplicateEventCount;
    private DamageEncounterSnapshot? lastEncounter;
    private DamageEncounterSnapshot? cachedCurrentEncounter;
    private long mutationRevision;
    private long cachedCurrentEncounterRevision = -1;
    private long cachedCurrentEncounterTimeBucket = -1;

    public Action<IReadOnlyList<ParsedDamageEvent>>? PeriodicEventsResolved { get; set; }

    public void ResetCalibration()
    {
        lock (syncRoot)
        {
            periodicDamageTracker.ClearCalibration();
        }
    }

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
            packet = NormalizeActors(packet);
            usesExplicitCombatLifecycle |= !allowAutomaticEncounterStart;
            FlushPendingPeriodicTicksCore(packet.SeenAtUtc, force: false);
            PrunePreEncounterState(packet.SeenAtUtc);
            var decoded = parser.Parse(packet);
            var unseen = FilterNewDirectEvents(decoded);
            if (decoded.Count > 0 && unseen.Count == 0)
            {
                if (startedAtUtc is not null || combatActive)
                {
                    packetCount++;
                }
                return [];
            }

            var parsed = unseen
                .Select(periodicDamageTracker.ResolveReactiveDamage)
                .Select(raidBuffTracker.ApplyFallback)
                .Select(ApplyMeterEligibility)
                .ToList();
            if (parsed.Count > 0)
            {
                periodicDamageTracker.ObserveDirectDamage(parsed);
                parsed = effectiveDamageResolver.ObserveDirect(parsed).ToList();
                RecordParsedEvents(parsed, 1, allowAutomaticEncounterStart);
            }

            foreach (var application in packet.StatusApplications)
            {
                ObserveStatusCore(application);
            }

            ObserveOutgoingActivity(packet);

            return parsed;
        }
    }

    public void ObserveStatus(DamageStatusApplication application)
    {
        lock (syncRoot)
        {
            application = NormalizeActors(application);
            FlushPendingPeriodicTicksCore(application.SeenAtUtc, force: false);
            PrunePreEncounterState(application.SeenAtUtc);
            ObserveStatusCore(application);
        }
    }

    public void ObserveEffectResult(DamageEffectResult result)
    {
        lock (syncRoot)
        {
            result = result with { Target = RememberActor(result.Target) };
            FlushPendingPeriodicTicksCore(result.SeenAtUtc, force: false);
            PrunePreEncounterState(result.SeenAtUtc);
            ApplyEventReplacements(effectiveDamageResolver.ObserveEffectResult(result));
        }
    }

    public void RefreshStatus(uint targetEntityId, uint statusId, DateTime seenAtUtc)
    {
        lock (syncRoot)
        {
            FlushPendingPeriodicTicksCore(seenAtUtc, force: false);
            PrunePreEncounterState(seenAtUtc);
            periodicDamageTracker.Refresh(targetEntityId, statusId, seenAtUtc);
            raidBuffTracker.Refresh(targetEntityId, statusId, seenAtUtc);
            MarkPreEncounterActivity(seenAtUtc);
        }
    }

    public IReadOnlyList<ParsedDamageEvent> ProcessPeriodicTick(
        PeriodicDamageTick tick,
        bool allowAutomaticEncounterStart = true)
    {
        lock (syncRoot)
        {
            tick = NormalizeActors(tick);
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

    public void RecordDeath(DamageActorIdentity actor)
    {
        lock (syncRoot)
        {
            if (startedAtUtc is null)
            {
                return;
            }

            actor = RememberActor(actor);
            var sourceKey = GetActorKey(actor);
            if (!sources.TryGetValue(sourceKey, out var source))
            {
                source = new MutableDamageSource(actor);
                sources[sourceKey] = source;
            }

            source.RecordDeath(actor);
            mutationRevision++;
        }
    }

    public void ObserveOffensiveCast(DateTime? castStartedAtUtc, DateTime observedAtUtc)
    {
        lock (syncRoot)
        {
            PrunePendingOffensiveCast(observedAtUtc);
            if (meterStartedAtUtc is not null ||
                castStartedAtUtc is null ||
                castStartedAtUtc.Value > observedAtUtc ||
                observedAtUtc - castStartedAtUtc.Value > OffensiveCastRetention)
            {
                pendingOffensiveCastStartedAtUtc = null;
                return;
            }

            pendingOffensiveCastStartedAtUtc = castStartedAtUtc.Value;
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
                    cachedCurrentEncounterTimeBucket = -1;
                    activitySegmentId++;
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

            var snapshotAtUtc = latestEventAtUtc.Value;
            var meterSnapshotAtUtc = latestMeterDamageAtUtc ?? snapshotAtUtc;
            var timeBucket = meterSnapshotAtUtc.Ticks / TimeSpan.TicksPerSecond;
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

            var effectiveEnd = latestEventAtUtc.Value;
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
        var sourceSnapshots = sources
            .Select(entry => entry.Value.ToSnapshot(sourceActivities.GetValueOrDefault(entry.Key)))
            .OrderByDescending(source => source.EffectiveMeterDamage)
            .ThenBy(source => source.Source.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var raidAdjustments = RaidDamageCalculator.Calculate(
            events,
            sourceSnapshots,
            damageEvent => damageEvent.MeterEligibility != DamageMeterEligibility.FriendlyTarget
                ? damageEvent.RawMeterAmount
                : 0.0);
        var meterRaidAdjustments = RaidDamageCalculator.Calculate(
            events,
            sourceSnapshots,
            damageEvent => damageEvent.MeterAggregateAmount);
        sourceSnapshots = sourceSnapshots
            .Select(source => ApplyRaidAdjustment(source, raidAdjustments, meterRaidAdjustments))
            .ToList();
        foreach (var adjustment in raidAdjustments.Values.Where(adjustment =>
                     sourceSnapshots.All(source =>
                         !string.Equals(
                             RaidDamageCalculator.GetActorKey(source.Source),
                             RaidDamageCalculator.GetActorKey(adjustment.Source),
                             StringComparison.Ordinal))))
        {
            meterRaidAdjustments.TryGetValue(
                RaidDamageCalculator.GetActorKey(adjustment.Source),
                out var meterAdjustment);
            sourceSnapshots.Add(CreateRaidOnlySource(adjustment, meterAdjustment));
        }

        sourceSnapshots = sourceSnapshots
            .OrderByDescending(source => source.EffectiveMeterDamage)
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
            MeterStartedAtUtc = meterStartedAtUtc,
            MeterSnapshotAtUtc = latestMeterDamageAtUtc,
            MeterEndedAtUtc = endedAtUtc is null
                ? null
                : latestMeterDamageAtUtc,
            MeterDamage = sourceSnapshots.Sum(source => source.EffectiveMeterDamage),
            RawMeterDamage = sourceSnapshots.Sum(source => source.ObservedMeterDamage),
            EstimatedDamage = sourceSnapshots.Aggregate(0UL, (total, source) => total + source.EstimatedDamage),
            UnattributedDamage = sourceSnapshots.Aggregate(0UL, (total, source) => total + source.UnattributedDamage),
            ExactDamage = sourceSnapshots.Aggregate(0UL, (total, source) =>
                total + source.TotalDamage - source.EstimatedDamage - source.UnattributedDamage),
            RaidAdjustedDamage = sourceSnapshots.Sum(source => source.RaidAdjustedDamage),
            MeterRaidAdjustedDamage = sourceSnapshots.Sum(source => source.EffectiveMeterRaidAdjustedDamage),
            Diagnostics = BuildDiagnostics(events),
        };
    }

    private static DamageEncounterDiagnostics BuildDiagnostics(
        IReadOnlyList<ParsedDamageEvent> damageEvents)
    {
        var damagingEvents = damageEvents
            .Where(damageEvent =>
                damageEvent.Outcome == DamageEventOutcome.Damage &&
                damageEvent.Amount > 0)
            .ToList();
        var eligibleEvents = damagingEvents
            .Where(damageEvent => damageEvent.MeterEligibility == DamageMeterEligibility.Eligible)
            .ToList();
        var resolution = eligibleEvents
            .GroupBy(damageEvent => damageEvent.ResolutionQuality)
            .OrderBy(group => group.Key)
            .Select(group => new DamageResolutionDiagnostic(
                group.Key,
                group.Count(),
                group.Sum(damageEvent => damageEvent.RawMeterAmount),
                group.Sum(damageEvent => damageEvent.EffectiveMeterAmount)))
            .ToList();
        var eligibility = damagingEvents
            .GroupBy(damageEvent => damageEvent.MeterEligibility)
            .OrderBy(group => group.Key)
            .Select(group => new DamageEligibilityDiagnostic(
                group.Key,
                group.Count(),
                group.Sum(damageEvent => damageEvent.RawMeterAmount),
                group.Sum(damageEvent => damageEvent.EffectiveMeterAmount)))
            .ToList();
        var periodicEvents = eligibleEvents
            .Where(damageEvent => damageEvent.IsPeriodic)
            .ToList();
        var periodicAllocations = periodicEvents
            .GroupBy(damageEvent => new
            {
                SourceKey = GetActorKey(damageEvent.AttributedSource ?? damageEvent.Source),
                TargetKey = GetActorKey(damageEvent.Target),
                damageEvent.StatusId,
                damageEvent.ActionName,
                damageEvent.PeriodicAllocationBasis,
                damageEvent.PeriodicCandidateCount,
            })
            .Select(group => new PeriodicAllocationDiagnostic(
                group.First().AttributedSource ?? group.First().Source,
                group.First().Target,
                group.Key.StatusId,
                group.Key.ActionName,
                group.Key.PeriodicAllocationBasis,
                group.Key.PeriodicCandidateCount,
                group.Count(),
                group.Average(damageEvent => damageEvent.PeriodicAllocationWeight),
                group.Sum(damageEvent => damageEvent.RawMeterAmount),
                group.Sum(damageEvent => damageEvent.EffectiveMeterAmount))
            {
                IndependentEstimates = group
                    .GroupBy(damageEvent => damageEvent.SimulatedPeriodicAmount is not null
                        ? null : damageEvent.PeriodicEstimateUnavailableReason ?? "Unavailable in captured data")
                    .Select(estimates => new PeriodicEstimateDiagnostic(
                        estimates.Key,
                        estimates.Count(),
                        estimates.Sum(damageEvent => damageEvent.RawMeterAmount),
                        estimates.Key is null ? estimates.Sum(damageEvent => damageEvent.SimulatedPeriodicAmount) : null)
                    {
                        CompatibilityTickCount = estimates.Count(damageEvent => damageEvent.PeriodicCompatibilityEstimate is not null),
                        CompatibilityDamage = estimates.Any(damageEvent => damageEvent.PeriodicCompatibilityEstimate is not null)
                            ? estimates.Sum(damageEvent => damageEvent.PeriodicCompatibilityEstimate?.EstimatedDamage ?? 0) : null,
                    })
                    .OrderBy(diagnostic => diagnostic.UnavailableReason, StringComparer.Ordinal)
                    .ToList(),
            })
            .OrderByDescending(diagnostic => diagnostic.AllocatedDamage)
            .ThenBy(diagnostic => diagnostic.Source.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(diagnostic => diagnostic.StatusName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var periodicTicks = periodicEvents
            .GroupBy(damageEvent => new
            {
                damageEvent.PacketSequence,
                damageEvent.SeenAtUtc,
                TargetKey = GetActorKey(damageEvent.Target),
            })
            .Select(group => new
            {
                Basis = group.Select(damageEvent => damageEvent.PeriodicAllocationBasis).Distinct().Count() == 1
                    ? group.First().PeriodicAllocationBasis
                    : PeriodicAllocationBasis.None,
                CandidateCount = group.Max(damageEvent => damageEvent.PeriodicCandidateCount),
                CombinedDamage = group.Max(damageEvent => (double)damageEvent.PeriodicCombinedAmount),
                AllocatedDamage = group.Sum(damageEvent => damageEvent.RawMeterAmount),
            })
            .GroupBy(tick => new { tick.Basis, tick.CandidateCount })
            .Select(group => new PeriodicTickDiagnostic(
                group.Key.Basis,
                group.Key.CandidateCount,
                group.Count(),
                group.Sum(tick => tick.CombinedDamage),
                group.Sum(tick => tick.AllocatedDamage)))
            .OrderBy(diagnostic => diagnostic.Basis)
            .ThenBy(diagnostic => diagnostic.CandidateCount)
            .ToList();
        var directEvents = eligibleEvents
            .Where(damageEvent => !damageEvent.IsPeriodic)
            .ToList();
        var targetDiagnostics = eligibleEvents
            .GroupBy(damageEvent => GetActorKey(damageEvent.Target))
            .Select(group =>
            {
                var direct = group.Where(damageEvent => !damageEvent.IsPeriodic).ToList();
                var periodic = group.Where(damageEvent => damageEvent.IsPeriodic).ToList();
                return new DamageTargetDiagnostic(
                    group.First().Target,
                    group.Count(),
                    group.Sum(damageEvent => damageEvent.RawMeterAmount),
                    group.Sum(damageEvent => damageEvent.EffectiveMeterAmount),
                    direct.Sum(damageEvent => damageEvent.RawMeterAmount),
                    direct.Sum(damageEvent => damageEvent.EffectiveMeterAmount),
                    periodic.Sum(damageEvent => damageEvent.RawMeterAmount),
                    periodic.Sum(damageEvent => damageEvent.EffectiveMeterAmount),
                    group.Where(damageEvent =>
                            damageEvent.ResolutionQuality == DamageResolutionQuality.Unresolved)
                        .Sum(damageEvent => damageEvent.RawMeterAmount),
                    group.Where(damageEvent =>
                            damageEvent.ResolutionQuality == DamageResolutionQuality.KnownZeroHp)
                        .Sum(damageEvent => damageEvent.RawMeterAmount));
            })
            .OrderByDescending(diagnostic => diagnostic.EffectiveDamage)
            .ThenBy(diagnostic => diagnostic.Target.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new DamageEncounterDiagnostics(
            damageEvents.Count,
            eligibleEvents.Sum(damageEvent => damageEvent.RawMeterAmount),
            eligibleEvents.Sum(damageEvent => damageEvent.EffectiveMeterAmount),
            directEvents.Sum(damageEvent => damageEvent.RawMeterAmount),
            directEvents.Sum(damageEvent => damageEvent.EffectiveMeterAmount),
            periodicEvents.Sum(damageEvent => damageEvent.RawMeterAmount),
            periodicEvents.Sum(damageEvent => damageEvent.EffectiveMeterAmount),
            resolution,
            eligibility,
            periodicAllocations,
            periodicTicks)
        {
            Targets = targetDiagnostics,
        };
    }

    private static DamageSourceSummary ApplyRaidAdjustment(
        DamageSourceSummary source,
        IReadOnlyDictionary<string, RaidDamageAdjustment> adjustments,
        IReadOnlyDictionary<string, RaidDamageAdjustment> meterAdjustments)
    {
        var actorKey = RaidDamageCalculator.GetActorKey(source.Source);
        adjustments.TryGetValue(actorKey, out var adjustment);
        meterAdjustments.TryGetValue(actorKey, out var meterAdjustment);

        return source with
        {
            ExternalBuffDamageReceived = adjustment?.ExternalBuffDamageReceived ?? 0.0,
            RaidBuffDamageGiven = adjustment?.RaidBuffDamageGiven ?? 0.0,
            SingleTargetBuffDamageReceived = adjustment?.SingleTargetBuffDamageReceived ?? 0.0,
            MeterExternalBuffDamageReceived = meterAdjustment?.ExternalBuffDamageReceived ?? 0.0,
            MeterRaidBuffDamageGiven = meterAdjustment?.RaidBuffDamageGiven ?? 0.0,
            MeterSingleTargetBuffDamageReceived = meterAdjustment?.SingleTargetBuffDamageReceived ?? 0.0,
            RaidAdjustedDamage = Math.Max(
                0.0,
                source.ObservedMeterDamage - (adjustment?.ExternalBuffDamageReceived ?? 0.0) +
                (adjustment?.RaidBuffDamageGiven ?? 0.0)),
            MeterRaidAdjustedDamage = Math.Max(
                0.0,
                source.EffectiveMeterDamage - (meterAdjustment?.ExternalBuffDamageReceived ?? 0.0) +
                (meterAdjustment?.RaidBuffDamageGiven ?? 0.0)),
        };
    }

    private static DamageSourceSummary CreateRaidOnlySource(
        RaidDamageAdjustment adjustment,
        RaidDamageAdjustment? meterAdjustment)
    {
        return new DamageSourceSummary(
            adjustment.Source,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            [])
        {
            MeterDamage = 0.0,
            RawMeterDamage = 0.0,
            RaidAdjustedDamage = adjustment.RaidBuffDamageGiven,
            MeterRaidAdjustedDamage = meterAdjustment?.RaidBuffDamageGiven ?? adjustment.RaidBuffDamageGiven,
            ExternalBuffDamageReceived = adjustment.ExternalBuffDamageReceived,
            RaidBuffDamageGiven = adjustment.RaidBuffDamageGiven,
            SingleTargetBuffDamageReceived = adjustment.SingleTargetBuffDamageReceived,
            MeterExternalBuffDamageReceived = meterAdjustment?.ExternalBuffDamageReceived ?? 0.0,
            MeterRaidBuffDamageGiven = meterAdjustment?.RaidBuffDamageGiven ?? adjustment.RaidBuffDamageGiven,
            MeterSingleTargetBuffDamageReceived = meterAdjustment?.SingleTargetBuffDamageReceived ??
                adjustment.SingleTargetBuffDamageReceived,
        };
    }

    private void ClearCurrentEncounter()
    {
        events.Clear();
        eventIds.Clear();
        eventIndices.Clear();
        knownActors.Clear();
        sources.Clear();
        sourceActivities.Clear();
        targets.Clear();
        pendingPeriodicTicks.Clear();
        stagedDamageBatches.Clear();
        periodicDamageTracker.Clear(preserveCalibration: true);
        effectiveDamageResolver.Clear();
        raidBuffTracker.Clear();
        startedAtUtc = null;
        latestEventAtUtc = null;
        meterStartedAtUtc = null;
        latestMeterDamageAtUtc = null;
        pendingOffensiveCastStartedAtUtc = null;
        latestPreEncounterActivityAtUtc = null;
        combatActive = false;
        activitySegmentId = 0;
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
        // Target DoTs already carry their application-time effects. Ground ticks
        // keep their existing live-status fallback.
        var parsed = periodicDamageTracker.Process(entry.Tick)
            .Select(damageEvent => entry.Tick.StatusId != 0 ? raidBuffTracker.ApplyFallback(damageEvent) : damageEvent)
            .ToList();
        if (parsed.Count == 0)
        {
            return parsed;
        }

        parsed = effectiveDamageResolver.ResolvePeriodic(entry.Tick, parsed)
            .Select(ApplyMeterEligibility)
            .ToList();
        RecordParsedEvents(parsed, 1, entry.AllowAutomaticEncounterStart);
        PeriodicEventsResolved?.Invoke(parsed);
        return parsed;
    }

    private void ObserveStatusCore(DamageStatusApplication application)
    {
        application = raidBuffTracker.ApplyFallback(application);
        periodicDamageTracker.Observe(application);
        raidBuffTracker.Observe(application);
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
        PrunePendingOffensiveCast(nowUtc);
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
        periodicDamageTracker.Clear(preserveCalibration: true);
        effectiveDamageResolver.Clear();
        raidBuffTracker.Clear();
        latestPreEncounterActivityAtUtc = null;
    }

    private void PrunePendingOffensiveCast(DateTime nowUtc)
    {
        if (pendingOffensiveCastStartedAtUtc is { } castStartedAtUtc &&
            nowUtc - castStartedAtUtc > OffensiveCastRetention)
        {
            pendingOffensiveCastStartedAtUtc = null;
        }
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

    private IReadOnlyList<ParsedDamageEvent> FilterNewDirectEvents(IReadOnlyList<ParsedDamageEvent> decoded)
    {
        var accepted = new List<ParsedDamageEvent>(decoded.Count);
        var packetEventIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var damageEvent in decoded)
        {
            if (eventIds.Contains(damageEvent.EventId) ||
                stagedDamageBatches.Any(batch => batch.Events.Any(entry => entry.EventId == damageEvent.EventId)) ||
                !packetEventIds.Add(damageEvent.EventId))
            {
                duplicateEventCount++;
                mutationRevision++;
                continue;
            }
            accepted.Add(damageEvent);
        }
        return accepted;
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

            startedAtUtc = startedAtUtc is null || damageEvent.SeenAtUtc < startedAtUtc
                ? damageEvent.SeenAtUtc
                : startedAtUtc;
            latestEventAtUtc = latestEventAtUtc is null || damageEvent.SeenAtUtc > latestEventAtUtc
                ? damageEvent.SeenAtUtc
                : latestEventAtUtc;
            eventIndices[damageEvent.EventId] = events.Count;
            events.Add(damageEvent);

            var attributedSource = damageEvent.AttributedSource ?? damageEvent.Source;
            if (IsMeterDamage(damageEvent, attributedSource))
            {
                var meterEventAtUtc = damageEvent.SeenAtUtc;
                latestMeterDamageAtUtc = latestMeterDamageAtUtc is null ||
                    meterEventAtUtc > latestMeterDamageAtUtc
                        ? meterEventAtUtc
                        : latestMeterDamageAtUtc;
                if (meterStartedAtUtc is null)
                {
                    meterStartedAtUtc = ResolveMeterStart(damageEvent, meterEventAtUtc);
                    pendingOffensiveCastStartedAtUtc = null;
                }
                else if (meterEventAtUtc < meterStartedAtUtc)
                {
                    meterStartedAtUtc = meterEventAtUtc;
                }
            }

            var sourceKey = GetActorKey(attributedSource);
            if (!sources.TryGetValue(sourceKey, out var source))
            {
                source = new MutableDamageSource(attributedSource);
                sources[sourceKey] = source;
            }

            var isMeterEligible = damageEvent.MeterEligibility != DamageMeterEligibility.FriendlyTarget;
            source.Add(damageEvent, isMeterEligible, activitySegmentId);

            var targetKey = GetActorKey(damageEvent.Target);
            if (!targets.TryGetValue(targetKey, out var target))
            {
                target = new MutableDamageTarget(damageEvent.Target);
                targets[targetKey] = target;
            }

            target.Add(damageEvent, isMeterEligible, activitySegmentId);
            mutationRevision++;
        }
    }

    private void ObserveOutgoingActivity(DamageActionPacket packet)
    {
        if ((!combatActive && startedAtUtc is null) ||
            !packet.Targets.Any(target => target.Effects.Any(effect => effect.Type is 1 or 2 or 3 or 4 or 5 or 6 or 7 or 74)))
        {
            return;
        }

        var attributedSource = packet.ActionCategoryId == 9
            ? new DamageActorIdentity(0, "Limit Break", 0, string.Empty, false, 0)
            {
                IsLimitBreak = true,
                IsPartyMember = true,
            }
            : packet.Source.IsPet && packet.SourceOwner is not null
                ? packet.SourceOwner
                : packet.Source;
        if (!IsAlliedMeterSource(attributedSource))
        {
            return;
        }

        var sourceKey = GetActorKey(attributedSource);
        if (!sourceActivities.TryGetValue(sourceKey, out var activity))
        {
            activity = new MutableActivityDuration();
            sourceActivities[sourceKey] = activity;
        }

        activity.Observe(packet.SeenAtUtc, activitySegmentId);
        mutationRevision++;
    }

    private void ApplyEventReplacements(IReadOnlyList<ParsedDamageEvent> replacements)
    {
        foreach (var replacementCandidate in replacements)
        {
            var replacement = replacementCandidate;
            if (eventIndices.TryGetValue(replacement.EventId, out var index))
            {
                var previous = events[index];
                replacement = replacement with { MeterEligibility = previous.MeterEligibility };
                events[index] = replacement;
                var attributedSource = previous.AttributedSource ?? previous.Source;
                if (sources.TryGetValue(GetActorKey(attributedSource), out var source))
                {
                    source.ApplyMeterCorrection(previous, replacement);
                }

                mutationRevision++;
                continue;
            }

            foreach (var batch in stagedDamageBatches)
            {
                var stagedIndex = batch.Events.FindIndex(entry =>
                    string.Equals(entry.EventId, replacement.EventId, StringComparison.Ordinal));
                if (stagedIndex < 0)
                {
                    continue;
                }

                batch.Events[stagedIndex] = replacement;
                break;
            }
        }
    }

    private static string GetActorKey(DamageActorIdentity actor)
    {
        return actor.EntityId != 0
            ? $"entity:{actor.EntityId:X8}"
            : $"name:{actor.Name}";
    }

    private DamageActionPacket NormalizeActors(DamageActionPacket packet)
    {
        var sourceOwner = packet.SourceOwner is null
            ? null
            : RememberActor(packet.SourceOwner);
        var source = RememberActor(packet.Source);
        if (source.IsPet && sourceOwner is null && source.OwnerEntityId != 0)
        {
            knownActors.TryGetValue(source.OwnerEntityId, out sourceOwner);
        }

        var sourceStatuses = NormalizeActors(packet.SourceStatuses);
        var targets = packet.Targets
            .Select(target => target with
            {
                Target = RememberActor(target.Target),
                TargetStatuses = NormalizeActors(target.TargetStatuses),
            })
            .ToList();
        var applications = packet.StatusApplications
            .Select(NormalizeActors)
            .ToList();
        return packet with
        {
            Source = source,
            SourceOwner = sourceOwner,
            SourceStatuses = sourceStatuses,
            Targets = targets,
            StatusApplications = applications,
        };
    }

    private DamageStatusApplication NormalizeActors(DamageStatusApplication application)
    {
        return application with
        {
            Target = RememberActor(application.Target),
            Source = RememberActor(application.Source),
            SourceStatuses = NormalizeActors(application.SourceStatuses),
            TargetStatuses = NormalizeActors(application.TargetStatuses),
        };
    }

    private PeriodicDamageTick NormalizeActors(PeriodicDamageTick tick)
    {
        return tick with
        {
            Target = RememberActor(tick.Target),
            Source = tick.Source is null ? null : RememberActor(tick.Source),
        };
    }

    private IReadOnlyList<DamageStatusSnapshot> NormalizeActors(
        IReadOnlyList<DamageStatusSnapshot> statuses)
    {
        return statuses.Count == 0
            ? statuses
            : statuses
                .Select(status => status with { Source = RememberActor(status.Source) })
                .ToList();
    }

    private DamageActorIdentity RememberActor(DamageActorIdentity actor)
    {
        if (actor.EntityId == 0)
        {
            return actor;
        }

        if (!knownActors.TryGetValue(actor.EntityId, out var known))
        {
            knownActors[actor.EntityId] = actor;
            return actor;
        }

        var actorName = IsGenericActorName(actor.Name) && !IsGenericActorName(known.Name)
            ? known.Name
            : actor.Name;
        var ownerEntityId = actor.OwnerEntityId != 0 ? actor.OwnerEntityId : known.OwnerEntityId;
        var ownerName = actor.OwnerEntityId != 0 && !string.IsNullOrWhiteSpace(actor.OwnerName)
            ? actor.OwnerName
            : known.OwnerEntityId == ownerEntityId && !string.IsNullOrWhiteSpace(known.OwnerName)
                ? known.OwnerName
                : actor.OwnerName;
        var merged = actor with
        {
            Name = actorName,
            OwnerEntityId = ownerEntityId,
            OwnerName = ownerName,
            IsPlayer = actor.IsPlayer || known.IsPlayer,
            ClassJobId = actor.ClassJobId != 0 ? actor.ClassJobId : known.ClassJobId,
            Level = actor.Level != 0 ? actor.Level : known.Level,
            BaseId = actor.BaseId != 0 ? actor.BaseId : known.BaseId,
            ObjectKind = actor.ObjectKind != 0 ? actor.ObjectKind : known.ObjectKind,
            SubKind = actor.SubKind != 0 ? actor.SubKind : known.SubKind,
            IsPet = actor.IsPet || known.IsPet,
            IsLimitBreak = actor.IsLimitBreak || known.IsLimitBreak,
            IsPartyMember = actor.IsPartyMember || known.IsPartyMember,
        };
        knownActors[actor.EntityId] = merged;
        return merged;
    }

    private static bool IsAlliedMeterSource(DamageActorIdentity source)
    {
        return source.IsPartyMember || source.IsPlayer || source.IsLimitBreak;
    }

    private static bool IsMeterDamage(ParsedDamageEvent damageEvent, DamageActorIdentity attributedSource)
    {
        return damageEvent.Outcome == DamageEventOutcome.Damage &&
            damageEvent.Amount > 0 &&
            damageEvent.MeterEligibility == DamageMeterEligibility.Eligible &&
            IsAlliedMeterSource(attributedSource);
    }

    private static ParsedDamageEvent ApplyMeterEligibility(ParsedDamageEvent damageEvent)
    {
        var attributedSource = damageEvent.AttributedSource ?? damageEvent.Source;
        var eligibility = !IsAlliedMeterSource(attributedSource)
            ? DamageMeterEligibility.NonAlliedSource
            : damageEvent.Target.IsPlayer || damageEvent.Target.IsPartyMember
                ? DamageMeterEligibility.FriendlyTarget
                : DamageMeterEligibility.Eligible;
        return damageEvent with { MeterEligibility = eligibility };
    }

    private DateTime ResolveMeterStart(ParsedDamageEvent damageEvent, DateTime meterEventAtUtc)
    {
        if (pendingOffensiveCastStartedAtUtc is not { } castStartedAtUtc)
        {
            return meterEventAtUtc;
        }

        if (damageEvent.CapturedAtUtc is { } capturedAtUtc)
        {
            var castLead = capturedAtUtc - castStartedAtUtc;
            if (castLead >= TimeSpan.Zero && castLead <= OffensiveCastRetention)
            {
                return meterEventAtUtc - castLead;
            }
        }

        return castStartedAtUtc <= meterEventAtUtc &&
            meterEventAtUtc - castStartedAtUtc <= OffensiveCastRetention
                ? castStartedAtUtc
                : meterEventAtUtc;
    }

    private sealed record PendingPeriodicTick(
        PeriodicDamageTick Tick,
        bool AllowAutomaticEncounterStart);

    private sealed record StagedDamageBatch(
        DateTime SeenAtUtc,
        int PacketCount,
        List<ParsedDamageEvent> Events);

    private sealed class MutableDamageSource
    {
        private readonly Dictionary<(uint ActionId, string ActionName), MutableDamageTotals> actions = [];
        private readonly MutableDamageTotals totals = new();
        private DamageActorIdentity source;

        public MutableDamageSource(DamageActorIdentity source)
        {
            this.source = source;
        }

        public void Add(ParsedDamageEvent damageEvent, bool isMeterEligible, int activitySegmentId)
        {
            var candidateSource = damageEvent.AttributedSource ?? damageEvent.Source;
            if (GetIdentityQuality(candidateSource) > GetIdentityQuality(source))
            {
                source = candidateSource;
            }

            totals.Add(damageEvent, isMeterEligible, activitySegmentId);
            var actionKey = (damageEvent.ActionId, damageEvent.ActionName);
            if (!actions.TryGetValue(actionKey, out var action))
            {
                action = new MutableDamageTotals();
                actions[actionKey] = action;
            }

            action.Add(damageEvent, isMeterEligible, activitySegmentId);
        }

        public void RecordDeath(DamageActorIdentity actor)
        {
            if (GetIdentityQuality(actor) > GetIdentityQuality(source))
            {
                source = actor;
            }

            totals.RecordDeath();
        }

        public void ApplyMeterCorrection(ParsedDamageEvent previous, ParsedDamageEvent replacement)
        {
            totals.ApplyMeterCorrection(previous, replacement);
            var actionKey = (previous.ActionId, previous.ActionName);
            if (actions.TryGetValue(actionKey, out var action))
            {
                action.ApplyMeterCorrection(previous, replacement);
            }
        }

        public DamageSourceSummary ToSnapshot(MutableActivityDuration? additionalActivity)
        {
            var actionSnapshots = actions
                .Select(entry => entry.Value.ToActionSnapshot(entry.Key.ActionId, entry.Key.ActionName))
                .OrderByDescending(action => action.EffectiveMeterDamage)
                .ThenBy(action => action.ActionName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return totals.ToSourceSnapshot(source, actionSnapshots, additionalActivity);
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

        public void Add(ParsedDamageEvent damageEvent, bool isMeterEligible, int activitySegmentId)
        {
            if (GetIdentityQuality(damageEvent.Target) > GetIdentityQuality(target))
            {
                target = damageEvent.Target;
            }

            totals.Add(damageEvent, isMeterEligible, activitySegmentId);
        }

        public DamageTargetSummary ToSnapshot()
        {
            return totals.ToTargetSnapshot(target);
        }
    }

    private sealed class MutableDamageTotals
    {
        private ulong totalDamage;
        private double rawMeterDamage;
        private double meterDamage;
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
        private ulong maxHitAmount;
        private string maxHitActionName = string.Empty;
        private int deaths;
        private bool isAutoAttack;
        private uint actionCategoryId;
        private readonly MutableActivityDuration activity = new();

        public void Add(ParsedDamageEvent damageEvent, bool isMeterEligible, int activitySegmentId)
        {
            swings++;
            if (isMeterEligible)
            {
                activity.Observe(damageEvent.SeenAtUtc, activitySegmentId);
            }

            isAutoAttack |= damageEvent.IsAutoAttack;
            actionCategoryId = actionCategoryId == 0 ? damageEvent.ActionCategoryId : actionCategoryId;
            switch (damageEvent.Outcome)
            {
                case DamageEventOutcome.Damage:
                    totalDamage += damageEvent.Amount;
                    rawMeterDamage += isMeterEligible ? damageEvent.RawMeterAmount : 0.0;
                    meterDamage += isMeterEligible ? damageEvent.EffectiveMeterAmount : 0.0;
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
                    if (damageEvent.Amount > maxHitAmount)
                    {
                        maxHitAmount = damageEvent.Amount;
                        maxHitActionName = damageEvent.ActionName;
                    }

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

        public void RecordDeath()
        {
            deaths++;
        }

        public void ApplyMeterCorrection(ParsedDamageEvent previous, ParsedDamageEvent replacement)
        {
            if (previous.MeterEligibility == DamageMeterEligibility.FriendlyTarget)
            {
                return;
            }

            meterDamage = Math.Max(
                0.0,
                meterDamage - previous.EffectiveMeterAmount + replacement.EffectiveMeterAmount);
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
                RawMeterDamage = rawMeterDamage,
                MeterDamage = meterDamage,
                IsAutoAttack = isAutoAttack,
                ActionCategoryId = actionCategoryId,
                PeriodicDamage = periodicDamage,
                EstimatedDamage = estimatedDamage,
                UnattributedDamage = unattributedDamage,
                PeriodicHits = periodicHits,
                MaxHitAmount = maxHitAmount,
                ActiveDurationSeconds = activity.GetSummary().DurationSeconds,
            };
        }

        public DamageSourceSummary ToSourceSnapshot(
            DamageActorIdentity source,
            IReadOnlyList<DamageActionSummary> actionSnapshots,
            MutableActivityDuration? additionalActivity)
        {
            var active = activity.GetSummary(additionalActivity);
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
                RawMeterDamage = rawMeterDamage,
                MeterDamage = meterDamage,
                PeriodicDamage = periodicDamage,
                EstimatedDamage = estimatedDamage,
                UnattributedDamage = unattributedDamage,
                PeriodicHits = periodicHits,
                MaxHitAmount = maxHitAmount,
                MaxHitActionName = maxHitActionName,
                Deaths = deaths,
                ActiveStartedAtUtc = active.StartedAtUtc,
                ActiveEndedAtUtc = active.EndedAtUtc,
                ActiveDurationSeconds = active.DurationSeconds,
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
                invulnerableHits)
            {
                MeterDamage = meterDamage,
            };
        }
    }

    private sealed class MutableActivityDuration
    {
        private readonly Dictionary<int, ActivitySegment> segments = [];

        public void Observe(DateTime seenAtUtc, int segmentId)
        {
            if (!segments.TryGetValue(segmentId, out var segment))
            {
                segments[segmentId] = new ActivitySegment(seenAtUtc, seenAtUtc);
                return;
            }

            segments[segmentId] = new ActivitySegment(
                seenAtUtc < segment.StartedAtUtc ? seenAtUtc : segment.StartedAtUtc,
                seenAtUtc > segment.EndedAtUtc ? seenAtUtc : segment.EndedAtUtc);
        }

        public ActivityDurationSummary GetSummary(MutableActivityDuration? additional = null)
        {
            var combined = new Dictionary<int, ActivitySegment>(segments);
            if (additional is not null)
            {
                foreach (var (segmentId, segment) in additional.segments)
                {
                    if (!combined.TryGetValue(segmentId, out var existing))
                    {
                        combined[segmentId] = segment;
                        continue;
                    }

                    combined[segmentId] = new ActivitySegment(
                        segment.StartedAtUtc < existing.StartedAtUtc ? segment.StartedAtUtc : existing.StartedAtUtc,
                        segment.EndedAtUtc > existing.EndedAtUtc ? segment.EndedAtUtc : existing.EndedAtUtc);
                }
            }

            if (combined.Count == 0)
            {
                return default;
            }

            return new ActivityDurationSummary(
                combined.Values.Min(segment => segment.StartedAtUtc),
                combined.Values.Max(segment => segment.EndedAtUtc),
                combined.Values.Sum(segment => Math.Max(
                    0.0,
                    (segment.EndedAtUtc - segment.StartedAtUtc).TotalSeconds)));
        }

        private readonly record struct ActivitySegment(DateTime StartedAtUtc, DateTime EndedAtUtc);
    }

    private readonly record struct ActivityDurationSummary(
        DateTime? StartedAtUtc,
        DateTime? EndedAtUtc,
        double DurationSeconds);
}
