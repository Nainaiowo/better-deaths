namespace BetterDeaths;

using System;
using System.Collections.Generic;
using System.Linq;

public sealed partial class Plugin
{
    internal const uint PhysicalDamageReductionIconId = 60011;
    internal const uint MagicDamageReductionIconId = 60012;
    private const string IncomingAllDamageReductionTooltip = "Reduces all incoming damage.";
    private const string IncomingPhysicalDamageReductionTooltip = "Reduces incoming physical damage.";
    private const string IncomingMagicDamageReductionTooltip = "Reduces incoming magic damage.";
    private const string ShieldTooltip = "Absorbs damage with a shield.";
    private const string RegenTooltip = "Adds a regen or delayed healing effect.";
    private const string HealingReceivedTooltip = "Increases healing received.";
    private const string MaxHpTooltip = "Increases maximum HP.";
    private const string InvulnerabilityTooltip = "Prevents or heavily limits lethal damage.";
    private const string BlockParryTooltip = "Adds block or parry-based damage reduction.";
    private const string OtherMitigationTooltip = "Tracked defensive context that Better Deaths does not classify more specifically yet.";
    private const string BossAllDamageDownTooltip = "Reduces all damage dealt by the affected enemy.";
    private const string BossPhysicalDamageDownTooltip = "Reduces physical damage dealt by the affected enemy.";
    private const string BossMagicDamageDownTooltip = "Reduces magic damage dealt by the affected enemy.";
    private const string BossDamageDownTooltip = "Boss-side damage-down debuff captured at death.";
    private const string DebuffTooltip = "Relevant debuff captured at death.";

    internal sealed record MitigationTypeDisplay(string Label, uint IconId = 0, string? Tooltip = null);

    internal enum MitigationPercentScope
    {
        All,
        Physical,
        Magic,
    }

    internal sealed record MitigationPercentDisplay(
        string Text,
        float Percent,
        MitigationPercentScope Scope,
        uint IconId = 0,
        string? Tooltip = null);

    internal sealed record InducedMitigationDisplay(uint StatusId, string Name);

    internal sealed record MitigationDisplayInfo(
        IReadOnlyList<MitigationTypeDisplay> Types,
        IReadOnlyList<MitigationPercentDisplay> MitigationPercents,
        bool HasVariableMitigationPercent,
        string? MitigationPercentTooltip,
        IReadOnlyList<InducedMitigationDisplay> InducedStatuses);

    [Flags]
    private enum DefensiveStatusEffect
    {
        None = 0,
        DamageReduction = 1 << 0,
        Shield = 1 << 1,
        HealingOrRegen = 1 << 2,
        HealingReceivedIncrease = 1 << 3,
        MaxHpIncrease = 1 << 4,
        InvulnerabilityOrDeathPrevention = 1 << 5,
        BlockOrParry = 1 << 6,
        HealingPotencyIncrease = 1 << 7,
    }

    private sealed record DefensiveStatusDefinition(
        string Name,
        DefensiveStatusEffect Effects,
        float? DamageReductionPercent = null,
        float? PhysicalDamageReductionPercent = null,
        float? MagicDamageReductionPercent = null,
        bool HasVariableMitigationPercent = false,
        string? MitigationPercentTooltip = null,
        InducedMitigationDisplay[]? InducedStatuses = null,
        uint? StatusId = null,
        uint[]? StatusIds = null);

    private sealed record BossMitigationStatusDefinition(
        uint StatusId,
        string Name,
        float? DamageDownPercent = null,
        float? PhysicalDamageDownPercent = null,
        float? MagicDamageDownPercent = null);

