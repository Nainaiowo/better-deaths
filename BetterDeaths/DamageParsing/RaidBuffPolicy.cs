namespace BetterDeaths.DamageParsing;

using System;
using System.Collections.Generic;
using System.Linq;

internal enum RaidBuffEffectKind
{
    DamageMultiplier,
    CriticalChance,
    DirectHitChance,
}

internal enum RaidBuffDamageScope
{
    All,
    Physical,
    Magic,
    Astral,
    Umbral,
}

internal enum RaidBuffTargeting
{
    Area,
    SingleTarget,
}

internal readonly record struct RaidBuffEffect(
    uint StatusId,
    RaidBuffEffectKind Kind,
    double Amount,
    DamageActorIdentity Source,
    RaidBuffDamageScope DamageScope = RaidBuffDamageScope.All,
    RaidBuffTargeting Targeting = RaidBuffTargeting.Area);

internal static class RaidBuffPolicy
{
    private const uint LifeSurgeStatusId = 0x74;
    private const uint ReassembleStatusId = 0x353;
    private const uint BerserkStatusId = 0x56;
    private const uint InnerReleaseStatusId = 0x499;
    private const uint OpoOpoFormStatusId = 0x6B;
    private const uint PerfectBalanceStatusId = 0x6E;
    private const uint FormlessFistStatusId = 0x9D1;

    private static readonly HashSet<uint> RelevantStatusIds =
    [
        0x75A, // The Balance (legacy)
        0x75D, // The Spear (legacy)
        0xF2F, // The Balance
        0xF31, // The Spear
        0x756, // Divination
        0x8D,  // Battle Voice
        0x8A8, // The Wanderer's Minuet
        0x8A9, // Mage's Ballad
        0x8AA, // Army's Paeon
        0xB94, // Radiant Finale
        0x71E, // Technical Finish
        0x839, // Standard Finish (partner)
        0x721, // Devilment
        0x312, // Battle Litany
        0x5AE, // Dragon Sight (legacy Left Eye)
        0x4A1, // Brotherhood
        0xA27, // Arcane Circle
        0x511, // Embolden (party)
        0xA8F, // Searing Light
        0xE65, // Starry Muse
        0x4C5, // Chain Stratagem
        0x27E, // Vulnerability Up (Mug)
        0xF09, // Dokumori
        0x6B5, // Off-guard
        0x6B9, // Peculiar Light
        0x849, // Astral Attenuation
        0x84A, // Umbral Attenuation
        0x84B, // Physical Attenuation
        LifeSurgeStatusId,
        ReassembleStatusId,
        BerserkStatusId,
        InnerReleaseStatusId,
        OpoOpoFormStatusId,
        PerfectBalanceStatusId,
        FormlessFistStatusId,
    ];

    private static readonly HashSet<uint> GuaranteedCriticalActionIds =
    [
        0x8C6,  // Assassinate
        0x1D3F, // Midare Setsugekka
        0x4051, // Inner Chaos
        0x404F, // Chaotic Cyclone
        0x4066, // Kaeshi: Setsugekka
        0x6499, // Primal Rend
        0x64C0, // Starfall Dance
        0x64B5, // Ogi Namikiri
        0x64B6, // Kaeshi: Namikiri
        0x8776, // Hammer Stamp
        0x8777, // Hammer Brush
        0x8778, // Polishing Hammer
        0x903D, // Primal Ruination
        0x9066, // Tendo Setsugekka
        0x9068, // Tendo Kaeshi Setsugekka
        0x9076, // Full Metal Field
    ];

    private static readonly HashSet<uint> OpoOpoGuaranteedCriticalActionIds =
    [
        0x35,   // Bootshine
        0x64A7, // Shadow of the Destroyer
        0x9051, // Leaping Opo
    ];

    private static readonly HashSet<uint> GuaranteedDirectHitActionIds =
    [
        0x8C6,  // Assassinate
        0x4051, // Inner Chaos
        0x404F, // Chaotic Cyclone
        0x6499, // Primal Rend
        0x64C0, // Starfall Dance
        0x8776, // Hammer Stamp
        0x8777, // Hammer Brush
        0x8778, // Polishing Hammer
        0x903D, // Primal Ruination
        0x9076, // Full Metal Field
    ];

    private static readonly HashSet<uint> InnerReleaseActionIds =
    [
        0xDDD, // Fell Cleave
        0xDDE, // Decimate
    ];

