using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterDeaths;

public sealed partial class Plugin
{
    private const float ReplayDebuffRefreshToleranceSeconds = 0.5f;

    private readonly record struct ReplayDebuffTrackingKey(
        string MemberKey,
        uint StatusId,
        uint SourceId);

    private sealed record TrackedReplayDebuff(
        PartyMemberSnapshot Member,
        StatusSnapshot Status,
        DateTime ObservedAtUtc,
        DateTime FirstObservedAtUtc);

    private void TrackRecentReplayDebuffs(IReadOnlyList<PartyMemberSnapshot> members, DateTime now)
    {
        var observedKeys = new HashSet<ReplayDebuffTrackingKey>();
        foreach (var member in members)
        {
            foreach (var status in member.Statuses)
            {
                if (!IsReplayPlayerDebuffStatus(status.Id))
                {
                    continue;
                }

                var key = BuildReplayDebuffTrackingKey(member.MemberKey, status);
                observedKeys.Add(key);
                ApplyReplayDebuffObservation(member, status, now, active: true);
            }
        }

        foreach (var pair in activeReplayDebuffs.ToList())
        {
            if (observedKeys.Contains(pair.Key))
            {
                continue;
            }

            ApplyReplayDebuffObservation(
                pair.Value.Member,
                pair.Value.Status with { RemainingTime = 0.0f },
                now,
                active: false);
        }
    }

    private void CaptureReplayPlayerDebuffStatusChange(
        RawActorControlPacket packet,
        PartyMemberSnapshot member,
        StatusSnapshot status)
    {
        if (!IsReplayPlayerDebuffStatus(status.Id))
        {
            return;
        }

        var active = packet.Category is ActorControlGainEffectCategory or ActorControlUpdateEffectCategory;
        ApplyReplayDebuffObservation(member, status, packet.SeenAtUtc, active);
    }

    private void ApplyReplayDebuffObservation(
        PartyMemberSnapshot member,
        StatusSnapshot observedStatus,
        DateTime seenAtUtc,
        bool active)
    {
        if (ReplayDebuffSourceCorrectionPolicy.CanReconcileStatusSource(observedStatus.Id) &&
            observedStatus.SourceId == 0 &&
            HasRecentKnownReplayDebuffSource(member.MemberKey, observedStatus.Id, seenAtUtc))
        {
            RemoveProvisionalReplayDebuff(member.MemberKey, observedStatus.Id, seenAtUtc);
            return;
        }

        if (ReplayDebuffSourceCorrectionPolicy.CanReconcileStatusSource(observedStatus.Id) &&
            active &&
            observedStatus.SourceId != 0)
        {
            RemoveProvisionalReplayDebuff(member.MemberKey, observedStatus.Id, seenAtUtc);
        }

        var key = BuildReplayDebuffTrackingKey(member.MemberKey, observedStatus);
        activeReplayDebuffs.TryGetValue(key, out var tracked);

        if (!active)
        {
            if (tracked is null)
            {
                return;
            }

            var removedStatus = tracked.Status with
            {
                StackCount = observedStatus.StackCount,
                RemainingTime = 0.0f,
            };
            AddRecentReplayDebuffChange(tracked.Member, removedStatus, seenAtUtc, active: false);
            activeReplayDebuffs.Remove(key);
            return;
        }

        var status = NormalizeReplayDebuffObservation(observedStatus, tracked, seenAtUtc);
        var shouldRecord = tracked is null || ReplayDebuffObservationChanged(tracked, status, seenAtUtc);
        activeReplayDebuffs[key] = new TrackedReplayDebuff(
            member,
            status,
            seenAtUtc,
            tracked?.FirstObservedAtUtc ?? seenAtUtc);
        if (shouldRecord)
        {
            AddRecentReplayDebuffChange(member, status, seenAtUtc, active: true);
        }
    }

    private bool HasRecentKnownReplayDebuffSource(string memberKey, uint statusId, DateTime seenAtUtc)
    {
        return activeReplayDebuffs.Any(pair =>
            pair.Key.MemberKey == memberKey &&
            pair.Key.StatusId == statusId &&
            pair.Key.SourceId != 0 &&
            ReplayDebuffSourceCorrectionPolicy.IsWithinCorrectionWindow(pair.Value.FirstObservedAtUtc, seenAtUtc));
    }

