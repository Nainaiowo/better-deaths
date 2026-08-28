namespace BetterDeaths.DamageParsing;

using System;
using System.Linq;
using System.Text.RegularExpressions;

internal sealed record ActionPotencyProfile(double? DirectPotency, double? PeriodicPotency)
{
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
        "remaining stacks",
        "for each",
    ];

    private static readonly string[] PeriodicMarkers =
    [
        "damage over time",
        "sustaining damage",
        "poison",
        "burn",
    ];

    public static ActionPotencyProfile Parse(string description, bool appliesPeriodicDamage)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return ActionPotencyProfile.Empty;
        }

        var normalized = WhitespaceRegex().Replace(description, " ").Trim();
        var periodicStart = FindFirstMarker(normalized, PeriodicMarkers);
        var additionalEffectStart = normalized.IndexOf("Additional Effect", StringComparison.OrdinalIgnoreCase);
        var directEnd = new[] { periodicStart, additionalEffectStart }
            .Where(index => index >= 0)
            .DefaultIfEmpty(normalized.Length)
            .Min();
        var directSection = normalized[..directEnd];
        double? directPotency = VariableDirectPotencyMarkers.Any(marker =>
                directSection.Contains(marker, StringComparison.OrdinalIgnoreCase))
            ? null
            : TryGetFirstPotency(directSection);

        double? periodicPotency = null;
        if (appliesPeriodicDamage && periodicStart >= 0)
        {
            periodicPotency = TryGetLastPotency(normalized[periodicStart..]);
        }

        return new ActionPotencyProfile(directPotency, periodicPotency);
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
        return match.Success && double.TryParse(match.Groups[1].Value, out var potency)
            ? potency
            : null;
    }

    private static double? TryGetLastPotency(string value)
    {
        var matches = PotencyRegex().Matches(value);
        if (matches.Count == 0)
        {
            return null;
        }

        return double.TryParse(matches[^1].Groups[1].Value, out var potency)
            ? potency
            : null;
    }

    [GeneratedRegex(@"\bpotency(?:\s+of|\s*:)?\s*(\d+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PotencyRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
