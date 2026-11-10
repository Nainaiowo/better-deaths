using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterDeaths;

internal sealed class StatusHistoryTrack<T> where T : class
{
    private sealed record Entry(T Value, StatusSetSnapshot Statuses, IReadOnlyList<StatusSourceSnapshot> NewSources,
        IReadOnlyList<StatusSourceSnapshot> Sources);

    private readonly List<Entry> entries = [];
    private readonly StatusTimeline timeline = new([]);

    public DateTime? FirstSeenAtUtc => entries.Count == 0 ? null : entries[0].Statuses.SeenAtUtc;

    public void Add(T value, StatusSetSnapshot statuses, IReadOnlyList<StatusSourceSnapshot> sources)
    {
        var index = timeline.Add(statuses);
        entries.Insert(index, new(value, statuses, sources, []));
        var knownSources = index == 0 ? [] : entries[index - 1].Sources;
        for (var current = index; current < entries.Count; current++)
        {
            var entry = entries[current];
            if (entry.NewSources.Any(source => !knownSources.Contains(source)))
                knownSources = knownSources.Concat(entry.NewSources).GroupBy(source => source.EntityId).Select(group => group.Last()).ToArray();
            entries[current] = entry with { Sources = knownSources };
        }
    }

    public (T? Previous, IReadOnlyList<StatusSetSnapshot> Seeds, IReadOnlyList<StatusSourceSnapshot> Sources,
        IReadOnlyList<T> Observations) Between(DateTime start, DateTime end)
    {
        if (end < start)
            return (null, [], [], []);
        var first = timeline.ObservationCountAt(start, inclusive: false);
        var after = timeline.ObservationCountAt(end);
        var previous = first == 0 ? null : entries[first - 1];
        var observations = new T[after - first];
        for (var index = first; index < after; index++)
            observations[index - first] = entries[index].Value;
        return (previous?.Value, timeline.SeedAt(start, inclusive: false), previous?.Sources ?? [], observations);
    }
}
