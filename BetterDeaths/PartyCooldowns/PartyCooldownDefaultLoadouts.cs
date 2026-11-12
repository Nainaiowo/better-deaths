using System.Collections.Generic;
using System.Linq;

namespace BetterDeaths.PartyCooldowns;

internal sealed record PartyCooldownDefaultLoadout(
    IReadOnlyList<uint> Defensives, IReadOnlyList<uint> TwoMinute, IReadOnlyList<uint> OneMinute)
{
    public IEnumerable<uint> Actions => Defensives.Concat(TwoMinute).Concat(OneMinute);
}

internal static class PartyCooldownDefaultLoadouts
{
    internal const int Version = 1;

    // Freeze the pre-loadout defaults so future What-if additions cannot change an upgrade's migration.
    private static readonly HashSet<uint> LegacyDefaultActions =
    [
        7535, 7549, 7560, 2887, 7531, 36920, 36923, 3634, 36927, 16140, 36935,
        7394, 36962, 25751, 16464, 25754, 25758, 25861, 25873, 24303,
        16471, 16160, 16536, 188, 25868, 3613, 37031, 24298, 24310, 7405, 16889, 16012, 25857,
    ];

    internal static bool WasLegacyDefault(uint actionId) => LegacyDefaultActions.Contains(actionId);

    // Use parent cycles for granted follow-ups. Healing buffs that strengthen shields are defensive;
    // pure heals, optional control, movement-only tools and casting/MP role utility are not defaults.
    internal static readonly IReadOnlyDictionary<uint, PartyCooldownDefaultLoadout> Jobs =
        new Dictionary<uint, PartyCooldownDefaultLoadout>
        {
            [19] = new([7531, 7535, 7548, 36920, 22, 27, 30, 3540, 25746, 7382, 7385], [], [20, 36921]),
            [21] = new([7531, 7535, 7548, 40, 43, 36923, 25751, 16464, 7388], [], [7389]),
            [32] = new([7531, 7535, 7548, 3634, 3638, 7393, 16471, 25754, 36927], [16472], [7390, 3643, 25757]),
            [37] = new([7531, 7535, 7548, 16140, 16152, 16160, 25758, 36935], [], [16138, 16159, 16164, 25760]),
            [24] = new([7559, 7432, 16536, 25861], [136], []),
            [28] = new([7559, 188, 3585, 16538, 16542, 16545, 25867, 25868, 37014], [7436], [166]),
            [33] = new([7559, 3613, 16556, 16559, 25873], [16552], [37017, 7439]),
            [40] = new([7559, 24298, 24300, 24303, 24305, 24310, 24311, 24317, 37035], [24318], [37033]),
            [20] = new([7548, 7549, 65, 7394], [7396], [7395]),
            [22] = new([7548, 7549], [96, 3557], [85, 3555]),
            [30] = new([7548, 7549, 2241], [36957, 7403, 16489], [36958, 2264, 3566]),
            [34] = new([7548, 7549, 36962], [16482], [7499, 16481]),
            [39] = new([7548, 7549, 24404], [24405], [24393]),
            [41] = new([7548, 7549], [34647], []),
            [23] = new([7548, 3561, 7405, 7408], [101, 107, 114, 116, 118, 3559, 25785], [3562]),
            [31] = new([7548, 2887, 16889], [2878, 7414], [2876, 7418, 25788]),
            [38] = new([7548, 16012, 16014], [15998, 16011], [16013]),
            [25] = new([7559, 7560, 157], [158, 3573, 25796], [7421]),
            [27] = new([7559, 7560, 25799], [25801], [7427, 16508]),
            [35] = new([7559, 7560, 25857], [7520, 7521], [7518]),
            [42] = new([7559, 7560, 34685], [35349], [35348]),
            [36] = new([7559, 7560, 18306, 18320, 23273, 23280, 34575],
                [23285, 23287, 23288, 23290, 34580, 34581, 34582], [11411, 11421, 11428, 11429, 18324, 18325]),
            [43] = new([44896, 44897, 44898, 44899, 44901, 44903], [], [44893]),
        };

    internal static IReadOnlyList<uint> Create(uint jobId, IReadOnlyList<PartyCooldownAction> available)
    {
        if (!Jobs.TryGetValue(jobId, out var loadout))
            return [];
        var allowed = available.Where(action => action.UnsupportedReason is null && !action.CaptureOnly)
            .Select(action => action.Id).ToHashSet();
        return loadout.Actions.Where(allowed.Contains).ToArray();
    }

    internal static uint SyncedPredecessor(uint actionId) => actionId == 3566 ? 2246u : 0;
}
