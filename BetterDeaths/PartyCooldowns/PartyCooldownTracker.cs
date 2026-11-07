using System;
using System.Collections.Generic;

namespace BetterDeaths.PartyCooldowns;

internal sealed class PartyCooldownTracker
{
    private readonly Dictionary<(ulong Member, byte Group), ChargeState> states = [];

    public bool Observe(ulong member, PartyCooldownProfile profile, double now, uint sequence)
    {
        if (!Valid(profile, now) || member == 0)
            return false;
        var state = GetState(member, profile, now);
        if (sequence != 0 && state.HasRecentSequence(sequence, now) || now < state.LastUsed ||
            sequence == 0 && now - state.LastUsed < .1)
            return false;
        state.RememberSequence(sequence, now);
        state.Profile = profile;
        state.HasTrustedHistory = true;
        state.LastUsed = now;
        state.Minimum.Spend(now, profile);
        state.Maximum.Spend(now, profile);
        return true;
    }

    public bool Reduce(ulong member, PartyCooldownProfile profile, TemperaReduction reduction, double now)
    {
        if (!Valid(profile, now) || !double.IsFinite(reduction.Seconds) || reduction.Seconds <= 0 ||
            reduction.Seconds > profile.RecastSeconds || !double.IsFinite(reduction.CastAt) ||
            !states.TryGetValue((member, profile.Group), out var state) || state.LastUsed != reduction.CastAt)
            return false;
        foreach (var model in new[] { state.Minimum, state.Maximum })
        {
            for (var index = 0; index < model.Ends.Count; index++)
                model.Ends[index] -= reduction.Seconds;
            model.Advance(now);
        }
        return true;
    }

    public PartyCooldownStatus Get(ulong member, PartyCooldownProfile profile, double now)
    {
        if (!Valid(profile, now) || member == 0)
            return default;
        var state = GetState(member, profile, now);
        state.Minimum.Advance(now);
        state.Maximum.Advance(now);
        var minimum = profile.MaxCharges - state.Minimum.Ends.Count;
        var maximum = profile.MaxCharges - state.Maximum.Ends.Count;
        var known = minimum == maximum && (minimum == profile.MaxCharges ||
            Math.Abs(state.Minimum.Ends[0] - state.Maximum.Ends[0]) < .001);
        var next = state.Minimum.Ends.Count > 0 ? Math.Max(0, state.Minimum.Ends[0] - now) : 0;
        return new(minimum, profile.MaxCharges, next, known);
    }

    public void Reset(ulong member)
    {
        foreach (var key in new List<(ulong Member, byte Group)>(states.Keys))
            if (key.Member == member)
                states.Remove(key);
    }

    public void RetainMembers(IReadOnlySet<ulong> members)
    {
        foreach (var key in new List<(ulong Member, byte Group)>(states.Keys))
            if (!members.Contains(key.Member))
                states.Remove(key);
    }

    public void Clear() => states.Clear();

    public void ConfirmReady(ulong member, PartyCooldownProfile profile, double now)
    {
        if (!Valid(profile, now) || member == 0)
            return;
        var state = GetState(member, profile, now);
        state.Minimum.Ends.Clear();
        state.Maximum.Ends.Clear();
        state.HasTrustedHistory = true;
        state.ClearSequences();
        state.LastUsed = double.NegativeInfinity;
    }

    private ChargeState GetState(ulong member, PartyCooldownProfile profile, double now)
    {
        var key = (member, profile.Group);
        if (states.TryGetValue(key, out var state) && state.Profile.MaxCharges == profile.MaxCharges &&
            (state.Profile.ActionId != profile.ActionId || state.Profile.RecastSeconds == profile.RecastSeconds))
        {
            if (!state.HasTrustedHistory && profile.RecastSeconds > state.Profile.RecastSeconds)
            {
                state.Profile = profile;
                state.Minimum.Ends.Clear();
                for (var index = 1; index <= profile.MaxCharges; index++)
                    state.Minimum.Ends.Add(state.StartedAt + index * profile.RecastSeconds);
            }
            return state;
        }
        state = new ChargeState(profile, now);
        // Missing history is bounded by empty/full charge models; neither invents a known Ready state.
        for (var index = 1; index <= profile.MaxCharges; index++)
            state.Minimum.Ends.Add(now + index * profile.RecastSeconds);
        states[key] = state;
        return state;
    }

    private static bool Valid(PartyCooldownProfile profile, double now) => profile.Group > 0 &&
        profile.MaxCharges is >= 1 and <= 5 && double.IsFinite(profile.RecastSeconds) &&
        profile.RecastSeconds > 0 && double.IsFinite(now);

    private sealed class ChargeState(PartyCooldownProfile profile, double startedAt)
    {
        public PartyCooldownProfile Profile { get; set; } = profile;
        public double StartedAt { get; } = startedAt;
        public bool HasTrustedHistory { get; set; }
        public ChargeModel Minimum { get; } = new();
        public ChargeModel Maximum { get; } = new();
        public double LastUsed { get; set; } = double.NegativeInfinity;
        private readonly (uint Sequence, double SeenAt)[] sequences = new (uint, double)[8];
        private int sequenceIndex;

        public bool HasRecentSequence(uint sequence, double now)
        {
            foreach (var entry in sequences)
                if (entry.Sequence == sequence && now - entry.SeenAt <= 5)
                    return true;
            return false;
        }

        public void RememberSequence(uint sequence, double now)
        {
            if (sequence != 0)
            {
                sequences[sequenceIndex] = (sequence, now);
                sequenceIndex = (sequenceIndex + 1) % sequences.Length;
            }
        }

        public void ClearSequences()
        {
            Array.Clear(sequences);
            sequenceIndex = 0;
        }
    }

    private sealed class ChargeModel
    {
        public List<double> Ends { get; } = [];

        public void Advance(double now)
        {
            while (Ends.Count > 0 && Ends[0] <= now)
                Ends.RemoveAt(0);
        }

        public void Spend(double now, PartyCooldownProfile profile)
        {
            Advance(now);
            if (Ends.Count >= profile.MaxCharges)
            {
                // The observed use proves a charge existed, even when history started mid-recharge.
                Ends.Clear();
                for (var index = 1; index <= profile.MaxCharges; index++)
                    Ends.Add(now + index * profile.RecastSeconds);
                return;
            }
            Ends.Add((Ends.Count == 0 ? now : Math.Max(now, Ends[^1])) + profile.RecastSeconds);
        }
    }
}
