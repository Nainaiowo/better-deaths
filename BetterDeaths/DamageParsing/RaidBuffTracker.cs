namespace BetterDeaths.DamageParsing;

using System;
using System.Collections.Generic;
using System.Linq;

internal sealed class RaidBuffTracker(bool confirmedOnly = false)
{
    private const double HistoryRetentionSeconds = 5.0;
    private const double PendingApplicationRetentionSeconds = 35.0;
    private readonly Dictionary<StatusKey, TrackedStatus> statuses = [];
    private readonly List<TrackedStatus> history = [];
    private readonly Dictionary<StatusKey, PendingApplication> pendingApplications = [];
    private readonly Dictionary<(StatusKey Status, uint Sequence), PendingApplication> applicationHistory = [];
    private readonly DamageStatusSlotTracker slots = new();

    public void Observe(DamageStatusApplication application)
    {
        if (!DamageStatusCapturePolicy.IsRelevant(application.StatusId))
        {
            if (slots.Observe(application) is { } replaced)
                ObserveState(replaced);
            return;
        }

        Prune(application.SeenAtUtc);
        var key = new StatusKey(application.Target.EntityId, application.StatusId, application.Source.EntityId);
        if (!application.IsRemoval && (application.ObservationKind == DamageStatusObservationKind.Announcement ||
                confirmedOnly && application.ActionId != 0 && application.DurationSeconds <= 0))
        {
            // Announcements describe the next application, never the currently active one.
            if (application.Source.EntityId != 0 &&
                (!pendingApplications.TryGetValue(key, out var prior) || prior.SeenAtUtc <= application.SeenAtUtc))
            {
                if (prior is not null && application.ApplicationSequence is > 0 &&
                    prior.ApplicationSequence == application.ApplicationSequence && !HasParameter(application))
                    application = application with { Parameter = prior.Parameter, HasParameter = prior.HasParameter };
                if (prior is not null && application.ApplicationSequence is > 0 &&
                    prior.ApplicationSequence == application.ApplicationSequence)
                    application = application with { AppliedParameter = application.AppliedParameter ?? prior.AppliedParameter };
                pendingApplications[key] = new(application.SeenAtUtc, application.ApplicationSequence,
                    application.Parameter, HasParameter(application), application.AppliedParameter);
                applicationHistory[(key, application.ApplicationSequence.GetValueOrDefault())] = pendingApplications[key];
            }
            return;
        }
        if (application.IsRemoval)
        {
            foreach (var pendingKey in pendingApplications.Where(entry =>
                         entry.Key.TargetEntityId == application.Target.EntityId && entry.Key.StatusId == application.StatusId &&
                         (application.Source.EntityId == 0 || entry.Key.SourceEntityId == application.Source.EntityId) &&
                         entry.Value.SeenAtUtc <= application.SeenAtUtc).Select(entry => entry.Key).ToList())
                pendingApplications.Remove(pendingKey);
        }
        var startsApplication = false;
        if (!application.IsRemoval && pendingApplications.TryGetValue(key, out var pending) &&
            pending.SeenAtUtc <= application.SeenAtUtc &&
            application.ObservationKind is DamageStatusObservationKind.Landing or DamageStatusObservationKind.Unspecified &&
            application.ActionId == 0 && HasDuration(application) &&
            (application.ApplicationSequence is null or 0 || pending.ApplicationSequence is null or 0 ||
                application.ApplicationSequence == pending.ApplicationSequence))
        {
            if (!HasParameter(application) && pending.AppliedParameter is null)
                application = application with { Parameter = pending.Parameter, HasParameter = pending.HasParameter };
            application = application with { AppliedParameter = application.AppliedParameter ?? pending.AppliedParameter };
            if (application.ApplicationSequence is null or 0)
                application = application with { ApplicationSequence = pending.ApplicationSequence };
            pendingApplications.Remove(key);
            startsApplication = true;
        }
        if (!application.IsRemoval && application.ObservationKind == DamageStatusObservationKind.Landing &&
            application.AppliedParameter is null && !HasParameter(application) &&
            FindAssociatedApplication(application) is { } associated)
        {
            application = application with
            {
                AppliedParameter = associated.AppliedParameter,
                Parameter = associated.AppliedParameter is null && !HasParameter(application) ? associated.Parameter : application.Parameter,
                HasParameter = associated.AppliedParameter is null && !HasParameter(application) ? associated.HasParameter : application.HasParameter,
                ApplicationSequence = application.ApplicationSequence is > 0 ? application.ApplicationSequence : associated.ApplicationSequence,
            };
        }
        ObserveCore(application, startsApplication);
    }

