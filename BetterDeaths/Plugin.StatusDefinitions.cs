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
        uint? StatusId = null);

    private sealed record BossMitigationStatusDefinition(
        uint StatusId,
        string Name,
        float? DamageDownPercent = null,
        float? PhysicalDamageDownPercent = null,
        float? MagicDamageDownPercent = null);

    private static readonly DefensiveStatusDefinition[] DefensiveStatusDefinitions =
    [
        // Shared / limit breaks
        new("Rampart", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 20.0f),
        new("Shield Wall", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 20.0f),
        new("Stronghold", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 40.0f),
        new("Last Bastion", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 80.0f),
        new("Land Waker", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 80.0f),

        // Paladin
        new("Sheltron", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 15.0f),
        new("Holy Sheltron", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 15.0f),
        new("Knight's Resolve", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 15.0f),
        new("Sentinel", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 30.0f),
        new("Guardian", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.Shield, DamageReductionPercent: 40.0f),
        new("Guardian's Will", DefensiveStatusEffect.Shield),
        new(
            "Intervention",
            DefensiveStatusEffect.DamageReduction,
            DamageReductionPercent: 10.0f,
            HasVariableMitigationPercent: true,
            MitigationPercentTooltip: "Intervention can gain additional mitigation based on the active defensives running on the caster.",
            InducedStatuses: [new(2675, "Knight's Resolve"), new(2676, "Knight's Benediction")]),
        new("Passage of Arms", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 15.0f),
        new("Arms Up", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 15.0f, StatusId: 1176),
        new("Bulwark", DefensiveStatusEffect.BlockOrParry),
        new("Divine Veil", DefensiveStatusEffect.Shield | DefensiveStatusEffect.HealingOrRegen),
        new("Radiant Veil", DefensiveStatusEffect.Shield, StatusId: 3546),
        new("Knight's Benediction", DefensiveStatusEffect.HealingOrRegen, StatusId: 2676),
        new("Hallowed Ground", DefensiveStatusEffect.InvulnerabilityOrDeathPrevention),

        // Warrior
        new("Raw Intuition", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.HealingOrRegen, DamageReductionPercent: 10.0f),
        new(
            "Bloodwhetting",
            DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.HealingOrRegen,
            DamageReductionPercent: 10.0f,
            InducedStatuses: [new(2679, "Stem the Flow"), new(2680, "Stem the Tide")]),
        new("Nascent Flash", DefensiveStatusEffect.HealingOrRegen),
        new("Nascent Glint", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.HealingOrRegen, DamageReductionPercent: 10.0f),
        new("Stem the Flow", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 10.0f),
        new("Stem the Tide", DefensiveStatusEffect.Shield),
        new("Vengeance", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 30.0f),
        new("Damnation", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.HealingOrRegen, DamageReductionPercent: 40.0f),
        new("Primeval Impulse", DefensiveStatusEffect.HealingOrRegen, StatusId: 3900),
        new("Shake It Off", DefensiveStatusEffect.Shield | DefensiveStatusEffect.HealingOrRegen),
        new("Shake It Off (Over Time)", DefensiveStatusEffect.HealingOrRegen, StatusId: 2108),
        new("Thrill of Battle", DefensiveStatusEffect.MaxHpIncrease | DefensiveStatusEffect.HealingReceivedIncrease),
        new("Equilibrium", DefensiveStatusEffect.HealingOrRegen, StatusId: 2681),
        new("Holmgang", DefensiveStatusEffect.InvulnerabilityOrDeathPrevention),

        // Dark Knight
        new("Dark Mind", DefensiveStatusEffect.DamageReduction, PhysicalDamageReductionPercent: 10.0f, MagicDamageReductionPercent: 20.0f),
        new("Dark Missionary", DefensiveStatusEffect.DamageReduction, PhysicalDamageReductionPercent: 5.0f, MagicDamageReductionPercent: 10.0f),
        new("Shadow Wall", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 30.0f),
        new("Shadowed Vigil", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.HealingOrRegen, DamageReductionPercent: 40.0f),
        new("Oblation", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 10.0f),
        new("The Blackest Night", DefensiveStatusEffect.Shield),
        new("Blackest Night", DefensiveStatusEffect.Shield, StatusId: 1178),
        new("Blackest Night", DefensiveStatusEffect.Shield, StatusId: 1308),
        new("Living Dead", DefensiveStatusEffect.InvulnerabilityOrDeathPrevention | DefensiveStatusEffect.HealingOrRegen),
        new("Walking Dead", DefensiveStatusEffect.InvulnerabilityOrDeathPrevention | DefensiveStatusEffect.HealingOrRegen),
        new("Undead Rebirth", DefensiveStatusEffect.InvulnerabilityOrDeathPrevention),
        new("Undead Redemption", DefensiveStatusEffect.InvulnerabilityOrDeathPrevention | DefensiveStatusEffect.HealingOrRegen, StatusId: 3039),
        new("Dark Force", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 80.0f),

        // Gunbreaker
        new("Camouflage", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.BlockOrParry, DamageReductionPercent: 10.0f),
        new("Nebula", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 30.0f),
        new("Great Nebula", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.MaxHpIncrease | DefensiveStatusEffect.HealingOrRegen, DamageReductionPercent: 40.0f),
        new("Heart of Stone", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.Shield, DamageReductionPercent: 15.0f),
        new("Heart of Corundum", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.Shield | DefensiveStatusEffect.HealingOrRegen, DamageReductionPercent: 15.0f),
        new("Clarity of Corundum", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 15.0f),
        new("Catharsis of Corundum", DefensiveStatusEffect.HealingOrRegen, StatusId: 2685),
        new("Heart of Light", DefensiveStatusEffect.DamageReduction, PhysicalDamageReductionPercent: 5.0f, MagicDamageReductionPercent: 10.0f),
        new("Brutal Shell", DefensiveStatusEffect.Shield | DefensiveStatusEffect.HealingOrRegen),
        new("Superbolide", DefensiveStatusEffect.InvulnerabilityOrDeathPrevention),
        new("Gunmetal Soul", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 80.0f),

        // White Mage
        new("Divine Benison", DefensiveStatusEffect.Shield),
        new("Temperance", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.HealingReceivedIncrease, DamageReductionPercent: 10.0f),
        new("Aquaveil", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 15.0f),
        new("Divine Caress", DefensiveStatusEffect.Shield | DefensiveStatusEffect.HealingOrRegen),
        new("Confession", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.HealingOrRegen, DamageReductionPercent: 10.0f),

        // Scholar
        new("Galvanize", DefensiveStatusEffect.Shield),
        new("Catalyze", DefensiveStatusEffect.Shield),
        new("Seraphic Veil", DefensiveStatusEffect.Shield),
        new("Consolation", DefensiveStatusEffect.Shield),
        new("Sacred Soil", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.HealingOrRegen, DamageReductionPercent: 10.0f),
        new("Expedient", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 10.0f),
        new("Desperate Measures", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 10.0f, StatusId: 2711),
        new("Fey Illumination", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.HealingReceivedIncrease, MagicDamageReductionPercent: 5.0f),
        new("Seraphic Illumination", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.HealingReceivedIncrease, MagicDamageReductionPercent: 5.0f),
        new("Protraction", DefensiveStatusEffect.MaxHpIncrease | DefensiveStatusEffect.HealingReceivedIncrease),

        // Astrologian
        new("Collective Unconscious", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.HealingOrRegen, DamageReductionPercent: 10.0f),
        new("Wheel of Fortune", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 10.0f, StatusId: 1206),
        new("Exaltation", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.HealingOrRegen, DamageReductionPercent: 10.0f),
        new("Sun Sign", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 10.0f),
        new("The Bole", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 10.0f),
        new("The Arrow", DefensiveStatusEffect.HealingReceivedIncrease, StatusId: 3888),
        new("The Spire", DefensiveStatusEffect.Shield),
        new("Macrocosmos", DefensiveStatusEffect.HealingOrRegen, StatusId: 2718),
        new("Macrocosmos", DefensiveStatusEffect.HealingOrRegen, StatusId: 3104),
        new("Celestial Intersection", DefensiveStatusEffect.Shield | DefensiveStatusEffect.HealingOrRegen),
        new("Intersection", DefensiveStatusEffect.Shield | DefensiveStatusEffect.HealingOrRegen),
        new("Neutral Sect", DefensiveStatusEffect.Shield),
        new("Nocturnal Field", DefensiveStatusEffect.Shield),
        new("Nocturnal Balance", DefensiveStatusEffect.Shield),
        new("Nocturnal Intersection", DefensiveStatusEffect.Shield),

        // Sage
        new("Eukrasian Diagnosis", DefensiveStatusEffect.Shield),
        new("Differential Diagnosis", DefensiveStatusEffect.Shield),
        new("Eukrasian Prognosis", DefensiveStatusEffect.Shield),
        new("Eukrasian Prognosis II", DefensiveStatusEffect.Shield),
        new("Kerachole", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.HealingOrRegen, DamageReductionPercent: 10.0f),
        new("Kerakeia", DefensiveStatusEffect.HealingOrRegen, StatusId: 2938),
        new("Taurochole", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 10.0f),
        new("Holos", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.Shield | DefensiveStatusEffect.HealingOrRegen, DamageReductionPercent: 10.0f),
        new("Holosakos", DefensiveStatusEffect.Shield, StatusId: 3365),
        new("Physis II", DefensiveStatusEffect.HealingOrRegen, StatusId: 2620),
        new("Autophysis", DefensiveStatusEffect.HealingReceivedIncrease, StatusId: 2621),
        new("Krasis", DefensiveStatusEffect.HealingReceivedIncrease, StatusId: 2622),
        new("Haima", DefensiveStatusEffect.Shield | DefensiveStatusEffect.HealingOrRegen),
        new("Haimatinon", DefensiveStatusEffect.Shield | DefensiveStatusEffect.HealingOrRegen, StatusId: 2642),
        new("Panhaima", DefensiveStatusEffect.Shield | DefensiveStatusEffect.HealingOrRegen),
        new("Panhaimatinon", DefensiveStatusEffect.Shield | DefensiveStatusEffect.HealingOrRegen, StatusId: 2643),

        // Melee DPS
        new("Riddle of Earth", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.HealingOrRegen, DamageReductionPercent: 20.0f),
        new("Mantra", DefensiveStatusEffect.HealingReceivedIncrease, StatusId: 102),
        new("Shade Shift", DefensiveStatusEffect.Shield),
        new("Third Eye", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 10.0f),
        new("Tengentsu", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 10.0f, StatusId: 3853),
        new("Tengentsu's Foresight", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.HealingOrRegen, DamageReductionPercent: 10.0f, StatusId: 3854),
        new("Arcane Crest", DefensiveStatusEffect.Shield | DefensiveStatusEffect.HealingOrRegen),
        new("Crest of Time Borrowed", DefensiveStatusEffect.Shield | DefensiveStatusEffect.HealingOrRegen),

        // Physical ranged DPS
        new("Troubadour", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 15.0f),
        new("Nature's Minne", DefensiveStatusEffect.HealingReceivedIncrease, StatusId: 1202),
        new("Tactician", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 15.0f),
        new("Shield Samba", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 15.0f),
        new("Improvisation", DefensiveStatusEffect.HealingReceivedIncrease, StatusId: 1828),
        new("Improvised Finish", DefensiveStatusEffect.Shield),

        // Magical ranged DPS
        new("Manaward", DefensiveStatusEffect.Shield),
        new("Radiant Aegis", DefensiveStatusEffect.Shield),
        new("Magick Barrier", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.HealingReceivedIncrease, MagicDamageReductionPercent: 10.0f),
        new("Tempera Coat", DefensiveStatusEffect.Shield),
        new("Tempera Grassa", DefensiveStatusEffect.Shield),
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

    private static bool TryGetDefensiveStatusDefinition(StatusSnapshot status, out DefensiveStatusDefinition definition)
    {
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
