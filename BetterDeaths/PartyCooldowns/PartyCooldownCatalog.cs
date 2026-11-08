using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterDeaths.PartyCooldowns;

internal sealed class PartyCooldownCatalog
{
    public static IReadOnlyList<PartyCooldownJob> Jobs { get; } =
    [
        new(19, "Paladin", "PLD", PartyCooldownRole.Tank),
        new(21, "Warrior", "WAR", PartyCooldownRole.Tank),
        new(32, "Dark Knight", "DRK", PartyCooldownRole.Tank),
        new(37, "Gunbreaker", "GNB", PartyCooldownRole.Tank),
        new(24, "White Mage", "WHM", PartyCooldownRole.Healer),
        new(28, "Scholar", "SCH", PartyCooldownRole.Healer),
        new(33, "Astrologian", "AST", PartyCooldownRole.Healer),
        new(40, "Sage", "SGE", PartyCooldownRole.Healer),
        new(20, "Monk", "MNK", PartyCooldownRole.Melee),
        new(22, "Dragoon", "DRG", PartyCooldownRole.Melee),
        new(30, "Ninja", "NIN", PartyCooldownRole.Melee),
        new(34, "Samurai", "SAM", PartyCooldownRole.Melee),
        new(39, "Reaper", "RPR", PartyCooldownRole.Melee),
        new(41, "Viper", "VPR", PartyCooldownRole.Melee),
        new(43, "Beastmaster", "BST", PartyCooldownRole.Melee),
        new(23, "Bard", "BRD", PartyCooldownRole.Ranged),
        new(31, "Machinist", "MCH", PartyCooldownRole.Ranged),
        new(38, "Dancer", "DNC", PartyCooldownRole.Ranged),
        new(25, "Black Mage", "BLM", PartyCooldownRole.Ranged),
        new(27, "Summoner", "SMN", PartyCooldownRole.Ranged),
        new(35, "Red Mage", "RDM", PartyCooldownRole.Ranged),
        new(42, "Pictomancer", "PCT", PartyCooldownRole.Ranged),
        new(36, "Blue Mage", "BLU", PartyCooldownRole.Ranged),
    ];

    private readonly Dictionary<uint, PartyCooldownAction> actions;
    private readonly Dictionary<uint, IReadOnlyList<PartyCooldownAction>> actionsByJob;
    private readonly Dictionary<uint, IReadOnlyList<PartyCooldownAction>> captureActionsByJob;
    private readonly Dictionary<uint, IReadOnlyList<uint>> defaultsByJob;

    public PartyCooldownCatalog(IEnumerable<PartyCooldownAction> source)
    {
        var definitions = source.Where(action => action.Id != 0 && action.Group is > 0 and not 58 &&
            float.IsFinite(action.RecastSeconds) && action.RecastSeconds > 0 &&
            (action.RecastSeconds >= 10 || action.CaptureOnly || PartyCooldownSelectionPolicy.Includes(action.Id)) &&
            !string.IsNullOrWhiteSpace(action.Name)).DistinctBy(action => action.Id).ToArray();
        var groupsByJob = Jobs.ToDictionary(job => job.Id, job => definitions
            .Where(action => !action.CaptureOnly && action.Jobs.Contains(job.Id)).Select(action => action.Group).ToHashSet());
        actions = definitions.Where(action => action.Jobs.Any(job => groupsByJob.TryGetValue(job, out var groups) &&
            groups.Contains(action.Group))).ToDictionary(action => action.Id);
        captureActionsByJob = Jobs.ToDictionary(job => job.Id, job => (IReadOnlyList<PartyCooldownAction>)actions.Values
            .Where(action => action.Jobs.Contains(job.Id) && groupsByJob[job.Id].Contains(action.Group))
            .OrderBy(action => action.Level).ThenBy(action => action.Name).ToArray());
        actionsByJob = Jobs.ToDictionary(job => job.Id, job => (IReadOnlyList<PartyCooldownAction>)captureActionsByJob[job.Id]
            .Where(action => !action.CaptureOnly && !PartyCooldownSelectionPolicy.IsExecutionVariant(action.Id))
            .DistinctBy(action => (action.Name, action.Group, action.Level)).ToArray());
        defaultsByJob = Jobs.ToDictionary(job => job.Id, job => (IReadOnlyList<uint>)actionsByJob[job.Id]
            .Where(action => action.DefaultSelected && action.UnsupportedReason is null).Select(action => action.Id).ToArray());
    }

    public PartyCooldownAction? Find(uint actionId) => actions.GetValueOrDefault(actionId);
    public IReadOnlyList<PartyCooldownAction> ForJob(uint jobId) => actionsByJob.GetValueOrDefault(NormalizeJob(jobId)) ?? [];
    public IReadOnlyList<PartyCooldownAction> CaptureActionsForJob(uint jobId) => captureActionsByJob.GetValueOrDefault(NormalizeJob(jobId)) ?? [];

    public IReadOnlyList<uint> DefaultSelection(uint jobId) => defaultsByJob.GetValueOrDefault(NormalizeJob(jobId)) ?? [];
    public List<uint> Defaults(uint jobId) => DefaultSelection(jobId).ToList();

    public List<uint> NormalizeSelection(uint jobId, IEnumerable<uint>? selected) =>
        selected is null ? Defaults(jobId) : selected.Select(PartyCooldownSelectionPolicy.CanonicalAction).Distinct()
            .Where(id => Find(id) is { UnsupportedReason: null, CaptureOnly: false } action &&
            action.Jobs.Contains(NormalizeJob(jobId))).Take(12).ToList();

    public PartyCooldownAction? AtLevel(uint jobId, uint actionId, byte level)
    {
        if (Find(actionId) is not { } action || !action.Jobs.Contains(NormalizeJob(jobId)))
            return null;
        if (action.Level <= level)
            return action;
        return ForJob(jobId).Where(candidate => candidate.Level <= level && candidate.Group == action.Group &&
            candidate.TargetsOtherPlayers == action.TargetsOtherPlayers && candidate.UnsupportedReason is null)
            .OrderByDescending(candidate => candidate.Level).FirstOrDefault();
    }

    public static bool PlaceBefore(List<uint> selection, uint source, uint? target) => Place(selection, source, target, false);

    public static bool PlaceAfter(List<uint> selection, uint source, uint target) => Place(selection, source, target, true);

    private static bool Place(List<uint> selection, uint source, uint? target, bool after)
    {
        if (source == 0 || source == target || target is { } id && !selection.Contains(id) ||
            !selection.Contains(source) && selection.Count >= 12)
            return false;
        return TileOrderPolicy.Place(selection, source, target, after);
    }

    public static uint NormalizeJob(uint jobId) => jobId switch
    {
        1 => 19,
        2 => 20,
        3 => 21,
        4 => 22,
        5 => 23,
        6 => 24,
        7 => 25,
        26 => 27,
        29 => 30,
        _ => jobId,
    };

    public static double RecastAtLevel(uint actionId, byte level, double baseSeconds) => actionId switch
    {
        7405 or 16889 or 16012 when level >= 88 => 90,
        7561 when level >= 94 => 40,
        3606 when level >= 68 => 60,
        7519 when level >= 74 => 35,
        24300 when level >= 88 => 90,
        158 when level >= 84 => 100,
        3585 when level >= 88 => 90,
        7496 or 16481 when level >= 94 => 60,
        16542 when level >= 98 => 60,
        24294 when level >= 94 => 60,
        44905 when level >= 42 => 90,
        44904 when level >= 48 => 90,
        _ => baseSeconds,
    };
}
