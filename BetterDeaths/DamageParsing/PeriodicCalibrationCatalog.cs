namespace BetterDeaths.DamageParsing;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

internal static class PeriodicCalibrationCatalog
{
    internal sealed record Profile(uint Id, double? Primary, double? Combo, double? Secondary, double? SecondaryCombo);
    private sealed record Catalog(int Version, Profile[] Actions, uint[] VariableDamageStatuses);
    private static readonly Catalog Data = Load();
    private static readonly IReadOnlyDictionary<uint, Profile> Profiles = Data.Actions.ToDictionary(profile => profile.Id);
    private static readonly HashSet<uint> VariableDamageStatuses = Data.VariableDamageStatuses.ToHashSet();

    public static bool Contains(uint actionId) => Profiles.ContainsKey(actionId);

    public static bool SupportsVariableDamageStatus(uint statusId) => VariableDamageStatuses.Contains(statusId);

    public static double GetPotency(uint actionId, bool combo, bool secondary)
    {
        var profile = Profiles[actionId];
        return (secondary ? combo ? profile.SecondaryCombo : profile.Secondary
            : combo ? profile.Combo : profile.Primary) ?? 0;
    }

    private static Catalog Load()
    {
        using var stream = typeof(PeriodicCalibrationCatalog).Assembly.GetManifestResourceStream(
            "BetterDeaths.DamageParsing.PeriodicCalibrationCatalog.json")
            ?? throw new InvalidOperationException("Missing periodic calibration catalog.");
        var catalog = JsonSerializer.Deserialize<Catalog>(stream)
            ?? throw new InvalidOperationException("Invalid periodic calibration catalog.");
        if (catalog.Version != PeriodicCalibrationPotencyPolicy.Version || catalog.Actions.Length == 0 ||
            catalog.VariableDamageStatuses is not { Length: > 0 })
            throw new InvalidOperationException("Incompatible periodic calibration catalog.");
        return catalog;
    }
}