    private static readonly DefensiveStatusDefinition[] DefensiveStatusDefinitions =
    [
        // Shared / limit breaks
        new("Rampart", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 20.0f, StatusIds: [71, 1191, 1978, 4168]),
        new("Shield Wall", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 20.0f, StatusIds: [194]),
        new("Stronghold", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 40.0f, StatusIds: [195]),
        new("Last Bastion", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 80.0f, StatusIds: [196]),
        new("Land Waker", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 80.0f, StatusIds: [863]),

        // Paladin
        new("Sheltron", DefensiveStatusEffect.BlockOrParry, StatusId: 728),
        new("Sheltron", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 15.0f, StatusIds: [1856]),
        new("Holy Sheltron", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 15.0f, StatusIds: [2674]),
        new("Knight's Resolve", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 15.0f, StatusIds: [2675]),
        new("Sentinel", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 30.0f, StatusIds: [74]),
        new("Guardian", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.Shield, DamageReductionPercent: 40.0f, StatusIds: [3829]),
        new("Guardian's Will", DefensiveStatusEffect.Shield, StatusIds: [3830]),
        new(
            "Intervention",
            DefensiveStatusEffect.DamageReduction,
            DamageReductionPercent: 10.0f,
            HasVariableMitigationPercent: true,
            MitigationPercentTooltip: "Intervention can gain additional mitigation based on the active defensives running on the caster.",
            InducedStatuses: [new(2675, "Knight's Resolve"), new(2676, "Knight's Benediction")], StatusIds: [1174, 2020]),
        new("Passage of Arms", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 15.0f, StatusIds: [1175]),
        new("Arms Up", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 15.0f, StatusId: 1176),
        new("Bulwark", DefensiveStatusEffect.BlockOrParry, StatusIds: [77]),
        new("Divine Veil", DefensiveStatusEffect.Shield | DefensiveStatusEffect.HealingOrRegen, StatusIds: [726, 727, 1362, 2168, 2169]),
        new("Radiant Veil", DefensiveStatusEffect.Shield, StatusId: 3546),
        new("Knight's Benediction", DefensiveStatusEffect.HealingOrRegen, StatusId: 2676),
        new("Hallowed Ground", DefensiveStatusEffect.InvulnerabilityOrDeathPrevention, StatusIds: [82, 1302]),

        // Warrior
        new("Raw Intuition", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.HealingOrRegen, DamageReductionPercent: 10.0f, StatusIds: [735]),
        new(
            "Bloodwhetting",
            DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.HealingOrRegen,
            DamageReductionPercent: 10.0f,
            InducedStatuses: [new(2679, "Stem the Flow"), new(2680, "Stem the Tide")], StatusIds: [2678]),
        new("Nascent Flash", DefensiveStatusEffect.HealingOrRegen, StatusIds: [1857, 2061, 2227]),
        new("Nascent Glint", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.HealingOrRegen, DamageReductionPercent: 10.0f, StatusIds: [1858, 2062]),
        new("Stem the Flow", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 10.0f, StatusIds: [2679]),
        new("Stem the Tide", DefensiveStatusEffect.Shield, StatusIds: [2680, 3031]),
        new("Vengeance", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 30.0f, StatusIds: [89]),
        new("Damnation", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.HealingOrRegen, DamageReductionPercent: 40.0f, StatusIds: [3832]),
        new("Primeval Impulse", DefensiveStatusEffect.HealingOrRegen, StatusId: 3900),
        new("Shake It Off", DefensiveStatusEffect.Shield | DefensiveStatusEffect.HealingOrRegen, StatusIds: [1457, 1993]),
        new("Shake It Off (Over Time)", DefensiveStatusEffect.HealingOrRegen, StatusId: 2108),
        new("Thrill of Battle", DefensiveStatusEffect.MaxHpIncrease | DefensiveStatusEffect.HealingReceivedIncrease, StatusIds: [87, 3185]),
        new("Equilibrium", DefensiveStatusEffect.HealingOrRegen, StatusId: 2681),
        new("Holmgang", DefensiveStatusEffect.InvulnerabilityOrDeathPrevention, StatusIds: [409, 1304]),

        // Dark Knight
        new("Dark Mind", DefensiveStatusEffect.DamageReduction, PhysicalDamageReductionPercent: 10.0f, MagicDamageReductionPercent: 20.0f, StatusIds: [746]),
        new("Dark Missionary", DefensiveStatusEffect.DamageReduction, PhysicalDamageReductionPercent: 5.0f, MagicDamageReductionPercent: 10.0f, StatusIds: [1894, 2171]),
        new("Shadow Wall", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 30.0f, StatusIds: [747]),
        new("Shadowed Vigil", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.HealingOrRegen, DamageReductionPercent: 40.0f, StatusIds: [3835]),
        new("Oblation", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 10.0f, StatusIds: [2682]),
        new("Blackest Night", DefensiveStatusEffect.Shield, StatusId: 1178),
        new("Blackest Night", DefensiveStatusEffect.Shield, StatusId: 1308),
        new("Living Dead", DefensiveStatusEffect.InvulnerabilityOrDeathPrevention | DefensiveStatusEffect.HealingOrRegen, StatusIds: [810]),
        new("Walking Dead", DefensiveStatusEffect.InvulnerabilityOrDeathPrevention | DefensiveStatusEffect.HealingOrRegen, StatusIds: [811]),
        new("Undead Rebirth", DefensiveStatusEffect.InvulnerabilityOrDeathPrevention, StatusIds: [3255]),
        new("Undead Redemption", DefensiveStatusEffect.InvulnerabilityOrDeathPrevention | DefensiveStatusEffect.HealingOrRegen, StatusId: 3039),
        new("Dark Force", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 80.0f, StatusIds: [864]),

        // Gunbreaker
        new("Camouflage", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.BlockOrParry, DamageReductionPercent: 10.0f, StatusIds: [1832]),
        new("Nebula", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 30.0f, StatusIds: [1834]),
        new("Great Nebula", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.MaxHpIncrease | DefensiveStatusEffect.HealingOrRegen, DamageReductionPercent: 40.0f, StatusIds: [3838]),
        new("Heart of Stone", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.Shield, DamageReductionPercent: 15.0f, StatusIds: [1840]),
        new("Heart of Corundum", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.Shield | DefensiveStatusEffect.HealingOrRegen, DamageReductionPercent: 15.0f, StatusIds: [2683, 4295]),
        new("Clarity of Corundum", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 15.0f, StatusIds: [2684]),
        new("Catharsis of Corundum", DefensiveStatusEffect.HealingOrRegen, StatusId: 2685),
        new("Heart of Light", DefensiveStatusEffect.DamageReduction, PhysicalDamageReductionPercent: 5.0f, MagicDamageReductionPercent: 10.0f, StatusIds: [1839, 2000]),
        new("Brutal Shell", DefensiveStatusEffect.Shield | DefensiveStatusEffect.HealingOrRegen, StatusIds: [1898, 1997]),
        new("Superbolide", DefensiveStatusEffect.InvulnerabilityOrDeathPrevention, StatusIds: [1836]),
        new("Gunmetal Soul", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 80.0f, StatusIds: [1931]),

        // White Mage
        new("Divine Benison", DefensiveStatusEffect.Shield, StatusIds: [1218, 1404]),
        new("Temperance", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.HealingPotencyIncrease, DamageReductionPercent: 10.0f, StatusId: 1872),
        new("Temperance", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 10.0f, StatusIds: [1873, 3985]),
        new("Aquaveil", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 15.0f, StatusId: 2708),
        new("Divine Caress", DefensiveStatusEffect.Shield | DefensiveStatusEffect.HealingOrRegen, StatusIds: [3903]),
        new("Confession", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.HealingOrRegen, DamageReductionPercent: 10.0f, StatusIds: [1219]),

        // Scholar
        new("Galvanize", DefensiveStatusEffect.Shield, StatusIds: [297, 1331, 3087]),
        new("Catalyze", DefensiveStatusEffect.Shield, StatusIds: [1918]),
        new("Seraphic Veil", DefensiveStatusEffect.Shield, StatusIds: [1917, 2040, 3097]),
        new("Consolation", DefensiveStatusEffect.Shield, StatusIds: [3098]),
        new("Sacred Soil", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.HealingOrRegen, DamageReductionPercent: 10.0f, StatusIds: [298, 299, 1944, 2637, 2638]),
        new("Desperate Measures", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 10.0f, StatusId: 2711),
        new("Fey Illumination", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.HealingPotencyIncrease, MagicDamageReductionPercent: 5.0f, StatusIds: [317]),
        new("Seraphic Illumination", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.HealingPotencyIncrease, MagicDamageReductionPercent: 5.0f, StatusIds: [1875]),
        new("Protraction", DefensiveStatusEffect.MaxHpIncrease | DefensiveStatusEffect.HealingReceivedIncrease, StatusIds: [2710]),

        // Astrologian
        new("Collective Unconscious", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.HealingOrRegen, DamageReductionPercent: 10.0f, StatusIds: [849, 2283]),
        new("Wheel of Fortune", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 10.0f, StatusId: 1206),
        new("Exaltation", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.HealingOrRegen, DamageReductionPercent: 10.0f, StatusIds: [2717]),
        new("Sun Sign", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 10.0f, StatusId: 3896),
        new("The Bole", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 10.0f, StatusIds: [830, 1339, 3890]),
        new("The Arrow", DefensiveStatusEffect.HealingReceivedIncrease, StatusId: 3888),
        new("The Spire", DefensiveStatusEffect.Shield, StatusIds: [3892]),
        new("Macrocosmos", DefensiveStatusEffect.HealingOrRegen, StatusId: 2718),
        new("Macrocosmos", DefensiveStatusEffect.HealingOrRegen, StatusId: 3104),
        new("Intersection", DefensiveStatusEffect.Shield | DefensiveStatusEffect.HealingOrRegen, StatusIds: [1889, 4040]),
        new("Neutral Sect", DefensiveStatusEffect.Shield, StatusIds: [1921, 3988]),
        new("Neutral Sect", DefensiveStatusEffect.HealingPotencyIncrease, StatusId: 1892),
        new("Nocturnal Field", DefensiveStatusEffect.Shield, StatusIds: [837, 2183]),
        new("Nocturnal Balance", DefensiveStatusEffect.Shield, StatusIds: [2043]),

        // Sage
        new("Eukrasian Diagnosis", DefensiveStatusEffect.Shield, StatusIds: [2607, 2865, 3109]),
        new("Differential Diagnosis", DefensiveStatusEffect.Shield, StatusIds: [2608]),
        new("Eukrasian Prognosis", DefensiveStatusEffect.Shield, StatusIds: [2609, 2866]),
        new("Kerachole", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.HealingOrRegen, DamageReductionPercent: 10.0f, StatusIds: [2618]),
        new("Kerakeia", DefensiveStatusEffect.HealingOrRegen, StatusId: 2938),
        new("Taurochole", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 10.0f, StatusIds: [2619]),
        new("Holos", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.Shield | DefensiveStatusEffect.HealingOrRegen, DamageReductionPercent: 10.0f, StatusIds: [3003]),
        new("Holosakos", DefensiveStatusEffect.Shield, StatusId: 3365),
        new("Physis II", DefensiveStatusEffect.HealingOrRegen, StatusId: 2620),
        new("Autophysis", DefensiveStatusEffect.HealingReceivedIncrease, StatusId: 2621),
        new("Krasis", DefensiveStatusEffect.HealingReceivedIncrease, StatusId: 2622),
        new("Haima", DefensiveStatusEffect.Shield | DefensiveStatusEffect.HealingOrRegen, StatusIds: [2612, 2869, 3110]),
        new("Haimatinon", DefensiveStatusEffect.Shield | DefensiveStatusEffect.HealingOrRegen, StatusId: 2642),
        new("Panhaima", DefensiveStatusEffect.Shield | DefensiveStatusEffect.HealingOrRegen, StatusIds: [2613]),
        new("Panhaimatinon", DefensiveStatusEffect.Shield | DefensiveStatusEffect.HealingOrRegen, StatusId: 2643),

        // Melee DPS
        new("Riddle of Earth", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.HealingOrRegen, DamageReductionPercent: 20.0f, StatusIds: [1179, 1310, 2008]),
        new("Mantra", DefensiveStatusEffect.HealingReceivedIncrease, StatusId: 102),
        new("Shade Shift", DefensiveStatusEffect.Shield, StatusIds: [488, 2011]),
        new("Third Eye", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 10.0f, StatusIds: [1232]),
        new("Tengentsu", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 10.0f, StatusId: 3853),
        new("Tengentsu's Foresight", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.HealingOrRegen, DamageReductionPercent: 10.0f, StatusId: 3854),
        new("Crest of Time Borrowed", DefensiveStatusEffect.Shield | DefensiveStatusEffect.HealingOrRegen, StatusIds: [2596, 2597, 2861]),

        // Physical ranged DPS
        new("Troubadour", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 15.0f, StatusIds: [1934]),
        new("Nature's Minne", DefensiveStatusEffect.HealingReceivedIncrease, StatusId: 1202),
        new("Tactician", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 15.0f, StatusIds: [1951, 2177]),
        new("Shield Samba", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 15.0f, StatusIds: [1826]),
        new("Improvisation", DefensiveStatusEffect.HealingReceivedIncrease, StatusId: 1828),
        new("Improvised Finish", DefensiveStatusEffect.Shield, StatusIds: [2697]),

        // Magical ranged DPS
        new("Manaward", DefensiveStatusEffect.Shield, StatusIds: [168, 1989]),
        new("Radiant Aegis", DefensiveStatusEffect.Shield, StatusIds: [2702, 3224]),
        new("Magick Barrier", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.HealingReceivedIncrease, MagicDamageReductionPercent: 10.0f, StatusIds: [2707, 3240]),
        new("Tempera Coat", DefensiveStatusEffect.Shield, StatusIds: [3686, 4114]),
        new("Tempera Grassa", DefensiveStatusEffect.Shield, StatusIds: [3687, 4115]),
    ];