    private static readonly HashSet<uint> BalancePreferredClassJobIds =
    [
        1,  // Gladiator
        2,  // Pugilist
        3,  // Marauder
        4,  // Lancer
        19, // Paladin
        20, // Monk
        21, // Warrior
        22, // Dragoon
        29, // Rogue
        30, // Ninja
        32, // Dark Knight
        34, // Samurai
        37, // Gunbreaker
        39, // Reaper
        41, // Viper
    ];

    private static readonly HashSet<uint> SpearPreferredClassJobIds =
    [
        5,  // Archer
        6,  // Conjurer
        7,  // Thaumaturge
        23, // Bard
        24, // White Mage
        25, // Black Mage
        26, // Arcanist
        27, // Summoner
        28, // Scholar
        31, // Machinist
        33, // Astrologian
        35, // Red Mage
        36, // Blue Mage
        38, // Dancer
        40, // Sage
        42, // Pictomancer
    ];

    public static bool IsRelevantStatus(uint statusId)
    {
        return RelevantStatusIds.Contains(statusId);
    }

    public static double GetDefaultDurationSeconds(uint statusId)
    {
        return statusId switch
        {
            0x75A or 0x75D or 0xF2F or 0xF31 => 15.0,
            0x8A8 or 0x8A9 or 0x8AA => 45.0,
            0x839 => 60.0,
            0x6B5 or 0x6B9 => 15.0,
            0x849 or 0x84A or 0x84B => 30.0,
            BerserkStatusId or InnerReleaseStatusId => 15.0,
            LifeSurgeStatusId or ReassembleStatusId => 5.0,
            _ => 20.0,
        };
    }

    public static IReadOnlyList<RaidBuffEffect> GetEffects(
        DamageStatusSnapshot status,
        bool isTargetStatus,
        DamageActorIdentity recipient)
    {
        if (isTargetStatus)
        {
            return status.StatusId switch
            {
                0x4C5 => [new RaidBuffEffect(status.StatusId, RaidBuffEffectKind.CriticalChance, 0.10, status.Source)],
                0x27E or 0xF09 => [new RaidBuffEffect(status.StatusId, RaidBuffEffectKind.DamageMultiplier, 0.05, status.Source)],
                0x6B5 => [Damage(status, 0.05)],
                0x6B9 => [Damage(status, 0.05, RaidBuffDamageScope.Magic)],
                0x849 => [Damage(status, 0.05, RaidBuffDamageScope.Astral)],
                0x84A => [Damage(status, 0.05, RaidBuffDamageScope.Umbral)],
                0x84B => [Damage(status, 0.05, RaidBuffDamageScope.Physical)],
                _ => [],
            };
        }

        return status.StatusId switch
        {
            0x75A or 0xF2F => [Damage(status, GetAstrologianCardAmount(
                status.Parameter,
                BalancePreferredClassJobIds.Contains(recipient.ClassJobId)),
                targeting: RaidBuffTargeting.SingleTarget)],
            0x75D or 0xF31 => [Damage(status, GetAstrologianCardAmount(
                status.Parameter,
                SpearPreferredClassJobIds.Contains(recipient.ClassJobId)),
                targeting: RaidBuffTargeting.SingleTarget)],
            0x756 => [Damage(status, 0.06)],
            0x8D => [DirectHit(status, 0.20)],
            0x8A8 => [Critical(status, 0.02)],
            0x8A9 => [Damage(status, 0.01)],
            0x8AA => [DirectHit(status, 0.03)],
            0xB94 => [Damage(status, GetRadiantFinaleAmount(status.Parameter))],
            0x71E => [Damage(status, GetTechnicalFinishAmount(status.Parameter))],
            0x839 => [Damage(
                status,
                GetStandardFinishAmount(status.Parameter),
                targeting: RaidBuffTargeting.SingleTarget)],
            0x721 =>
            [
                Critical(status, 0.20, RaidBuffTargeting.SingleTarget),
                DirectHit(status, 0.20, RaidBuffTargeting.SingleTarget),
            ],
            0x312 => [Critical(status, 0.10)],
            0x5AE => [Damage(status, 0.05, targeting: RaidBuffTargeting.SingleTarget)],
            0x4A1 => [Damage(status, 0.05)],
            0xA27 => [Damage(status, 0.03)],
            0x511 => [Damage(status, 0.05)],
            0xA8F => [Damage(status, 0.05)],
            0xE65 => [Damage(status, 0.05)],
            _ => [],
        };
    }

