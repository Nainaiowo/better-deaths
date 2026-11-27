using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BetterDeaths.WtfDig;

internal readonly record struct BlackHoleActor(int Id, int? Instance);
internal readonly record struct BlackHoleBossPose(double Time, Vector2 Position, double Heading);

// Marker evidence is deliberately independent of who held a tether or got hit.
internal sealed class BlackHoleMarkerEvidence(
    IReadOnlyDictionary<BlackHoleActor, double> appearances,
    IReadOnlyDictionary<BlackHoleActor, double> identities,
    IReadOnlyDictionary<BlackHoleActor, BlackHoleBossPose[]> poses)
{
    internal const double MaxPoseAgeMs = 3000;

    internal Vector2? AtFirstAppearance(IEnumerable<BlackHoleActor> holes, double after, double firstFire)
    {
        var appearance = holes.Where(appearances.ContainsKey).Select(h => appearances[h])
            .Where(t => t > after && t < firstFire).DefaultIfEmpty(double.NaN).Min();
        if (!double.IsFinite(appearance)) return null;

        // Ambiguous identity is not resolved by whichever actor sent an update last.
        var candidates = identities.Where(kv => kv.Value <= appearance).Take(2).ToArray();
        if (candidates.Length != 1 || !poses.TryGetValue(candidates[0].Key, out var track)) return null;
        var index = Array.FindLastIndex(track, p => p.Time <= appearance);
        if (index < 0) return null;
        var pose = track[index];
        if (pose.Time <= after || appearance - pose.Time > MaxPoseAgeMs ||
            !double.IsFinite(pose.Heading) || !(pose.Position.LengthSquared() <= 9)) return null;

        return new Vector2((float)-Math.Sin(pose.Heading), (float)-Math.Cos(pose.Heading)) * BlackHoleAnalyzer.BigKefkaRadius;
    }

    internal static BlackHoleMarkerEvidence FromReport(IReadOnlySet<int> bosses,
        IReadOnlyList<FflogsEvent> casts, IReadOnlyList<FflogsEvent> events, Vector2 center)
    {
        var spawns = events.Where(e => e.Type == "summon" && e.TargetID.HasValue)
            .GroupBy(e => new BlackHoleActor(e.TargetID!.Value, e.TargetInstance))
            .ToDictionary(g => g.Key, g => g.Min(e => e.Timestamp));
        var tracks = casts.Where(e => e.Type == "cast" && e.SourceID is { } id && bosses.Contains(id) && e.SourceResources != null)
            .GroupBy(e => new BlackHoleActor(e.SourceID!.Value, e.SourceInstance))
            .ToDictionary(g => g.Key, g => g.Select(e =>
            {
                var r = e.SourceResources!;
                return new BlackHoleBossPose(e.Timestamp, WtfDigAnalysisHelpers.RawToArena(r.X, r.Y, center),
                    r.HasFacing ? WtfDigAnalysisHelpers.FacingToRadians(r.Facing) : double.NaN);
            }).OrderBy(p => p.Time).ToArray());
        // FFLogs distinguishes boss and NPC actors even when both are named Kefka.
        var identities = tracks.Where(kv => kv.Value.Any(p => p.Position.LengthSquared() <= 9))
            .ToDictionary(kv => kv.Key, kv => kv.Value.First(p => p.Position.LengthSquared() <= 9).Time);
        return new(spawns, identities, tracks);
    }
}
