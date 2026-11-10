using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterDeaths;

internal sealed class StatusTimeline
{
    private sealed record ObservedStatus(DateTime SeenAtUtc, StatusSnapshot Status, bool Authoritative);
    private sealed record RemovedStatus(uint Id, uint SourceId, DateTime? PreviousExpiry);
    private sealed record Point(DateTime SeenAtUtc, ObservedStatus[] Statuses, RemovedStatus[] Removed);
    private readonly List<StatusSetSnapshot> observations = [];
    private readonly List<Point> points = [];
    private Dictionary<(uint Id, uint SourceId), ObservedStatus> state = [];
    private Dictionary<(uint Id, uint SourceId), RemovedStatus> removed = [];

    public StatusTimeline(IEnumerable<StatusSetSnapshot> observations)
    {
        foreach (var observation in observations.OrderBy(snapshot => snapshot.SeenAtUtc))
            Add(observation);
    }

    // Capture appends incrementally. A late client read only rebuilds the affected tail.
    public int Add(StatusSetSnapshot observation)
    {
        var index = FindPoint(observation.SeenAtUtc) + 1;
        observations.Insert(index, observation);
        if (index < points.Count)
        {
            state = index == 0 ? [] : points[index - 1].Statuses.ToDictionary(value => (value.Status.Id, value.Status.SourceId));
            removed = index == 0 ? [] : points[index - 1].Removed.ToDictionary(value => (value.Id, value.SourceId));
            points.RemoveRange(index, points.Count - index);
        }
        for (var current = index; current < observations.Count; current++)
        {
            var next = observations[current];
            Apply(state, removed, next);
            points.Add(new(next.SeenAtUtc, state.Values.ToArray(), removed.Values.ToArray()));
        }
        return index;
    }

    public int ObservationCountAt(DateTime time, bool inclusive = true) => FindPoint(time, inclusive) + 1;

