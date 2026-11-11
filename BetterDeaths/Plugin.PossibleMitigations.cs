namespace BetterDeaths;

using System;
using System.Collections.Generic;
using System.Linq;
using BetterDeaths.PartyCooldowns;
using FFXIVClientStructs.FFXIV.Client.Game;
using LuminaAction = Lumina.Excel.Sheets.Action;

public sealed partial class Plugin
{
    private const int MaxPossibleMitigationsPerDeath = 64;

    private readonly MitigationUseTracker possibleMitigationUses = new();
    private DateTime? possibleMitigationTimingChangedAtUtc;
    private readonly Dictionary<(uint Action, byte Level), PartyCooldownProfile> possibleMitigationCooldownProfiles = [];
    private readonly Dictionary<uint, HashSet<byte>> possibleMitigationCooldownGroups = [];

    private HashSet<byte> GetPossibleMitigationCooldownGroups(uint jobId)
    {
        if (possibleMitigationCooldownGroups.TryGetValue(jobId, out var groups))
            return groups;
        var sheet = DataManager.GetExcelSheet<LuminaAction>();
        if (sheet is null)
            return [];
        groups = PossibleMitigationDefinitions.Where(definition => DefinitionMatchesJob(definition, jobId))
            .SelectMany(definition => new[] { definition.ActionId, definition.GrantedByActionId })
            .Where(actionId => actionId != 0)
            .Select(actionId => sheet.GetRowOrDefault(actionId)?.CooldownGroup ?? 0)
            .Where(group => group is > 0 and not 58).ToHashSet();
        possibleMitigationCooldownGroups[jobId] = groups;
        return groups;
    }

    private unsafe PartyCooldownProfile? GetPossibleMitigationCooldownProfile(uint actionId, byte level)
    {
        if (level == 0)
            return null;
        var key = (actionId, level);
        if (possibleMitigationCooldownProfiles.TryGetValue(key, out var cached))
            return cached;
        if (DataManager.GetExcelSheet<LuminaAction>()?.GetRowOrDefault(actionId) is not { } action ||
            action.CooldownGroup is 0 or 58 || action.Recast100ms == 0)
            return null;
        var profile = new PartyCooldownProfile(actionId, action.CooldownGroup,
            PartyCooldownCatalog.RecastAtLevel(actionId, level, action.Recast100ms / 10d),
            Math.Clamp((int)ActionManager.GetMaxCharges(actionId, level), 1, 5));
        possibleMitigationCooldownProfiles[key] = profile;
        return profile;
    }

    private static string BuildPossibleMitigationUseKey(PartyMemberSnapshot member, PossibleMitigationDefinition definition)
    {
        return $"{member.MemberKey}:{definition.Key}";
    }

    private void TrackPossibleMitigationActionUse(RawActionEffectPacket packet)
    {
        var member = FindCurrentMemberByEntityId(packet.CasterEntityId);
        if (member is null || member.MaxHp == 0)
        {
            return;
        }

        var groups = GetPossibleMitigationCooldownGroups(member.ClassJobId);
        if (DataManager.GetExcelSheet<LuminaAction>()?.GetRowOrDefault(packet.ActionId) is not { } action ||
            !groups.Contains(action.CooldownGroup) ||
            GetPossibleMitigationCooldownProfile(packet.ActionId, member.ClassJobLevel) is not { } cooldown)
            return;

        var actionName = GetActionName(packet.ActionId);
        var definitions = PossibleMitigationDefinitions
            .Where(definition => DefinitionMatchesAction(definition, packet.ActionId, actionName))
            .Where(definition => DefinitionMatchesJob(definition, member.ClassJobId))
            .ToList();
        var targets = packet.Targets.Select(target => GetDamageTargetEntityId(target.TargetId)).Where(id => id != 0).Distinct().ToArray();
        // Capture every action sharing the recast, even when it is not a selectable What-if ability.
        var keys = definitions.Count > 0 ? definitions.Select(definition => BuildPossibleMitigationUseKey(member, definition))
            : [$"{member.MemberKey}:cooldown-group:{cooldown.Group}"];
        foreach (var key in keys)
        {
            if (possibleMitigationUses.ObserveUse(new(packet.Sequence, key,
                member.MemberKey, member.EntityId, packet.ActionSequence, packet.SeenAtUtc,
                pullStartedAtUtc is null ? 0.0f : CalculatePullElapsed(packet.SeenAtUtc), packet.ActionId,
                actionName, GetActionIconId(packet.ActionId), targets)
            { CooldownGroup = cooldown.Group, CooldownSeconds = cooldown.RecastSeconds }))
                MarkPossibleMitigationTimingChanged(packet.SeenAtUtc);
        }
    }

