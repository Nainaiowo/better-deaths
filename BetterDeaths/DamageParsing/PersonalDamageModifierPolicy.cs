namespace BetterDeaths.DamageParsing;

using System.Collections.Generic;

internal static class PersonalDamageModifierPolicy
{
    private const uint MedicatedStatusId = 0x31;
    private const uint WeaknessStatusId = 0x2B;
    private const uint BrinkOfDeathStatusId = 0x2C;

    private static readonly HashSet<uint> RelevantStatusIds =
    [
        MedicatedStatusId,
        WeaknessStatusId,
        BrinkOfDeathStatusId,
        0xB5F, // Damage Down (packet-specified strength)
        0x7D,  // Raging Strikes
        0x748, // Lance Charge
        0x77A, // Power Surge
        0xAA0, // Power Surge
        0x5AD, // Dragon Sight (legacy Right Eye)
        0x776, // Dragon Sight (legacy Right Eye only)
        0xF04, // Life of the Dragon
        0x727, // No Mercy
        0x4C,  // Fight or Flight
        0x512, // Fugetsu
        0x6B4, // Boost
        0x6B6, // Waxing Nocturne
        0x6B7, // Mighty Guard
        0x846, // Harmonized
        0x9C2, // Basic Instinct
    ];

    public static bool IsRelevantStatus(uint statusId)
    {
        return RelevantStatusIds.Contains(statusId);
    }

    public static bool ChangesAttributes(uint statusId) =>
        statusId is MedicatedStatusId or WeaknessStatusId or BrinkOfDeathStatusId;

    public static bool HasUnknownStrength(DamageStatusSnapshot status) =>
        status.StatusId == 0xB5F && status.Parameter is < 156 or > 255;

    public static double GetDefaultDurationSeconds(uint statusId)
    {
        return statusId switch
        {
            MedicatedStatusId => 30.0,
            WeaknessStatusId or BrinkOfDeathStatusId => 100.0,
            0xB5F => 180.0,
            0x77A or 0xAA0 => 30.0,
            0x512 => 40.0,
            _ => 20.0,
        };
    }

    public static IReadOnlyList<RaidBuffEffect> GetEffects(
        DamageStatusSnapshot status,
        uint actionCategoryId,
        byte damageType)
    {
        var amount = status.StatusId switch
        {
            0xB5F when !HasUnknownStrength(status) => unchecked((sbyte)(byte)status.Parameter) / 100.0,
            0x7D => 0.15,
            0x748 => 0.10,
            0x77A or 0xAA0 => 0.10,
            0x5AD or 0x776 => 0.10,
            0xF04 => 0.15,
            0x727 => 0.20,
            0x4C => 0.25,
            0x512 => 0.13,
            0x6B4 when actionCategoryId == 2 => 0.50,
            0x6B6 => 0.50,
            0x6B7 => -0.40,
            0x846 when actionCategoryId == 2 && IsPhysical(damageType) => 0.80,
            0x9C2 => 1.00,
            _ => 0.0,
        };

        return amount == 0.0
            ? []
            : [new RaidBuffEffect(
                status.StatusId,
                RaidBuffEffectKind.DamageMultiplier,
                amount,
                status.Source)];
    }

    private static bool IsPhysical(byte damageType)
    {
        return damageType is 1 or 2 or 3 or 4 or 7;
    }
}
