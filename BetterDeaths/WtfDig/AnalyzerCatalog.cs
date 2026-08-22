using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterDeaths.WtfDig;

internal sealed record WtfDigAnalyzerDefinition(
    string Key,
    string Label,
    string Phase,
    string Description,
    double MinimumFightMs,
    uint AnchorAbilityId,
    string MechanicLabel);

internal static class WtfDigAnalyzerCatalog
{
    internal const int DancingMadEncounterId = 1085;

    internal static readonly IReadOnlyList<WtfDigAnalyzerDefinition> All =
    [
        new("arrows", "Arrows", "P1", "Arrow placement", 150_000, 47801, "P1 arrows (~2:30)"),
        new("forsaken", "Forsaken", "P2", "Tower pulls and assignments", 225_000, 47804, "P2 Forsaken (~3:45)"),
        new("kefka-lc", "Limit Cut", "P3", "Start, direction, numbers, and positioning", 515_000, 47843, "P3 Limit Cut (~8:49)"),
        new("black-hole", "Black Hole", "P3", "Tethers and cleanse states", 560_000, 47867, "P3 Black Hole (~9:40)"),
        new("kefka-says", "Kefka Says", "P4", "Casts, debuffs, and real or fake tells", 730_000, 49884, "P4 Kefka Says (~12:15)"),
    ];

    internal static bool IsDancingMad(FflogsFight fight) =>
        fight.EncounterID == DancingMadEncounterId ||
        fight.Name.Contains("dancing mad", StringComparison.OrdinalIgnoreCase);

    internal static IReadOnlyList<FflogsFight> EligibleFights(
        FflogsReportSummary report,
        WtfDigAnalyzerDefinition analyzer) =>
        report.Fights
            .Where(fight => IsDancingMad(fight) && fight.DurationMs >= analyzer.MinimumFightMs)
            .OrderBy(fight => fight.StartTime)
            .ToArray();
}
