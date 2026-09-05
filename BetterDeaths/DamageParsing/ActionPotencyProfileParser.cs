namespace BetterDeaths.DamageParsing;

using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

internal sealed record ActionPotencyProfile(double? DirectPotency, double? PeriodicPotency)
{
    public double? SecondaryTargetMultiplier { get; init; }

    public static ActionPotencyProfile Empty { get; } = new(null, null);
}

internal static partial class ActionPotencyProfileParser
{
    private static readonly string[] VariableDirectPotencyMarkers =
    [
        "combo potency",
        "rear potency",
        "flank potency",
        "potency increases",
        "potency varies",
        "potency scales",
        "remaining stacks",
        "for each",
        "when executed from",
    ];

    private static readonly string[] PeriodicMarkers =
    [
        "damage over time",
        "sustaining damage",
        "creates a patch",
        "creates a windstorm",
    ];

    public static ActionPotencyProfile Parse(string description, bool appliesPeriodicDamage)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return ActionPotencyProfile.Empty;
        }

        var normalized = WhitespaceRegex().Replace(description, " ").Trim();
        var periodicStart = FindFirstMarker(normalized, PeriodicMarkers);
        var namedPeriodicEffect = NamedPeriodicEffectRegex().Match(normalized);
        if (namedPeriodicEffect.Success && (periodicStart < 0 || namedPeriodicEffect.Groups[1].Index < periodicStart))
        {
            periodicStart = namedPeriodicEffect.Groups[1].Index;
        }
        var additionalEffectStart = normalized.IndexOf("Additional Effect", StringComparison.OrdinalIgnoreCase);
        var directEnd = new[] { periodicStart, additionalEffectStart }
            .Where(index => index >= 0)
            .DefaultIfEmpty(normalized.Length)
            .Min();
        var directSection = normalized[..directEnd];
        // A tooltip can describe a heal, a future attack, or alternative damage
        // potencies. None of those is a reliable fixed-potency calibration hit.
        var directMatch = DirectDamageRegex().Match(directSection);
        var damageText = CurePotencyRegex().Replace(normalized, "healing strength");
        double? directPotency = !directMatch.Success || directSection.Contains('?') ||
            PotencyRegex().Matches(directSection).Count != 1 ||
            VariableDirectPotencyMarkers.Any(marker => directSection.Contains(marker, StringComparison.OrdinalIgnoreCase)) ||
            ConditionalDamageRegex().IsMatch(damageText)
                ? null
                : TryGetFirstPotency(directMatch.Value);

        double? periodicPotency = null;
        if (appliesPeriodicDamage && periodicStart >= 0)
        {
            var periodicSection = normalized[periodicStart..];
            var periodicEnd = PeriodicEndRegex().Match(periodicSection);
            if (periodicEnd.Success)
            {
                periodicSection = periodicSection[..periodicEnd.Index];
            }
            periodicPotency = TryGetFirstPotency(periodicSection);
        }

        return new ActionPotencyProfile(directPotency, periodicPotency)
        {
            SecondaryTargetMultiplier = directPotency is > 0 ? GetSecondaryTargetMultiplier(directSection) : null,
        };
    }

    private static double? GetSecondaryTargetMultiplier(string directSection)
    {
        var falloff = TargetFalloffRegex().Match(directSection);
        if (falloff.Success && int.TryParse(falloff.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture,
                out var percent) && percent is >= 0 and < 100)
        {
            return (100 - percent) / 100.0;
        }

        // Only uniform area-damage clauses are safe without an explicit falloff.
        return UniformAreaDamageRegex().IsMatch(directSection) &&
            !UnknownTargetScalingRegex().IsMatch(directSection) ? 1.0 : null;
    }

    private static int FindFirstMarker(string value, string[] markers)
    {
        return markers
            .Select(marker => value.IndexOf(marker, StringComparison.OrdinalIgnoreCase))
            .Where(index => index >= 0)
            .DefaultIfEmpty(-1)
            .Min();
    }

    private static double? TryGetFirstPotency(string value)
    {
        var match = PotencyRegex().Match(value);
        return match.Success && ValidPotencyNumberRegex().IsMatch(match.Groups[1].Value) &&
            double.TryParse(match.Groups[1].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var potency)
            ? potency
            : null;
    }

    [GeneratedRegex(@"\bpotency(?:\s+of|\s*:)?\s*([\d,]+(?:\.\d+)?|\?)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotencyRegex();

    [GeneratedRegex(@"\A(?:\d+|[1-9]\d{0,2}(?:,\d{3})+)\z")]
    private static partial Regex ValidPotencyNumberRegex();

    [GeneratedRegex(@"\b(?:deals?|delivers?|dealing|delivering|takes?)\b(?=[^.!?]*\b(?:damage|attack|hit)\b)[^.!?]*\bpotency(?:\s+of|\s*:)?\s*[\d,]+(?:\.\d+)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DirectDamageRegex();

    [GeneratedRegex(@"\b(?:Additional Effect|Combo Bonus):\s*(Venom|Poison|Burn)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamedPeriodicEffectRegex();

    [GeneratedRegex(@"\b(?:Duration:|Additional Effect:|Cure Potency\b|Executing\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PeriodicEndRegex();

    [GeneratedRegex(@"\bCure potency\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CurePotencyRegex();

    [GeneratedRegex(@"\b(?:combo potency|potency (?:increases|increased|is increased|varies|scales)|potencies are increased)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ConditionalDamageRegex();

    [GeneratedRegex(@"\bfor the first enemy, and (\d+)% less for all remaining enemies\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TargetFalloffRegex();

    [GeneratedRegex(@"\bto (?:all nearby enemies|target and all enemies nearby it)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UniformAreaDamageRegex();

    [GeneratedRegex(@"\b(?:less|reduced|decreases|remaining|divided|split)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnknownTargetScalingRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
