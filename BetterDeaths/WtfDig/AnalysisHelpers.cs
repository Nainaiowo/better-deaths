using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BetterDeaths.WtfDig;

internal sealed record WtfDigJobInfo(string Abbreviation, string Role, string Color);

internal sealed record WtfDigResourceSample(double Time, FflogsResources Resources);

internal static class WtfDigAnalysisHelpers
{
    internal static readonly Vector2 DefaultCenter = new(100.0f, 100.0f);

    private static readonly IReadOnlyDictionary<string, WtfDigJobInfo> Jobs =
        new Dictionary<string, WtfDigJobInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["Paladin"] = new("PLD", "tank", "#a8d2e7"),
            ["Warrior"] = new("WAR", "tank", "#cf2621"),
            ["DarkKnight"] = new("DRK", "tank", "#d126cc"),
            ["Gunbreaker"] = new("GNB", "tank", "#796d30"),
            ["WhiteMage"] = new("WHM", "healer", "#fff0dc"),
            ["Scholar"] = new("SCH", "healer", "#8657ff"),
            ["Astrologian"] = new("AST", "healer", "#ffe74a"),
            ["Sage"] = new("SGE", "healer", "#80a0f0"),
            ["Monk"] = new("MNK", "dps", "#d69c00"),
            ["Dragoon"] = new("DRG", "dps", "#4164cd"),
            ["Ninja"] = new("NIN", "dps", "#af1964"),
            ["Samurai"] = new("SAM", "dps", "#e46d04"),
            ["Reaper"] = new("RPR", "dps", "#965a90"),
            ["Viper"] = new("VPR", "dps", "#108210"),
            ["Bard"] = new("BRD", "dps", "#91ba5e"),
            ["Machinist"] = new("MCH", "dps", "#6ee1d6"),
            ["Dancer"] = new("DNC", "dps", "#e2b0af"),
            ["BlackMage"] = new("BLM", "dps", "#a579d6"),
            ["Summoner"] = new("SMN", "dps", "#2d9b78"),
            ["RedMage"] = new("RDM", "dps", "#e87b7b"),
            ["Pictomancer"] = new("PCT", "dps", "#fc92e1"),
        };

    internal static WtfDigJobInfo JobInfo(string subType)
    {
        if (Jobs.TryGetValue(subType, out var job))
        {
            return job;
        }

        var abbreviation = subType.Length <= 3 ? subType : subType[..3];
        return new WtfDigJobInfo(abbreviation.ToUpperInvariant(), "dps", "#9aa0ae");
    }

    internal static IReadOnlyList<FflogsActor> FightPlayers(FflogsReportSummary report, FflogsFight fight)
    {
        var friendlyIds = fight.FriendlyPlayers?.ToHashSet() ?? [];
        return report.Actors
            .Where(actor =>
                string.Equals(actor.Type, "Player", StringComparison.Ordinal) &&
                !string.Equals(actor.SubType, "LimitBreak", StringComparison.Ordinal) &&
                friendlyIds.Contains(actor.Id))
            .ToArray();
    }

    internal static Dictionary<int, List<WtfDigResourceSample>> BuildSampleMap(
        IEnumerable<FflogsEvent> damage,
        IEnumerable<FflogsEvent> friendlyCasts)
    {
        var samples = new Dictionary<int, List<WtfDigResourceSample>>();
        foreach (var entry in damage)
        {
            AddSample(samples, entry.TargetID, entry.Timestamp, entry.TargetResources);
        }

        foreach (var entry in friendlyCasts)
        {
            AddSample(samples, entry.SourceID, entry.Timestamp, entry.SourceResources);
        }

        return samples;
    }

    internal static FflogsResources? SampleAt(
        IReadOnlyDictionary<int, List<WtfDigResourceSample>> samples,
        int actorId,
        double time)
    {
        if (!samples.TryGetValue(actorId, out var entries) || entries.Count == 0)
        {
            return null;
        }

        FflogsResources? result = null;
        var bestDistance = double.PositiveInfinity;
        foreach (var entry in entries)
        {
            var distance = Math.Abs(entry.Time - time);
            if (distance >= bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            result = entry.Resources;
        }

        return result;
    }

    internal static Vector2? PositionAt(
        IReadOnlyDictionary<int, List<WtfDigResourceSample>> samples,
        int actorId,
        double time,
        Vector2 center)
    {
        var resource = SampleAt(samples, actorId, time);
        return resource is null ? null : RawToArena(resource.X, resource.Y, center);
    }

    internal static Func<int, double, bool> MakeDeadAt(
        IReadOnlyDictionary<int, List<WtfDigResourceSample>> samples,
        IReadOnlyList<FflogsEvent> deaths)
    {
        return (actorId, time) =>
        {
            var lastDeath = deaths
                .Where(entry => entry.TargetID == actorId && entry.Timestamp <= time)
                .Select(entry => entry.Timestamp)
                .DefaultIfEmpty(double.NegativeInfinity)
                .Max();
            if (double.IsNegativeInfinity(lastDeath))
            {
                return false;
            }

            return !samples.TryGetValue(actorId, out var entries) ||
                !entries.Any(entry =>
                    entry.Time > lastDeath + 500 &&
                    entry.Time <= time &&
                    entry.Resources.HitPoints > 0);
        };
    }

    internal static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(value => value).ToArray();
        return sorted.Length == 0 ? 0 : sorted[sorted.Length / 2];
    }

    internal static Vector2 RawToArena(double rawX, double rawY, Vector2 center) =>
        new((float)(rawX / 100.0 - center.X), (float)(rawY / 100.0 - center.Y));

    internal static double FacingToRadians(double rawFacing) => (-rawFacing - 150 * Math.PI) / 100;

    internal static string FormatDuration(double milliseconds)
    {
        var seconds = Math.Max(0, (int)Math.Floor(milliseconds / 1000));
        return $"{seconds / 60}:{seconds % 60:00}";
    }

    internal static void AddSample(
        IDictionary<int, List<WtfDigResourceSample>> samples,
        int? actorId,
        double time,
        FflogsResources? resources)
    {
        if (actorId is not { } id || resources is null)
        {
            return;
        }

        if (!samples.TryGetValue(id, out var entries))
        {
            entries = [];
            samples[id] = entries;
        }

        entries.Add(new WtfDigResourceSample(time, resources));
    }
}
