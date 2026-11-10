using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterDeaths;

internal sealed class PlayerStatusHistory
{
    private readonly Dictionary<string, PlayerStatusSnapshot> latest = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StatusHistoryTrack<PlayerStatusSnapshot>> tracks = new(StringComparer.Ordinal);

    public bool Observe(PlayerStatusSnapshot observation, bool available = true)
    {
        if (!available)
            return false;
        if (latest.TryGetValue(observation.MemberKey, out var previous) &&
            StatusTimeline.Equivalent(previous.ToStatusSet(), observation.ToStatusSet()) && previous.Sources.SequenceEqual(observation.Sources))
            return false;
        AddToTrack(observation);
        if (previous is null || observation.SeenAtUtc >= previous.SeenAtUtc)
            latest[observation.MemberKey] = observation;
        return true;
    }

    private void AddToTrack(PlayerStatusSnapshot observation)
    {
        if (!tracks.TryGetValue(observation.MemberKey, out var track))
            tracks[observation.MemberKey] = track = new();
        track.Add(observation, observation.ToStatusSet(), observation.Sources);
    }

    public IReadOnlyList<PlayerStatusSnapshot> Between(DateTime start, DateTime end, string? memberKey = null)
    {
        var result = new List<PlayerStatusSnapshot>();
        foreach (var pair in tracks)
        {
            if (memberKey is not null && pair.Key != memberKey)
                continue;
            var range = pair.Value.Between(start, end);
            if (range.Previous is not null)
            {
                foreach (var seed in range.Seeds)
                    result.Add(range.Previous with
                    {
                        SeenAtUtc = start,
                        Statuses = seed.Statuses,
                        RemovedStatuses = seed.RemovedStatuses,
                        IsComplete = seed.IsComplete,
                        IsUpdateOnly = seed.IsUpdateOnly,
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
