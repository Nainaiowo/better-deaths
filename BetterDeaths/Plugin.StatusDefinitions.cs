namespace BetterDeaths;

using System;
using System.Collections.Generic;
using System.Linq;

public sealed partial class Plugin
{
    private const uint PhysicalDamageReductionIconId = 60011;
    private const uint MagicDamageReductionIconId = 60012;

    internal sealed record MitigationTypeDisplay(string Label, uint IconId = 0, string? Tooltip = null);

    internal sealed record InducedMitigationDisplay(uint StatusId, string Name);

    internal sealed record MitigationDisplayInfo(
        IReadOnlyList<MitigationTypeDisplay> Types,
        string MitigationPercentText,
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
        InducedMitigationDisplay[]? InducedStatuses = null);

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
        new("Bulwark", DefensiveStatusEffect.BlockOrParry),
        new("Divine Veil", DefensiveStatusEffect.Shield | DefensiveStatusEffect.HealingOrRegen),
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
        new("Shake It Off", DefensiveStatusEffect.Shield | DefensiveStatusEffect.HealingOrRegen),
        new("Thrill of Battle", DefensiveStatusEffect.MaxHpIncrease | DefensiveStatusEffect.HealingReceivedIncrease),
        new("Holmgang", DefensiveStatusEffect.InvulnerabilityOrDeathPrevention),

        // Dark Knight
        new("Dark Mind", DefensiveStatusEffect.DamageReduction, PhysicalDamageReductionPercent: 10.0f, MagicDamageReductionPercent: 20.0f),
        new("Dark Missionary", DefensiveStatusEffect.DamageReduction, PhysicalDamageReductionPercent: 5.0f, MagicDamageReductionPercent: 10.0f),
        new("Shadow Wall", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 30.0f),
        new("Shadowed Vigil", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.HealingOrRegen, DamageReductionPercent: 40.0f),
        new("Oblation", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 10.0f),
        new("The Blackest Night", DefensiveStatusEffect.Shield),
        new("Living Dead", DefensiveStatusEffect.InvulnerabilityOrDeathPrevention | DefensiveStatusEffect.HealingOrRegen),
        new("Walking Dead", DefensiveStatusEffect.InvulnerabilityOrDeathPrevention | DefensiveStatusEffect.HealingOrRegen),
        new("Undead Rebirth", DefensiveStatusEffect.InvulnerabilityOrDeathPrevention),
        new("Dark Force", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 80.0f),

        // Gunbreaker
        new("Camouflage", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.BlockOrParry, DamageReductionPercent: 10.0f),
        new("Nebula", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 30.0f),
        new("Great Nebula", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.MaxHpIncrease | DefensiveStatusEffect.HealingOrRegen, DamageReductionPercent: 40.0f),
        new("Heart of Stone", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.Shield, DamageReductionPercent: 15.0f),
        new("Heart of Corundum", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.Shield | DefensiveStatusEffect.HealingOrRegen, DamageReductionPercent: 15.0f),
        new("Clarity of Corundum", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 15.0f),
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
        new("Fey Illumination", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.HealingReceivedIncrease, MagicDamageReductionPercent: 5.0f),
        new("Seraphic Illumination", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.HealingReceivedIncrease, MagicDamageReductionPercent: 5.0f),
        new("Protraction", DefensiveStatusEffect.MaxHpIncrease | DefensiveStatusEffect.HealingReceivedIncrease),

        // Astrologian
        new("Collective Unconscious", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.HealingOrRegen, DamageReductionPercent: 10.0f),
        new("Exaltation", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.HealingOrRegen, DamageReductionPercent: 10.0f),
        new("Sun Sign", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 10.0f),
        new("The Bole", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 10.0f),
        new("The Spire", DefensiveStatusEffect.Shield),
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
        new("Taurochole", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 10.0f),
        new("Holos", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.Shield | DefensiveStatusEffect.HealingOrRegen, DamageReductionPercent: 10.0f),
        new("Haima", DefensiveStatusEffect.Shield | DefensiveStatusEffect.HealingOrRegen),
        new("Panhaima", DefensiveStatusEffect.Shield | DefensiveStatusEffect.HealingOrRegen),

        // Melee DPS
        new("Riddle of Earth", DefensiveStatusEffect.DamageReduction | DefensiveStatusEffect.HealingOrRegen, DamageReductionPercent: 20.0f),
        new("Shade Shift", DefensiveStatusEffect.Shield),
        new("Third Eye", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 10.0f),
        new("Arcane Crest", DefensiveStatusEffect.Shield | DefensiveStatusEffect.HealingOrRegen),
        new("Crest of Time Borrowed", DefensiveStatusEffect.Shield | DefensiveStatusEffect.HealingOrRegen),

