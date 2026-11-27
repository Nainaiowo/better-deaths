using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace BetterDeaths;

internal readonly record struct KefkaReplayBody(uint EntityId, Vector2 Position, bool OnPlatform);
internal readonly record struct KefkaFootfall(float Time, Vector2 Position, uint SourceId);
internal readonly record struct KefkaReplayFrame(KefkaReplayBody? Body, KefkaFootfall? Footfall, uint ReplacedBossId = 0)
{
    internal bool Replaces(ReplayPositionSnapshot actor) => actor.ActorKind == ReplayActorKind.Enemy &&
        actor.EntityId != 0 && (actor.EntityId == ReplacedBossId || actor.EntityId == Footfall?.SourceId);
}

// A playback-only track. Black Hole's analyzer instead locks one reading per tether set.
internal sealed class DmuKefkaReplayTrack
{
    internal const float ImpactDisplaySeconds = 0.75f;
    private const float MaxPoseAge = 3;
    private static readonly Vector2 Center = new(100, 100);
    private readonly record struct Pose(float Time, Vector2 Position, float Rotation);
    private readonly Pose[] poses;
    private readonly KefkaFootfall[] impacts;
    private readonly uint boss;
    private readonly float start, platformStart, end;
    internal static readonly DmuKefkaReplayTrack Empty = new(0, 0, 0, -1, [], []);

    private DmuKefkaReplayTrack(uint boss, float start, float platformStart, float end, Pose[] poses, KefkaFootfall[] impacts)
    {
        this.boss = boss;
        this.start = start;
        this.platformStart = platformStart;
        this.end = end;
        this.poses = poses;
        this.impacts = impacts;
    }

    internal static DmuKefkaReplayTrack Build(uint territory, IReadOnlyList<ReplayPositionSnapshot> positions,
        IReadOnlyList<ReplayMechanicSnapshot> mechanics)
    {
        if (!ReplayEncounterModules.IsDancingMadUltimate(territory) || mechanics.Count == 0) return Empty;

        var identities = new Dictionary<uint, float>();
        var visualCasters = new HashSet<uint>();
        var landings = new Dictionary<string, KefkaFootfall>(StringComparer.Ordinal);
        var wallStart = float.PositiveInfinity;
        var wallEnd = float.NegativeInfinity;
        var stompStart = float.PositiveInfinity;
        var visualEnd = float.NegativeInfinity;
        foreach (var m in mechanics)
        {
            var t = m.PullElapsedSeconds;
            if (!float.IsFinite(t)) continue;
            var identityCast = m.RawEventId is 50167 or 47846 or 47847 &&
                m.RawEventKind is "bossmod-cast" or "dmu-p3-slap-happy-predicted";
            var stompVisual = m.RawEventId == 47855 &&
                m.RawEventKind is "bossmod-cast" or "dmu-p3-stomp-a-mole-predicted";
            if (identityCast || stompVisual)
            {
                var caster = SourceEntity(m.SourceKey);
                if (caster == 0) continue;
                identities[caster] = MathF.Min(t, identities.GetValueOrDefault(caster, float.PositiveInfinity));
                if (stompVisual)
                {
                    stompStart = MathF.Min(stompStart, t);
                    if (float.IsFinite(m.DurationSeconds))
                        visualEnd = MathF.Max(visualEnd, t + Math.Clamp(m.DurationSeconds, 0, 10));
                    visualCasters.Add(caster);
                }
                else if (m.RawEventId != 50167)
                {
                    wallStart = MathF.Min(wallStart, t);
                    wallEnd = MathF.Max(wallEnd, t);
                }
            }
            else if (m is { RawEventId: 0x4C38, RawEventKind: "object" } or
                { RawEventId: 47868, RawEventKind: "dmu-p3-nothingness" })
            {
                wallStart = MathF.Min(wallStart, t);
                wallEnd = MathF.Max(wallEnd, t);
            }
            else if (m is { RawEventId: 47856, RawEventKind: "dmu-p3-stomp-a-mole" } &&
                float.IsFinite(m.X) && float.IsFinite(m.Z))
            {
                var source = SourceEntity(m.SourceKey);
                if (source == 0) continue;
                // Packet sequence is part of the key. A helper can land more than once.
                var hit = new KefkaFootfall(t, new(m.X, m.Z), source);
                if (!landings.TryGetValue(m.SourceKey, out var existing) || t < existing.Time)
                    landings[m.SourceKey] = hit;
            }
        }

        var impacts = landings.Values.OrderBy(h => h.Time).ToArray();
        var boss = identities.Count == 1 ? identities.Keys.Single() : 0;
        if (boss == 0 && impacts.Length == 0) return Empty;
        var platform = boss != 0 && visualCasters.Contains(boss) ? stompStart : float.PositiveInfinity;
        var start = MathF.Min(wallStart, platform);
        if (boss != 0) start = MathF.Max(start, identities[boss]);
        // Never extend the body into another mechanic on a partial capture. A missing
        // visual cast cannot turn a foot's position into the position of the boss.
        var end = float.IsFinite(platform)
            ? MathF.Max(MathF.Max(platform, visualEnd), impacts.Length > 0 ? impacts[^1].Time + ImpactDisplaySeconds : platform)
            : impacts.Length > 0 ? MathF.Min(wallEnd, MathF.BitDecrement(impacts[0].Time)) : wallEnd;
        var poses = boss == 0 ? [] : positions
            .Where(p => p.ActorKind == ReplayActorKind.Enemy && p.EntityId == boss &&
                p.SampleSource is ReplayPositionSampleSource.PeriodicEnemyObject or ReplayPositionSampleSource.Unknown &&
                p.PullElapsedSeconds >= start - MaxPoseAge && p.PullElapsedSeconds <= end)
            .OrderBy(p => p.PullElapsedSeconds)
            .Select(p => new Pose(p.PullElapsedSeconds, new(p.X, p.Z), p.Rotation)).ToArray();
        return new(boss, start, platform, end, poses, impacts);
    }

    private static uint SourceEntity(string key)
    {
        var parts = key.Split(':');
        var index = parts.Length > 2 && parts[1] == "cast" ? 2 : 1;
        return parts.Length > index && parts[index].Length == 8 &&
            uint.TryParse(parts[index], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id) ? id : 0;
    }

    internal KefkaReplayFrame At(float time)
    {
        if (!float.IsFinite(time)) return default;
        var h = LastAt(impacts, time, static x => x.Time);
        KefkaFootfall? foot = h >= 0 && time - impacts[h].Time < ImpactDisplaySeconds ? impacts[h] : null;
        if (time < start || time > end) return new(null, foot);
        var i = LastAt(poses, time, static x => x.Time);
        if (i < 0) return new(null, foot, boss);
        var pose = poses[i];
        if (time - pose.Time > MaxPoseAge || !(Vector2.DistanceSquared(pose.Position, Center) <= 9) ||
            !float.IsFinite(pose.Rotation)) return new(null, foot, boss);
        var onPlatform = time >= platformStart;
        var point = onPlatform ? pose.Position :
            Center - 20 * new Vector2(MathF.Sin(pose.Rotation), MathF.Cos(pose.Rotation));
        return new(new KefkaReplayBody(boss, point, onPlatform), foot, boss);
    }

    private static int LastAt<T>(T[] samples, float time, Func<T, float> timestamp)
    {
        int lo = 0, hi = samples.Length;
        while (lo < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (timestamp(samples[mid]) <= time) lo = mid + 1;
            else hi = mid;
        }
        return lo - 1;
    }
}
