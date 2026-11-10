using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterDeaths;

public sealed partial class Plugin
{
    private readonly PlayerStatusHistory playerStatusHistory = new();
    private readonly SourceMitigationHistory sourceMitigationHistory = new();
    private readonly Dictionary<uint, StatusSourceSnapshot> knownStatusSources = [];
    private DateTime lastMitigationStatusPollAtUtc;

    private bool ShouldCaptureReviewStatusHistory() => CaptureTimingPolicy.ShouldCaptureReviewStatusHistory(
        IsDutyCaptureActive(), IsPvPCaptureBlocked(), Configuration.CapturePartyDeaths || Configuration.CaptureOtherDeaths);

    private void CaptureReviewStatusHistory(IReadOnlyList<RawActionEffectPacket> actions,
        IReadOnlyList<RawEffectResultPacket> results, IReadOnlyList<RawActorControlPacket> controls, IReadOnlyList<RawStatusTimingUpdate> timing)
    {
        if (actions.Count == 0 && results.Count == 0 && controls.Count == 0 && timing.Count == 0 || !ShouldCaptureReviewStatusHistory())
            return;
        // Only review action snapshots are complete; damage-only action snapshots are filtered.
        var packets = actions.Where(value => value.CaptureForReview || value.CaptureForDamageParsing).Select(value => (value.DamageCaptureOrder, Packet: (object)value))
            .Concat(results.Where(value => value.CaptureForReview || value.CaptureForDamageParsing).Select(value => (value.DamageCaptureOrder, Packet: (object)value)))
            .Concat(controls.Where(value => value.CaptureForReview || value.CaptureForDamageParsing).Select(value => (value.DamageCaptureOrder, Packet: (object)value)))
            .Concat(timing.Where(value => value.CaptureForReview).Select(value => (value.DamageCaptureOrder, Packet: (object)value)));
        foreach (var entry in packets.OrderBy(value => value.DamageCaptureOrder))
        {
            switch (entry.Packet)
            {
                case RawActionEffectPacket action:
                    TrackPossibleMitigationActionUse(action);
                    if (!action.CaptureForReview)
                        break;
                    var caster = FindCurrentMemberByEntityId(action.CasterEntityId);
                    if (caster is not null && action.SourceSnapshot is not null)
                        ObservePlayerStatusSnapshot(caster, action.SeenAtUtc, GetRelevantDeathStatuses(BuildStatusSnapshots(action.SourceSnapshot)), true);
                    else if (action.SourceSnapshot is not null)
                        TrackRecentSourceMitigationSnapshot(action.CasterEntityId, action.CasterName, action.SeenAtUtc,
                            BuildStatusSnapshots(action.SourceSnapshot), true);
                    foreach (var target in action.Targets)
                    {
                        var member = FindCurrentMemberByTargetId(target.TargetId);
                        if (member is not null && target.TargetSnapshot is not null)
                            ObservePlayerStatusSnapshot(member, action.SeenAtUtc, GetRelevantDeathStatuses(BuildStatusSnapshots(target.TargetSnapshot)), true);
                    }
                    break;
                case RawEffectResultPacket result:
                    var recipient = FindCurrentMemberByEffectResultPacket(result);
                    var gained = BuildEffectResultStatusSnapshots(result);
                    TrackPossibleMitigationApplications(result, gained);
                    if (recipient is not null)
                    {
                        var relevant = GetRelevantDeathStatuses(gained);
                        if (relevant.Count > 0)
                            ObservePlayerStatusSnapshot(recipient, result.SeenAtUtc, relevant, false);
                    }
                    else if (gained.Any(IsBossMitigationStatus))
                    {
                        var targetId = NormalizeActorEntityId(result.TargetId);
                        if (targetId == 0)
                            targetId = NormalizeActorEntityId(result.ActorId);
                        TrackRecentSourceMitigationSnapshot(targetId, GetEntityDisplayName(targetId), result.SeenAtUtc, gained);
                    }
                    break;
                case RawActorControlPacket control:
                    var player = FindCurrentMemberByEntityId(control.EntityId);
                    if (player is null)
                    {
                        CaptureActorControlSourceMitigationStatusChange(control);
                        break;
                    }
                    var lookup = BuildStatusSnapshots(control.TargetSnapshot);
                    if (TryCreateActorControlStatusSnapshot(control, lookup, out var status) && IsRelevantDeathStatus(status))
                        ObservePlayerStatusSnapshot(player, control.SeenAtUtc,
                            control.Category == ActorControlLoseEffectCategory ? [] : [status], false,
                            control.Category == ActorControlLoseEffectCategory ? [status] : [],
                            control.Category == ActorControlUpdateEffectCategory);
                    else if (control.TargetSnapshot is not null && control.Category is ActorControlDotCategory or ActorControlHotCategory)
                        ObservePlayerStatusSnapshot(player, control.SeenAtUtc, GetRelevantDeathStatuses(lookup), true);
                    break;
                case RawStatusTimingUpdate update:
                    var gainedStatus = update.StatusId == 0 ? null : new StatusSnapshot(update.StatusId, GetStatusName(update.StatusId),
                        GetStatusIconId(update.StatusId), update.SourceId, update.Parameter, update.Duration);
                    var previous = update.PreviousStatus;
                    var priorStatus = previous is null ? null : BuildStatusSnapshots([previous]).FirstOrDefault();
                    var change = StatusTimeline.SlotChange(update.SeenAtUtc, priorStatus, gainedStatus);
                    var statusTarget = FindCurrentMemberByEntityId(update.TargetId);
                    if (statusTarget is not null)
                        ObservePlayerStatusSnapshot(statusTarget, update.SeenAtUtc,
                            GetRelevantDeathStatuses(change.Statuses), false,
                            GetRelevantDeathStatuses(change.RemovedStatuses));
                    else
                        TrackRecentSourceMitigationSnapshot(update.TargetId, GetEntityDisplayName(update.TargetId), update.SeenAtUtc,
                            change.Statuses, removed: change.RemovedStatuses);
                    break;
            }
        }
    }