        // Physical ranged DPS
        new("Troubadour", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 15.0f),
        new("Tactician", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 15.0f),
        new("Shield Samba", DefensiveStatusEffect.DamageReduction, DamageReductionPercent: 15.0f),
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

    private static readonly HashSet<string> DefensiveStatusNames = DefensiveStatusDefinitions
        .Select(status => status.Name)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<uint> BossMitigationStatusIds = BossMitigationStatusDefinitions
        .Select(status => status.StatusId)
        .ToHashSet();

    internal static MitigationDisplayInfo GetMitigationDisplayInfo(StatusSnapshot status)
    {
        var defensiveStatus = DefensiveStatusDefinitions
            .FirstOrDefault(definition => definition.Name.Equals(status.Name, StringComparison.OrdinalIgnoreCase));
        if (defensiveStatus is not null)
        {
            return new MitigationDisplayInfo(
                BuildMitigationTypes(defensiveStatus),
                FormatMitigationPercent(
                    defensiveStatus.DamageReductionPercent,
                    defensiveStatus.PhysicalDamageReductionPercent,
                    defensiveStatus.MagicDamageReductionPercent,
                    defensiveStatus.HasVariableMitigationPercent),
                defensiveStatus.HasVariableMitigationPercent,
                defensiveStatus.MitigationPercentTooltip,
                defensiveStatus.InducedStatuses ?? []);
        }

        var bossStatus = BossMitigationStatusDefinitions
            .FirstOrDefault(definition => definition.StatusId == status.Id);
        if (bossStatus is not null)
        {
            return new MitigationDisplayInfo(
                BuildBossMitigationTypes(bossStatus),
                FormatMitigationPercent(
                    bossStatus.DamageDownPercent,
                    bossStatus.PhysicalDamageDownPercent,
                    bossStatus.MagicDamageDownPercent,
                    false),
                false,
                null,
                []);
        }

        return new MitigationDisplayInfo(
            [new("Debuff")],
            "-",
            false,
            null,
            []);
    }

    private static IReadOnlyList<MitigationTypeDisplay> BuildMitigationTypes(DefensiveStatusDefinition definition)
    {
        var types = new List<MitigationTypeDisplay>();
        if (definition.DamageReductionPercent is not null)
        {
            types.Add(new("All"));
        }

        if (definition.PhysicalDamageReductionPercent is not null)
        {
            types.Add(new("Physical", PhysicalDamageReductionIconId, "Physical damage reduction"));
        }

        if (definition.MagicDamageReductionPercent is not null)
        {
            types.Add(new("Magic", MagicDamageReductionIconId, "Magic damage reduction"));
        }

        if (definition.Effects.HasFlag(DefensiveStatusEffect.Shield))
        {
            types.Add(new("Shield"));
        }

        if (definition.Effects.HasFlag(DefensiveStatusEffect.HealingOrRegen))
        {
            types.Add(new("Regen"));
        }

        if (definition.Effects.HasFlag(DefensiveStatusEffect.HealingReceivedIncrease))
        {
            types.Add(new("Heal+"));
        }

        if (definition.Effects.HasFlag(DefensiveStatusEffect.MaxHpIncrease))
        {
            types.Add(new("Max HP"));
        }

        if (definition.Effects.HasFlag(DefensiveStatusEffect.InvulnerabilityOrDeathPrevention))
        {
            types.Add(new("Invuln"));
        }

        if (definition.Effects.HasFlag(DefensiveStatusEffect.BlockOrParry))
        {
            types.Add(new("Block"));
        }

        return types.Count == 0 ? [new("Other")] : types;
    }

    private static IReadOnlyList<MitigationTypeDisplay> BuildBossMitigationTypes(BossMitigationStatusDefinition definition)
    {
        var types = new List<MitigationTypeDisplay>();
        if (definition.DamageDownPercent is not null)
        {
            types.Add(new("All"));
        }

        if (definition.PhysicalDamageDownPercent is not null)
        {
            types.Add(new("Physical", PhysicalDamageReductionIconId, "Physical damage dealt reduction"));
        }

        if (definition.MagicDamageDownPercent is not null)
        {
            types.Add(new("Magic", MagicDamageReductionIconId, "Magic damage dealt reduction"));
        }

        return types.Count == 0 ? [new("Boss DD")] : types;
    }

    private static string FormatMitigationPercent(
        float? allPercent,
        float? physicalPercent,
        float? magicPercent,
        bool variable)
    {
        var parts = new List<string>();
        if (allPercent is not null)
        {
            parts.Add($"{allPercent:0.#}%");
        }

        if (physicalPercent is not null)
        {
            parts.Add($"P {physicalPercent:0.#}%");
        }

        if (magicPercent is not null)
        {
            parts.Add($"M {magicPercent:0.#}%");
        }

        if (parts.Count == 0)
        {
            return "-";
        }

        var value = string.Join(" / ", parts);
        return variable ? $"{value}+" : value;
    }
}