    private void TrackPossibleMitigationApplications(RawEffectResultPacket packet, IReadOnlyList<StatusSnapshot> statuses)
    {
        if (packet.RelatedActionSequence == 0)
            return;
        var target = NormalizeActorEntityId(packet.TargetId);
        if (target == 0)
            target = NormalizeActorEntityId(packet.ActorId);
        foreach (var status in statuses.Where(status => IsPossibleMitigationEffectStatusId(status.Id)))
        {
            var provider = currentMembers.FirstOrDefault(member => member.EntityId == status.SourceId)?.MemberKey;
            if (provider is null)
                provider = CaptureStatusSources([status]).FirstOrDefault()?.MemberKey;
            if (provider is not null && possibleMitigationUses.ObserveApplication(new(packet.Sequence, packet.RelatedActionSequence,
                packet.SeenAtUtc, target, provider, status)))
                MarkPossibleMitigationTimingChanged(packet.SeenAtUtc);
        }
    }

    private void MarkPossibleMitigationTimingChanged(DateTime time) => possibleMitigationTimingChangedAtUtc =
        possibleMitigationTimingChangedAtUtc is { } previous && previous < time ? previous : time;

    private IReadOnlyList<PossibleMitigationSnapshot> BuildPossibleMitigationSnapshotsForDeath(
        PartyDeathRecord death)
    {
        if (currentMembers.Count == 0)
        {
            return [];
        }

        var snapshots = new List<PossibleMitigationSnapshot>();
        foreach (var member in currentMembers
            .Where(member => member.IsPartyMember)
            .OrderBy(member => member.PartyIndex))
        {
            foreach (var definition in PossibleMitigationDefinitions)
            {
                if (!DefinitionMatchesJob(definition, member.ClassJobId) ||
                    !DefinitionCanAffectDeath(definition, member, death.MemberKey) ||
                    !DefinitionHasCalculableReduction(definition))
                {
                    continue;
                }

                if (CreatePossibleMitigationSnapshot(member, definition, death.SeenAtUtc) is { } snapshot)
                    snapshots.Add(snapshot);
            }
        }

        return PossibleMitigationTimingPolicy.ResolveAll(snapshots, death, possibleMitigationUses,
            death.SeenAtUtc + FatalSequenceEndBuffer, MaxPossibleMitigationsPerDeath);
    }

    private PossibleMitigationSnapshot? CreatePossibleMitigationSnapshot(
        PartyMemberSnapshot member,
        PossibleMitigationDefinition definition,
        DateTime deathSeenAtUtc)
    {
        var actionId = definition.ActionId;
        if (GetPossibleMitigationCooldownProfile(actionId, member.ClassJobLevel) is not { } cooldown)
            return null;
        MitigationPrerequisite? prerequisite = null;
        if (definition.GrantedByActionId != 0)
        {
            if (DataManager.GetExcelSheet<LuminaAction>()?.GetRowOrDefault(actionId) is not { } action ||
                action.ClassJobLevel > member.ClassJobLevel ||
                GetPossibleMitigationCooldownProfile(definition.GrantedByActionId, member.ClassJobLevel) is not { } parent)
                return null;
            var history = playerStatusHistory.Between(deathSeenAtUtc.AddSeconds(-LeadUpTimingPolicy.CaptureSeconds),
                deathSeenAtUtc + FatalSequenceEndBuffer, member.MemberKey).Select(value => value.ToStatusSet() with
                {
                    Statuses = value.Statuses.Where(status => status.Id == definition.ReadyStatusId).ToArray(),
                    RemovedStatuses = value.RemovedStatuses.Where(status => status.Id == definition.ReadyStatusId).ToArray(),
                }).ToArray();
            prerequisite = new(definition.GrantedByActionId, parent.Group, (float)parent.RecastSeconds,
                parent.MaxCharges, definition.ReadyStatusId, history);
        }
        var actionName = definition.ActionName;
        var actionIconId = actionId == 0 ? 0 : GetActionIconId(actionId);
        var statuses = definition.Statuses
            .Select(status => CreatePossibleMitigationStatusSnapshot(status, definition.DurationSeconds, member.EntityId, actionIconId))
            .ToList();
        return new PossibleMitigationSnapshot(
            $"{member.MemberKey}:{definition.Key}",
            member.MemberKey,
            member.MemberName,
            member.PartyIndex,
            member.ClassJobId,
            member.ClassJobName,
            actionId,
            actionName,
            actionIconId,
            definition.Scope,
            (float)cooldown.RecastSeconds,
            null,
            null,
            "Ready",
            statuses)
        {
            CooldownGroup = cooldown.Group,
            MaxCharges = cooldown.MaxCharges,
            Prerequisite = prerequisite,
            EffectStatusIds = GetPossibleMitigationEffectStatusIds(definition.Key),
        };
    }