    private void PollMitigationStatusHistory(DateTime observedAtUtc)
    {
        if (!ShouldCaptureReviewStatusHistory() || observedAtUtc - lastMitigationStatusPollAtUtc < HpHistorySampleInterval)
            return;
        lastMitigationStatusPollAtUtc = observedAtUtc;
        foreach (var member in currentMembers)
            if (member.HasWorldObject)
                ObservePlayerStatusSnapshot(member, observedAtUtc, GetRelevantDeathStatuses(member.Statuses), true);
        playerStatusHistory.Prune(GetCurrentPullReplayStartAtUtc(observedAtUtc));
    }

    private void ObservePlayerStatusSnapshot(PartyMemberSnapshot member, DateTime seenAtUtc,
        IReadOnlyList<StatusSnapshot> statuses, bool isComplete, IReadOnlyList<StatusSnapshot>? removed = null, bool isUpdateOnly = false)
    {
        var sources = CaptureStatusSources(statuses);
        playerStatusHistory.Observe(new(seenAtUtc, CalculatePullElapsed(seenAtUtc), member.MemberKey, member.MemberName,
            member.PartyIndex, member.ClassJobId, member.ClassJobName, member.EntityId, statuses)
        {
            IsComplete = isComplete,
            IsUpdateOnly = isUpdateOnly,
            IsAuthoritative = !isComplete,
            RemovedStatuses = removed ?? [],
            Sources = sources,
        });
    }

    private IReadOnlyList<StatusSourceSnapshot> CaptureStatusSources(IEnumerable<StatusSnapshot> statuses)
    {
        var sources = new List<StatusSourceSnapshot>();
        foreach (var id in statuses.Select(status => status.SourceId).Where(id => id != 0).Distinct())
        {
            var member = currentMembers.FirstOrDefault(value => value.EntityId == id);
            if (member is null && !knownStatusSources.ContainsKey(id) &&
                ObjectTable.SearchByEntityId(id) is Dalamud.Game.ClientState.Objects.Types.IBattleNpc pet &&
                pet.BattleNpcKind is Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind.Pet or Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind.Buddy)
                member = currentMembers.FirstOrDefault(value => value.EntityId == NormalizeActorEntityId(pet.OwnerId));
            if (member is not null)
                knownStatusSources[id] = new(id, member.MemberKey, member.MemberName, member.PartyIndex, member.ClassJobId, member.ClassJobName);
            if (knownStatusSources.TryGetValue(id, out var source))
                sources.Add(source);
        }
        return sources;
    }

    private IReadOnlyList<PlayerStatusSnapshot> GetPlayerStatusHistory(string memberKey, DateTime endAtUtc) =>
        playerStatusHistory.Between(endAtUtc.AddSeconds(-LeadUpTimingPolicy.CaptureSeconds), endAtUtc, memberKey);

    private IReadOnlyList<PlayerStatusSnapshot> GetCurrentPullReplayStatusHistory(DateTime endAtUtc) =>
        playerStatusHistory.Between(GetCurrentPullReplayStartAtUtc(endAtUtc), endAtUtc)
            .Select(value => value with { PullElapsedSeconds = CalculatePullElapsed(value.SeenAtUtc) }).ToArray();

    private IReadOnlyList<SourceMitigationSnapshot> GetCurrentPullReplaySourceStatusHistory(DateTime endAtUtc) =>
        sourceMitigationHistory.Between(GetCurrentPullReplayStartAtUtc(endAtUtc), endAtUtc)
            .Select(value => value with { PullElapsedSeconds = CalculatePullElapsed(value.SeenAtUtc) }).ToArray();
}
