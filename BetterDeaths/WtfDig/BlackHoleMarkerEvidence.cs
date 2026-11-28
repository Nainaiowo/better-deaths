using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BetterDeaths.WtfDig;

internal readonly record struct BlackHoleActor(int Id, int? Instance);
internal readonly record struct BlackHoleBossPose(double Time, Vector2 Position, double Heading, bool IsBodySample = true);
internal sealed record LocalBlackHoleOpening(
    IReadOnlyDictionary<BlackHoleActor, double> FirstTethers,
    IReadOnlyCollection<(BlackHoleActor Caster, double Time)> Slaps);

// Marker evidence is deliberately independent of who held a tether or got hit.
internal sealed class BlackHoleMarkerEvidence(
    IReadOnlyDictionary<BlackHoleActor, double> appearances,
    IReadOnlyDictionary<BlackHoleActor, double> identities,
    IReadOnlyDictionary<BlackHoleActor, BlackHoleBossPose[]> poses,
    LocalBlackHoleOpening? local = null)
{
    internal const double MaxPoseAgeMs = 3000;
    internal const double OpeningLookbackMs = 8000;
    // All seven complete local recordings first see an opening hole within a second of Black Hole finishing.
    private const double LocalOpeningGraceMs = 2000;

    internal Vector2? AtFirstAppearance(IEnumerable<BlackHoleActor> holes, double after, double firstFire)
        => AtObservation(FirstAppearance(holes, after, firstFire), after, bodyOnly: false);

    private double FirstAppearance(IEnumerable<BlackHoleActor> holes, double after, double firstFire) =>
        holes.Where(appearances.ContainsKey).Select(h => appearances[h])
            .Where(t => t > after && t < firstFire).DefaultIfEmpty(double.NaN).Min();

    private Vector2? AtObservation(double appearance, double after, bool bodyOnly)
    {
        if (!double.IsFinite(appearance)) return null;

        // Ambiguous identity is not resolved by whichever actor sent an update last.
        var candidates = identities.Where(kv => kv.Value <= appearance).Take(2).ToArray();
        if (candidates.Length != 1 || !poses.TryGetValue(candidates[0].Key, out var track)) return null;
        var index = Array.FindLastIndex(track, p => p.Time <= appearance && (!bodyOnly || p.IsBodySample));
        if (index < 0) return null;
        var pose = track[index];
        if (pose.Time <= after || appearance - pose.Time > MaxPoseAgeMs ||
            !double.IsFinite(pose.Heading) || !(pose.Position.LengthSquared() <= 9)) return null;

        return WallMarker(pose.Heading);
    }

    internal Vector2? AtLocalOpening(BlackHoleActor[] holes, FflogsEvent anchor, double firstFire, out bool inferred)
    {
        inferred = false;
        if (local is null) return null;
        var after = anchor.Timestamp - OpeningLookbackMs;
        var appearance = FirstAppearance(holes, after, firstFire);
        var tether = holes.Where(local.FirstTethers.ContainsKey).Select(h => local.FirstTethers[h])
            .Where(t => t > after && t < firstFire).DefaultIfEmpty(double.PositiveInfinity).Min();
        // Once a hole already has a tether, a later sighting of its object cannot be its spawn.
        if (appearance <= tether && appearance <= anchor.Timestamp + LocalOpeningGraceMs)
            return AtObservation(appearance, after, bodyOnly: true);
        if (!double.IsFinite(tether) || tether - anchor.Timestamp is < 0 or > LocalOpeningGraceMs) return null;

        // Local Slap Happy cast snapshots are made during the cast, about nine seconds before Black Hole.
        var slaps = local.Slaps.Where(s => s.Time >= anchor.Timestamp - 12000 && s.Time < anchor.Timestamp).ToArray();
        if (slaps.Length == 0 || slaps.Any(s => s.Caster != slaps[0].Caster)) return null;
        var slap = slaps.MinBy(s => s.Time);
        var atSlap = AtObservation(slap.Time, double.NegativeInfinity, bodyOnly: true);
        var atTether = AtObservation(tether, after, bodyOnly: true);
        if (atSlap is not { } wall || atTether is not { } end || Vector2.DistanceSquared(wall, end) > 0.25f ||
            !poses.TryGetValue(slap.Caster, out var track)) return null;
        foreach (var p in track)
        {
            if (!p.IsBodySample || p.Time < slap.Time || p.Time > tether) continue;
            if (!double.IsFinite(p.Heading) || !(p.Position.LengthSquared() <= 9) ||
                Vector2.DistanceSquared(wall, WallMarker(p.Heading)) > 0.25f) return null;
        }
        inferred = true;
        return wall;
    }

    internal Vector2? InferMissingOpeningSpawn(BlackHoleActor hole, FflogsEvent anchor, double firstFire,
        IReadOnlySet<int> bosses, IReadOnlyList<FflogsEvent> bossEvents, IReadOnlyList<FflogsEvent> tetherEvents, Vector2 center)
    {
        // The opening has one beam about seven seconds after Black Hole. A partial log can start at a later volley.
        if (appearances.ContainsKey(hole) || anchor.Type != "cast" || anchor.AbilityGameID != BlackHoleAnalyzer.BlackHoleCastId ||
            firstFire - anchor.Timestamp is < 5000 or > 10000) return null;
        var firstTether = tetherEvents.Where(e => e.Type == "tether" && e.SourceID == hole.Id && e.SourceInstance == hole.Instance &&
                e.Timestamp >= anchor.Timestamp && e.Timestamp < firstFire)
            .Select(e => e.Timestamp).DefaultIfEmpty(double.NaN).Min();
        if (!double.IsFinite(firstTether)) return null;

        var slaps = bossEvents.Where(e => e.Type == "cast" && e.AbilityGameID is 47846 or 47847 &&
            e.SourceID is { } id && bosses.Contains(id) && e.Timestamp < anchor.Timestamp &&
            e.Timestamp >= anchor.Timestamp - OpeningLookbackMs).Take(2).ToArray();
        if (slaps.Length != 1 || slaps[0].SourceResources is not { HasFacing: true } r) return null;
        var slap = slaps[0];
        var boss = new BlackHoleActor(slap.SourceID!.Value, slap.SourceInstance);
        var heading = WtfDigAnalysisHelpers.FacingToRadians(r.Facing);
        if (!double.IsFinite(heading) || !AtCenter(r) || identities.Any(kv => kv.Key != boss && kv.Value <= firstTether))
            return null;

        // Check both sides of events: a player hitting Kefka can record his facing too.
        foreach (var e in bossEvents.Where(e => e.Timestamp >= slap.Timestamp && e.Timestamp <= firstTether))
        {
            if (Conflicts(e.SourceID, e.SourceInstance, e.SourceResources) ||
                Conflicts(e.TargetID, e.TargetInstance, e.TargetResources)) return null;
        }
        return WallMarker(heading);

        bool AtCenter(FflogsResources p) => WtfDigAnalysisHelpers.RawToArena(p.X, p.Y, center).LengthSquared() <= 9;
        bool Conflicts(int? id, int? instance, FflogsResources? p)
        {
            if (p == null || id is not { } actorId || !bosses.Contains(actorId)) return false;
            if (new BlackHoleActor(actorId, instance) != boss) return AtCenter(p);
            // FFLogs headings are quantized to hundredths of a radian.
            return !AtCenter(p) || !p.HasFacing || !double.IsFinite(p.Facing) ||
                Math.Abs(Math.IEEERemainder(WtfDigAnalysisHelpers.FacingToRadians(p.Facing) - heading, Math.Tau)) > 0.02;
        }
    }

    private static Vector2 WallMarker(double heading) =>
        new Vector2((float)-Math.Sin(heading), (float)-Math.Cos(heading)) * BlackHoleAnalyzer.BigKefkaRadius;

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
