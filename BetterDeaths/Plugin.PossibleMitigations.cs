namespace BetterDeaths;

using System;
using System.Collections.Generic;
using System.Linq;

public sealed partial class Plugin
{
    private const int MaxPossibleMitigationsPerDeath = 64;

    private static readonly uint[] TankJobIds = [19, 21, 32, 37];
    private static readonly uint[] MeleeJobIds = [20, 22, 30, 34, 39, 41];
    private static readonly uint[] CasterJobIds = [25, 27, 35, 42];

    private sealed record PossibleMitigationDefinition(
        string Key,
        string ActionName,
        uint ActionId,
        IReadOnlyList<uint> JobIds,
        PossibleMitigationScope Scope,
        float CooldownSeconds,
        float DurationSeconds,
        IReadOnlyList<PossibleMitigationStatusTemplate> Statuses);

    private sealed record PossibleMitigationStatusTemplate(
        uint StatusId,
        string Name,
        float? DurationSeconds = null);

    private sealed record TrackedPossibleMitigationUse(
        DateTime SeenAtUtc,
        float PullElapsedSeconds,
        uint ActionId,
        string ActionName,
        uint ActionIconId);

    private static readonly PossibleMitigationDefinition[] PossibleMitigationDefinitions =
    [
        new("reprisal", "Reprisal", 7535, TankJobIds, PossibleMitigationScope.Boss, 60.0f, 15.0f, [new(1193, "Reprisal")]),
        new("feint", "Feint", 7549, MeleeJobIds, PossibleMitigationScope.Boss, 90.0f, 15.0f, [new(1195, "Feint")]),
        new("addle", "Addle", 7560, CasterJobIds, PossibleMitigationScope.Boss, 90.0f, 15.0f, [new(1203, "Addle")]),
        new("dismantle", "Dismantle", 2887, [31], PossibleMitigationScope.Boss, 120.0f, 10.0f, [new(860, "Dismantled")]),

        new("rampart", "Rampart", 7531, TankJobIds, PossibleMitigationScope.Personal, 90.0f, 20.0f, [new(1191, "Rampart")]),
        new("sentinel", "Sentinel", 17, [19], PossibleMitigationScope.Personal, 120.0f, 15.0f, [new(74, "Sentinel")]),
        new("guardian", "Guardian", 36920, [19], PossibleMitigationScope.Personal, 120.0f, 15.0f, [new(3829, "Guardian")]),
        new("vengeance", "Vengeance", 44, [21], PossibleMitigationScope.Personal, 120.0f, 15.0f, [new(89, "Vengeance")]),
        new("damnation", "Damnation", 36923, [21], PossibleMitigationScope.Personal, 120.0f, 15.0f, [new(3832, "Damnation")]),
        new("dark-mind", "Dark Mind", 3634, [32], PossibleMitigationScope.Personal, 60.0f, 10.0f, [new(746, "Dark Mind")]),
        new("shadow-wall", "Shadow Wall", 3636, [32], PossibleMitigationScope.Personal, 120.0f, 15.0f, [new(747, "Shadow Wall")]),
        new("shadowed-vigil", "Shadowed Vigil", 36927, [32], PossibleMitigationScope.Personal, 120.0f, 15.0f, [new(3835, "Shadowed Vigil")]),
        new("camouflage", "Camouflage", 16140, [37], PossibleMitigationScope.Personal, 90.0f, 20.0f, [new(1832, "Camouflage")]),
        new("nebula", "Nebula", 16148, [37], PossibleMitigationScope.Personal, 120.0f, 15.0f, [new(1834, "Nebula")]),
        new("great-nebula", "Great Nebula", 36935, [37], PossibleMitigationScope.Personal, 120.0f, 15.0f, [new(3838, "Great Nebula")]),
        new("riddle-of-earth", "Riddle of Earth", 7394, [20], PossibleMitigationScope.Personal, 120.0f, 10.0f, [new(1179, "Riddle of Earth")]),
        new("third-eye", "Third Eye", 7498, [34], PossibleMitigationScope.Personal, 15.0f, 4.0f, [new(1232, "Third Eye")]),

        new("bloodwhetting", "Bloodwhetting", 25751, [21], PossibleMitigationScope.Personal, 25.0f, 8.0f, [new(2678, "Bloodwhetting")]),
        new("nascent-flash", "Nascent Flash", 16464, [21], PossibleMitigationScope.Targeted, 25.0f, 8.0f, [new(1857, "Nascent Glint")]),
        new("oblation", "Oblation", 25754, [32], PossibleMitigationScope.Targeted, 60.0f, 10.0f, [new(2682, "Oblation")]),
        new("heart-of-corundum", "Heart of Corundum", 25758, [37], PossibleMitigationScope.Targeted, 25.0f, 8.0f, [new(2683, "Heart of Corundum")]),
        new("aquaveil", "Aquaveil", 25861, [24], PossibleMitigationScope.Targeted, 60.0f, 8.0f, [new(2708, "Aquaveil")]),
        new("exaltation", "Exaltation", 25873, [33], PossibleMitigationScope.Targeted, 60.0f, 8.0f, [new(2717, "Exaltation")]),
        new("taurochole", "Taurochole", 24303, [40], PossibleMitigationScope.Targeted, 45.0f, 15.0f, [new(2619, "Taurochole")]),

        new("dark-missionary", "Dark Missionary", 16471, [32], PossibleMitigationScope.Party, 90.0f, 15.0f, [new(0, "Dark Missionary")]),
        new("heart-of-light", "Heart of Light", 16160, [37], PossibleMitigationScope.Party, 90.0f, 15.0f, [new(0, "Heart of Light")]),
        new("temperance", "Temperance", 16536, [24], PossibleMitigationScope.Party, 120.0f, 20.0f, [new(0, "Temperance")]),
        new("sacred-soil", "Sacred Soil", 188, [28], PossibleMitigationScope.Party, 30.0f, 15.0f, [new(299, "Sacred Soil")]),
        new("expedient", "Expedient", 25868, [28], PossibleMitigationScope.Party, 120.0f, 20.0f, [new(0, "Expedient")]),
        new("collective-unconscious", "Collective Unconscious", 3613, [33], PossibleMitigationScope.Party, 60.0f, 18.0f, [new(0, "Collective Unconscious")]),
        new("kerachole", "Kerachole", 24298, [40], PossibleMitigationScope.Party, 30.0f, 15.0f, [new(0, "Kerachole")]),
        new("holos", "Holos", 24310, [40], PossibleMitigationScope.Party, 120.0f, 20.0f, [new(0, "Holos")]),
        new("troubadour", "Troubadour", 7405, [23], PossibleMitigationScope.Party, 90.0f, 15.0f, [new(1934, "Troubadour")]),
        new("tactician", "Tactician", 16889, [31], PossibleMitigationScope.Party, 90.0f, 15.0f, [new(0, "Tactician")]),
        new("shield-samba", "Shield Samba", 16012, [38], PossibleMitigationScope.Party, 90.0f, 15.0f, [new(0, "Shield Samba")]),
        new("magick-barrier", "Magick Barrier", 25857, [35], PossibleMitigationScope.Party, 120.0f, 10.0f, [new(0, "Magick Barrier")]),
    ];

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

