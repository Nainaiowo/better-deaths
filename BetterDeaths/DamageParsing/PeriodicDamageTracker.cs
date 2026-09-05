namespace BetterDeaths.DamageParsing;

using System;
using System.Collections.Generic;
using System.Linq;

internal sealed class PeriodicDamageTracker
{
    private const double MinimumTickSpacingSeconds = 2.6;
    private const double UnknownDurationRetentionSeconds = 60.0;
    private const double DeferredStatusArrivalToleranceSeconds = 0.05;
    private const double DuplicateApplicationWindowSeconds = 0.5;
    private const double NormalStatusExpiryGraceSeconds = 1.0;
    private const double PeriodicStatusRetentionSeconds = 5.0;
    private const int MaximumLearnedTickSamples = 7;
    private const int MaximumPotencySamples = 1000;
    private const int MinimumObservedRateSamples = 11;
    private const double DefaultCriticalRate = 0.15;
    private const double DefaultDirectHitRate = 0.05;
    private const double DirectHitMultiplier = 1.25;
    private readonly Dictionary<StatusKey, TrackedStatus> statuses = [];
    private readonly List<TrackedStatus> statusHistory = [];
    private readonly Dictionary<ApplicationKey, TickSamples> learnedApplicationTicks = [];
    private readonly Dictionary<ProfileKey, TickSamples> learnedProfileTicks = [];
    private readonly Dictionary<string, PotencySamples> sourcePotencySamples = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HitRateSamples> sourceHitRateSamples = new(StringComparer.Ordinal);
    private readonly HashSet<uint> observedGroundDamageStatusIds = [];
    private readonly PeriodicDirectHitCompatibility directHitCompatibility = new();
    private long nextApplicationGeneration;

    public void Observe(DamageStatusApplication application)
    {
        if (application.IsRemoval)
        {
            Retire(application.Target.EntityId, application.StatusId, application.Source.EntityId, application.SeenAtUtc);
            return;
        }

        if (application.Source.EntityId != 0 && (application.Source.IsPlayer || application.Source.IsPartyMember))
        {
            GetSourceSamples(application.Source, application.SourceBaseRates);
            directHitCompatibility.ObserveContext(application.Source, application.SourceBaseRates);
        }

        var nominalExpiresAtUtc = application.DurationSeconds > 0.0f
            ? application.SeenAtUtc.AddSeconds(application.DurationSeconds)
            : application.SeenAtUtc.AddSeconds(UnknownDurationRetentionSeconds);
        var retainUntilUtc = nominalExpiresAtUtc.AddSeconds(PeriodicStatusRetentionSeconds);
        var matchingStatuses = statuses
            .Where(entry => entry.Key.TargetEntityId == application.Target.EntityId &&
                entry.Key.StatusId == application.StatusId)
            .ToList();
        if (application.Source.EntityId == 0 && matchingStatuses.Count > 0)
        {
            foreach (var match in matchingStatuses)
            {
                if (match.Value.AwaitingStatusConfirmation)
                {
                    continue;
                }
                UpdateTrackedStatus(
                    match.Value,
                    application with { Source = match.Value.Application.Source },
                    nominalExpiresAtUtc,
                    retainUntilUtc);
            }

            return;
        }

        if (application.Source.EntityId != 0)
        {
            RemoveTrackedStatus(new StatusKey(application.Target.EntityId, application.StatusId, 0));
        }

        var key = new StatusKey(
            application.Target.EntityId,
            application.StatusId,
            application.Source.EntityId);
        if (statuses.TryGetValue(key, out var existing))
        {
            UpdateTrackedStatus(existing, application, nominalExpiresAtUtc, retainUntilUtc);
            return;
        }

        var tracked = new TrackedStatus(
            application,
            nominalExpiresAtUtc,
            retainUntilUtc,
            ++nextApplicationGeneration);
        CaptureIndependentEstimate(tracked);
        statuses[key] = tracked;
        RetireExclusiveApplications(tracked);
    }

    public void Retire(uint targetEntityId, uint statusId, uint sourceEntityId, DateTime removedAtUtc)
    {
        // Target DoTs can expire between a replacement cast and its status result.
        // Preserve the pending snapshot, without changing ground-effect attribution.
        foreach (var status in statuses.Values.Concat(statusHistory)
                     .Where(status => (!status.AwaitingStatusConfirmation || IsGroundDamageStatus(status.Application.StatusId)) &&
                         status.Application.Target.EntityId == targetEntityId &&
                         status.Application.StatusId == statusId && status.Application.SeenAtUtc <= removedAtUtc &&
                         (sourceEntityId == 0 || status.Application.Source.EntityId == sourceEntityId)))
        {
            if (status.RemovedAtUtc is null || status.RemovedAtUtc > removedAtUtc)
            {
                status.RemovedAtUtc = removedAtUtc;
            }
        }
    }

    public void ConfirmGroundDamageStatus(uint statusId)
    {
        if (statusId != 0)
        {
            observedGroundDamageStatusIds.Add(statusId);
        }
    }

    public void Advance(DateTime nowUtc)
    {
        Prune(nowUtc);
    }

    public void Refresh(uint targetEntityId, uint statusId, DateTime seenAtUtc)
    {
        foreach (var status in statuses.Values.Where(status =>
                     status.Application.Target.EntityId == targetEntityId &&
                     status.Application.StatusId == statusId))
        {
            status.NominalExpiresAtUtc = seenAtUtc.AddSeconds(UnknownDurationRetentionSeconds);
            status.RetainUntilUtc = status.NominalExpiresAtUtc.AddSeconds(PeriodicStatusRetentionSeconds);
            status.LateTickConsumed = false;
        }
    }

    public ParsedDamageEvent ResolveReactiveDamage(ParsedDamageEvent damageEvent)
    {
        if (!damageEvent.IsSourceEntry || damageEvent.PacketTarget is null)
        {
            return damageEvent;
        }

        Prune(damageEvent.SeenAtUtc);
        var candidates = statuses.Values
            .Where(status => status.Application.Target.EntityId == damageEvent.PacketTarget.EntityId &&
                status.Application.IsReactiveDamage &&
                IsActiveAt(
                    status,
                    damageEvent.SeenAtUtc,
                    allowDeferredApplication: false,
                    allowLatePeriodicTick: false))
            .OrderByDescending(status => status.Application.SeenAtUtc)
            .ToList();
        if (candidates.Count == 0)
        {
            var unknownSource = CreateUnknownSource(!damageEvent.Target.IsPlayer);
            return damageEvent with
            {
                AttributedSource = unknownSource,
                SourceBaseRates = null,
                AttributionQuality = DamageAttributionQuality.Unattributed,
            };
        }

        var selected = candidates[0].Application;
        return damageEvent with
        {
            ActionId = selected.StatusId,
            ActionName = selected.StatusName,
            ActionCategoryId = 0,
            IsAutoAttack = false,
            AttributedSource = selected.Source,
            SourceBaseRates = selected.SourceBaseRates,
            AttributionQuality = candidates.Count == 1
                ? DamageAttributionQuality.Exact
                : DamageAttributionQuality.Estimated,
            StatusId = selected.StatusId,
            StatusIconId = selected.StatusIconId,
        };
    }