    private void ObserveCore(DamageStatusApplication application, bool startsApplication = false)
    {
        if (!application.IsRemoval && application.ObservationKind == DamageStatusObservationKind.Observation &&
            application.Target.IsPet && application.Target.OwnerEntityId != 0 && application.Source.EntityId != 0 &&
            RaidBuffPolicy.UsesApplicationParameter(application.StatusId) && HasDuration(application))
            application = AssociateObservedPetBuff(application, ref startsApplication);
        var replaced = slots.Observe(application);
        ObserveState(application, startsApplication);
        if (replaced is not null)
            ObserveState(replaced);
    }

    private DamageStatusApplication AssociateObservedPetBuff(DamageStatusApplication application, ref bool startsApplication)
    {
        var previous = FindActive(application.Target.EntityId);
        if (previous is not null && application.ApplicationSequence is > 0 &&
            application.ApplicationSequence != previous.ApplicationSequence)
            previous = null;

        // Keep the pet's specific application, not whichever buff the owner has on later hits.
        // Explicit pet strength (including zero) takes precedence over inherited metadata.
        startsApplication = true;
        if (application.AppliedParameter is not null || HasParameter(application))
            return application with
            {
                ApplicationSequence = application.ApplicationSequence is > 0 ? application.ApplicationSequence : previous?.ApplicationSequence,
                StatusSlot = application.StatusSlot ?? previous?.StatusSlot,
            };

        var origin = previous;
        if (origin is null || origin.AppliedParameter is null && !HasParameter(origin) && origin.ApplicationSequence is > 0)
        {
            var owner = FindActive(application.Target.OwnerEntityId);
            if (origin is null || owner?.ApplicationSequence == origin.ApplicationSequence)
                origin = owner ?? origin;
        }
        if (origin is null || application.ApplicationSequence is > 0 &&
            application.ApplicationSequence != origin.ApplicationSequence)
            return application;
        return application with
        {
            Source = ChooseMoreCompleteSource(application.Source, origin.Source),
            AppliedParameter = origin.AppliedParameter,
            Parameter = origin.AppliedParameter is null && HasParameter(origin) ? origin.Parameter : application.Parameter,
            HasParameter = origin.AppliedParameter is null && HasParameter(origin) ? true : application.HasParameter,
            ApplicationSequence = application.ApplicationSequence is > 0 ? application.ApplicationSequence : origin.ApplicationSequence,
            StatusSlot = application.StatusSlot ?? previous?.StatusSlot,
        };

        DamageStatusApplication? FindActive(uint targetEntityId)
        {
            var key = new StatusKey(targetEntityId, application.StatusId, application.Source.EntityId);
            if (statuses.TryGetValue(key, out var current) && current.Application.SeenAtUtc <= application.SeenAtUtc)
                return IsActive(current) ? current.Application : null;
            return history.Where(status => status.Application.Target.EntityId == targetEntityId &&
                    status.Application.StatusId == application.StatusId &&
                    status.Application.Source.EntityId == application.Source.EntityId && IsActive(status))
                .OrderByDescending(status => status.Application.SeenAtUtc)
                .Select(status => status.Application).FirstOrDefault();
        }

        bool IsActive(TrackedStatus status) => status.Application.SeenAtUtc <= application.SeenAtUtc &&
            status.ExpiresAtUtc > application.SeenAtUtc &&
            (status.RemovedAtUtc is null || status.RemovedAtUtc > application.SeenAtUtc);
    }