    private static readonly BossMitigationStatusDefinition[] BossMitigationStatusDefinitions =
    [
        new(1203, "Addle", PhysicalDamageDownPercent: 5.0f, MagicDamageDownPercent: 10.0f),
        new(1195, "Feint", PhysicalDamageDownPercent: 10.0f, MagicDamageDownPercent: 5.0f),
        new(1193, "Reprisal", DamageDownPercent: 10.0f),
        new(860, "Dismantled", DamageDownPercent: 10.0f),
    ];

    private static readonly HashSet<uint> BossMitigationStatusIds = BossMitigationStatusDefinitions
        .Select(status => status.StatusId)
        .ToHashSet();

    private static readonly IReadOnlyDictionary<uint, DefensiveStatusDefinition> DefensiveStatusesById = DefensiveStatusDefinitions
        .SelectMany(definition => (definition.StatusId is { } id ? new[] { id } : definition.StatusIds ?? [])
            .Select(id => (Id: id, Definition: definition)))
        .GroupBy(value => value.Id).ToDictionary(group => group.Key, group => group.First().Definition);

    internal static bool IsDefensiveStatus(StatusSnapshot status) => TryGetDefensiveStatusDefinition(status, out _);

    internal static bool IsDefensiveStatusId(uint statusId) => DefensiveStatusesById.ContainsKey(statusId);