    public void ObserveDirectDamage(IEnumerable<ParsedDamageEvent> damageEvents)
    {
        foreach (var damageEvent in damageEvents)
        {
            directHitCompatibility.Observe(damageEvent);
            if (damageEvent.IsPeriodic || damageEvent.MeterEligibility != DamageMeterEligibility.Eligible ||
                damageEvent.Outcome != DamageEventOutcome.Damage || damageEvent.Amount == 0)
            {
                continue;
            }

            var source = damageEvent.AttributedSource ?? damageEvent.Source;
            if ((!source.IsPlayer && !source.IsPartyMember) ||
                source.IsLimitBreak)
            {
                continue;
            }

            var sourceKey = GetActorKey(source);
            var rateSamples = GetSourceSamples(source, damageEvent.SourceBaseRates);

            var effects = GetApplicableEffects(
                damageEvent.SourceStatuses,
                damageEvent.TargetStatuses,
                source,
                damageEvent.ActionCategoryId,
                damageEvent.DamageType,
                damageEvent.ElementType);
            if (!RaidBuffPolicy.IsGuaranteedCritical(damageEvent) &&
                effects.All(effect => effect.Kind != RaidBuffEffectKind.CriticalChance))
            {
                rateSamples.CriticalSwings++;
                rateSamples.CriticalHits += damageEvent.Critical ? 1 : 0;
            }

            if (!RaidBuffPolicy.IsGuaranteedDirectHit(damageEvent) &&
                effects.All(effect => effect.Kind != RaidBuffEffectKind.DirectHitChance))
            {
                rateSamples.DirectHitSwings++;
                rateSamples.DirectHits += damageEvent.DirectHit ? 1 : 0;
            }

            var calibrationPotency = JobDamageCalibrationPolicy.GetCalibrationPotency(damageEvent);
            if (damageEvent.Source.IsPet ||
                HasAttributeChange(damageEvent.SourceStatuses) ||
                HasUnknownDamageModifier(damageEvent.SourceStatuses) ||
                calibrationPotency is not > 0.0 ||
                damageEvent.Blocked ||
                damageEvent.Parried)
            {
                continue;
            }

            // Guaranteed hits under chance buffs have additional damage conversions.
            if ((RaidBuffPolicy.IsGuaranteedCritical(damageEvent) || RaidBuffPolicy.IsGuaranteedDirectHit(damageEvent)) &&
                effects.Any(effect => effect.Kind is RaidBuffEffectKind.CriticalChance or RaidBuffEffectKind.DirectHitChance))
            {
                continue;
            }

            var amount = (double)damageEvent.Amount;
            if (damageEvent.DirectHit)
            {
                amount /= DirectHitMultiplier;
            }
            if (damageEvent.Critical)
            {
                amount /= GetCriticalMultiplier(GetBaseRates(sourceKey).Critical);
            }
            // Preserve division order: tick reconstruction truncates this estimate.
            amount /= calibrationPotency.Value;
            var potencyMultiplier = amount / GetDamageMultiplier(effects);
            if (!double.IsFinite(potencyMultiplier) || potencyMultiplier <= 0.0)
            {
                continue;
            }

            if (!sourcePotencySamples.TryGetValue(sourceKey, out var potencySamples))
            {
                potencySamples = new PotencySamples();
                sourcePotencySamples[sourceKey] = potencySamples;
            }

            potencySamples.Add(potencyMultiplier);
        }
    }

