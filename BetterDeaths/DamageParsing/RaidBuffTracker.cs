namespace BetterDeaths.DamageParsing;

using System;
using System.Collections.Generic;
using System.Linq;

internal sealed class RaidBuffTracker
{
    private const double ExpiryGraceSeconds = 1.0;
    private const double HistoryRetentionSeconds = 5.0;
    private readonly Dictionary<StatusKey, TrackedStatus> statuses = [];
    private readonly List<TrackedStatus> history = [];

    public void Observe(DamageStatusApplication application)
    {
        if (!DamageStatusCapturePolicy.IsRelevant(application.StatusId))
        {
            return;
        }

        Prune(application.SeenAtUtc);
        if (application.IsRemoval)
        {
            foreach (var status in statuses.Values.Concat(history).Where(status =>
                         status.Application.Target.EntityId == application.Target.EntityId &&
                         status.Application.StatusId == application.StatusId &&
                         status.Application.SeenAtUtc <= application.SeenAtUtc &&
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

        var matchingKeys = statuses
            .Where(entry => entry.Key.TargetEntityId == application.Target.EntityId &&
                entry.Key.StatusId == application.StatusId)
            .Select(entry => entry.Key)
            .ToList();
        if (application.Source.EntityId == 0 && matchingKeys.Count > 0)
        {
            foreach (var key in matchingKeys)
            {
                Update(statuses[key], application with { Source = statuses[key].Application.Source });
            }

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
            Update(existing, application);
        }
        else
        {
            statuses[statusKey] = Create(application);
        }
    }

    public void Refresh(uint targetEntityId, uint statusId, DateTime seenAtUtc)
    {
        Prune(seenAtUtc);
        foreach (var status in statuses.Values.Where(status =>
                     status.Application.Target.EntityId == targetEntityId &&
                     status.Application.StatusId == statusId))
        {
            status.ExpiresAtUtc = seenAtUtc.AddSeconds(
                GetDefaultDurationSeconds(statusId));
            status.RemovedAtUtc = null;
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
                    seenAtUtc <= status.ExpiresAtUtc.AddSeconds(ExpiryGraceSeconds) &&
                    (snapshot.Source.EntityId == 0 ||
                        status.Application.Source.EntityId == snapshot.Source.EntityId))
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
                Parameter = tracked.Application.Parameter != 0
                    ? tracked.Application.Parameter
                    : snapshot.Parameter,
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
                seenAtUtc <= status.ExpiresAtUtc.AddSeconds(ExpiryGraceSeconds))
            .GroupBy(status => (status.Application.StatusId, status.Application.Source.EntityId))
            .Select(group => group.OrderByDescending(status => status.Application.SeenAtUtc).First())
            .Select(status => new DamageStatusSnapshot(
                status.Application.StatusId,
                status.Application.Source,
                status.Application.Parameter,
                Math.Max(0.0f, (float)(status.ExpiresAtUtc - seenAtUtc).TotalSeconds)))
            .ToList();
    }

    private static TrackedStatus Create(DamageStatusApplication application)
    {
        var duration = application.DurationSeconds > 0.0f
            ? application.DurationSeconds
            : GetDefaultDurationSeconds(application.StatusId);
        return new TrackedStatus(application, application.SeenAtUtc.AddSeconds(duration));
    }

    private void Update(TrackedStatus status, DamageStatusApplication application)
    {
        if (application.SeenAtUtc < status.Application.SeenAtUtc)
        {
            var earlier = Create(application);
            earlier.RemovedAtUtc = status.Application.SeenAtUtc;
            history.Add(earlier);
            return;
        }
        if (application.SeenAtUtc > status.Application.SeenAtUtc)
        {
            history.Add(new TrackedStatus(status.Application, status.ExpiresAtUtc)
            {
                RemovedAtUtc = status.RemovedAtUtc ?? application.SeenAtUtc,
            });
        }
        if (application.Parameter == 0 && status.Application.Parameter != 0)
        {
            application = application with { Parameter = status.Application.Parameter };
        }

        status.Application = application;
        status.ExpiresAtUtc = application.SeenAtUtc.AddSeconds(
            application.DurationSeconds > 0.0f
                ? application.DurationSeconds
                : GetDefaultDurationSeconds(application.StatusId));
        status.RemovedAtUtc = null;
    }

    private void Prune(DateTime seenAtUtc)
    {
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

    private sealed class TrackedStatus(DamageStatusApplication application, DateTime expiresAtUtc)
    {
        public DamageStatusApplication Application { get; set; } = application;

        public DateTime ExpiresAtUtc { get; set; } = expiresAtUtc;

        public DateTime? RemovedAtUtc { get; set; }
    }
}