    private PendingApplication? FindAssociatedApplication(DamageStatusApplication application)
    {
        var exact = Find(new StatusKey(application.Target.EntityId, application.StatusId, application.Source.EntityId));
        if (exact is not null)
            return exact;
        var origin = application.StatusId == 0x839
            ? new StatusKey(application.Source.EntityId, 0x71D, application.Source.EntityId)
            : new StatusKey(application.Target.OwnerEntityId, application.StatusId, application.Source.EntityId);
        return origin.TargetEntityId == 0 ? null : Find(origin);

        PendingApplication? Find(StatusKey key) => applicationHistory
            .Where(entry => entry.Key.Status == key && entry.Value.SeenAtUtc <= application.SeenAtUtc &&
                entry.Value.SeenAtUtc.AddSeconds(PendingApplicationRetentionSeconds) >= application.SeenAtUtc &&
                (application.ApplicationSequence is null or 0 || entry.Value.ApplicationSequence is null or 0 ||
                    application.ApplicationSequence == entry.Value.ApplicationSequence))
            .OrderByDescending(entry => entry.Value.SeenAtUtc)
            .Select(entry => entry.Value).FirstOrDefault();
    }

    private void ObserveState(DamageStatusApplication application, bool startsApplication = false)
    {
        if (application.IsRemoval)
        {
            foreach (var status in statuses.Values.Concat(history).Where(status =>
                         status.Application.Target.EntityId == application.Target.EntityId &&
                         status.Application.StatusId == application.StatusId &&
                         status.Application.SeenAtUtc <= application.SeenAtUtc &&
                         (application.ApplicationSequence is null or 0 || status.Application.ApplicationSequence is null or 0 ||
                             application.ApplicationSequence == status.Application.ApplicationSequence) &&
                         (application.Source.EntityId == 0 ||
                             status.Application.Source.EntityId == application.Source.EntityId)))
            {
                if (status.RemovedAtUtc is null || status.RemovedAtUtc > application.SeenAtUtc)
                {
                    status.RemovedAtUtc = application.SeenAtUtc;
                }
            }

            return;
        }

        if (application.Source.EntityId == 0)
        {
            var matchingKeys = statuses.Keys.Where(key => key.TargetEntityId == application.Target.EntityId &&
                key.StatusId == application.StatusId).ToList();
            // Missing identity cannot refresh several different providers at once.
            if (matchingKeys.Count == 1)
            {
                Update(statuses[matchingKeys[0]], application with { Source = statuses[matchingKeys[0]].Application.Source }, startsApplication);
                return;
            }
            if (matchingKeys.Count > 1)
                return;
        }

        if (application.Source.EntityId != 0)
        {
            statuses.Remove(new StatusKey(application.Target.EntityId, application.StatusId, 0));
        }

        var statusKey = new StatusKey(
            application.Target.EntityId,
            application.StatusId,
            application.Source.EntityId);
        if (statuses.TryGetValue(statusKey, out var existing))
        {
            Update(existing, application, startsApplication);
        }
        else if (HasDuration(application))
            statuses[statusKey] = Create(application);
    }

    public void ObserveSnapshots(DamageStatusApplication application)
    {
        if (application.HasSourceStatusSnapshot)
            ObserveSnapshot(application.Source with { EntityId = application.SourceStatusActorId ?? application.Source.EntityId },
                application.SourceStatuses, application.SeenAtUtc);
        if (application.HasTargetStatusSnapshot &&
            (!application.HasSourceStatusSnapshot || application.Target.EntityId != (application.SourceStatusActorId ?? application.Source.EntityId)))
            ObserveSnapshot(application.Target, application.TargetStatuses, application.SeenAtUtc);
    }

    public void ObserveSnapshots(ParsedDamageEvent damageEvent)
    {
        if (damageEvent.HasSourceStatusSnapshot)
            ObserveSnapshot(damageEvent.Source, damageEvent.SourceStatuses, damageEvent.SeenAtUtc);
        var target = damageEvent.PacketTarget ?? damageEvent.Target;
        if (damageEvent.HasTargetStatusSnapshot &&
            (!damageEvent.HasSourceStatusSnapshot || target.EntityId != damageEvent.Source.EntityId))
            ObserveSnapshot(target, damageEvent.TargetStatuses, damageEvent.SeenAtUtc);
    }

