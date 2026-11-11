using System;
using System.Collections.Generic;
using System.Linq;
using BetterDeaths.PartyCooldowns;

namespace BetterDeaths;

internal sealed record MitigationUseEvidence(long CaptureId, string Key, string MemberKey, uint CasterEntityId,
    uint ActionSequence, DateTime SeenAtUtc, float PullElapsedSeconds, uint ActionId, string ActionName,
    uint ActionIconId, IReadOnlyList<uint> TargetEntityIds)
{
    public byte CooldownGroup { get; init; }

    public double CooldownSeconds { get; init; }
}

internal sealed record MitigationApplicationEvidence(long CaptureId, uint ActionSequence, DateTime SeenAtUtc,
    uint TargetEntityId, string ProviderMemberKey, StatusSnapshot Status);

internal sealed class MitigationUseTracker
{
    private readonly Dictionary<string, List<MitigationUseEvidence>> uses = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Member, byte Group), List<MitigationUseEvidence>> cooldownUses = [];
    private readonly Dictionary<(string Provider, uint Sequence), List<MitigationApplicationEvidence>> applications = [];

    public bool ObserveUse(MitigationUseEvidence use)
    {
        if (!uses.TryGetValue(use.Key, out var history))
            uses[use.Key] = history = [];
        if (history.Any(value => SameUse(value, use)))
            return false;
        var index = history.FindLastIndex(value => value.SeenAtUtc <= use.SeenAtUtc) + 1;
        history.Insert(index, use);
        if (use.CooldownGroup > 0)
        {
            var key = (use.MemberKey, use.CooldownGroup);
            if (!cooldownUses.TryGetValue(key, out var shared))
                cooldownUses[key] = shared = [];
            if (!shared.Any(value => SameUse(value, use)))
                shared.Insert(shared.FindLastIndex(value => value.SeenAtUtc <= use.SeenAtUtc) + 1, use);
        }
        return true;
    }

    private static bool SameUse(MitigationUseEvidence left, MitigationUseEvidence right) =>
        left.CaptureId == right.CaptureId || left.ActionSequence != 0 && left.ActionSequence == right.ActionSequence &&
            left.ActionId == right.ActionId && left.CasterEntityId == right.CasterEntityId &&
            Math.Abs((left.SeenAtUtc - right.SeenAtUtc).TotalSeconds) <= 5;

    public bool ObserveApplication(MitigationApplicationEvidence application)
    {
        if (application.ActionSequence == 0 || application.TargetEntityId is 0 or 0xE0000000 or uint.MaxValue ||
            string.IsNullOrEmpty(application.ProviderMemberKey))
            return false;
        var key = (application.ProviderMemberKey, application.ActionSequence);
        if (!applications.TryGetValue(key, out var history))
            applications[key] = history = [];
        if (history.Any(value => value.CaptureId == application.CaptureId && value.TargetEntityId == application.TargetEntityId &&
            value.Status.Id == application.Status.Id && value.Status.SourceId == application.Status.SourceId))
            return false;
        var index = history.FindLastIndex(value => value.SeenAtUtc <= application.SeenAtUtc) + 1;
        history.Insert(index, application);
        return true;
    }

    public IReadOnlyList<MitigationUseEvidence> UsesFor(string key) => uses.GetValueOrDefault(key) ?? [];

    public bool IsReadyAt(PossibleMitigationSnapshot candidate, DateTime time, long? excludingCaptureId = null)
    {
        var uses = UsesFor(candidate.Key);
        if (!IsCooldownReady(candidate.MemberKey, candidate.ActionId, candidate.CooldownGroup,
                candidate.CooldownSeconds, candidate.MaxCharges, uses, time, excludingCaptureId))
            return false;
        if (candidate.Prerequisite is not { } prerequisite)
            return true;
        // A delayed application is judged just before its use consumed the enabling buff.
        var excluded = excludingCaptureId is { } id ? uses.FirstOrDefault(use => use.CaptureId == id) : null;
        var checkAt = excluded is not null && excluded.SeenAtUtc <= time && excluded.SeenAtUtc > DateTime.MinValue
            ? excluded.SeenAtUtc.AddTicks(-1) : time;
        if (IsCooldownReady(candidate.MemberKey, prerequisite.ActionId, prerequisite.CooldownGroup,
                prerequisite.CooldownSeconds, prerequisite.MaxCharges, [], checkAt))
            return true;
        var spent = uses.Where(use => use.SeenAtUtc <= checkAt && use.CaptureId != excludingCaptureId)
            .Select(use => new StatusSetSnapshot(use.SeenAtUtc, [])
            {
                IsAuthoritative = true,
                RemovedStatuses = [new(prerequisite.ReadyStatusId, "", 0, 0, 0, 0)],
            });
        return new StatusTimeline(prerequisite.StatusHistory.Concat(spent)).At(checkAt)
            .Any(status => status.Id == prerequisite.ReadyStatusId);
    }

    private bool IsCooldownReady(string memberKey, uint actionId, byte group, double cooldownSeconds,
        int maxCharges, IReadOnlyList<MitigationUseEvidence> fallback, DateTime time, long? excludingCaptureId = null)
    {
        var history = group > 0 ? cooldownUses.GetValueOrDefault((memberKey, group)) ?? [] : fallback;
        var prior = history.Where(use => use.SeenAtUtc <= time && use.CaptureId != excludingCaptureId).ToArray();
        var origin = prior.Length == 0 ? time : prior[0].SeenAtUtc;
        var profile = new PartyCooldownProfile(actionId, group == 0 ? (byte)1 : group, cooldownSeconds, maxCharges);
        var tracker = new PartyCooldownTracker();
        // Preserve What-ifs' initial-ready assumption; replay actual uses with the HUD's recharge rules.
        tracker.ConfirmReady(1, profile, 0);
        foreach (var use in prior)
        {
            var observed = profile with
            {
                ActionId = use.ActionId,
                RecastSeconds = use.CooldownSeconds > 0 ? use.CooldownSeconds : profile.RecastSeconds
            };
            tracker.Observe(1, observed, (use.SeenAtUtc - origin).TotalSeconds, use.ActionSequence);
        }
        return tracker.Get(1, profile, (time - origin).TotalSeconds).IsReady;
    }

    public IReadOnlyList<MitigationApplicationEvidence> ApplicationsFor(MitigationUseEvidence use) =>
        use.ActionSequence == 0 ? [] : applications.GetValueOrDefault((use.MemberKey, use.ActionSequence)) ?? [];

    public void Clear()
    {
        uses.Clear();
        cooldownUses.Clear();
        applications.Clear();
    }
}