    public IReadOnlyList<ParsedDamageEvent> Process(PeriodicDamageTick tick)
    {
        if (tick.Amount == 0)
        {
            return [];
        }

        Prune(tick.SeenAtUtc);
        if (tick.StatusId != 0)
        {
            ConfirmGroundDamageStatus(tick.StatusId);
            var matchingStatuses = FindGroundStatuses(tick);
            if (tick.Source is not null && tick.Source.EntityId != 0)
            {
                var matchingStatus = matchingStatuses.FirstOrDefault(status =>
                    status.Application.Source.EntityId == tick.Source.EntityId);
                if (matchingStatus is not null)
                {
                    matchingStatus.LastTickAtUtc = tick.SeenAtUtc;
                }

                ConsumeLateTickIfNeeded(matchingStatus, tick.SeenAtUtc);
                return [CreateEvent(
                    tick,
                    matchingStatus,
                    tick.Source,
                    tick.Amount,
                    tick.Amount,
                    DamageAttributionQuality.Exact,
                    0,
                    PeriodicAllocationBasis.ExactSource,
                    Math.Max(1, matchingStatuses.Count),
                    tick.Amount)];
            }

            if (matchingStatuses.Count > 0)
            {
                var selectedStatus = matchingStatuses[0];
                selectedStatus.LastTickAtUtc = tick.SeenAtUtc;
                ConsumeLateTickIfNeeded(selectedStatus, tick.SeenAtUtc);
                var selected = selectedStatus.Application;
                return [CreateEvent(
                    tick,
                    selectedStatus,
                    selected.Source,
                    tick.Amount,
                    tick.Amount,
                    matchingStatuses.Count == 1
                        ? DamageAttributionQuality.Exact
                        : DamageAttributionQuality.Estimated,
                    0,
                    matchingStatuses.Count == 1
                        ? PeriodicAllocationBasis.SingleCandidate
                        : PeriodicAllocationBasis.EqualFallback,
                    matchingStatuses.Count,
                    tick.Amount)];
            }

            var unknownGroundSource = CreateUnknownSource(!tick.Target.IsPlayer);
            return [CreateEvent(
                tick,
                null,
                unknownGroundSource,
                tick.Amount,
                tick.Amount,
                DamageAttributionQuality.Unattributed,
                0,
                PeriodicAllocationBasis.Unattributed,
                0,
                0.0)];
        }

        var candidates = statuses.Values.Concat(statusHistory)
            .Where(status => IsEligibleForTick(status, tick))
            .GroupBy(status => (status.Application.Source.EntityId,
                PeriodicDamageRefreshPolicy.GetExclusiveFamily(status.Application.StatusId) ?? status.Application.StatusId))
            .Select(group => group.Where(status => status.ActivatedAtUtc <= tick.SeenAtUtc)
                .OrderByDescending(status => status.ActivatedAtUtc)
                .ThenByDescending(status => status.ApplicationGeneration).FirstOrDefault() ??
                group.OrderBy(status => status.ActivatedAtUtc)
                    .ThenByDescending(status => status.ApplicationGeneration).First())
            .OrderBy(status => status.Application.Source.EntityId)
            .ThenBy(status => status.Application.StatusId)
            .ToList();
        if (candidates.Count > 1)
        {
            // Exhausted snapshots must not take a share of another effect's tick.
            // With one candidate, the packet still provides an unambiguous amount.
            candidates.RemoveAll(status =>
                PeriodicDamageRefreshPolicy.GetMaximumTicks(status.Application.StatusId) is { } maximumTicks &&
                status.TickCount >= maximumTicks);
        }
        if (candidates.Count == 0)
        {
            var unknownSource = CreateUnknownSource(!tick.Target.IsPlayer);
            return [CreateEvent(
                tick,
                null,
                unknownSource,
                tick.Amount,
                tick.Amount,
                DamageAttributionQuality.Unattributed,
                0,
                PeriodicAllocationBasis.Unattributed,
                0,
                0.0)];
        }

        if (candidates.Count == 1)
        {
            var candidate = candidates[0];
            candidate.LastTickAtUtc = tick.SeenAtUtc;
            candidate.TickCount++;
            ConsumeLateTickIfNeeded(candidate, tick.SeenAtUtc);
            Learn(candidate, tick.Amount);
            return [CreateEvent(
                tick,
                candidate,
                candidate.Application.Source,
                tick.Amount,
                tick.Amount,
                DamageAttributionQuality.Exact,
                0,
                PeriodicAllocationBasis.SingleCandidate,
                1,
                tick.Amount)];
        }

        var allocations = Allocate(tick.Amount, candidates);
        var events = new List<ParsedDamageEvent>(candidates.Count);
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            if (allocations[index].Amount == 0)
            {
                continue;
            }

            candidate.LastTickAtUtc = tick.SeenAtUtc;
            candidate.TickCount++;
            ConsumeLateTickIfNeeded(candidate, tick.SeenAtUtc);
            events.Add(CreateEvent(
                tick,
                candidate,
                candidate.Application.Source,
                allocations[index].Amount,
                allocations[index].Amount,
                DamageAttributionQuality.Estimated,
                index,
                allocations[index].Basis,
                candidates.Count,
                allocations[index].Weight));
        }