    public void ObserveSnapshots(DamageActionPacket packet)
    {
        if (packet.HasSourceStatusSnapshot)
            ObserveSnapshot(packet.Source, packet.SourceStatuses, packet.SeenAtUtc);
        foreach (var target in packet.Targets.DistinctBy(target => target.Target.EntityId))
            if (target.HasTargetStatusSnapshot &&
                (!packet.HasSourceStatusSnapshot || target.Target.EntityId != packet.Source.EntityId))
                ObserveSnapshot(target.Target, target.TargetStatuses, packet.SeenAtUtc);
    }

    private void ObserveSnapshot(DamageActorIdentity target, IReadOnlyList<DamageStatusSnapshot> snapshots, DateTime seenAtUtc)
    {
        if (target.EntityId == 0)
            return;

        Prune(seenAtUtc);
        var present = snapshots.Where(snapshot => DamageStatusCapturePolicy.IsRelevant(snapshot.StatusId)).ToList();
        // A captured status list is evidence of landed buffs, including auras without a gain packet.
        // An absent status retires only history at or before this snapshot, never a later application.
        var current = statuses.Values.Where(status => status.Application.Target.EntityId == target.EntityId).ToList();
        var candidates = current.Any(status => status.Application.SeenAtUtc > seenAtUtc)
            ? current.Concat(history.Where(status => status.Application.Target.EntityId == target.EntityId)) : current;
        foreach (var status in candidates.Where(status => status.Application.SeenAtUtc <= seenAtUtc &&
                     (status.RemovedAtUtc is null || status.RemovedAtUtc > seenAtUtc)))
        {
            if (!present.Any(snapshot => snapshot.StatusId == status.Application.StatusId &&
                    (snapshot.Source.EntityId == 0 || status.Application.Source.EntityId == 0 ||
                        snapshot.Source.EntityId == status.Application.Source.EntityId)))
                status.RemovedAtUtc = seenAtUtc;
        }

        foreach (var snapshot in present)
        {
            if (!float.IsFinite(snapshot.RemainingTime) || snapshot.RemainingTime <= 0)
                continue;
            var source = snapshot.Source;
            if (statuses.TryGetValue(new StatusKey(target.EntityId, snapshot.StatusId, source.EntityId), out var tracked))
                source = ChooseMoreCompleteSource(source, tracked.Application.Source);
            ObserveCore(new DamageStatusApplication(target, source, snapshot.StatusId, string.Empty,
                0, 0, string.Empty, seenAtUtc, snapshot.RemainingTime, false, false, false)
            { Parameter = snapshot.Parameter, HasParameter = snapshot.HasParameter ?? snapshot.Parameter != 0,
                AppliedParameter = snapshot.AppliedParameter, StatusSlot = snapshot.StatusSlot,
                ApplicationSequence = snapshot.ApplicationSequence,
                ObservationKind = DamageStatusObservationKind.Observation });
        }
    }

    public void Refresh(uint targetEntityId, uint statusId, DateTime seenAtUtc)
    {
        Prune(seenAtUtc);
        foreach (var status in statuses.Values.Where(status =>
                     status.Application.Target.EntityId == targetEntityId &&
                     status.Application.StatusId == statusId &&
                     status.Application.SeenAtUtc <= seenAtUtc &&
                     (status.RemovedAtUtc is null || status.RemovedAtUtc > seenAtUtc) &&
                     status.ExpiresAtUtc > seenAtUtc))
        {
            Update(status, status.Application with { SeenAtUtc = seenAtUtc,
                DurationSeconds = (float)GetDefaultDurationSeconds(statusId) });
        }
    }