    internal static bool IsBossMitigationStatus(StatusSnapshot status) => BossMitigationStatusIds.Contains(status.Id);

    internal static MitigationDisplayInfo GetMitigationDisplayInfo(StatusSnapshot status)
    {
        if (TryGetDefensiveStatusDefinition(status, out var defensiveStatus))
        {
            var mitigationPercents = BuildMitigationPercentDisplays(
                defensiveStatus.DamageReductionPercent,
                defensiveStatus.PhysicalDamageReductionPercent,
                defensiveStatus.MagicDamageReductionPercent,
                defensiveStatus.HasVariableMitigationPercent,
                IncomingAllDamageReductionTooltip,
                IncomingPhysicalDamageReductionTooltip,
                IncomingMagicDamageReductionTooltip);
            return new MitigationDisplayInfo(
                BuildMitigationTypes(defensiveStatus),
                mitigationPercents,
                defensiveStatus.HasVariableMitigationPercent,
                defensiveStatus.MitigationPercentTooltip,
                defensiveStatus.InducedStatuses ?? []);
        }

        var bossStatus = BossMitigationStatusDefinitions
            .FirstOrDefault(definition => definition.StatusId == status.Id);
        if (bossStatus is not null)
        {
            var mitigationPercents = BuildMitigationPercentDisplays(
                bossStatus.DamageDownPercent,
                bossStatus.PhysicalDamageDownPercent,
                bossStatus.MagicDamageDownPercent,
                false,
                BossAllDamageDownTooltip,
                BossPhysicalDamageDownTooltip,
                BossMagicDamageDownTooltip);
            return new MitigationDisplayInfo(
                BuildBossMitigationTypes(bossStatus),
                mitigationPercents,
                false,
                null,
                []);
        }

        return new MitigationDisplayInfo(
            [new("Debuff", Tooltip: DebuffTooltip)],
            [],
            false,
            null,
            []);
    }