    public static bool IsGuaranteedCritical(ParsedDamageEvent damageEvent)
    {
        if (GuaranteedCriticalActionIds.Contains(damageEvent.ActionId))
        {
            return true;
        }

        if (OpoOpoGuaranteedCriticalActionIds.Contains(damageEvent.ActionId) &&
            damageEvent.SourceStatuses.Any(status =>
                status.RemainingTime > 0.0f &&
                status.StatusId is OpoOpoFormStatusId or PerfectBalanceStatusId or FormlessFistStatusId))
        {
            return true;
        }

        return damageEvent.SourceStatuses.Any(status =>
            status.RemainingTime > 0.0f &&
            (status.StatusId == LifeSurgeStatusId && IsWeaponskill(damageEvent) ||
             status.StatusId == ReassembleStatusId && IsWeaponskill(damageEvent) ||
             status.StatusId == BerserkStatusId && IsWeaponskill(damageEvent) ||
             status.StatusId == InnerReleaseStatusId && InnerReleaseActionIds.Contains(damageEvent.ActionId)));
    }

    public static bool IsGuaranteedDirectHit(ParsedDamageEvent damageEvent)
    {
        if (GuaranteedDirectHitActionIds.Contains(damageEvent.ActionId))
        {
            return true;
        }

        return damageEvent.SourceStatuses.Any(status =>
            status.RemainingTime > 0.0f &&
            (status.StatusId == ReassembleStatusId && IsWeaponskill(damageEvent) ||
             status.StatusId == BerserkStatusId && IsWeaponskill(damageEvent) ||
             status.StatusId == InnerReleaseStatusId && InnerReleaseActionIds.Contains(damageEvent.ActionId)));
    }

    public static bool AppliesToDamage(RaidBuffEffect effect, ParsedDamageEvent damageEvent)
    {
        return AppliesToDamage(effect, damageEvent.DamageType, damageEvent.ElementType);
    }

    public static bool AppliesToDamage(RaidBuffEffect effect, byte damageType, byte elementType)
    {
        return effect.DamageScope switch
        {
            RaidBuffDamageScope.All => true,
            RaidBuffDamageScope.Physical => damageType is 1 or 2 or 3 or 4 or 7,
            RaidBuffDamageScope.Magic => damageType == 5,
            RaidBuffDamageScope.Astral => elementType is 1 or 3 or 5,
            RaidBuffDamageScope.Umbral => elementType is 2 or 4 or 6,
            _ => false,
        };
    }

    private static bool IsWeaponskill(ParsedDamageEvent damageEvent)
    {
        return damageEvent.ActionCategoryId == 3 && !damageEvent.IsAutoAttack;
    }

    private static double GetRadiantFinaleAmount(ushort parameter)
    {
        return parameter switch
        {
            1 => 0.02,
            2 => 0.02,
            3 => 0.06,
            4 => 0.04,
            _ => 0.06,
        };
    }

    private static double GetAstrologianCardAmount(ushort parameter, bool preferredRole)
    {
        if (parameter is 3 or 6)
        {
            return parameter / 100.0;
        }

        return preferredRole ? 0.06 : 0.03;
    }

    private static double GetTechnicalFinishAmount(ushort parameter)
    {
        return parameter switch
        {
            1 => 0.01,
            2 => 0.02,
            3 => 0.03,
            _ => 0.05,
        };
    }

    private static double GetStandardFinishAmount(ushort parameter)
    {
        return parameter is 1 or 2 ? 0.02 : 0.05;
    }

    private static RaidBuffEffect Damage(
        DamageStatusSnapshot status,
        double amount,
        RaidBuffDamageScope damageScope = RaidBuffDamageScope.All,
        RaidBuffTargeting targeting = RaidBuffTargeting.Area)
    {
        return new RaidBuffEffect(
            status.StatusId,
            RaidBuffEffectKind.DamageMultiplier,
            amount,
            status.Source,
            damageScope,
            targeting);
    }

    private static RaidBuffEffect Critical(
        DamageStatusSnapshot status,
        double amount,
        RaidBuffTargeting targeting = RaidBuffTargeting.Area)
    {
        return new RaidBuffEffect(
            status.StatusId,
            RaidBuffEffectKind.CriticalChance,
            amount,
            status.Source,
            Targeting: targeting);
    }

    private static RaidBuffEffect DirectHit(
        DamageStatusSnapshot status,
        double amount,
        RaidBuffTargeting targeting = RaidBuffTargeting.Area)
    {
        return new RaidBuffEffect(
            status.StatusId,
            RaidBuffEffectKind.DirectHitChance,
            amount,
            status.Source,
            Targeting: targeting);
    }
}