    private StatusSnapshot CreatePossibleMitigationStatusSnapshot(
        PossibleMitigationStatusTemplate template,
        float definitionDurationSeconds,
        uint sourceId,
        uint fallbackIconId)
    {
        var iconId = template.StatusId == 0 ? fallbackIconId : GetStatusIconId(template.StatusId);
        if (iconId == 0)
        {
            iconId = fallbackIconId;
        }

        return new StatusSnapshot(
            template.StatusId,
            template.Name,
            iconId,
            sourceId,
            0,
            template.DurationSeconds ?? definitionDurationSeconds);
    }

    private void RefreshRecentPossibleMitigations()
    {
        if (possibleMitigationTimingChangedAtUtc is not { } changed)
            return;
        possibleMitigationTimingChangedAtUtc = null;
        if (currentPullClosedForReview)
            return;
        RefreshRecentPossibleMitigations(currentDeaths, changed);
        RefreshRecentPossibleMitigations(pendingDeathRecapLinks, changed);
    }

    private void RefreshRecentPossibleMitigations(List<PartyDeathRecord> deaths, DateTime changed)
    {
        for (var index = 0; index < deaths.Count; index++)
        {
            var death = deaths[index];
            if (changed > death.SeenAtUtc + FatalSequenceEndBuffer || death.EntityId == 0)
                continue;
            var options = BuildPossibleMitigationSnapshotsForDeath(death);
            if (!PossibleMitigationTimingPolicy.SameOptions(death.PossibleMitigations, options))
                deaths[index] = death with { PossibleMitigations = options };
        }
    }

    private static bool DefinitionMatchesAction(PossibleMitigationDefinition definition, uint actionId, string actionName)
    {
        return ActionMatches(definition.ActionId, definition.ActionName, actionId, actionName) ||
            definition.AlternateActions?.Any(action => ActionMatches(action.ActionId, action.Name, actionId, actionName)) == true;
    }

    private static bool ActionMatches(uint definitionActionId, string definitionActionName, uint actionId, string actionName)
    {
        return definitionActionId != 0 && definitionActionId == actionId ||
            string.Equals(definitionActionName, actionName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool DefinitionMatchesJob(PossibleMitigationDefinition definition, uint classJobId)
    {
        return definition.JobIds.Count == 0 || definition.JobIds.Contains(classJobId);
    }

    private static bool DefinitionCanAffectDeath(
        PossibleMitigationDefinition definition,
        PartyMemberSnapshot member,
        string deathMemberKey)
    {
        return definition.Scope switch
        {
            PossibleMitigationScope.Personal => string.Equals(member.MemberKey, deathMemberKey, StringComparison.Ordinal),
            _ => true,
        };
    }

    private static bool DefinitionHasCalculableReduction(PossibleMitigationDefinition definition)
    {
        return definition.Statuses.Any(status =>
        {
            var displayInfo = GetMitigationDisplayInfo(new StatusSnapshot(status.StatusId, status.Name, 0, 0, 0, status.DurationSeconds ?? definition.DurationSeconds));
            return displayInfo.MitigationPercents.Count > 0;
        });
    }

}