    public ParsedDamageEvent ApplyFallback(ParsedDamageEvent damageEvent)
    {
        Prune(damageEvent.SeenAtUtc);
        var sourceStatuses = damageEvent.HasSourceStatusSnapshot
            ? Enrich(damageEvent.SourceStatuses, damageEvent.Source.EntityId, damageEvent.SeenAtUtc)
            : GetActive(damageEvent.Source.EntityId, damageEvent.SeenAtUtc);
        var packetTarget = damageEvent.PacketTarget ?? damageEvent.Target;
        var targetStatuses = damageEvent.HasTargetStatusSnapshot
            ? Enrich(damageEvent.TargetStatuses, packetTarget.EntityId, damageEvent.SeenAtUtc)
            : GetActive(packetTarget.EntityId, damageEvent.SeenAtUtc);
        return damageEvent with
        {
            SourceStatuses = sourceStatuses,
            TargetStatuses = targetStatuses,
        };
    }

    public DamageStatusApplication ApplyFallback(DamageStatusApplication application)
    {
        return application with
        {
            SourceStatuses = application.HasSourceStatusSnapshot
                ? Enrich(application.SourceStatuses, application.SourceStatusActorId ?? application.Source.EntityId, application.SeenAtUtc)
                : GetActive(application.SourceStatusActorId ?? application.Source.EntityId, application.SeenAtUtc),
            TargetStatuses = application.HasTargetStatusSnapshot
                ? Enrich(application.TargetStatuses, application.Target.EntityId, application.SeenAtUtc)
                : GetActive(application.Target.EntityId, application.SeenAtUtc),
        };
    }

    public DamageStatusApplication ApplyConfirmed(DamageStatusApplication application) => application with
    {
        SourceStatuses = GetActive(application.SourceStatusActorId ?? application.Source.EntityId, application.SeenAtUtc),
        TargetStatuses = GetActive(application.Target.EntityId, application.SeenAtUtc),
    };

    private IReadOnlyList<DamageStatusSnapshot> Enrich(
        IReadOnlyList<DamageStatusSnapshot> snapshots,
        uint targetEntityId,
        DateTime seenAtUtc)
    {
        if (snapshots.Count == 0 || targetEntityId == 0)
        {
            return snapshots;
        }

        List<DamageStatusSnapshot>? enriched = null;
        for (var index = 0; index < snapshots.Count; index++)
        {
            var snapshot = snapshots[index];
            var tracked = statuses.Values.Concat(history)
                .Where(status => status.Application.Target.EntityId == targetEntityId &&
                    status.Application.StatusId == snapshot.StatusId &&
                    status.Application.SeenAtUtc <= seenAtUtc &&
                    (status.RemovedAtUtc is null || seenAtUtc < status.RemovedAtUtc) &&
                    seenAtUtc < status.ExpiresAtUtc &&
                    (snapshot.Source.EntityId == 0 ||
                        status.Application.Source.EntityId == snapshot.Source.EntityId) &&
                    (snapshot.ApplicationSequence is null or 0 || status.Application.ApplicationSequence == snapshot.ApplicationSequence))
                .OrderByDescending(status => status.Application.SeenAtUtc)
                .FirstOrDefault();
            if (tracked is null)
            {
                continue;
            }

            enriched ??= snapshots.ToList();
            enriched[index] = snapshot with
            {
                Source = ChooseMoreCompleteSource(snapshot.Source, tracked.Application.Source),
                Parameter = HasParameter(tracked.Application)
                    ? tracked.Application.Parameter
                    : snapshot.Parameter,
                HasParameter = HasParameter(tracked.Application) ? true : snapshot.HasParameter,
                AppliedParameter = snapshot.AppliedParameter ?? tracked.Application.AppliedParameter,
                StatusSlot = snapshot.StatusSlot ?? tracked.Application.StatusSlot,
                ApplicationSequence = snapshot.ApplicationSequence ?? tracked.Application.ApplicationSequence,
            };
        }

        return enriched ?? snapshots;
    }

    private static DamageActorIdentity ChooseMoreCompleteSource(
        DamageActorIdentity snapshotSource,
        DamageActorIdentity trackedSource)
    {
        if (snapshotSource.EntityId == 0)
        {
            return trackedSource;
        }

        if (snapshotSource.EntityId != trackedSource.EntityId)
        {
            return snapshotSource;
        }

        return trackedSource.IsPartyMember && !snapshotSource.IsPartyMember ||
            trackedSource.IsPlayer && !snapshotSource.IsPlayer ||
            trackedSource.ClassJobId != 0 && snapshotSource.ClassJobId == 0 ||
            !string.IsNullOrWhiteSpace(trackedSource.Name) && string.IsNullOrWhiteSpace(snapshotSource.Name)
                ? trackedSource
                : snapshotSource;
    }

