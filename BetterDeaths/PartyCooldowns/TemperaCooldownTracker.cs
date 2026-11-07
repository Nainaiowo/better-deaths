using System;
using System.Collections.Generic;

namespace BetterDeaths.PartyCooldowns;

internal enum TemperaEventKind { Applied, Removed, Damage, Landing, Death, Conversion }

internal readonly record struct TemperaEvent(TemperaEventKind Kind, double SeenAt)
{
    public uint StatusId { get; init; }
    public uint Sequence { get; init; }
    public long ServerFrame { get; init; }
    public double Remaining { get; init; }
    public uint Hp { get; init; }
    public bool HasHp { get; init; }
}

internal readonly record struct TemperaReduction(double CastAt, double Seconds);

internal sealed class TemperaCooldownTracker
{
    public const uint CoatAction = 34685, GrassaAction = 34686, CoatStatus = 3686, GrassaStatus = 3687;
    private const double ResultWindow = 2, SettleTime = .2, ExpiryMargin = .1;
    private readonly Dictionary<ulong, Cycle> cycles = [];

    public static bool IsOwnShield(uint target, uint source, uint status) =>
        target is > 0 and not 0xE0000000 && target == source && IsShield(status);
    public static bool IsTargetDamage(byte type, byte flags) => type is 3 or 5 or 6 or 74 && (flags & 0x80) == 0;

    public void Cast(ulong member, double now)
    {
        if (member != 0 && double.IsFinite(now))
            cycles[member] = new(now);
    }

    public void Observe(ulong member, TemperaEvent observation)
    {
        if (!double.IsFinite(observation.SeenAt) || !cycles.TryGetValue(member, out var cycle) ||
            observation.SeenAt < cycle.CastAt)
            return;
        switch (observation.Kind)
        {
            case TemperaEventKind.Applied:
                if (!IsShield(observation.StatusId) ||
                    !double.IsFinite(observation.Remaining) || observation.Remaining is <= 0 or > 10.5)
                    return;
                if (observation.StatusId == CoatStatus)
                    cycle.CoatExpiresAt = observation.SeenAt + observation.Remaining;
                else
                {
                    cycle.Converted = true;
                    cycle.GrassaExpiresAt = observation.SeenAt + observation.Remaining;
                }
                break;
            case TemperaEventKind.Conversion:
                cycle.Converted = true;
                break;
            case TemperaEventKind.Removed:
                if (IsShield(observation.StatusId))
                {
                    cycle.Removals[observation.StatusId] = observation;
                    if (observation.HasHp && observation.Hp == 0)
                        cycle.Dead = true;
                }
                break;
            case TemperaEventKind.Damage:
                if (observation.Sequence != 0)
                {
                    Prune(cycle.Damage, observation.SeenAt);
                    cycle.Damage[observation.Sequence] = observation.SeenAt;
                }
                break;
            case TemperaEventKind.Landing:
                if (!observation.HasHp)
                    break;
                if (observation.Hp == 0)
                    cycle.Dead = true;
                break;
            case TemperaEventKind.Death:
                cycle.Dead = true;
                break;
        }
    }

    public bool TryReduce(ulong member, double now, out TemperaReduction reduction)
    {
        reduction = default;
        if (!double.IsFinite(now) || !cycles.TryGetValue(member, out var cycle) || cycle.Dead || cycle.Refunded)
            return false;
        var status = cycle.Converted ? GrassaStatus : CoatStatus;
        if (!cycle.Removals.TryGetValue(status, out var loss) || now - loss.SeenAt < SettleTime ||
            !IsEarly(cycle, loss))
            return false;
        // Native result nesting and its action sequence prove consumption, not a packet clock.
        if (!loss.HasHp || loss.Hp == 0 || !MatchesDamage(cycle, loss))
            return false;
        cycle.Refunded = true;
        reduction = new(cycle.CastAt, cycle.Converted ? 30 : 60);
        return true;
    }

    public bool IsUncertain(ulong member, double now = double.NaN)
    {
        if (!cycles.TryGetValue(member, out var cycle) || cycle.Dead || cycle.Refunded)
            return false;
        if (!double.IsFinite(cycle.CoatExpiresAt) || cycle.Converted && !double.IsFinite(cycle.GrassaExpiresAt))
            return true;
        var status = cycle.Converted ? GrassaStatus : CoatStatus;
        var expiry = status == CoatStatus ? cycle.CoatExpiresAt : cycle.GrassaExpiresAt;
        if (!cycle.Removals.TryGetValue(status, out var removal))
            return double.IsFinite(now) && now > expiry + ExpiryMargin;
        return !double.IsFinite(removal.Remaining) || removal.Remaining > ExpiryMargin || removal.SeenAt < expiry;
    }

    public void Reset(ulong member) => cycles.Remove(member);
    public void Clear() => cycles.Clear();
    public void RetainMembers(IReadOnlySet<ulong> members)
    {
        foreach (var key in new List<ulong>(cycles.Keys))
            if (!members.Contains(key))
                cycles.Remove(key);
    }

    private static bool IsShield(uint status) => status is CoatStatus or GrassaStatus;
    private static bool IsEarly(Cycle cycle, TemperaEvent loss)
    {
        var expiry = loss.StatusId == CoatStatus ? cycle.CoatExpiresAt : cycle.GrassaExpiresAt;
        return double.IsFinite(expiry) && double.IsFinite(loss.Remaining) && loss.Remaining > ExpiryMargin &&
            loss.SeenAt < expiry - ExpiryMargin;
    }

    private static bool MatchesDamage(Cycle cycle, TemperaEvent result) => result.Sequence != 0 &&
        cycle.Damage.TryGetValue(result.Sequence, out var seenAt) && result.SeenAt >= seenAt &&
        result.SeenAt - seenAt <= ResultWindow;

    private static void Prune(Dictionary<uint, double> damage, double now)
    {
        if (damage.Count < 16)
            return;
        foreach (var key in new List<uint>(damage.Keys))
            if (now - damage[key] > ResultWindow)
                damage.Remove(key);
        if (damage.Count >= 16)
            damage.Clear();
    }

    private sealed class Cycle(double castAt)
    {
        public double CastAt { get; } = castAt;
        public double CoatExpiresAt { get; set; } = double.NaN;
        public double GrassaExpiresAt { get; set; } = double.NaN;
        public bool Converted { get; set; }
        public bool Dead { get; set; }
        public bool Refunded { get; set; }
        public Dictionary<uint, double> Damage { get; } = [];
        public Dictionary<uint, TemperaEvent> Removals { get; } = [];
    }
}