        return events;
    }

    public void Clear(bool preserveCalibration = false)
    {
        statuses.Clear();
        statusHistory.Clear();
        learnedApplicationTicks.Clear();
        learnedProfileTicks.Clear();
        if (!preserveCalibration)
        {
            ClearCalibration();
        }
        nextApplicationGeneration = 0;
    }

    public void ClearCalibration()
    {
        sourcePotencySamples.Clear();
        sourceHitRateSamples.Clear();
        directHitCompatibility.Clear();
    }

    private HitRateSamples GetSourceSamples(DamageActorIdentity source, DamageBaseRateSnapshot? knownRates)
    {
        var key = GetActorKey(source);
        sourceHitRateSamples.TryGetValue(key, out var samples);
        if (samples is not null &&
            (source.ClassJobId != 0 && samples.Identity.ClassJobId != 0 && source.ClassJobId != samples.Identity.ClassJobId ||
             source.Level != 0 && samples.Identity.Level != 0 && source.Level != samples.Identity.Level ||
             knownRates is not null && samples.KnownCriticalRate is not null &&
             (knownRates.Critical != samples.KnownCriticalRate || knownRates.DirectHit != samples.KnownDirectHitRate)))
        {
            sourcePotencySamples.Remove(key);
            samples = null;
        }

        if (samples is null)
        {
            samples = new HitRateSamples { Identity = source };
            sourceHitRateSamples[key] = samples;
        }
        else
        {
            samples.Identity = source with
            {
                ClassJobId = source.ClassJobId != 0 ? source.ClassJobId : samples.Identity.ClassJobId,
                Level = source.Level != 0 ? source.Level : samples.Identity.Level,
            };
        }

        if (knownRates is not null)
        {
            samples.KnownCriticalRate = knownRates.Critical;
            samples.KnownDirectHitRate = knownRates.DirectHit;
        }
        return samples;
    }

    private List<TrackedStatus> FindGroundStatuses(PeriodicDamageTick tick)
    {
        var candidates = statuses.Values
            .Where(status => status.Application.StatusId == tick.StatusId &&
                IsActiveAt(
                    status,
                    tick.SeenAtUtc,
                    allowDeferredApplication: true,
                    allowLatePeriodicTick: true))
            .ToList();
        var targetStatuses = candidates
            .Where(status => status.Application.Target.EntityId == tick.Target.EntityId)
            .ToList();
        return (targetStatuses.Count > 0 ? targetStatuses : candidates)
            .OrderBy(status => status.LastTickAtUtc ?? status.Application.SeenAtUtc.AddSeconds(-3.0))
            .ThenBy(status => status.Application.SeenAtUtc)
            .ThenBy(status => status.Application.Source.EntityId)
            .ToList();
    }

    private bool IsEligibleForTick(TrackedStatus status, PeriodicDamageTick tick)
    {
        if (!status.Application.IsPeriodicDamage ||
            IsGroundDamageStatus(status.Application.StatusId) ||
            status.Application.Target.EntityId != tick.Target.EntityId ||
            status.ActivatedAtUtc is not { } activatedAtUtc ||
            tick.SeenAtUtc.AddSeconds(DeferredStatusArrivalToleranceSeconds) < activatedAtUtc ||
            !IsActiveAt(
                status,
                tick.SeenAtUtc,
                allowDeferredApplication: true,
                allowLatePeriodicTick: true))
        {
            return false;
        }

        return status.LastTickAtUtc is null ||
            (tick.SeenAtUtc - status.LastTickAtUtc.Value).TotalSeconds >= MinimumTickSpacingSeconds;
    }

    private PeriodicAllocation[] Allocate(uint amount, IReadOnlyList<TrackedStatus> candidates)
    {
        var weights = candidates
            .Select(GetAllocationWeight)
            .ToArray();
        if (weights.All(weight => weight.Value <= 0.0))
        {
            for (var index = 0; index < weights.Length; index++)
            {
                weights[index] = new AllocationWeight(1.0, PeriodicAllocationBasis.EqualFallback);
            }
        }
        else
        {
            var knownMedian = Median(weights
                .Where(weight => weight.Value > 0.0)
                .Select(weight => weight.Value));
            for (var index = 0; index < weights.Length; index++)
            {
                if (weights[index].Value <= 0.0)
                {
                    weights[index] = new AllocationWeight(
                        knownMedian,
                        PeriodicAllocationBasis.MedianFallback);
                }
            }
        }

        var totalWeight = weights.Sum(weight => weight.Value);
        var allocations = new uint[candidates.Count];
        var fractions = new (int Index, double Fraction)[candidates.Count];
        ulong allocated = 0;
        for (var index = 0; index < candidates.Count; index++)
        {
            var exact = amount * weights[index].Value / totalWeight;
            allocations[index] = (uint)Math.Floor(exact);
            allocated += allocations[index];
            fractions[index] = (index, exact - allocations[index]);
        }

        var remainder = (uint)(amount - allocated);
        foreach (var entry in fractions.OrderByDescending(entry => entry.Fraction).ThenBy(entry => entry.Index))
        {
            if (remainder == 0)
            {
                break;
            }

            allocations[entry.Index]++;
            remainder--;
        }

        return allocations
            .Select((allocation, index) => new PeriodicAllocation(
                allocation,
                weights[index].Value,
                weights[index].Basis))
            .ToArray();
    }

    private AllocationWeight GetAllocationWeight(TrackedStatus status)
    {
        if (learnedApplicationTicks.TryGetValue(GetApplicationKey(status), out var applicationSamples))
        {
            return new AllocationWeight(
                applicationSamples.Median,
                PeriodicAllocationBasis.LearnedApplication);
        }

        if (!string.IsNullOrWhiteSpace(status.Application.SnapshotKey) &&
            learnedProfileTicks.TryGetValue(GetProfileKey(status.Application), out var profileSamples))
        {
            return new AllocationWeight(
                profileSamples.Median,
                PeriodicAllocationBasis.LearnedSnapshot);
        }

        return new AllocationWeight(
            Math.Round(EstimateTickWeight(status) ?? 0.0, MidpointRounding.AwayFromZero),
            PeriodicAllocationBasis.PotencyEstimate);
    }

    private void Learn(TrackedStatus status, uint amount)
    {
        AddLearnedSample(learnedApplicationTicks, GetApplicationKey(status), amount);
        if (!string.IsNullOrWhiteSpace(status.Application.SnapshotKey))
        {
            AddLearnedSample(learnedProfileTicks, GetProfileKey(status.Application), amount);
        }
    }

    private static void AddLearnedSample<TKey>(
        Dictionary<TKey, TickSamples> samplesByKey,
        TKey key,
        uint amount)
        where TKey : notnull
    {
        if (!samplesByKey.TryGetValue(key, out var samples))
        {
            samples = new TickSamples();
            samplesByKey[key] = samples;
        }

        samples.Add(amount);
    }

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values.OrderBy(value => value).ToArray();
        if (ordered.Length == 0)
        {
            return 0.0;
        }

        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2.0
            : ordered[middle];
    }

    private void UpdateTrackedStatus(
        TrackedStatus existing,
        DamageStatusApplication application,
        DateTime nominalExpiresAtUtc,
        DateTime retainUntilUtc)
    {
        var isExplicitResnapshot = PeriodicDamageRefreshPolicy.IsExplicitResnapshot(application.ActionId);
        // Action effects carry snapshot bytes before the status-result packet
        // supplies its duration. That acknowledgement is not another application.
        var isStatusAcknowledgement = application.ActionId == 0 &&
            application.DurationSeconds > 0 && application.BaseDamageLowByte is null &&
            application.CriticalRateLowByte is null;
        var confirmsPendingAction = existing.AwaitingStatusConfirmation && isStatusAcknowledgement &&
            application.SeenAtUtc >= existing.Application.SeenAtUtc &&
            application.SeenAtUtc <= existing.NominalExpiresAtUtc;
        var compatibilityConfirmationAtUtc = confirmsPendingAction ? application.SeenAtUtc : (DateTime?)null;
        var repeatsConfirmation = isStatusAcknowledgement && existing.ActivatedAtUtc is { } activatedAtUtc &&
            Math.Abs((application.SeenAtUtc - activatedAtUtc).TotalSeconds) <= DuplicateApplicationWindowSeconds;
        var isDuplicateObservation = !isExplicitResnapshot && existing.RemovedAtUtc is null &&
            (confirmsPendingAction || repeatsConfirmation || Math.Abs((application.SeenAtUtc - existing.Application.SeenAtUtc).TotalSeconds) <=
                DuplicateApplicationWindowSeconds);
        if (isDuplicateObservation)
        {
            if (confirmsPendingAction)
            {
                existing.AwaitingStatusConfirmation = false;
                existing.ActivatedAtUtc = application.SeenAtUtc;
                foreach (var prior in statusHistory.Where(prior =>
                             prior.Application.Target.EntityId == application.Target.EntityId &&
                             prior.Application.Source.EntityId == application.Source.EntityId &&
                             prior.Application.StatusId == application.StatusId &&
                             prior.ApplicationGeneration < existing.ApplicationGeneration))
                {
                    if (prior.RemovedAtUtc is null || prior.RemovedAtUtc > application.SeenAtUtc)
                    {
                        prior.RemovedAtUtc = application.SeenAtUtc;
                    }
                }
            }
            var existingApplication = existing.Application;
            application = application with
            {
                SeenAtUtc = application.SeenAtUtc < existingApplication.SeenAtUtc
                    ? application.SeenAtUtc
                    : existingApplication.SeenAtUtc,
                ActionId = existingApplication.ActionId != 0
                    ? existingApplication.ActionId
                    : application.ActionId,
                ActionName = !string.IsNullOrWhiteSpace(existingApplication.ActionName)
                    ? existingApplication.ActionName
                    : application.ActionName,
                SnapshotKey = !string.IsNullOrWhiteSpace(existingApplication.SnapshotKey)
                    ? existingApplication.SnapshotKey
                    : application.SnapshotKey,
                Parameter = application.Parameter != 0
                    ? application.Parameter
                    : existingApplication.Parameter,
                ActionCategoryId = application.ActionCategoryId != 0
                    ? application.ActionCategoryId
                    : existingApplication.ActionCategoryId,
                DamageType = application.DamageType != 0
                    ? application.DamageType
                    : existingApplication.DamageType,
                ElementType = application.ElementType != 0
                    ? application.ElementType
                    : existingApplication.ElementType,
                PeriodicPotency = application.PeriodicPotency ?? existingApplication.PeriodicPotency,
                BaseDamageLowByte = application.BaseDamageLowByte ?? existingApplication.BaseDamageLowByte,
                CriticalRateLowByte = application.CriticalRateLowByte ?? existingApplication.CriticalRateLowByte,
                EffectParameterByte = application.EffectParameterByte ?? existingApplication.EffectParameterByte,
                SourceBaseRates = existingApplication.SourceBaseRates ?? application.SourceBaseRates,
                SourceStatuses = existingApplication.HasSourceStatusSnapshot || existingApplication.SourceStatuses.Count > 0
                    ? existingApplication.SourceStatuses
                    : application.SourceStatuses,
                TargetStatuses = existingApplication.HasTargetStatusSnapshot || existingApplication.TargetStatuses.Count > 0
                    ? existingApplication.TargetStatuses
                    : application.TargetStatuses,
                HasSourceStatusSnapshot = existingApplication.HasSourceStatusSnapshot ||
                    application.HasSourceStatusSnapshot,
                HasTargetStatusSnapshot = existingApplication.HasTargetStatusSnapshot ||
                    application.HasTargetStatusSnapshot,
            };
        }
        else
        {
            if (application.SeenAtUtc < existing.Application.SeenAtUtc)
            {
                var earlier = new TrackedStatus(application, nominalExpiresAtUtc, retainUntilUtc, ++nextApplicationGeneration)
                {
                    RemovedAtUtc = existing.Application.SeenAtUtc,
                };
                CaptureIndependentEstimate(earlier);
                statusHistory.Add(earlier);
                RetireExclusiveApplications(earlier);
                return;
            }
            statusHistory.Add(new TrackedStatus(existing.Application, existing.NominalExpiresAtUtc,
                existing.RetainUntilUtc, existing.ApplicationGeneration)
            {
                RemovedAtUtc = existing.RemovedAtUtc is { } removed && removed < application.SeenAtUtc
                    ? removed : IsPendingActionApplication(application) ? existing.RemovedAtUtc : application.SeenAtUtc,
                ActivatedAtUtc = existing.ActivatedAtUtc,
                LastTickAtUtc = existing.LastTickAtUtc,
                TickCount = existing.TickCount,
                LateTickConsumed = existing.LateTickConsumed,
                IndependentEstimate = existing.IndependentEstimate,
                EstimateInputs = existing.EstimateInputs,
                Calibration = existing.Calibration,
                CompatibilityDirectHit = existing.CompatibilityDirectHit,
            });
            var existingApplication = existing.Application;
            if (application.IsPeriodicDamage)
            {
                application = application with
                {
                    ActionId = application.ActionId != 0
                        ? application.ActionId
                        : existingApplication.ActionId,
                    ActionName = !string.IsNullOrWhiteSpace(application.ActionName)
                        ? application.ActionName
                        : existingApplication.ActionName,
                    PeriodicPotency = application.PeriodicPotency ?? existingApplication.PeriodicPotency,
                };
            }

            learnedApplicationTicks.Remove(GetApplicationKey(existing));
            existing.ApplicationGeneration = ++nextApplicationGeneration;
            existing.LastTickAtUtc = null;
            existing.TickCount = 0;
            existing.AwaitingStatusConfirmation = IsPendingActionApplication(application);
            existing.ActivatedAtUtc = existing.AwaitingStatusConfirmation ? null : application.SeenAtUtc;
        }

        existing.Application = application;
        CaptureIndependentEstimate(existing, recaptureCalibration: !isDuplicateObservation,
            compatibilityAtUtc: compatibilityConfirmationAtUtc);
        existing.NominalExpiresAtUtc = nominalExpiresAtUtc;
        existing.RetainUntilUtc = retainUntilUtc;
        existing.RemovedAtUtc = null;
        existing.LateTickConsumed = false;
        RetireExclusiveApplications(existing);
    }

    private void RetireExclusiveApplications(TrackedStatus current)
    {
        if (!current.Application.IsPeriodicDamage || current.Application.Source.EntityId == 0 ||
            current.ActivatedAtUtc is not { } activatedAtUtc || current.AwaitingStatusConfirmation ||
            PeriodicDamageRefreshPolicy.GetExclusiveFamily(current.Application.StatusId) is not { } family)
        {
            return;
        }

        foreach (var other in statuses.Values.Concat(statusHistory).Where(other =>
                     other.Application.IsPeriodicDamage && !other.AwaitingStatusConfirmation &&
                     other.ActivatedAtUtc is not null &&
                     other.Application.Source.EntityId == current.Application.Source.EntityId &&
                     other.Application.Target.EntityId == current.Application.Target.EntityId &&
                     other.Application.StatusId != current.Application.StatusId &&
                     PeriodicDamageRefreshPolicy.GetExclusiveFamily(other.Application.StatusId) == family))
        {
            // Keep the old snapshot for delayed ticks, but never resurrect it after replacement.
            var currentIsNewer = activatedAtUtc > other.ActivatedAtUtc ||
                activatedAtUtc == other.ActivatedAtUtc && current.ApplicationGeneration > other.ApplicationGeneration;
            var retired = currentIsNewer ? other : current;
            var replacedAtUtc = currentIsNewer ? activatedAtUtc : other.ActivatedAtUtc!.Value;
            if (retired.RemovedAtUtc is null || retired.RemovedAtUtc > replacedAtUtc)
            {
                retired.RemovedAtUtc = replacedAtUtc;
            }
        }
    }

    private static bool IsPendingActionApplication(DamageStatusApplication application) =>
        application.IsPeriodicDamage && application.ActionId != 0 && application.DurationSeconds <= 0;

    private ParsedDamageEvent CreateEvent(
        PeriodicDamageTick tick,
        TrackedStatus? tracked,
        DamageActorIdentity source,
        uint amount,
        double meterAmount,
        DamageAttributionQuality quality,
        int allocationIndex,
        PeriodicAllocationBasis allocationBasis,
        int candidateCount,
        double allocationWeight)
    {
        var status = tracked?.Application;
        var statusId = status?.StatusId ?? tick.StatusId;
        var statusName = status?.StatusName ??
            (!string.IsNullOrWhiteSpace(tick.StatusName) ? tick.StatusName : "Unattributed DoT");
        var statusIconId = status?.StatusIconId ?? tick.StatusIconId;
        return new ParsedDamageEvent(
            $"periodic:{tick.PacketSequence}:{tick.Target.EntityId:X8}:{statusId}:{source.EntityId:X8}:{allocationIndex}",
            tick.PacketSequence,
            tick.SeenAtUtc,
            0,
            source,
            tick.Target,
            statusId,
            statusName,
            0,
            allocationIndex,
            DamageEventOutcome.Damage,
            amount,
            0,
            false,
            false,
            false,
            false,
            0,
            0,
            0,
            0,
            0)
        {
            CapturedAtUtc = tick.CapturedAtUtc,
            MeterAmount = meterAmount,
            SimulatedPeriodicAmount = tick.StatusId == 0 ? tracked?.IndependentEstimate : null,
            PeriodicEstimateInputs = tick.StatusId == 0 ? tracked?.EstimateInputs : null,
            PeriodicCompatibilityEstimate = tick.StatusId == 0 && tracked?.EstimateInputs is { } inputs &&
                tracked.CompatibilityDirectHit is { } compatibility
                    ? new PeriodicCompatibilityEstimate(compatibility,
                        inputs.BaseDamage * (1 + (inputs.CriticalMultiplier - 1) * inputs.CriticalRate) *
                        (1 + 0.25 * compatibility.Factor))
                    : null,
            PeriodicEstimateUnavailableReason = tick.StatusId != 0 ? "Observed source-specific tick" :
                status is null ? "Missing application" :
                HasAttributeChange(status.SourceStatuses) ? "Attribute-changing status" :
                HasUnknownDamageModifier(status.SourceStatuses) ? "Unknown damage modifier" :
                status.PeriodicPotency is not > 0 ? "Missing potency" :
                tracked?.IndependentEstimate is null ? "Missing application-time calibration" : null,
            AttributedSource = source,
            AttributionQuality = quality,
            IsPeriodic = true,
            DamageType = status?.DamageType ?? 0,
            ElementType = status?.ElementType ?? 0,
            ActionCategoryId = status?.ActionCategoryId ?? 0,
            StatusId = statusId,
            StatusIconId = statusIconId,
            CriticalRateLowByte = status?.CriticalRateLowByte,
            SourceBaseRates = status?.SourceBaseRates,
            SourceStatuses = status?.SourceStatuses ?? [],
            TargetStatuses = status?.TargetStatuses ?? [],
            HasSourceStatusSnapshot = status?.HasSourceStatusSnapshot ?? false,
            HasTargetStatusSnapshot = status?.HasTargetStatusSnapshot ?? false,
            PeriodicAllocationBasis = allocationBasis,
            PeriodicCandidateCount = candidateCount,
            PeriodicAllocationWeight = allocationWeight,
            PeriodicCombinedAmount = tick.Amount,
        };
    }

    private static DamageActorIdentity CreateUnknownSource(bool outgoing)
    {
        return new DamageActorIdentity(
            0,
            outgoing ? "Unattributed outgoing" : "Unattributed incoming",
            0,
            string.Empty,
            false,
            0)
        {
            IsPartyMember = outgoing,
        };
    }

    private double? EstimateTickWeight(TrackedStatus status)
    {
        return GetEstimateInputs(status)?.ExpectedAmount;
    }

    private void CaptureIndependentEstimate(TrackedStatus status, bool recaptureCalibration = true,
        DateTime? compatibilityAtUtc = null)
    {
        if (recaptureCalibration)
        {
            status.Calibration = GetCalibration(status.Application.Source);
        }

        if (recaptureCalibration || compatibilityAtUtc is not null)
        {
            status.CompatibilityDirectHit = directHitCompatibility.Capture(status.Application,
                compatibilityAtUtc ?? status.Application.SeenAtUtc);
        }

        // Enrich packet metadata without substituting samples from after application.
        status.EstimateInputs = GetEstimateInputs(status, status.Calibration);
        status.IndependentEstimate = status.EstimateInputs?.ExpectedAmount;
    }

    private CalibrationSnapshot GetCalibration(DamageActorIdentity source)
    {
        var sourceKey = GetActorKey(source);
        sourcePotencySamples.TryGetValue(sourceKey, out var potencySamples);
        sourceHitRateSamples.TryGetValue(sourceKey, out var rateSamples);
        return new CalibrationSnapshot(potencySamples?.Median, GetBaseRates(sourceKey),
            potencySamples?.Count ?? 0, rateSamples?.CriticalSwings ?? 0, rateSamples?.DirectHitSwings ?? 0,
            rateSamples?.KnownCriticalRate is not null);
    }

    private PeriodicDamageEstimateInputs? GetEstimateInputs(TrackedStatus status) =>
        GetEstimateInputs(status, GetCalibration(status.Application.Source));

    private static PeriodicDamageEstimateInputs? GetEstimateInputs(TrackedStatus status, CalibrationSnapshot calibration)
    {
        var application = status.Application;
        if (application.PeriodicPotency is not > 0.0 || HasAttributeChange(application.SourceStatuses) ||
            HasUnknownDamageModifier(application.SourceStatuses))
        {
            return null;
        }

        if (calibration.DamagePerPotency is not > 0.0)
        {
            return null;
        }

        var effects = GetApplicableEffects(
            application.SourceStatuses,
            application.TargetStatuses,
            application.Source,
            application.ActionCategoryId,
            application.DamageType,
            application.ElementType);
        var damageMultiplier = GetDamageMultiplier(effects);
        var baseAmount = application.PeriodicPotency.Value * calibration.DamagePerPotency.Value * damageMultiplier;
        baseAmount = ReconstructBaseAmount(baseAmount, application.BaseDamageLowByte);

        var baseRates = application.SourceBaseRates is null
            ? calibration.BaseRates
            : new BaseRates(
                application.SourceBaseRates.Critical,
                application.SourceBaseRates.DirectHit);
        var criticalBuffRate = effects
            .Where(effect => effect.Kind == RaidBuffEffectKind.CriticalChance)
            .Sum(effect => effect.Amount);
        var directHitBuffRate = effects
            .Where(effect => effect.Kind == RaidBuffEffectKind.DirectHitChance)
            .Sum(effect => effect.Amount);
        var expectedCriticalRate = Math.Clamp(baseRates.Critical + criticalBuffRate, 0.0, 1.0);
        var criticalRate = ResolveCriticalRate(
            application.CriticalRateLowByte,
            expectedCriticalRate);
        var directHitRate = Math.Clamp(baseRates.DirectHit + directHitBuffRate, 0.0, 1.0);
        var criticalMultiplier = GetCriticalMultiplier(
            Math.Clamp(criticalRate - criticalBuffRate, 0.05, 0.95));
        var inputs = new PeriodicDamageEstimateInputs(calibration.DamagePerPotency.Value, application.PeriodicPotency.Value,
            damageMultiplier, application.BaseDamageLowByte, baseAmount, criticalRate, directHitRate, criticalMultiplier)
        {
            CalibrationSampleCount = calibration.PotencySamples,
            CriticalSampleCount = calibration.CriticalSamples,
            DirectHitSampleCount = calibration.DirectHitSamples,
            CalibrationBaseRates = new DamageBaseRateSnapshot(calibration.BaseRates.Critical, calibration.BaseRates.DirectHit),
            UsedKnownAttributes = application.SourceBaseRates is not null || calibration.UsedKnownAttributes,
        };
        return double.IsFinite(inputs.ExpectedAmount) && inputs.ExpectedAmount >= 0.0
            ? inputs
            : null;
    }

    internal static double ReconstructBaseAmount(double estimate, byte? lowByte)
    {
        var reconstructed = Math.Truncate(estimate);
        if (lowByte is not null && reconstructed >= lowByte.Value)
        {
            var buckets = Math.Clamp(Math.Round((reconstructed - lowByte.Value) / 256.0,
                MidpointRounding.ToEven), 0.0, byte.MaxValue);
            reconstructed = buckets * 256.0 + lowByte.Value;
        }
        return reconstructed > 4096.0 ? reconstructed + 256.0 : reconstructed;
    }

    private BaseRates GetBaseRates(string sourceKey)
    {
        if (!sourceHitRateSamples.TryGetValue(sourceKey, out var samples))
        {
            return BaseRates.Default;
        }

        return new BaseRates(
            samples.KnownCriticalRate ?? (samples.CriticalSwings >= MinimumObservedRateSamples
                ? Math.Clamp(samples.CriticalHits / (double)samples.CriticalSwings, 0.05, 0.95)
                : DefaultCriticalRate),
            samples.KnownDirectHitRate ?? DamageBaseRatePolicy.EstimateObserved(
                samples.DirectHits, samples.DirectHitSwings, critical: false));
    }

    private static bool HasAttributeChange(IReadOnlyList<DamageStatusSnapshot> statuses) =>
        statuses.Any(status => status.RemainingTime > 0 && PersonalDamageModifierPolicy.ChangesAttributes(status.StatusId));

    private static bool HasUnknownDamageModifier(IReadOnlyList<DamageStatusSnapshot> statuses) =>
        statuses.Any(status => status.RemainingTime > 0 && PersonalDamageModifierPolicy.HasUnknownStrength(status));

    private static double ResolveCriticalRate(byte? lowByte, double expectedRate)
    {
        if (lowByte is null)
        {
            return expectedRate;
        }

        return DamageBaseRatePolicy.ResolveCriticalLowByte(lowByte.Value, expectedRate);
    }

    private static double GetCriticalMultiplier(double baseCriticalRate)
    {
        return Math.Max(1.4, 1.35 + baseCriticalRate);
    }

    private static IReadOnlyList<RaidBuffEffect> GetApplicableEffects(
        IReadOnlyList<DamageStatusSnapshot> sourceStatuses,
        IReadOnlyList<DamageStatusSnapshot> targetStatuses,
        DamageActorIdentity recipient,
        uint actionCategoryId,
        byte damageType,
        byte elementType)
    {
        var effects = new List<RaidBuffEffect>();
        var seen = new HashSet<(uint StatusId, RaidBuffEffectKind Kind, string SourceKey)>();
        AddApplicableEffects(
            sourceStatuses,
            false,
            recipient,
            actionCategoryId,
            damageType,
            elementType,
            effects,
            seen);
        AddApplicableEffects(
            targetStatuses,
            true,
            recipient,
            actionCategoryId,
            damageType,
            elementType,
            effects,
            seen);
        return effects;
    }

    private static void AddApplicableEffects(
        IReadOnlyList<DamageStatusSnapshot> statuses,
        bool isTargetStatus,
        DamageActorIdentity recipient,
        uint actionCategoryId,
        byte damageType,
        byte elementType,
        ICollection<RaidBuffEffect> effects,
        ISet<(uint StatusId, RaidBuffEffectKind Kind, string SourceKey)> seen)
    {
        foreach (var status in statuses.Where(status => status.RemainingTime > 0.0f))
        {
            foreach (var effect in RaidBuffPolicy.GetEffects(status, isTargetStatus, recipient))
            {
                var sourceKey = GetActorKey(effect.Source);
                if (RaidBuffPolicy.AppliesToDamage(effect, damageType, elementType) &&
                    seen.Add((effect.StatusId, effect.Kind, sourceKey)))
                {
                    effects.Add(effect);
                }
            }

            if (isTargetStatus)
            {
                continue;
            }

            foreach (var effect in PersonalDamageModifierPolicy.GetEffects(
                         status,
                         actionCategoryId,
                         damageType))
            {
                var sourceKey = GetActorKey(effect.Source);
                if (seen.Add((effect.StatusId, effect.Kind, sourceKey)))
                {
                    effects.Add(effect);
                }
            }
        }
    }

    private static double GetDamageMultiplier(IReadOnlyList<RaidBuffEffect> effects)
    {
        var multiplier = effects
            .Where(effect => effect.Kind == RaidBuffEffectKind.DamageMultiplier)
            .Aggregate(1.0, (total, effect) => total * (1.0 + effect.Amount));
        return Math.Max(0.0, multiplier);
    }

    private static string GetActorKey(DamageActorIdentity actor)
    {
        return actor.EntityId != 0
            ? $"entity:{actor.EntityId:X8}"
            : $"name:{actor.Name}";
    }

    private void Prune(DateTime now)
    {
        foreach (var retired in statusHistory.Where(status => status.RemovedAtUtc is { } removed &&
                     removed.AddSeconds(PeriodicStatusRetentionSeconds) < now).ToList())
        {
            learnedApplicationTicks.Remove(GetApplicationKey(retired));
            statusHistory.Remove(retired);
        }
        foreach (var key in statuses
                     .Where(entry => entry.Value.RetainUntilUtc < now ||
                         entry.Value.LateTickConsumed &&
                         entry.Value.NominalExpiresAtUtc.AddSeconds(NormalStatusExpiryGraceSeconds) < now ||
                         entry.Value.RemovedAtUtc is { } removedAtUtc &&
                         removedAtUtc.AddSeconds(PeriodicStatusRetentionSeconds) < now)
                     .Select(entry => entry.Key)
                     .ToList())
        {
            RemoveTrackedStatus(key);
        }
    }

    private void RemoveTrackedStatus(StatusKey key)
    {
        if (!statuses.Remove(key, out var status))
        {
            return;
        }

        learnedApplicationTicks.Remove(GetApplicationKey(status));
    }

    private static ApplicationKey GetApplicationKey(TrackedStatus status)
    {
        return new ApplicationKey(
            status.Application.Target.EntityId,
            status.Application.Source.EntityId,
            status.Application.StatusId,
            status.ApplicationGeneration);
    }

    private readonly record struct StatusKey(uint TargetEntityId, uint StatusId, uint SourceEntityId);

    private readonly record struct ApplicationKey(
        uint TargetEntityId,
        uint SourceEntityId,
        uint StatusId,
        long ApplicationGeneration);

    private readonly record struct ProfileKey(
        uint TargetIdentity,
        uint SourceEntityId,
        uint StatusId,
        string SnapshotKey,
        ushort? DamageDownParameter);

    private readonly record struct AllocationWeight(
        double Value,
        PeriodicAllocationBasis Basis);

    private readonly record struct PeriodicAllocation(
        uint Amount,
        double Weight,
        PeriodicAllocationBasis Basis);

    private static ProfileKey GetProfileKey(DamageStatusApplication application)
    {
        return new ProfileKey(
            application.Target.BaseId != 0 ? application.Target.BaseId : application.Target.EntityId,
            application.Source.EntityId,
            application.StatusId,
            application.SnapshotKey,
            application.SourceStatuses.FirstOrDefault(status => status.StatusId == 0xB5F && status.RemainingTime > 0)?.Parameter);
    }

    private bool IsGroundDamageStatus(uint statusId)
    {
        return GroundDamageStatusPolicy.IsKnown(statusId) || observedGroundDamageStatusIds.Contains(statusId);
    }

    private static bool IsActiveAt(
        TrackedStatus status,
        DateTime eventAtUtc,
        bool allowDeferredApplication,
        bool allowLatePeriodicTick)
    {
        var applicationCutoff = allowDeferredApplication
            ? eventAtUtc.AddSeconds(DeferredStatusArrivalToleranceSeconds)
            : eventAtUtc;
        if (status.Application.SeenAtUtc > applicationCutoff ||
            status.RemovedAtUtc is { } removedAtUtc && eventAtUtc > removedAtUtc)
        {
            return false;
        }

        if (eventAtUtc <= status.NominalExpiresAtUtc.AddSeconds(NormalStatusExpiryGraceSeconds))
        {
            return true;
        }

        return allowLatePeriodicTick &&
            !status.LateTickConsumed &&
            eventAtUtc <= status.RetainUntilUtc;
    }

    private static void ConsumeLateTickIfNeeded(TrackedStatus? status, DateTime tickAtUtc)
    {
        if (status is not null &&
            tickAtUtc > status.NominalExpiresAtUtc.AddSeconds(NormalStatusExpiryGraceSeconds))
        {
            status.LateTickConsumed = true;
        }
    }

    private sealed class TrackedStatus(
        DamageStatusApplication application,
        DateTime nominalExpiresAtUtc,
        DateTime retainUntilUtc,
        long applicationGeneration)
    {
        public DamageStatusApplication Application { get; set; } = application;

        public DateTime NominalExpiresAtUtc { get; set; } = nominalExpiresAtUtc;

        public DateTime RetainUntilUtc { get; set; } = retainUntilUtc;

        public DateTime? LastTickAtUtc { get; set; }

        public int TickCount { get; set; }

        public DateTime? RemovedAtUtc { get; set; }

        public bool LateTickConsumed { get; set; }

        public bool AwaitingStatusConfirmation { get; set; } = IsPendingActionApplication(application);

        public DateTime? ActivatedAtUtc { get; set; } = IsPendingActionApplication(application) ? null : application.SeenAtUtc;

        public long ApplicationGeneration { get; set; } = applicationGeneration;

        public double? IndependentEstimate { get; set; }

        public PeriodicDamageEstimateInputs? EstimateInputs { get; set; }

        public CalibrationSnapshot Calibration { get; set; } = new(null, BaseRates.Default);

        public PeriodicDirectHitSnapshot? CompatibilityDirectHit { get; set; }
    }

    private sealed record CalibrationSnapshot(double? DamagePerPotency, BaseRates BaseRates,
        int PotencySamples = 0, int CriticalSamples = 0, int DirectHitSamples = 0, bool UsedKnownAttributes = false);

    private sealed class TickSamples
    {
        private readonly Queue<uint> values = new();

        public double Median => PeriodicDamageTracker.Median(values.Select(value => (double)value));

        public void Add(uint amount)
        {
            values.Enqueue(amount);
            while (values.Count > MaximumLearnedTickSamples)
            {
                values.Dequeue();
            }
        }
    }

    private sealed class PotencySamples
    {
        private readonly Queue<double> values = new();

        public int Count => values.Count;

        public double Median => PeriodicDamageTracker.Median(values);

        public void Add(double value)
        {
            values.Enqueue(value);
            while (values.Count > MaximumPotencySamples)
            {
                values.Dequeue();
            }
        }
    }

    private sealed class HitRateSamples
    {
        public required DamageActorIdentity Identity { get; set; }

        public double? KnownCriticalRate { get; set; }

        public double? KnownDirectHitRate { get; set; }

        public int CriticalSwings { get; set; }

        public int CriticalHits { get; set; }

        public int DirectHitSwings { get; set; }

        public int DirectHits { get; set; }
    }

    private sealed record BaseRates(double Critical, double DirectHit)
    {
        public static BaseRates Default { get; } = new(DefaultCriticalRate, DefaultDirectHitRate);
    }
}