    public void Clear()
    {
        statuses.Clear();
        history.Clear();
        pendingApplications.Clear();
        applicationHistory.Clear();
        slots.Clear();
    }

    private IReadOnlyList<DamageStatusSnapshot> GetActive(uint targetEntityId, DateTime seenAtUtc)
    {
        if (targetEntityId == 0)
        {
            return [];
        }

        return statuses.Values.Concat(history)
            .Where(status => status.Application.Target.EntityId == targetEntityId &&
                status.Application.SeenAtUtc <= seenAtUtc &&
                (status.RemovedAtUtc is null || seenAtUtc < status.RemovedAtUtc) &&
                seenAtUtc < status.ExpiresAtUtc)
            .GroupBy(status => (status.Application.StatusId, status.Application.Source.EntityId))
            .Select(group => group.OrderByDescending(status => status.Application.SeenAtUtc).First())
            .Select(status => new DamageStatusSnapshot(
                status.Application.StatusId,
                status.Application.Source,
                status.Application.Parameter,
                Math.Max(0.0f, (float)(status.ExpiresAtUtc - seenAtUtc).TotalSeconds))
            {
                HasParameter = HasParameter(status.Application),
                AppliedParameter = status.Application.AppliedParameter,
                StatusSlot = status.Application.StatusSlot,
                ApplicationSequence = status.Application.ApplicationSequence,
            })
            .ToList();
    }

    private static TrackedStatus Create(DamageStatusApplication application)
    {
        return new TrackedStatus(application, application.SeenAtUtc.AddSeconds(application.DurationSeconds));
    }

    private static bool HasParameter(DamageStatusApplication application) => application.HasParameter ?? application.Parameter != 0;

    private static bool HasDuration(DamageStatusApplication application) =>
        float.IsFinite(application.DurationSeconds) && application.DurationSeconds > 0;

    private void Update(TrackedStatus status, DamageStatusApplication application, bool startsApplication = false)
    {
        if (application.SeenAtUtc < status.Application.SeenAtUtc)
        {
            var previous = history.Where(entry =>
                    entry.Application.Target.EntityId == application.Target.EntityId &&
                    entry.Application.Source.EntityId == application.Source.EntityId &&
                    entry.Application.StatusId == application.StatusId &&
                    entry.Application.SeenAtUtc <= application.SeenAtUtc &&
                    entry.ExpiresAtUtc > application.SeenAtUtc &&
                    (entry.RemovedAtUtc is null || entry.RemovedAtUtc > application.SeenAtUtc) &&
                    (application.ApplicationSequence is null or 0 || entry.Application.ApplicationSequence == application.ApplicationSequence))
                .OrderByDescending(entry => entry.Application.SeenAtUtc).FirstOrDefault();
            var sameApplication = !startsApplication && previous is not null &&
                (application.ObservationKind != DamageStatusObservationKind.Landing ||
                    application.ApplicationSequence is > 0 && application.ApplicationSequence == previous.Application.ApplicationSequence);
            if (!HasDuration(application) && !sameApplication)
                return;
            if (sameApplication && !HasParameter(application) && HasParameter(previous!.Application))
                application = application with { Parameter = previous.Application.Parameter, HasParameter = true };
            if (sameApplication)
                application = application with { AppliedParameter = application.AppliedParameter ?? previous!.Application.AppliedParameter,
                    StatusSlot = application.StatusSlot ?? previous!.Application.StatusSlot };
            var earlier = new TrackedStatus(application, HasDuration(application)
                ? application.SeenAtUtc.AddSeconds(application.DurationSeconds) : previous!.ExpiresAtUtc);
            earlier.RemovedAtUtc = sameApplication && previous!.RemovedAtUtc < status.Application.SeenAtUtc
                ? previous.RemovedAtUtc : status.Application.SeenAtUtc;
            history.Add(earlier);
            return;
        }
        var wasActive = (status.RemovedAtUtc is null || status.RemovedAtUtc > application.SeenAtUtc) &&
            application.SeenAtUtc < status.ExpiresAtUtc;
        if (!wasActive && !HasDuration(application))
            return;
        // A new sequenced landing is a replacement even when the previous buff is still active.
        startsApplication |= application.ObservationKind == DamageStatusObservationKind.Landing &&
            !(application.ApplicationSequence is > 0 && application.ApplicationSequence == status.Application.ApplicationSequence);
        if (application.SeenAtUtc > status.Application.SeenAtUtc)
        {
            history.Add(new TrackedStatus(status.Application, status.ExpiresAtUtc)
            {
                RemovedAtUtc = status.RemovedAtUtc ?? application.SeenAtUtc,
            });
        }
        if (!startsApplication && wasActive && !HasParameter(application) && HasParameter(status.Application))
        {
            application = application with { Parameter = status.Application.Parameter, HasParameter = true };
        }
        if (!startsApplication && wasActive)
            application = application with { AppliedParameter = application.AppliedParameter ?? status.Application.AppliedParameter,
                StatusSlot = application.StatusSlot ?? status.Application.StatusSlot };

        var futureRemoval = status.RemovedAtUtc > application.SeenAtUtc ? status.RemovedAtUtc : null;
        var expiresAtUtc = HasDuration(application) ? application.SeenAtUtc.AddSeconds(application.DurationSeconds) : status.ExpiresAtUtc;
        if (!startsApplication && application.ApplicationSequence is null or 0)
            application = application with { ApplicationSequence = status.Application.ApplicationSequence };
        status.Application = application;
        status.ExpiresAtUtc = expiresAtUtc;
        status.RemovedAtUtc = futureRemoval;
    }