    private int FindPoint(DateTime seenAtUtc, bool inclusive = true)
    {
        var low = 0;
        var high = points.Count - 1;
        var found = -1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            if (points[middle].SeenAtUtc < seenAtUtc || inclusive && points[middle].SeenAtUtc == seenAtUtc)
            {
                found = middle;
                low = middle + 1;
            }
            else
                high = middle - 1;
        }
        return found;
    }

    public IReadOnlyList<StatusSnapshot> At(DateTime seenAtUtc, StatusSetSnapshot? overlay = null)
    {
        var found = FindPoint(seenAtUtc);
        var statuses = found < 0 ? [] : points[found].Statuses;
        if (overlay is not null)
        {
            var state = statuses.ToDictionary(value => (value.Status.Id, value.Status.SourceId));
            var removed = found < 0 ? [] : points[found].Removed.ToDictionary(value => (value.Id, value.SourceId));
            Apply(state, removed, overlay);
            statuses = state.Values.ToArray();
        }
        return Active(statuses, seenAtUtc);
    }

    private static IReadOnlyList<StatusSnapshot> Active(IEnumerable<ObservedStatus> statuses, DateTime seenAtUtc)
    {
        var active = new List<StatusSnapshot>();
        foreach (var observed in statuses)
        {
            var status = observed.Status;
            if (status.RemainingTime > 0)
            {
                var remaining = status.RemainingTime - (float)(seenAtUtc - observed.SeenAtUtc).TotalSeconds;
                if (remaining <= 0)
                    continue;
                status = status with { RemainingTime = remaining };
            }
            active.Add(status);
        }
        return active.OrderBy(status => status.Id).ThenBy(status => status.SourceId).ToArray();
    }

    public bool HasObservationAt(DateTime seenAtUtc) => points.Count > 0 && points[0].SeenAtUtc <= seenAtUtc;

    public static StatusSetSnapshot SlotChange(DateTime time, StatusSnapshot? previous, StatusSnapshot? next) => new(time, next is null ? [] : [next])
    {
        IsAuthoritative = true,
        RemovedStatuses = previous is not null && (next is null || previous.Id != next.Id || previous.SourceId != next.SourceId)
            ? [previous] : [],
    };

    private static DateTime? Expiry(ObservedStatus observed) => observed.Status.RemainingTime <= 0 ? null :
        observed.SeenAtUtc.AddSeconds(Math.Min(observed.Status.RemainingTime, (DateTime.MaxValue - observed.SeenAtUtc).TotalSeconds));

    private static void Apply(Dictionary<(uint Id, uint SourceId), ObservedStatus> state,
        Dictionary<(uint Id, uint SourceId), RemovedStatus> removed, StatusSetSnapshot observation)
    {
        if (observation.IsComplete)
        {
            foreach (var pair in state.ToArray())
            {
                if (observation.Statuses.Any(value => value.Id == pair.Key.Id && value.SourceId == pair.Key.SourceId))
                    continue;
                if (observation.IsAuthoritative || !pair.Value.Authoritative || Expiry(pair.Value) <= observation.SeenAtUtc)
                {
                    state.Remove(pair.Key);
                    if (observation.IsAuthoritative || Expiry(pair.Value) <= observation.SeenAtUtc)
                        removed[pair.Key] = new(pair.Key.Id, pair.Key.SourceId, Expiry(pair.Value));
                }
            }
        }
        foreach (var status in observation.RemovedStatuses)
        {
            var matching = state.Where(pair => pair.Key.Id == status.Id && (status.SourceId == 0 || pair.Key.SourceId == status.SourceId)).ToArray();
            DateTime? expiry = status.RemainingTime > 0 ? Expiry(new(observation.SeenAtUtc, status, true)) : null;
            foreach (var pair in matching)
            {
                state.Remove(pair.Key);
                var previous = Expiry(pair.Value);
                removed[pair.Key] = new(pair.Key.Id, pair.Key.SourceId, previous);
                if (previous is not null && (expiry is null || previous > expiry))
                    expiry = previous;
            }
            if (expiry is null && removed.TryGetValue((status.Id, status.SourceId), out var existingRemoval))
                expiry = existingRemoval.PreviousExpiry;
            removed[(status.Id, status.SourceId)] = new(status.Id, status.SourceId, expiry);
        }
        foreach (var incoming in observation.Statuses)
        {
            if (incoming.Id == 0 || !float.IsFinite(incoming.RemainingTime))
                continue;
            var status = incoming;
            if (observation.IsUpdateOnly && status.SourceId == 0)
            {
                var matches = state.Values.Where(value => value.Status.Id == status.Id &&
                    (Expiry(value) is not { } expiry || expiry > observation.SeenAtUtc)).ToArray();
                if (matches.Length > 1)
                    continue;
                if (matches.Length == 1)
                    status = status with { SourceId = matches[0].Status.SourceId };
            }
            var key = (status.Id, status.SourceId);
            // A timerless update can modify an existing buff, but cannot establish a new lifetime.
            if (observation.IsUpdateOnly && status.RemainingTime <= 0 && !state.ContainsKey(key))
                continue;
            var blocked = false;
            var removalKeys = new[] { key, (status.Id, 0u) }.Distinct().ToArray();
            foreach (var removalKey in removalKeys)
            {
                if (!removed.TryGetValue(removalKey, out var removal))
                    continue;
                var newExpiry = Expiry(new(observation.SeenAtUtc, status, false));
                // A later timer extending beyond the retired generation is evidence of a new application.
                if ((!observation.IsAuthoritative || observation.IsUpdateOnly) && (removal.PreviousExpiry is null || newExpiry is null ||
                    (newExpiry.Value - removal.PreviousExpiry.Value).TotalSeconds <= .25))
                {
                    blocked = true;
                    break;
                }
            }
            if (blocked)
                continue;
            foreach (var removalKey in removalKeys)
                removed.Remove(removalKey);
            // A concrete provider can identify a previously sole, unattributed observation.
            if (status.SourceId != 0 && state.TryGetValue((status.Id, 0), out var unidentified) &&
                !state.Keys.Any(value => value.Id == status.Id && value.SourceId != 0))
            {
                state.Remove((status.Id, 0));
                state[key] = unidentified with { Status = unidentified.Status with { SourceId = status.SourceId } };
            }
            var confirmed = observation.IsAuthoritative;
            if (state.TryGetValue(key, out var old))
            {
                if ((!observation.IsAuthoritative || observation.IsUpdateOnly) && status.RemainingTime <= 0 && old.Status.RemainingTime > 0)
                {
                    if (Expiry(old) <= observation.SeenAtUtc)
                        continue;
                    status = status with { RemainingTime = old.Status.RemainingTime - (float)(observation.SeenAtUtc - old.SeenAtUtc).TotalSeconds };
                }
                confirmed |= old.Authoritative && !(Expiry(old) <= observation.SeenAtUtc);
            }
            state[key] = new(observation.SeenAtUtc, status, confirmed);
        }
    }

    public IReadOnlyList<StatusSetSnapshot> SeedAt(DateTime time, bool inclusive = true)
    {
        var found = FindPoint(time, inclusive);
        if (found < 0)
            return [];
        var point = points[found];
        var active = Active(point.Statuses, time);
        var result = new List<StatusSetSnapshot> { new(time, active) { IsComplete = true } };
        var confirmed = Active(point.Statuses.Where(value => value.Authoritative), time);
        if (confirmed.Count > 0)
            result.Add(new(time, confirmed) { IsAuthoritative = true });
        var removals = point.Removed.Where(value => value.PreviousExpiry is null || value.PreviousExpiry > time)
            .Select(value => new StatusSnapshot(value.Id, "", 0, value.SourceId, 0,
                value.PreviousExpiry is { } expiry ? (float)(expiry - time).TotalSeconds : 0)).ToArray();
        if (removals.Length > 0)
            result.Add(new(time, []) { RemovedStatuses = removals, IsAuthoritative = true });
        return active.Count == 0 && removals.Length == 0 ? [] : result;
    }

    public static bool Equivalent(StatusSetSnapshot previous, StatusSetSnapshot next)
    {
        if (!previous.IsComplete || !next.IsComplete || previous.IsAuthoritative != next.IsAuthoritative ||
            previous.IsUpdateOnly != next.IsUpdateOnly || next.SeenAtUtc < previous.SeenAtUtc ||
            previous.RemovedStatuses.Count > 0 || next.RemovedStatuses.Count > 0 || previous.Statuses.Count != next.Statuses.Count)
            return false;
        var elapsed = (float)(next.SeenAtUtc - previous.SeenAtUtc).TotalSeconds;
        foreach (var status in next.Statuses)
        {
            var before = previous.Statuses.FirstOrDefault(value => value.Id == status.Id && value.SourceId == status.SourceId);
            if (before is null || before.StackCount != status.StackCount || before.IconId != status.IconId ||
                before.Name != status.Name || !float.IsFinite(before.RemainingTime) || !float.IsFinite(status.RemainingTime) ||
                (before.RemainingTime > 0) != (status.RemainingTime > 0))
                return false;
            if (status.RemainingTime > 0 && MathF.Abs(before.RemainingTime - elapsed - status.RemainingTime) > .25f)
                return false;
        }
        return true;
    }
}
