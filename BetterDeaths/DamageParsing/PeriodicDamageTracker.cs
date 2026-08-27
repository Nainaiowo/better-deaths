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
    private readonly Dictionary<StatusKey, TrackedStatus> statuses = [];
    private readonly Dictionary<ApplicationKey, TickSamples> learnedApplicationTicks = [];
    private readonly Dictionary<ProfileKey, TickSamples> learnedProfileTicks = [];
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
            return [CreateEvent(tick, null, unknownSource, tick.Amount, DamageAttributionQuality.Unattributed, 0)];
        }

        if (candidates.Count == 1)
        {
            var candidate = candidates[0];
            candidate.LastTickAtUtc = tick.SeenAtUtc;
            ConsumeLateTickIfNeeded(candidate, tick.SeenAtUtc);
            Learn(candidate, tick.Amount);
            return [CreateEvent(
                tick,
                candidate.Application,
                candidate.Application.Source,
                tick.Amount,
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
            events.Add(CreateEvent(
                tick,
                candidate.Application,
                candidate.Application.Source,
                allocations[index],
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
}
