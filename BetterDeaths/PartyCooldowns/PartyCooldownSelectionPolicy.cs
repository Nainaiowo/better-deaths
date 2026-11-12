using System.Collections.Generic;

namespace BetterDeaths.PartyCooldowns;

internal static class PartyCooldownSelectionPolicy
{
    private static readonly HashSet<uint> Mitigations =
    [
        // Role defensives, including knockback immunity and personal recovery.
        7531, 7535, 7541, 7542, 7548, 7549, 7559, 7560,
        // Optional crowd control: interrupts, stuns, bind and heavy.
        7538, 7540, 7551, 7553, 7554, 7863,
        // Paladin, Warrior, Dark Knight, Gunbreaker (including level-synced predecessors).
        17, 22, 27, 30, 3540, 3542, 7382, 7385, 25746, 36920,
        40, 43, 44, 3551, 3552, 7388, 16464, 25751, 36923,
        3634, 3636, 3638, 3641, 7393, 16471, 25754, 36927,
        16140, 16148, 16151, 16152, 16160, 16161, 25758, 36935,
        // White Mage, Scholar, Astrologian, Sage.
        3570, 3571, 7432, 7433, 16536, 25861,
        188, 3583, 3585, 3586, 7434, 16537, 16538, 16542, 16543, 16545, 16546, 25867, 25868, 37014,
        3613, 3614, 16553, 16556, 16559, 25873,
        24288, 24298, 24299, 24300, 24301, 24302, 24303, 24305, 24310, 24311, 24317, 37035,
        // Melee, physical ranged and caster defensives.
        65, 2241, 7394, 7498, 24404, 36962,
        3561, 7405, 7408, 16012, 16014, 16015, 16889, 2887,
        157, 25799, 25857, 34685, 36997,
        // Limited jobs: independent defensive recasts, not shared-GCD spells.
        11431, 18306, 18320, 23267, 23273, 23280, 34575,
        44896, 44897, 44898, 44899, 44901, 44903,
    ];

    private static readonly HashSet<uint> MajorCooldowns =
    [
        // Independent damage cooldowns and burst windows, including synced predecessors.
        20, 23, 29, 7383, 16461, 25747, 36921,
        38, 52, 7386, 7387, 7389, 25752,
        3625, 3639, 3643, 7390, 16472, 25757,
        16138, 16144, 16146, 16159, 16164, 16165, 25760,
        // Major healing cooldowns and healer burst/raid buffs.
        136, 140, 3569, 7430, 25862,
        166, 3587, 7436, 16542, 37014,
        3606, 3612, 7439, 16552, 16557, 25874, 37017,
        24289, 24294, 24307, 24309, 24313, 24318, 37033, 37035,
        // Melee burst and raid buffs, including older upgraded buttons.
        69, 7395, 7396, 25766, 83, 85, 92, 96, 3555, 3557, 16478, 16480,
        2246, 2248, 2258, 2259, 2261, 2263, 2264, 3566, 7403, 16489, 16493, 36957, 36958,
        7492, 7493, 7496, 7497, 7499, 16481, 16482, 24380, 24381, 24393, 24405, 34620, 34623, 34647,
        // Physical ranged burst and raid buffs.
        101, 107, 110, 114, 116, 117, 118, 3558, 3559, 3562, 25785, 36975,
        2872, 2874, 2876, 2878, 2890, 7414, 7418, 16498, 16499, 16500, 25788, 36979, 36980,
        15997, 15998, 16011, 16013,
        // Caster burst and raid buffs.
        158, 3573, 7421, 25796, 3581, 7427, 7429, 16508, 16510, 25800, 25801,
        7506, 7515, 7517, 7518, 7519, 7520, 7521, 16527, 34676, 35347, 35348, 35349,
        // Casting, MP recovery, and positional support are optional, never defaults.
        7546, 7561, 7562,
        // Limited-job major offensive and healing recasts.
        11411, 11421, 11426, 11427, 11428, 11429, 11430, 18305, 18312, 18317, 18323, 18324, 18325,
        23264, 23272, 23275, 23277, 23285, 23287, 23288, 23290,
        34571, 34576, 34578, 34580, 34581, 34582,
        44890, 44891, 44893, 44895, 44902, 44904, 44905, 47092,
        47238, 47239, 47240, 47241, 47242, 47243, 47244, 47245,
    ];

    private static readonly Dictionary<uint, uint> ExecutionVariants = new()
    {
        [25831] = 7427, // Summon Phoenix -> Summon Bahamut.
        [36992] = 7427, // Summon Solar Bahamut -> Summon Bahamut.
        [34674] = 35348, // Striking Muse -> Steel Muse.
        [34675] = 35349, // Starry Muse -> Scenic Muse.
        [37018] = 37017, // Umbral Draw -> Astral Draw.
        [36984] = 15997, // Finishing Move -> Standard Step.
        [34670] = 35347, // Creature executions -> Living Muse.
        [34671] = 35347,
        [34672] = 35347,
        [34673] = 35347,
        [34677] = 34676, // Retribution of the Madeen -> Mog of the Ages.
        [16516] = 7429, // Enkindle Phoenix -> Enkindle Bahamut.
        [36998] = 7429, // Enkindle Solar Bahamut -> Enkindle Bahamut.
    };

    public static bool IsMitigation(uint actionId) => Mitigations.Contains(CanonicalAction(actionId));
    public static bool IsMajorCooldown(uint actionId) => MajorCooldowns.Contains(CanonicalAction(actionId));
    public static bool Includes(uint actionId) => IsMitigation(actionId) || IsMajorCooldown(actionId);
    public static bool IsExecutionVariant(uint actionId) => ExecutionVariants.ContainsKey(actionId);
    public static uint CanonicalAction(uint actionId) => ExecutionVariants.GetValueOrDefault(actionId, actionId);
    public static uint DisplayAction(uint actionId) => actionId == 35349 ? 34675u : actionId;

    public static string? UnsupportedReason(uint actionId) => actionId switch
    {
        52 => "Infuriate's recharge can be reduced by other attacks. Party tracking is not available yet.",
        110 or 117 or 36975 => "Song procs change this recharge. Party tracking is not available yet.",
        2874 or 2890 or 36979 or 36980 => "Overheated attacks change this recharge. Party tracking is not available yet.",
        2259 or 2261 or 2263 => "Mudra recharge needs Ninjutsu-specific tracking. Party tracking is not available yet.",
        2262 or 36926 => "Other actions can reset or reduce this movement cooldown. Party tracking is not available yet.",
        3586 => "Seraphism changes Emergency Tactics' recast. Party tracking is not available yet.",
        34576 => "Winged Reprobation resets its recast during its stack sequence. Party tracking is not available yet.",
        44890 or 47092 or 44895 or >= 47238 and <= 47245 => "Summoning a familiar can reset this cooldown. Party tracking is not available yet.",
        _ => null,
    };
}
