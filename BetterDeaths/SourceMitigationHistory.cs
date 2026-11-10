using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterDeaths;

internal sealed class SourceMitigationHistory
{
    private readonly Dictionary<uint, SourceMitigationSnapshot> latest = [];
    private readonly Dictionary<uint, StatusHistoryTrack<SourceMitigationSnapshot>> tracks = [];

    public bool Observe(SourceMitigationSnapshot observation, bool available = true)
    {
        if (!available || observation.SourceEntityId is 0 or 0xE0000000 or uint.MaxValue)
            return false;
        latest.TryGetValue(observation.SourceEntityId, out var previous);
        if (observation.Statuses.Count == 0 && observation.RemovedStatuses.Count == 0 &&
            (!observation.IsComplete || previous is null))
            return false;
        if (previous is not null && StatusTimeline.Equivalent(previous.ToStatusSet(), observation.ToStatusSet()) &&
            previous.Sources.SequenceEqual(observation.Sources))
            return false;
        AddToTrack(observation);
        if (previous is null || observation.SeenAtUtc >= previous.SeenAtUtc)
            latest[observation.SourceEntityId] = observation;
        return true;
    }

    private void AddToTrack(SourceMitigationSnapshot observation)
    {
        if (!tracks.TryGetValue(observation.SourceEntityId, out var track))
            tracks[observation.SourceEntityId] = track = new();
        track.Add(observation, observation.ToStatusSet(), observation.Sources);
    }

    public IReadOnlyList<SourceMitigationSnapshot> Between(DateTime start, DateTime end)
    {
        var result = new List<SourceMitigationSnapshot>();
        foreach (var track in tracks.Values)
        {
            var range = track.Between(start, end);
            if (range.Previous is not null)
            {
                foreach (var seed in range.Seeds)
                    result.Add(range.Previous with
                    {
                        SeenAtUtc = start,
                        Statuses = seed.Statuses,
                        IsComplete = seed.IsComplete,
                        IsUpdateOnly = seed.IsUpdateOnly,
                        RemovedStatuses = seed.RemovedStatuses,
                        IsAuthoritative = seed.IsAuthoritative,
                        Sources = range.Sources,
                    });
            }
            result.AddRange(range.Observations);
        }
        return result.OrderBy(value => value.SeenAtUtc).ToArray();
    }

    public void Prune(DateTime cutoff)
    {
        if (!tracks.Values.Any(track => track.FirstSeenAtUtc < cutoff))
            return;
        var retained = Between(cutoff, DateTime.MaxValue);
        tracks.Clear();
        foreach (var observation in retained)
            AddToTrack(observation);
    }

    public void Clear()
    {
        latest.Clear();
        tracks.Clear();
    }
}
