using System.Collections.Generic;
using System;
using System.Linq;

namespace BetterDeaths;

public sealed partial class Plugin
{
    internal static IReadOnlyList<uint> GetPossibleMitigationEffectStatusIds(string key) => PossibleMitigationEffectIds.Value[key];

    internal static bool IsPossibleMitigationEffectStatusId(uint id) => PossibleMitigationEffectIdSet.Value.Contains(id);

    internal static bool IsPossibleMitigationReadyStatusId(uint id) => id != 0 && PossibleMitigationReadyStatusIds.Value.Contains(id);

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
        IReadOnlyList<PossibleMitigationStatusTemplate> Statuses,
        IReadOnlyList<PossibleMitigationActionTemplate>? AlternateActions = null,
        IReadOnlyList<uint>? AdditionalEffectStatusIds = null,
        uint GrantedByActionId = 0,
        uint ReadyStatusId = 0);


    private sealed record PossibleMitigationActionTemplate(
        uint ActionId,
        string Name);


    private sealed record PossibleMitigationStatusTemplate(
        uint StatusId,
        string Name,
        float? DurationSeconds = null);


    private static readonly PossibleMitigationDefinition[] PossibleMitigationDefinitions =
    [
        new("reprisal", "Reprisal", 7535, TankJobIds, PossibleMitigationScope.Boss, 60.0f, 15.0f, [new(1193, "Reprisal")]),
        new("feint", "Feint", 7549, MeleeJobIds, PossibleMitigationScope.Boss, 90.0f, 15.0f, [new(1195, "Feint")]),
        new("addle", "Addle", 7560, CasterJobIds, PossibleMitigationScope.Boss, 90.0f, 15.0f, [new(1203, "Addle")]),
        new("dismantle", "Dismantle", 2887, [31], PossibleMitigationScope.Boss, 120.0f, 10.0f, [new(860, "Dismantled")]),

        new("rampart", "Rampart", 7531, TankJobIds, PossibleMitigationScope.Personal, 90.0f, 20.0f, [new(1191, "Rampart")]),
        new("guardian", "Guardian", 36920, [19], PossibleMitigationScope.Personal, 120.0f, 15.0f, [new(3829, "Guardian")]),
        new("damnation", "Damnation", 36923, [21], PossibleMitigationScope.Personal, 120.0f, 15.0f, [new(3832, "Damnation")]),
        new("dark-mind", "Dark Mind", 3634, [32], PossibleMitigationScope.Personal, 60.0f, 10.0f, [new(746, "Dark Mind")]),
        new("shadowed-vigil", "Shadowed Vigil", 36927, [32], PossibleMitigationScope.Personal, 120.0f, 15.0f, [new(3835, "Shadowed Vigil")]),
        new("camouflage", "Camouflage", 16140, [37], PossibleMitigationScope.Personal, 90.0f, 20.0f, [new(1832, "Camouflage")]),
        new("great-nebula", "Great Nebula", 36935, [37], PossibleMitigationScope.Personal, 120.0f, 15.0f, [new(3838, "Great Nebula")]),
        new("riddle-of-earth", "Riddle of Earth", 7394, [20], PossibleMitigationScope.Personal, 120.0f, 10.0f, [new(1179, "Riddle of Earth")]),
        new("third-eye", "Third Eye / Tengentsu", 36962, [34], PossibleMitigationScope.Personal, 15.0f, 4.0f, [new(3853, "Third Eye")], [new(7498, "Third Eye")], [1232]),

        new("bloodwhetting", "Bloodwhetting", 25751, [21], PossibleMitigationScope.Personal, 25.0f, 8.0f, [new(2678, "Bloodwhetting")]),
        new("nascent-flash", "Nascent Flash", 16464, [21], PossibleMitigationScope.Targeted, 25.0f, 8.0f, [new(1858, "Nascent Glint")]),
        new("oblation", "Oblation", 25754, [32], PossibleMitigationScope.Targeted, 60.0f, 10.0f, [new(2682, "Oblation")]),
        new("heart-of-corundum", "Heart of Corundum", 25758, [37], PossibleMitigationScope.Targeted, 25.0f, 8.0f, [new(2683, "Heart of Corundum")]),
        new("aquaveil", "Aquaveil", 25861, [24], PossibleMitigationScope.Targeted, 60.0f, 8.0f, [new(2708, "Aquaveil")]),
        new("exaltation", "Exaltation", 25873, [33], PossibleMitigationScope.Targeted, 60.0f, 8.0f, [new(2717, "Exaltation")]),
        new("taurochole", "Taurochole", 24303, [40], PossibleMitigationScope.Targeted, 45.0f, 15.0f, [new(2619, "Taurochole")]),

        new("dark-missionary", "Dark Missionary", 16471, [32], PossibleMitigationScope.Party, 90.0f, 15.0f, [new(0, "Dark Missionary")]),
        new("heart-of-light", "Heart of Light", 16160, [37], PossibleMitigationScope.Party, 90.0f, 15.0f, [new(0, "Heart of Light")]),
        new("temperance", "Temperance", 16536, [24], PossibleMitigationScope.Party, 120.0f, 20.0f, [new(0, "Temperance")]),
        new("sacred-soil", "Sacred Soil", 188, [28], PossibleMitigationScope.Party, 30.0f, 15.0f, [new(299, "Sacred Soil")]),
        new("expedient", "Expedient", 25868, [28], PossibleMitigationScope.Party, 120.0f, 20.0f, [new(2711, "Desperate Measures")]),
        new("collective-unconscious", "Collective Unconscious", 3613, [33], PossibleMitigationScope.Party, 60.0f, 18.0f, [new(1206, "Wheel of Fortune")]),
        new("sun-sign", "Sun Sign", 37031, [33], PossibleMitigationScope.Party, 120.0f, 15.0f,
            [new(3896, "Sun Sign")], GrantedByActionId: 16559, ReadyStatusId: 3895),
        new("kerachole", "Kerachole", 24298, [40], PossibleMitigationScope.Party, 30.0f, 15.0f, [new(0, "Kerachole")]),
        new("holos", "Holos", 24310, [40], PossibleMitigationScope.Party, 120.0f, 20.0f, [new(0, "Holos")]),
        new("troubadour", "Troubadour", 7405, [23], PossibleMitigationScope.Party, 90.0f, 15.0f, [new(1934, "Troubadour")]),
        new("tactician", "Tactician", 16889, [31], PossibleMitigationScope.Party, 90.0f, 15.0f, [new(0, "Tactician")]),
        new("shield-samba", "Shield Samba", 16012, [38], PossibleMitigationScope.Party, 90.0f, 15.0f, [new(0, "Shield Samba")]),
        new("magick-barrier", "Magick Barrier", 25857, [35], PossibleMitigationScope.Party, 120.0f, 10.0f, [new(0, "Magick Barrier")]),
    ];

    private static readonly Lazy<IReadOnlyDictionary<string, uint[]>> PossibleMitigationEffectIds = new(() =>
        PossibleMitigationDefinitions.ToDictionary(value => value.Key, value => value.Statuses
            .SelectMany(status => GetMitigationEffectStatusIds(new(status.StatusId, status.Name, 0, 0, 0, 0)))
            .Concat(value.AdditionalEffectStatusIds ?? []).Distinct().ToArray()));
    private static readonly Lazy<HashSet<uint>> PossibleMitigationEffectIdSet = new(() =>
        PossibleMitigationEffectIds.Value.Values.SelectMany(value => value).ToHashSet());
    private static readonly Lazy<HashSet<uint>> PossibleMitigationReadyStatusIds = new(() =>
        PossibleMitigationDefinitions.Select(value => value.ReadyStatusId).Where(id => id != 0).ToHashSet());

}