    internal static IReadOnlyList<uint> GetMitigationEffectStatusIds(StatusSnapshot template)
    {
        if (IsBossMitigationStatus(template))
            return [template.Id];
        var definitions = template.Id == 0
            ? DefensiveStatusDefinitions.Where(value => value.Name.Equals(template.Name, StringComparison.OrdinalIgnoreCase))
            : DefensiveStatusesById.TryGetValue(template.Id, out var definition) ? [definition] : Enumerable.Empty<DefensiveStatusDefinition>();
        return definitions.SelectMany(value => value.StatusId is { } id ? new[] { id } : value.StatusIds ?? [])
            .Distinct().ToArray();
    }

    private static bool TryGetDefensiveStatusDefinition(StatusSnapshot status, out DefensiveStatusDefinition definition)
    {
        if (status.Id != 0)
            return DefensiveStatusesById.TryGetValue(status.Id, out definition!);
        foreach (var candidate in DefensiveStatusDefinitions)
        {
            if (DefensiveStatusDefinitionMatches(candidate, status))
            {
                definition = candidate;
                return true;
            }
        }

        definition = null!;
        return false;
    }

    private static bool DefensiveStatusDefinitionMatches(DefensiveStatusDefinition definition, StatusSnapshot status)
    {
        if (definition.StatusId is { } statusId)
        {
            return status.Id == statusId;
        }

        if (definition.StatusIds is { } ids && status.Id != 0)
            return Array.IndexOf(ids, status.Id) >= 0;

        return definition.Name.Equals(status.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<MitigationTypeDisplay> BuildMitigationTypes(DefensiveStatusDefinition definition)
    {
        var types = new List<MitigationTypeDisplay>();
        if (definition.DamageReductionPercent is not null)
        {
            types.Add(new("All", Tooltip: IncomingAllDamageReductionTooltip));
        }

        if (definition.PhysicalDamageReductionPercent is not null)
        {
            types.Add(new("Physical", PhysicalDamageReductionIconId, IncomingPhysicalDamageReductionTooltip));
        }

        if (definition.MagicDamageReductionPercent is not null)
        {
            types.Add(new("Magic", MagicDamageReductionIconId, IncomingMagicDamageReductionTooltip));
        }

        if (definition.Effects.HasFlag(DefensiveStatusEffect.Shield))
        {
            types.Add(new("Shield", Tooltip: ShieldTooltip));
        }

        if (definition.Effects.HasFlag(DefensiveStatusEffect.HealingOrRegen))
        {
            types.Add(new("Regen", Tooltip: RegenTooltip));
        }

        if (definition.Effects.HasFlag(DefensiveStatusEffect.HealingReceivedIncrease))
        {
            types.Add(new("Heal+", Tooltip: HealingReceivedTooltip));
        }

        if (definition.Effects.HasFlag(DefensiveStatusEffect.MaxHpIncrease))
        {
            types.Add(new("Max HP", Tooltip: MaxHpTooltip));
        }

        if (definition.Effects.HasFlag(DefensiveStatusEffect.InvulnerabilityOrDeathPrevention))
        {
            types.Add(new("Invuln", Tooltip: InvulnerabilityTooltip));
        }

        if (definition.Effects.HasFlag(DefensiveStatusEffect.BlockOrParry))
        {
            types.Add(new("Block", Tooltip: BlockParryTooltip));
        }

        if (definition.Effects.HasFlag(DefensiveStatusEffect.HealingPotencyIncrease))
            types.Add(new("Heal potency", Tooltip: "Increases the caster's healing magic potency."));

        return types.Count == 0 ? [new("Other", Tooltip: OtherMitigationTooltip)] : types;
    }

    private static IReadOnlyList<MitigationTypeDisplay> BuildBossMitigationTypes(BossMitigationStatusDefinition definition)
    {
        var types = new List<MitigationTypeDisplay>();
        if (definition.DamageDownPercent is not null)
        {
            types.Add(new("All", Tooltip: BossAllDamageDownTooltip));
        }

        if (definition.PhysicalDamageDownPercent is not null)
        {
            types.Add(new("Physical", PhysicalDamageReductionIconId, BossPhysicalDamageDownTooltip));
        }

        if (definition.MagicDamageDownPercent is not null)
        {
            types.Add(new("Magic", MagicDamageReductionIconId, BossMagicDamageDownTooltip));
        }

        return types.Count == 0 ? [new("Boss DD", Tooltip: BossDamageDownTooltip)] : types;
    }

    private static IReadOnlyList<MitigationPercentDisplay> BuildMitigationPercentDisplays(
        float? allPercent,
        float? physicalPercent,
        float? magicPercent,
        bool variable,
        string allTooltip,
        string physicalTooltip,
        string magicTooltip)
    {
        var parts = new List<MitigationPercentDisplay>();
        if (allPercent is not null)
        {
            parts.Add(new(
                FormatMitigationPercentValue(allPercent.Value, variable),
                allPercent.Value,
                MitigationPercentScope.All,
                Tooltip: allTooltip));
        }

        if (physicalPercent is not null)
        {
            parts.Add(new(
                FormatMitigationPercentValue(physicalPercent.Value, variable),
                physicalPercent.Value,
                MitigationPercentScope.Physical,
                PhysicalDamageReductionIconId,
                physicalTooltip));
        }

        if (magicPercent is not null)
        {
            parts.Add(new(
                FormatMitigationPercentValue(magicPercent.Value, variable),
                magicPercent.Value,
                MitigationPercentScope.Magic,
                MagicDamageReductionIconId,
                magicTooltip));
        }

        return parts;
    }

    private static string FormatMitigationPercentValue(float percent, bool variable)
    {
        var value = $"{percent:0.#}%";
        return variable ? $"{value}+" : value;
    }
}
