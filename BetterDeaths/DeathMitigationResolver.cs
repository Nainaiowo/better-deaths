using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.CompilerServices;

namespace BetterDeaths;

internal sealed record BossStatusEvidence(uint EntityId, string Name, StatusSnapshot Status);

internal sealed record DeathMitigationContext(
    IReadOnlyList<StatusSnapshot> PlayerStatuses, IReadOnlyList<BossStatusEvidence> BossStatuses)
{
    public static readonly DeathMitigationContext Empty = new([], []);
}

internal sealed class DeathMitigationResolver
{
    private readonly record struct StatusQuery(DateTime Time, uint SourceId, string SourceName,
        CombatEventHpSource HpSource, bool HasSnapshot, IReadOnlyList<StatusSnapshot> Statuses,
        IReadOnlyList<CombatEventRecord> Components, bool Boss)
    {
        public static StatusQuery For(CombatEventRecord hit, bool boss) => new(hit.SeenAtUtc, hit.SourceEntityId, hit.SourceName,
            hit.HpSource, boss ? hit.HasSourceStatusSnapshot : hit.HasPlayerStatusSnapshot,
            boss ? hit.SourceStatuses : hit.Statuses, hit.ConstituentEvents, boss);
    }

    private sealed class StatusQueryComparer : IEqualityComparer<StatusQuery>
    {
        public bool Equals(StatusQuery left, StatusQuery right)
        {
            if (left.Time != right.Time || left.SourceId != right.SourceId || left.SourceName != right.SourceName ||
                left.HpSource != right.HpSource || left.HasSnapshot != right.HasSnapshot || left.Boss != right.Boss ||
                left.Components.Count != right.Components.Count || !left.Statuses.SequenceEqual(right.Statuses))
                return false;
            for (var index = 0; index < left.Components.Count; index++)
                if (!Equals(StatusQuery.For(left.Components[index], left.Boss), StatusQuery.For(right.Components[index], right.Boss)))
                    return false;
            return true;
        }

        public int GetHashCode(StatusQuery value)
        {
            var hash = new HashCode();
            hash.Add(value.Time);
            hash.Add(value.SourceId);
            hash.Add(value.SourceName, StringComparer.Ordinal);
            hash.Add(value.HpSource);
            hash.Add(value.HasSnapshot);
            foreach (var status in value.Statuses)
                hash.Add(status);
            hash.Add(value.Boss);
            foreach (var component in value.Components)
                hash.Add(GetHashCode(StatusQuery.For(component, value.Boss)));
            return hash.ToHashCode();
        }
    }

    private static readonly ConditionalWeakTable<PartyDeathRecord, DeathMitigationResolver> Cache = new();
    private readonly PartyDeathRecord death;
    private readonly Dictionary<uint, (string Name, StatusTimeline Timeline)> sources;
    private readonly StatusTimeline? player;
    private readonly Lazy<DeathMitigationContext> context;
    private readonly ConcurrentDictionary<StatusQuery, IReadOnlyList<BossStatusEvidence>> hitBossStatuses = new(new StatusQueryComparer());
    private readonly ConcurrentDictionary<StatusQuery, IReadOnlyList<StatusSnapshot>> hitPlayerStatuses = new(new StatusQueryComparer());

    private DeathMitigationResolver(PartyDeathRecord death)
    {
        this.death = death;
        var events = CombatEventComponents.Expand(DeathDisplaySelector.GetLeadUpEvents(death, LeadUpTimingPolicy.CaptureSeconds));
        var history = death.SourceMitigationHistory.Concat(events
            .Where(value => value.SourceEntityId != 0 && value.Kind != DeathEventKind.Heal)
            .Select(value => new SourceMitigationSnapshot(value.SeenAtUtc, value.PullElapsedSeconds,
                value.SourceEntityId, value.SourceName, value.SourceStatuses)
            { IsComplete = value.HasSourceStatusSnapshot }));
        sources = history.GroupBy(value => value.SourceEntityId)
            .ToDictionary(group => group.Key, group => (group.Last().SourceName, new StatusTimeline(group.Select(value => value.ToStatusSet()))));
        if (death.PlayerStatusHistory.Count > 0)
            player = new(death.PlayerStatusHistory.Select(value => value.ToStatusSet()));
        context = new(() => BuildContext(DeathDisplaySelector.Select(death)));
    }

    public static DeathMitigationResolver For(PartyDeathRecord death) => Cache.GetValue(death, value => new(value));

    public IReadOnlyList<BossStatusEvidence> BossAt(DateTime time, uint? entityId = null)
    {
        return sources.Where(source => entityId is null || source.Key == entityId)
            .SelectMany(source => source.Value.Timeline.At(time).Select(status => new BossStatusEvidence(source.Key, source.Value.Name, status)))
            .ToArray();
    }

