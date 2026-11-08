using System.Collections.Generic;

namespace BetterDeaths.PartyCooldowns;

internal static class PartyCooldownSelectionPolicy
{
    private static readonly HashSet<uint> Mitigations =
    [
        // Role defensives, including knockback immunity and personal recovery.
        7531, 7535, 7541, 7542, 7548, 7549, 7559, 7560,
        // Paladin, Warrior, Dark Knight, Gunbreaker (including level-synced predecessors).
        17, 22, 27, 30, 3540, 3542, 7382, 7385, 25746, 36920,
        40, 43, 44, 3551, 3552, 7388, 16464, 25751, 36923,
        3634, 3636, 3638, 7393, 16471, 25754, 36927,
        16140, 16148, 16151, 16152, 16160, 16161, 25758, 36935,
        // White Mage, Scholar, Astrologian, Sage.
        7432, 7433, 16536, 25861,
        188, 3585, 16538, 16545, 16546, 25867, 25868,
        3613, 16556, 16559, 25873,
        24288, 24298, 24300, 24302, 24303, 24305, 24310, 24311, 24317,
        // Melee, physical ranged and caster defensives.
        65, 2241, 7394, 7498, 24404, 36962,
        3561, 7405, 7408, 16012, 16014, 16015, 16889, 2887,
        157, 25799, 25857, 34685,
        // Limited jobs: independent defensive recasts, not shared-GCD spells.
        11431, 18306, 18320, 23267, 23273, 23280, 34575,
        44896, 44897, 44901,
    ];

    private static readonly HashSet<uint> MajorCooldowns =
    [
        // Tank burst windows.
        20, 7383, 36921, 38, 7389, 3625, 7390, 16472, 16138, 25760,
        // Major healing cooldowns and healer burst/raid buffs.
        136, 140, 3569, 25862,
        166, 3587, 7436, 16542, 37014,
        3606, 3612, 7439, 16552, 16557, 25874, 37017,
        24294, 24309, 24318, 37035,
        // Melee burst and raid buffs, including older upgraded buttons.
        7395, 7396, 25766, 85, 96, 3555, 3557,
        2248, 2258, 2264, 7403, 16489, 16493, 36957, 36958,
        7496, 7499, 16481, 16482, 24393, 24405, 34647,
        // Physical ranged burst and raid buffs.
        101, 107, 114, 116, 118, 3559, 25785,
        2876, 2878, 7414, 25788, 15998, 16011, 16013,
        // Caster burst and raid buffs.
        158, 3573, 25796, 3581, 7427, 25800, 25801, 7520, 7521, 35348, 35349,
        // Limited-job major offensive and healing recasts.
        11411, 11421, 11428, 11429, 11430, 18305, 18312, 18317, 18324, 18325,
        23264, 23272, 23277, 23285, 23287, 23288, 23290,
        34576, 34578, 34580, 34581, 34582, 44904, 44905,
    ];

    private static readonly Dictionary<uint, uint> ExecutionVariants = new()
    {
        [25831] = 7427, // Summon Phoenix -> Summon Bahamut.
        [36992] = 7427, // Summon Solar Bahamut -> Summon Bahamut.
        [34674] = 35348, // Striking Muse -> Steel Muse.
        [34675] = 35349, // Starry Muse -> Scenic Muse.
        [37018] = 37017, // Umbral Draw -> Astral Draw.
    };

    public static bool IsMitigation(uint actionId) => Mitigations.Contains(CanonicalAction(actionId));
    public static bool IsMajorCooldown(uint actionId) => MajorCooldowns.Contains(CanonicalAction(actionId));
    public static bool Includes(uint actionId) => IsMitigation(actionId) || IsMajorCooldown(actionId);
    public static bool IsExecutionVariant(uint actionId) => ExecutionVariants.ContainsKey(actionId);
    public static uint CanonicalAction(uint actionId) => ExecutionVariants.GetValueOrDefault(actionId, actionId);
    public static uint DisplayAction(uint actionId) => actionId == 35349 ? 34675u : actionId;
}