    private void Prune(DateTime seenAtUtc)
    {
        slots.Prune(seenAtUtc);
        foreach (var key in applicationHistory.Where(entry =>
                     entry.Value.SeenAtUtc.AddSeconds(PendingApplicationRetentionSeconds) < seenAtUtc)
                     .Select(entry => entry.Key).ToArray())
            applicationHistory.Remove(key);
        if (pendingApplications.Count > 0)
        {
            foreach (var key in pendingApplications.Where(entry =>
                         entry.Value.SeenAtUtc.AddSeconds(PendingApplicationRetentionSeconds) < seenAtUtc)
                         .Select(entry => entry.Key).ToList())
                pendingApplications.Remove(key);
        }
        history.RemoveAll(status => (status.RemovedAtUtc ?? status.ExpiresAtUtc)
            .AddSeconds(HistoryRetentionSeconds) < seenAtUtc);
        foreach (var key in statuses
                     .Where(entry => (entry.Value.RemovedAtUtc ?? entry.Value.ExpiresAtUtc)
                         .AddSeconds(HistoryRetentionSeconds) < seenAtUtc)
                     .Select(entry => entry.Key)
                     .ToList())
        {
            statuses.Remove(key);
        }
    }

    private static double GetDefaultDurationSeconds(uint statusId)
    {
        if (PersonalDamageModifierPolicy.IsRelevantStatus(statusId))
        {
            return PersonalDamageModifierPolicy.GetDefaultDurationSeconds(statusId);
        }

        return JobDamageCalibrationPolicy.IsRelevantStatus(statusId)
            ? JobDamageCalibrationPolicy.GetDefaultDurationSeconds(statusId)
            : RaidBuffPolicy.GetDefaultDurationSeconds(statusId);
    }

    private readonly record struct StatusKey(uint TargetEntityId, uint StatusId, uint SourceEntityId);

    private sealed record PendingApplication(DateTime SeenAtUtc, uint? ApplicationSequence, ushort Parameter, bool HasParameter,
        byte? AppliedParameter);

    private sealed class TrackedStatus(DamageStatusApplication application, DateTime expiresAtUtc)
    {
        public DamageStatusApplication Application { get; set; } = application;

        public DateTime ExpiresAtUtc { get; set; } = expiresAtUtc;

        public DateTime? RemovedAtUtc { get; set; }
    }
}