        var actionName = GetActionName(packet.ActionId);
        var definitions = PossibleMitigationDefinitions
            .Where(definition => DefinitionMatchesAction(definition, packet.ActionId, actionName))
            .Where(definition => DefinitionMatchesJob(definition, member.ClassJobId))
            .ToList();
        if (definitions.Count == 0)
        {
            return;
        }

        if (!possibleMitigationUsesByMember.TryGetValue(member.MemberKey, out var memberUses))
        {
            memberUses = new Dictionary<string, TrackedPossibleMitigationUse>(StringComparer.Ordinal);
            possibleMitigationUsesByMember[member.MemberKey] = memberUses;
        }

        foreach (var definition in definitions)
        {
            memberUses[definition.Key] = new TrackedPossibleMitigationUse(
                packet.SeenAtUtc,
                pullStartedAtUtc is null ? 0.0f : CalculatePullElapsed(packet.SeenAtUtc),
                packet.ActionId,
                actionName,
                GetActionIconId(packet.ActionId));
        }
    }

    private IReadOnlyList<PossibleMitigationSnapshot> BuildPossibleMitigationSnapshotsForDeath(
        PartyMemberSnapshot deathMember,
        DateTime deathSeenAtUtc)
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
                    !DefinitionCanAffectDeath(definition, member, deathMember) ||
                    !DefinitionHasCalculableReduction(definition))
                {
                    continue;
                }

                var use = GetTrackedPossibleMitigationUse(member.MemberKey, definition);
                if (use is not null &&
                    use.SeenAtUtc <= deathSeenAtUtc &&
                    (deathSeenAtUtc - use.SeenAtUtc).TotalSeconds < definition.CooldownSeconds)
                {
                    continue;
                }

                snapshots.Add(CreatePossibleMitigationSnapshot(member, definition, use, deathSeenAtUtc));
                if (snapshots.Count >= MaxPossibleMitigationsPerDeath)
                {
                    return snapshots;
                }
            }
        }

        return snapshots;
    }

    private PossibleMitigationSnapshot CreatePossibleMitigationSnapshot(
        PartyMemberSnapshot member,
        PossibleMitigationDefinition definition,
        TrackedPossibleMitigationUse? use,
        DateTime deathSeenAtUtc)
    {
        var actionId = use?.ActionId ?? definition.ActionId;
        var actionName = use?.ActionName ?? definition.ActionName;
        var actionIconId = use?.ActionIconId ?? (actionId == 0 ? 0 : GetActionIconId(actionId));
        var statuses = definition.Statuses
            .Select(status => CreatePossibleMitigationStatusSnapshot(status, definition.DurationSeconds, member.EntityId, actionIconId))
            .ToList();
        var availability = use is null || use.SeenAtUtc > deathSeenAtUtc
            ? "Ready"
            : $"Ready; Last used: {FormatMitigationPullTime(use.PullElapsedSeconds)}";

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
            definition.CooldownSeconds,
            use?.SeenAtUtc <= deathSeenAtUtc ? use.SeenAtUtc : null,
            use?.SeenAtUtc <= deathSeenAtUtc ? use.PullElapsedSeconds : null,
            availability,
            statuses);
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

    private TrackedPossibleMitigationUse? GetTrackedPossibleMitigationUse(
        string memberKey,
        PossibleMitigationDefinition definition)
    {
        return possibleMitigationUsesByMember.TryGetValue(memberKey, out var memberUses) &&
            memberUses.TryGetValue(definition.Key, out var use)
            ? use
            : null;
    }

    private static bool DefinitionMatchesAction(PossibleMitigationDefinition definition, uint actionId, string actionName)
    {
        return definition.ActionId != 0 && definition.ActionId == actionId ||
            string.Equals(definition.ActionName, actionName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool DefinitionMatchesJob(PossibleMitigationDefinition definition, uint classJobId)
    {
        return definition.JobIds.Count == 0 || definition.JobIds.Contains(classJobId);
    }

    private static bool DefinitionCanAffectDeath(
        PossibleMitigationDefinition definition,
        PartyMemberSnapshot member,
        PartyMemberSnapshot deathMember)
    {
        return definition.Scope switch
        {
            PossibleMitigationScope.Personal => string.Equals(member.MemberKey, deathMember.MemberKey, StringComparison.Ordinal),
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

    private static string FormatMitigationPullTime(float pullElapsedSeconds)
    {
        var seconds = Math.Max(0, (int)MathF.Floor(pullElapsedSeconds));
        return $"{seconds / 60:00}:{seconds % 60:00}";
    }
}
