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
    private readonly Dictionary<ApplicationKey, TickSamples> learnedApplicationTicks = [];
    private readonly Dictionary<ProfileKey, TickSamples> learnedProfileTicks = [];
    private readonly Dictionary<string, PotencySamples> sourcePotencySamples = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HitRateSamples> sourceHitRateSamples = new(StringComparer.Ordinal);
    private readonly HashSet<uint> observedGroundDamageStatusIds = [];
    private long nextApplicationGeneration;

    public void Observe(DamageStatusApplication application)
    {
        if (application.IsRemoval)
        {
            Retire(application.Target.EntityId, application.StatusId, application.Source.EntityId, application.SeenAtUtc);
            return;
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

        statuses[key] = new TrackedStatus(
            application,
            nominalExpiresAtUtc,
            retainUntilUtc,
            ++nextApplicationGeneration);
    }

    public void Retire(uint targetEntityId, uint statusId, uint sourceEntityId, DateTime removedAtUtc)
    {
        foreach (var status in statuses
                     .Where(entry => entry.Key.TargetEntityId == targetEntityId &&
                         entry.Key.StatusId == statusId &&
                         (sourceEntityId == 0 || entry.Key.SourceEntityId == sourceEntityId))
                     .Select(entry => entry.Value))
        {
            status.RemovedAtUtc = removedAtUtc;
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
            AttributionQuality = candidates.Count == 1
                ? DamageAttributionQuality.Exact
                : DamageAttributionQuality.Estimated,
            StatusId = selected.StatusId,
            StatusIconId = selected.StatusIconId,
        };
    }

    public void ObserveDirectDamage(IEnumerable<ParsedDamageEvent> damageEvents)
    {
        foreach (var damageEvent in damageEvents.Where(damageEvent =>
                     !damageEvent.IsPeriodic &&
                     damageEvent.Outcome == DamageEventOutcome.Damage &&
                     damageEvent.Amount > 0))
        {
            var source = damageEvent.AttributedSource ?? damageEvent.Source;
            if ((!source.IsPlayer && !source.IsPartyMember) ||
                source.IsLimitBreak ||
                damageEvent.Source.IsPet)
            {
                continue;
            }

            var sourceKey = GetActorKey(source);
            if (!sourceHitRateSamples.TryGetValue(sourceKey, out var rateSamples))
            {
                rateSamples = new HitRateSamples();
                sourceHitRateSamples[sourceKey] = rateSamples;
            }

            var effects = GetApplicableEffects(
                damageEvent.SourceStatuses,
                damageEvent.TargetStatuses,
                source,
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

            if (!damageEvent.CanCalibratePotency ||
                damageEvent.DirectPotency is not > 0.0 ||
                damageEvent.Blocked ||
                damageEvent.Parried ||
                damageEvent.Critical ||
                damageEvent.DirectHit)
            {
                continue;
            }

            var amount = (double)damageEvent.Amount;
            amount /= effects
                .Where(effect => effect.Kind == RaidBuffEffectKind.DamageMultiplier)
                .Aggregate(1.0, (multiplier, effect) => multiplier * (1.0 + effect.Amount));
            var potencyMultiplier = amount / damageEvent.DirectPotency.Value;
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
                    matchingStatus?.Application,
                    tick.Source,
                    tick.Amount,
                    tick.Amount,
                    DamageAttributionQuality.Exact,
                    0)];
            }

            if (matchingStatuses.Count > 0)
            {
                var selectedStatus = matchingStatuses[0];
                selectedStatus.LastTickAtUtc = tick.SeenAtUtc;
                ConsumeLateTickIfNeeded(selectedStatus, tick.SeenAtUtc);
                var selected = selectedStatus.Application;
                return [CreateEvent(
                    tick,
                    selected,
                    selected.Source,
                    tick.Amount,
                    tick.Amount,
                    matchingStatuses.Count == 1
                        ? DamageAttributionQuality.Exact
                        : DamageAttributionQuality.Estimated,
                    0)];
            }

            var unknownGroundSource = CreateUnknownSource(!tick.Target.IsPlayer);
            return [CreateEvent(
                tick,
                null,
                unknownGroundSource,
                tick.Amount,
                tick.Amount,
                DamageAttributionQuality.Unattributed,
                0)];
        }

        var candidates = statuses.Values
            .Where(status => IsEligibleForTick(status, tick))
            .OrderBy(status => status.Application.Source.EntityId)
            .ThenBy(status => status.Application.StatusId)
            .ToList();
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
                0)];
        }

        if (candidates.Count == 1)
        {
            var candidate = candidates[0];
            candidate.LastTickAtUtc = tick.SeenAtUtc;
            ConsumeLateTickIfNeeded(candidate, tick.SeenAtUtc);
            Learn(candidate, tick.Amount);
            var meterAmount = EstimateMeterTick(candidate) ?? tick.Amount;
            return [CreateEvent(
                tick,
                candidate.Application,
                candidate.Application.Source,
                tick.Amount,
                meterAmount,
                DamageAttributionQuality.Exact,
                0)];
        }

        var allocations = Allocate(tick.Amount, candidates);
        var events = new List<ParsedDamageEvent>(candidates.Count);
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            if (allocations[index] == 0)
            {
                continue;
            }

            candidate.LastTickAtUtc = tick.SeenAtUtc;
            ConsumeLateTickIfNeeded(candidate, tick.SeenAtUtc);
            var learnedAmount = GetLearnedWeight(candidate);
            var meterAmount = EstimateMeterTick(candidate) ??
                (learnedAmount > 0.0 ? learnedAmount : allocations[index]);
            events.Add(CreateEvent(
                tick,
                candidate.Application,
                candidate.Application.Source,
                allocations[index],
                meterAmount,
                DamageAttributionQuality.Estimated,
                index));
        }

        return events;
    }

    public void Clear()
    {
        statuses.Clear();
        learnedApplicationTicks.Clear();
        learnedProfileTicks.Clear();
        sourcePotencySamples.Clear();
        sourceHitRateSamples.Clear();
        nextApplicationGeneration = 0;
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

    private uint[] Allocate(uint amount, IReadOnlyList<TrackedStatus> candidates)
    {
        var weights = candidates
            .Select(GetLearnedWeight)
            .ToArray();
        if (weights.All(weight => weight <= 0.0))
        {
            Array.Fill(weights, 1.0);
        }
        else
        {
            var knownMedian = Median(weights.Where(weight => weight > 0.0));
            for (var index = 0; index < weights.Length; index++)
            {
                if (weights[index] <= 0.0)
                {
                    weights[index] = knownMedian;
                }
            }
        }

        var totalWeight = weights.Sum();
        var allocations = new uint[candidates.Count];
        var fractions = new (int Index, double Fraction)[candidates.Count];
        ulong allocated = 0;
        for (var index = 0; index < candidates.Count; index++)
        {
            var exact = amount * weights[index] / totalWeight;
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

        return allocations;
    }

    private void Learn(TrackedStatus status, uint amount)
    {
        AddLearnedSample(learnedApplicationTicks, GetApplicationKey(status), amount);
        if (!string.IsNullOrWhiteSpace(status.Application.SnapshotKey))
        {
            AddLearnedSample(learnedProfileTicks, GetProfileKey(status.Application), amount);
        }
    }

    private double GetLearnedWeight(TrackedStatus status)
    {
        if (learnedApplicationTicks.TryGetValue(GetApplicationKey(status), out var applicationSamples))
        {
            return applicationSamples.Median;
        }

        return !string.IsNullOrWhiteSpace(status.Application.SnapshotKey) &&
            learnedProfileTicks.TryGetValue(GetProfileKey(status.Application), out var profileSamples)
                ? profileSamples.Median
                : 0.0;
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
        var isDuplicateObservation = existing.RemovedAtUtc is null &&
            Math.Abs((application.SeenAtUtc - existing.Application.SeenAtUtc).TotalSeconds) <=
                DuplicateApplicationWindowSeconds;
        if (isDuplicateObservation)
        {
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
                SourceStatuses = existingApplication.HasSourceStatusSnapshot
                    ? existingApplication.SourceStatuses
                    : application.SourceStatuses,
                TargetStatuses = existingApplication.HasTargetStatusSnapshot
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
            learnedApplicationTicks.Remove(GetApplicationKey(existing));
            existing.ApplicationGeneration = ++nextApplicationGeneration;
            existing.LastTickAtUtc = null;
        }

        existing.Application = application;
        existing.NominalExpiresAtUtc = nominalExpiresAtUtc;
        existing.RetainUntilUtc = retainUntilUtc;
        existing.RemovedAtUtc = null;
        existing.LateTickConsumed = false;
    }

    private static ParsedDamageEvent CreateEvent(
        PeriodicDamageTick tick,
        DamageStatusApplication? status,
        DamageActorIdentity source,
        uint amount,
        double meterAmount,
        DamageAttributionQuality quality,
        int allocationIndex)
    {
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
            AttributedSource = source,
            AttributionQuality = quality,
            IsPeriodic = true,
            DamageType = status?.DamageType ?? 0,
            ElementType = status?.ElementType ?? 0,
            StatusId = statusId,
            StatusIconId = statusIconId,
            SourceStatuses = status?.SourceStatuses ?? [],
            TargetStatuses = status?.TargetStatuses ?? [],
            HasSourceStatusSnapshot = status?.HasSourceStatusSnapshot ?? false,
            HasTargetStatusSnapshot = status?.HasTargetStatusSnapshot ?? false,
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

    private double? EstimateMeterTick(TrackedStatus status)
    {
        var application = status.Application;
        if (application.PeriodicPotency is not > 0.0)
        {
            return null;
        }

        var sourceKey = GetActorKey(application.Source);
        if (!sourcePotencySamples.TryGetValue(sourceKey, out var potencySamples) ||
            potencySamples.Median is not > 0.0)
        {
            return null;
        }

        var effects = GetApplicableEffects(
            application.SourceStatuses,
            application.TargetStatuses,
            application.Source,
            application.DamageType,
            application.ElementType);
        var baseAmount = application.PeriodicPotency.Value * potencySamples.Median;
        baseAmount *= effects
            .Where(effect => effect.Kind == RaidBuffEffectKind.DamageMultiplier)
            .Aggregate(1.0, (multiplier, effect) => multiplier * (1.0 + effect.Amount));
        baseAmount = ReconstructBaseAmount(baseAmount, application.BaseDamageLowByte);

        var baseRates = GetBaseRates(sourceKey);
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
        var expectedAmount = baseAmount *
            (1.0 + (criticalMultiplier - 1.0) * criticalRate) *
            (1.0 + (DirectHitMultiplier - 1.0) * directHitRate);
        return double.IsFinite(expectedAmount) && expectedAmount > 0.0
            ? Math.Round(expectedAmount, MidpointRounding.AwayFromZero)
            : null;
    }

    private static double ReconstructBaseAmount(double estimate, byte? lowByte)
    {
        if (lowByte is null || estimate < lowByte.Value)
        {
            return estimate;
        }

        var highByte = Math.Clamp(
            Math.Round((estimate - lowByte.Value) / 256.0, MidpointRounding.AwayFromZero),
            0.0,
            byte.MaxValue);
        var reconstructed = highByte * 256.0 + lowByte.Value;
        return reconstructed > 4096.0 ? reconstructed + 256.0 : reconstructed;
    }

    private BaseRates GetBaseRates(string sourceKey)
    {
        if (!sourceHitRateSamples.TryGetValue(sourceKey, out var samples))
        {
            return BaseRates.Default;
        }

        return new BaseRates(
            samples.CriticalSwings >= MinimumObservedRateSamples
                ? Math.Clamp(samples.CriticalHits / (double)samples.CriticalSwings, 0.05, 0.95)
                : DefaultCriticalRate,
            samples.DirectHitSwings >= MinimumObservedRateSamples
                ? Math.Clamp(samples.DirectHits / (double)samples.DirectHitSwings, 0.05, 0.95)
                : DefaultDirectHitRate);
    }

    private static double ResolveCriticalRate(byte? lowByte, double expectedRate)
    {
        if (lowByte is null)
        {
            return expectedRate;
        }

        var rate = lowByte.Value / 1000.0;
        while (expectedRate - rate > 0.125)
        {
            rate += 0.255;
        }

        return rate is >= 0.0 and <= 1.0 ? rate : expectedRate;
    }

    private static double GetCriticalMultiplier(double baseCriticalRate)
    {
        return Math.Max(1.4, 1.35 + baseCriticalRate);
    }

    private static IReadOnlyList<RaidBuffEffect> GetApplicableEffects(
        IReadOnlyList<DamageStatusSnapshot> sourceStatuses,
        IReadOnlyList<DamageStatusSnapshot> targetStatuses,
        DamageActorIdentity recipient,
        byte damageType,
        byte elementType)
    {
        var effects = new List<RaidBuffEffect>();
        var seen = new HashSet<(uint StatusId, RaidBuffEffectKind Kind, string SourceKey)>();
        AddApplicableEffects(sourceStatuses, false, recipient, damageType, elementType, effects, seen);
        AddApplicableEffects(targetStatuses, true, recipient, damageType, elementType, effects, seen);
        return effects;
    }

    private static void AddApplicableEffects(
        IReadOnlyList<DamageStatusSnapshot> statuses,
        bool isTargetStatus,
        DamageActorIdentity recipient,
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
        }
    }

    private static string GetActorKey(DamageActorIdentity actor)
    {
        return actor.EntityId != 0
            ? $"entity:{actor.EntityId:X8}"
            : $"name:{actor.Name}";
    }

    private void Prune(DateTime now)
    {
        foreach (var key in statuses
                     .Where(entry => entry.Value.RetainUntilUtc < now ||
                         entry.Value.LateTickConsumed &&
                         entry.Value.NominalExpiresAtUtc.AddSeconds(NormalStatusExpiryGraceSeconds) < now ||
                         entry.Value.RemovedAtUtc is { } removedAtUtc &&
                         removedAtUtc.AddSeconds(DeferredStatusArrivalToleranceSeconds) < now)
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
        string SnapshotKey);

    private static ProfileKey GetProfileKey(DamageStatusApplication application)
    {
        return new ProfileKey(
            application.Target.BaseId != 0 ? application.Target.BaseId : application.Target.EntityId,
            application.Source.EntityId,
            application.StatusId,
            application.SnapshotKey);
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

        public DateTime? RemovedAtUtc { get; set; }

        public bool LateTickConsumed { get; set; }

        public long ApplicationGeneration { get; set; } = applicationGeneration;
    }

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