    public IReadOnlyList<BossStatusEvidence> ForHit(CombatEventRecord hit)
    {
        var key = StatusQuery.For(hit, true);
        if (hitBossStatuses.TryGetValue(key, out var captured))
            return captured;
        var resolved = ResolveBossForHit(hit);
        hitBossStatuses[key] = resolved;
        return resolved;
    }

    private IReadOnlyList<BossStatusEvidence> ResolveBossForHit(CombatEventRecord hit)
    {
        if (hit.ConstituentEvents.Count > 0)
            return hit.ConstituentEvents.SelectMany(ResolveBossForHit)
                .GroupBy(value => (value.EntityId, value.Status.Id, value.Status.SourceId)).Select(group => group.First()).ToArray();
        // Client-memory snapshots fill gaps without undoing confirmed server status changes.
        var timeline = sources.TryGetValue(hit.SourceEntityId, out var source) ? source.Timeline : new StatusTimeline([]);
        var statuses = timeline.At(hit.SeenAtUtc, new(hit.SeenAtUtc, hit.SourceStatuses) { IsComplete = hit.HasSourceStatusSnapshot });
        return Merge(statuses).Select(status => new BossStatusEvidence(hit.SourceEntityId, hit.SourceName, status)).ToArray();
    }

    public IReadOnlyList<StatusSnapshot> PlayerForHit(CombatEventRecord hit)
    {
        var key = StatusQuery.For(hit, false);
        if (hitPlayerStatuses.TryGetValue(key, out var captured))
            return captured;
        var resolved = ResolvePlayerForHit(hit);
        hitPlayerStatuses[key] = resolved;
        return resolved;
    }

    private IReadOnlyList<StatusSnapshot> ResolvePlayerForHit(CombatEventRecord hit)
    {
        if (hit.ConstituentEvents.Count > 0)
            return Merge(hit.ConstituentEvents.SelectMany(ResolvePlayerForHit));
        var hasSnapshot = hit.HasPlayerStatusSnapshot || hit.HpSource == CombatEventHpSource.DirectCombatEventSnapshot;
        if (player?.HasObservationAt(hit.SeenAtUtc) == true)
            return player.At(hit.SeenAtUtc, new(hit.SeenAtUtc, hit.Statuses) { IsComplete = hasSnapshot });
        if (hasSnapshot)
            return Merge(hit.Statuses);
        var prior = death.HpHistory.Where(value => value.SeenAtUtc <= hit.SeenAtUtc &&
                hit.SeenAtUtc - value.SeenAtUtc <= TimeSpan.FromMilliseconds(1500))
            .OrderBy(value => value.SeenAtUtc).LastOrDefault();
        return Merge((hit.HpSource == CombatEventHpSource.LatestPriorSample && prior is not null ? [] : hit.Statuses)
            .Concat(prior is null ? [] : Age(prior.Statuses, prior.SeenAtUtc, hit.SeenAtUtc)));
    }

    public DeathMitigationContext Resolve() => context.Value;

    private DeathMitigationContext BuildContext(DeathDisplaySelection selection)
    {
        var hits = selection.FatalEvents.SelectMany(group => group.Events).ToArray();
        var playerStatuses = hits.Length > 0 ? Merge(hits.SelectMany(PlayerForHit))
            : player?.HasObservationAt(selection.AnchorSeenAtUtc) == true ? player.At(selection.AnchorSeenAtUtc)
                : selection.Snapshot is { } snapshot ? Age(snapshot.Statuses, snapshot.SeenAtUtc, selection.AnchorSeenAtUtc).ToArray() : death.StatusesAtDeath;
        var boss = hits.SelectMany(ForHit).GroupBy(value => (value.EntityId, value.Status.Id, value.Status.SourceId))
            .Select(group => group.OrderBy(value => value.Status.RemainingTime <= 0 ? float.MaxValue : value.Status.RemainingTime).First()).ToArray();
        return new(playerStatuses, boss);
    }

    private static IEnumerable<StatusSnapshot> Age(IEnumerable<StatusSnapshot> statuses, DateTime observed, DateTime at)
    {
        foreach (var status in statuses)
        {
            var remaining = status.RemainingTime - (float)(at - observed).TotalSeconds;
            if (status.RemainingTime <= 0 || remaining > 0)
                yield return status with { RemainingTime = status.RemainingTime <= 0 ? 0 : remaining };
        }
    }

    private static IReadOnlyList<StatusSnapshot> Merge(IEnumerable<StatusSnapshot> statuses) => statuses
        .GroupBy(status => (status.Id, status.SourceId)).Select(group => group
            .OrderBy(status => status.RemainingTime <= 0 ? float.MaxValue : status.RemainingTime).First())
        .OrderBy(status => status.Name, StringComparer.OrdinalIgnoreCase).ThenBy(status => status.Id).ToArray();
}