    private void RemoveProvisionalReplayDebuff(string memberKey, uint statusId, DateTime seenAtUtc)
    {
        var key = new ReplayDebuffTrackingKey(memberKey, statusId, 0);
        activeReplayDebuffs.TryGetValue(key, out var tracked);
        var firstObservedAtUtc = tracked?.FirstObservedAtUtc ?? recentReplayDebuffs
            .Where(change =>
                change.MemberKey == memberKey &&
                change.Status.Id == statusId &&
                change.Status.SourceId == 0 &&
                change.Active &&
                ReplayDebuffSourceCorrectionPolicy.IsWithinCorrectionWindow(change.SeenAtUtc, seenAtUtc))
            .Select(change => (DateTime?)change.SeenAtUtc)
            .Min();
        if (firstObservedAtUtc is not { } startedAtUtc ||
            !ReplayDebuffSourceCorrectionPolicy.IsWithinCorrectionWindow(startedAtUtc, seenAtUtc))
        {
            return;
        }

        activeReplayDebuffs.Remove(key);
        recentReplayDebuffs.RemoveAll(change =>
            change.MemberKey == memberKey &&
            change.Status.Id == statusId &&
            change.Status.SourceId == 0 &&
            change.SeenAtUtc >= startedAtUtc &&
            change.SeenAtUtc <= seenAtUtc.AddMilliseconds(1));
    }

    private static StatusSnapshot NormalizeReplayDebuffObservation(
        StatusSnapshot status,
        TrackedReplayDebuff? tracked,
        DateTime seenAtUtc)
    {
        if (tracked is null || status.RemainingTime > 0.0f || tracked.Status.RemainingTime <= 0.0f)
        {
            return status;
        }

        var elapsed = MathF.Max(0.0f, (float)(seenAtUtc - tracked.ObservedAtUtc).TotalSeconds);
        return status with
        {
            RemainingTime = MathF.Max(0.0f, tracked.Status.RemainingTime - elapsed),
        };
    }

    private static bool ReplayDebuffObservationChanged(
        TrackedReplayDebuff tracked,
        StatusSnapshot status,
        DateTime seenAtUtc)
    {
        if (tracked.Status.StackCount != status.StackCount ||
            tracked.Status.IconId != status.IconId ||
            !string.Equals(tracked.Status.Name, status.Name, StringComparison.Ordinal))
        {
            return true;
        }

        if (tracked.Status.RemainingTime <= 0.0f)
        {
            return status.RemainingTime > 0.0f;
        }

        if (status.RemainingTime <= 0.0f)
        {
            return false;
        }

        var elapsed = MathF.Max(0.0f, (float)(seenAtUtc - tracked.ObservedAtUtc).TotalSeconds);
        var expectedRemaining = MathF.Max(0.0f, tracked.Status.RemainingTime - elapsed);
        return status.RemainingTime > expectedRemaining + ReplayDebuffRefreshToleranceSeconds;
    }

    private void AddRecentReplayDebuffChange(
        PartyMemberSnapshot member,
        StatusSnapshot status,
        DateTime seenAtUtc,
        bool active)
    {
        recentReplayDebuffs.Add(new ReplayDebuffSnapshot(
            seenAtUtc,
            CalculatePullElapsed(seenAtUtc),
            member.MemberKey,
            member.MemberName,
            member.PartyIndex,
            member.ClassJobId,
            member.ClassJobName,
            status,
            active));
    }

    private static ReplayDebuffTrackingKey BuildReplayDebuffTrackingKey(
        string memberKey,
        StatusSnapshot status)
    {
        return new ReplayDebuffTrackingKey(memberKey, status.Id, status.SourceId);
    }

    private void PruneRecentReplayDebuffs(DateTime now)
    {
        if (recentReplayDebuffs.Count == 0)
        {
            return;
        }

        var cutoff = GetCurrentPullReplayStartAtUtc(now);
        var retainedSeeds = recentReplayDebuffs
            .Where(change => change.SeenAtUtc < cutoff)
            .GroupBy(change => (change.MemberKey, change.Status.Id, change.Status.SourceId))
            .Select(group => group.OrderBy(change => change.SeenAtUtc).Last())
            .Where(change => change.Active)
            .Select(change => CreateReplayDebuffCutoffSeed(change, cutoff))
            .Where(change => change is not null)
            .Cast<ReplayDebuffSnapshot>()
            .ToList();

        recentReplayDebuffs.RemoveAll(change => change.SeenAtUtc < cutoff);
        recentReplayDebuffs.AddRange(retainedSeeds);
        recentReplayDebuffs.Sort((left, right) => left.SeenAtUtc.CompareTo(right.SeenAtUtc));
    }

    private ReplayDebuffSnapshot? CreateReplayDebuffCutoffSeed(
        ReplayDebuffSnapshot change,
        DateTime cutoff)
    {
        var status = change.Status;
        if (status.RemainingTime > 0.0f)
        {
            var elapsed = MathF.Max(0.0f, (float)(cutoff - change.SeenAtUtc).TotalSeconds);
            var remaining = status.RemainingTime - elapsed;
            if (remaining <= 0.05f)
            {
                return null;
            }

            status = status with { RemainingTime = remaining };
        }

        return change with
        {
            SeenAtUtc = cutoff,
            PullElapsedSeconds = CalculatePullElapsed(cutoff),
            Status = status,
        };
    }
}
