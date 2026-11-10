using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace BetterDeaths;

internal sealed class ReplayMitigationTimeline
{
    private sealed record Track(StatusSourceSnapshot Target, bool Enemy, StatusTimeline Timeline);
    private static readonly ConditionalWeakTable<PullDeathSnapshot, ReplayMitigationTimeline> Cache = new();
    private readonly Track[] tracks;
    private readonly Dictionary<uint, StatusSourceSnapshot> sources;

    public ReplayMitigationTimeline(IReadOnlyList<PlayerStatusSnapshot> players, IReadOnlyList<SourceMitigationSnapshot> enemies)
    {
        sources = players.SelectMany(value => value.Sources).Concat(enemies.SelectMany(value => value.Sources))
            .Concat(players.Select(value => new StatusSourceSnapshot(value.EntityId, value.MemberKey, value.MemberName,
                value.PartyIndex, value.ClassJobId, value.ClassJobName)))
            .Where(value => value.EntityId != 0).GroupBy(value => value.EntityId).ToDictionary(group => group.Key, group => group.Last());
        tracks = players.GroupBy(value => value.MemberKey).Select(group =>
        {
            var member = group.Last();
            return new Track(new(member.EntityId, member.MemberKey, member.MemberName, member.PartyIndex, member.ClassJobId, member.ClassJobName),
                false, new(group.Select(value => value.ToStatusSet())));
        }).Concat(enemies.GroupBy(value => value.SourceEntityId).Select(group =>
            new Track(new(group.Key, $"enemy:{group.Key:X8}", group.Last().SourceName, int.MaxValue, 0, ""),
                true, new(group.Select(value => value.ToStatusSet()))))).ToArray();
    }

    public static ReplayMitigationTimeline For(PullDeathSnapshot pull) =>
        Cache.GetValue(pull, value => new(value.ReplayStatusHistory, value.ReplaySourceStatusHistory));

    public static float RemainingSeconds(ReplayMitigationSnapshot snapshot, float pullElapsedSeconds) =>
        snapshot.ObservedEffect ? snapshot.DurationSeconds :
            snapshot.PullElapsedSeconds + Math.Max(.05f, snapshot.DurationSeconds) - pullElapsedSeconds;

    public IReadOnlyList<ReplayMitigationSnapshot> At(DateTime time, float pullElapsedSeconds)
    {
        return tracks.SelectMany(track => track.Timeline.At(time)
                .Where(status => track.Enemy ? Plugin.IsBossMitigationStatus(status) : Plugin.IsDefensiveStatus(status))
                .Select(status => (track.Target, track.Enemy, Status: status)))
            .GroupBy(value => (value.Status.Id, value.Status.SourceId, value.Enemy))
            .Select(group =>
            {
                var status = group.OrderByDescending(value => value.Status.RemainingTime).First().Status;
                var source = sources.GetValueOrDefault(status.SourceId) ??
                    new(status.SourceId, $"source:{status.SourceId:X8}", "Unknown source", int.MaxValue, 0, "");
                return new ReplayMitigationSnapshot(time, pullElapsedSeconds, source.MemberKey, source.MemberName, source.PartyIndex,
                    source.ClassJobId, source.ClassJobName, 0, status.Name, status.IconId,
                    group.Key.Enemy ? PossibleMitigationScope.Boss : PossibleMitigationScope.Targeted, status.RemainingTime, [status])
                {
                    ObservedEffect = true,
                    TargetMemberNames = group.OrderBy(value => value.Target.PartyIndex).Select(value => value.Target.MemberName).Distinct().ToArray(),
                };
            }).OrderBy(value => value.MemberName, StringComparer.OrdinalIgnoreCase).ThenBy(value => value.ActionName, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
