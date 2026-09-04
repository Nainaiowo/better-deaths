namespace BetterDeaths.DamageParsing;

using System;
using System.Collections.Generic;
using System.Linq;

internal sealed class EffectiveDamageResolver
{
    private static readonly TimeSpan MatchWindow = TimeSpan.FromSeconds(2);
    private readonly Dictionary<DirectKey, PendingDirectDamage> pendingDirectDamage = [];
    private readonly Dictionary<DirectKey, DamageEffectResult> pendingResults = [];
    private readonly Dictionary<string, TargetHpState> targetStates = new(StringComparer.Ordinal);

    public IReadOnlyList<ParsedDamageEvent> ObserveDirect(IReadOnlyList<ParsedDamageEvent> parsed)
    {
        if (parsed.Count == 0)
        {
            return parsed;
        }

        Prune(parsed.Max(entry => entry.SeenAtUtc));
        var replacements = new Dictionary<string, ParsedDamageEvent>(StringComparer.Ordinal);
        foreach (var group in parsed
                     .Where(entry => entry.Outcome == DamageEventOutcome.Damage)
                     .GroupBy(CreateDirectKey))
        {
            var entries = group.ToList();
            var before = SelectBeforeSnapshot(entries);
            var hasPendingResult = group.Key.ActionSequence != 0 &&
                pendingResults.TryGetValue(group.Key, out var pendingResult) &&
                IsWithinMatchWindow(entries[0].SeenAtUtc, pendingResult.SeenAtUtc);
            var state = hasPendingResult
                ? null
                : GetStateForDirect(entries[0].Target, before, entries[0].SeenAtUtc);
            if (before is { CurrentHp: > 0 } && state is not null && IsKnownZero(state.Snapshot))
            {
                state = null;
            }

            if (before is not null && state is not null && before.CurrentHp > state.Snapshot.CurrentHp)
            {
                state = null;
            }

            var pending = new PendingDirectDamage(
                entries.Min(entry => entry.SeenAtUtc),
                entries,
                state?.Snapshot ?? before);
            if (group.Key.ActionSequence != 0)
            {
                pendingDirectDamage[group.Key] = pending;
            }

            if ((before is not null && IsKnownZero(before)) ||
                (state is not null && IsKnownZero(state.Snapshot)))
            {
                foreach (var replacement in ResolveKnownZero(entries, before is not null && IsKnownZero(before)
                             ? before
                             : state!.Snapshot))
                {
                    replacements[replacement.EventId] = replacement;
                }

                if (group.Key.ActionSequence != 0)
                {
                    pendingDirectDamage[group.Key] = pending with
                    {
                        Events = entries
                            .Select(entry => replacements.GetValueOrDefault(entry.EventId, entry))
                            .ToList(),
                    };
                }
            }

            ObserveActionSnapshot(entries[0].Target, before, entries[0].SeenAtUtc);

            if (hasPendingResult && pendingResults.Remove(group.Key, out var result))
            {
                foreach (var replacement in ResolveDirect(pending, result))
                {
                    replacements[replacement.EventId] = replacement;
                }

                ObserveResultSnapshot(result);
            }
        }

        return Replace(parsed, replacements);
    }

    public IReadOnlyList<ParsedDamageEvent> ObserveEffectResult(DamageEffectResult result)
    {
        Prune(result.SeenAtUtc);
        var key = new DirectKey(result.ActionSequence, GetActorKey(result.Target));
        IReadOnlyList<ParsedDamageEvent> replacements = [];
        if (result.ActionSequence != 0 &&
            pendingDirectDamage.Remove(key, out var pending) &&
            IsWithinMatchWindow(pending.SeenAtUtc, result.SeenAtUtc))
        {
            replacements = ResolveDirect(pending, result);
        }
        else if (result.ActionSequence != 0)
        {
            pendingResults[key] = result;
        }

        ObserveResultSnapshot(result);
        return replacements;
    }

    public IReadOnlyList<ParsedDamageEvent> ResolvePeriodic(
        PeriodicDamageTick tick,
        IReadOnlyList<ParsedDamageEvent> parsed)
    {
        if (parsed.Count == 0)
        {
            return parsed;
        }

        Prune(tick.SeenAtUtc);
        var state = GetState(tick.Target);
        var before = state?.Snapshot;
        var after = tick.TargetHp is null
            ? null
            : InheritMaxHp(tick.TargetHp, before);
        var effective = (double)tick.Amount;
        var quality = after is null
            ? DamageResolutionQuality.Unresolved
            : DamageResolutionQuality.Observed;
        if (state is not null && IsKnownZero(state.Snapshot) &&
            (after is null || after.CurrentHp == 0))
        {
            effective = 0;
            quality = DamageResolutionQuality.KnownZeroHp;
        }
        else if (before is not null && after is { CurrentHp: 0 } && before.CurrentHp > 0)
        {
            effective = Math.Min(tick.Amount, before.CurrentHp - after.CurrentHp);
            quality = DamageResolutionQuality.Resolved;
        }

        IReadOnlyList<ParsedDamageEvent> resolved;
        if (quality is DamageResolutionQuality.Resolved or DamageResolutionQuality.KnownZeroHp)
        {
            var rawMeterTotal = parsed.Sum(entry => entry.RawMeterAmount);
            var effectiveMeterTotal = tick.Amount == 0
                ? 0.0
                : rawMeterTotal * effective / tick.Amount;
            resolved = AllocateEffectiveDamage(
                parsed,
                effectiveMeterTotal,
                tick.Amount == 0 ? 0.0 : effective / tick.Amount,
                before,
                after,
                quality);
        }
        else
        {
            resolved = parsed
                .Select(entry => entry with
                {
                    TargetHpBefore = before,
                    TargetHpAfter = after,
                    ResolutionQuality = quality,
                })
                .ToList();
        }

        ObservePeriodicSnapshot(tick.Target, after, tick.SeenAtUtc);
        return resolved;
    }

    public void Clear()
    {
        pendingDirectDamage.Clear();
        pendingResults.Clear();
        targetStates.Clear();
    }

    private IReadOnlyList<ParsedDamageEvent> ResolveDirect(
        PendingDirectDamage pending,
        DamageEffectResult result)
    {
        var rawTotal = pending.Events.Sum(entry => entry.RawMeterAmount);
        var before = ResolveBeforeSnapshot(pending, result.Target);
        var after = InheritMaxHp(result.Snapshot, before);
        if (before is null)
        {
            return pending.Events
                .Select(entry => entry with
                {
                    TargetHpAfter = after,
                    ResolutionQuality = DamageResolutionQuality.Observed,
                })
                .ToList();
        }

        if (IsKnownZero(before) && IsKnownZero(after))
        {
            return ResolveKnownZero(pending.Events, before, after);
        }

        var hpLoss = before.CurrentHp > after.CurrentHp
            ? before.CurrentHp - after.CurrentHp
            : 0;
        var shieldLoss = before.ShieldHp > after.ShieldHp
            ? before.ShieldHp - after.ShieldHp
            : 0;
        var shieldRoundingTolerance = Math.Max(1.0, before.MaxHp / 100.0);
        var likelyFullyAbsorbed = hpLoss == 0 &&
            before.ShieldHp > 0 &&
            rawTotal <= before.ShieldHp + shieldRoundingTolerance;
        var unexplainedShortfall = after.CurrentHp > 0 &&
            (double)hpLoss + shieldLoss < rawTotal && !likelyFullyAbsorbed;
        if ((hpLoss == 0 && !likelyFullyAbsorbed) || unexplainedShortfall)
        {
            return pending.Events
                .Select(entry => entry with
                {
                    CalculatedAmount = null,
                    AbsorbedDamage = 0,
                    OverkillDamage = 0,
                    TargetHpBefore = before,
                    TargetHpAfter = after,
                    ResolutionQuality = DamageResolutionQuality.Observed,
                })
                .ToList();
        }

        var effective = Math.Min(rawTotal, hpLoss);
        var absorbed = likelyFullyAbsorbed
            ? rawTotal
            : Math.Min(Math.Max(0.0, rawTotal - effective), shieldLoss);
        var overkill = after.CurrentHp == 0
            ? Math.Max(0.0, rawTotal - effective - absorbed)
            : 0.0;

        return AllocateDirectDamage(
            pending.Events,
            effective,
            absorbed,
            overkill,
            before,
            after,
            DamageResolutionQuality.Resolved);
    }

    private DamageHpSnapshot? ResolveBeforeSnapshot(
        PendingDirectDamage pending,
        DamageActorIdentity target)
    {
        var actionSnapshot = SelectBeforeSnapshot(pending.Events);
        if (pending.Before is not null && IsKnownZero(pending.Before) &&
            actionSnapshot is { CurrentHp: > 0 })
        {
            return actionSnapshot;
        }

        return pending.Before ?? actionSnapshot ?? GetState(target)?.Snapshot;
    }

    private static IReadOnlyList<ParsedDamageEvent> ResolveKnownZero(
        IReadOnlyList<ParsedDamageEvent> events,
        DamageHpSnapshot before,
        DamageHpSnapshot? after = null)
    {
        return events
            .Select(entry => entry with
            {
                CalculatedAmount = 0,
                OverkillDamage = entry.RawMeterAmount,
                TargetHpBefore = before,
                TargetHpAfter = after,
                ResolutionQuality = DamageResolutionQuality.KnownZeroHp,
            })
            .ToList();
    }

    private static IReadOnlyList<ParsedDamageEvent> AllocateDirectDamage(
        IReadOnlyList<ParsedDamageEvent> events,
        double effective,
        double absorbed,
        double overkill,
        DamageHpSnapshot before,
        DamageHpSnapshot after,
        DamageResolutionQuality quality)
    {
        var remainingEffective = effective;
        var remainingAbsorbed = absorbed;
        var remainingOverkill = overkill;
        var resolved = new List<ParsedDamageEvent>(events.Count);
        foreach (var entry in events.OrderBy(entry => entry.EffectIndex))
        {
            var raw = entry.RawMeterAmount;
            var entryAbsorbed = Math.Min(raw, remainingAbsorbed);
            remainingAbsorbed -= entryAbsorbed;
            var available = raw - entryAbsorbed;
            var entryEffective = Math.Min(available, remainingEffective);
            remainingEffective -= entryEffective;
            available -= entryEffective;
            var entryOverkill = Math.Min(available, remainingOverkill);
            remainingOverkill -= entryOverkill;
            resolved.Add(entry with
            {
                CalculatedAmount = entryEffective,
                AbsorbedDamage = entryAbsorbed,
                OverkillDamage = entryOverkill,
                TargetHpBefore = before,
                TargetHpAfter = after,
                ResolutionQuality = quality,
            });
        }

        return resolved;
    }

    private static IReadOnlyList<ParsedDamageEvent> AllocateEffectiveDamage(
        IReadOnlyList<ParsedDamageEvent> events,
        double effective,
        double observedEffectiveRatio,
        DamageHpSnapshot? before,
        DamageHpSnapshot? after,
        DamageResolutionQuality quality)
    {
        var weights = events.Select(entry => Math.Max(0.0, entry.RawMeterAmount)).ToArray();
        var totalWeight = weights.Sum();
        if (totalWeight <= 0)
        {
            totalWeight = events.Count;
            Array.Fill(weights, 1.0);
        }

        var remaining = effective;
        var resolved = new List<ParsedDamageEvent>(events.Count);
        for (var index = 0; index < events.Count; index++)
        {
            var share = index == events.Count - 1
                ? remaining
                : Math.Min(remaining, effective * weights[index] / totalWeight);
            remaining -= share;
            resolved.Add(events[index] with
            {
                CalculatedAmount = share,
                OverkillDamage = after is { CurrentHp: 0 }
                    ? events[index].Amount * Math.Max(0.0, 1.0 - observedEffectiveRatio)
                    : 0.0,
                TargetHpBefore = before,
                TargetHpAfter = after,
                ResolutionQuality = quality,
            });
        }

        return resolved;
    }

    private void ObserveActionSnapshot(
        DamageActorIdentity target,
        DamageHpSnapshot? snapshot,
        DateTime seenAtUtc)
    {
        if (snapshot is null)
        {
            return;
        }

        if (snapshot.MaxHp == 0)
        {
            return;
        }

        var key = GetActorKey(target);
        if (targetStates.TryGetValue(key, out var newerState) && newerState.SeenAtUtc >= seenAtUtc)
        {
            return;
        }

        if (!targetStates.TryGetValue(key, out var state) ||
            state.Snapshot.MaxHp != snapshot.MaxHp ||
            snapshot.CurrentHp > state.Snapshot.CurrentHp)
        {
            targetStates[key] = new TargetHpState(snapshot, seenAtUtc, false);
        }
    }

    private void ObservePeriodicSnapshot(
        DamageActorIdentity target,
        DamageHpSnapshot? snapshot,
        DateTime seenAtUtc)
    {
        if (snapshot is null)
        {
            return;
        }

        if (snapshot.MaxHp == 0)
        {
            return;
        }

        var key = GetActorKey(target);
        if (targetStates.TryGetValue(key, out var state) && state.SeenAtUtc > seenAtUtc)
        {
            return;
        }

        targetStates[key] = new TargetHpState(snapshot, seenAtUtc, false);
    }

    private void ObserveResultSnapshot(DamageEffectResult result)
    {
        var key = GetActorKey(result.Target);
        if (targetStates.TryGetValue(key, out var state) && state.SeenAtUtc > result.SeenAtUtc)
        {
            return;
        }

        var snapshot = InheritMaxHp(result.Snapshot, targetStates.GetValueOrDefault(key)?.Snapshot);
        if (snapshot.MaxHp == 0)
        {
            return;
        }

        targetStates[key] = new TargetHpState(
            snapshot,
            result.SeenAtUtc,
            true);
    }

    private TargetHpState? GetState(DamageActorIdentity actor)
    {
        return targetStates.GetValueOrDefault(GetActorKey(actor));
    }

    private TargetHpState? GetStateForDirect(
        DamageActorIdentity actor,
        DamageHpSnapshot? actionSnapshot,
        DateTime seenAtUtc)
    {
        var state = GetState(actor);
        if (state is null || IsKnownZero(state.Snapshot))
        {
            return state;
        }

        if (!state.Authoritative ||
            seenAtUtc - state.SeenAtUtc > MatchWindow ||
            actionSnapshot is not null && actionSnapshot.MaxHp != state.Snapshot.MaxHp)
        {
            return null;
        }

        return state;
    }

    private void Prune(DateTime nowUtc)
    {
        var cutoff = nowUtc - MatchWindow;
        foreach (var key in pendingDirectDamage
                     .Where(entry => entry.Value.SeenAtUtc < cutoff)
                     .Select(entry => entry.Key)
                     .ToList())
        {
            pendingDirectDamage.Remove(key);
        }

        foreach (var key in pendingResults
                     .Where(entry => entry.Value.SeenAtUtc < cutoff)
                     .Select(entry => entry.Key)
                     .ToList())
        {
            pendingResults.Remove(key);
        }
    }

    private static DamageHpSnapshot InheritMaxHp(DamageHpSnapshot snapshot, DamageHpSnapshot? fallback)
    {
        return snapshot.MaxHp == 0 && fallback is not null
            ? snapshot with { MaxHp = fallback.MaxHp }
            : snapshot;
    }

    private static DamageHpSnapshot? SelectBeforeSnapshot(IReadOnlyList<ParsedDamageEvent> events)
    {
        return events.Select(entry => entry.TargetHpBefore).FirstOrDefault(snapshot => snapshot is not null);
    }

    private static DirectKey CreateDirectKey(ParsedDamageEvent damageEvent)
    {
        return new DirectKey(
            damageEvent.ActionSequence,
            GetActorKey(damageEvent.Target));
    }

    private static string GetActorKey(DamageActorIdentity actor)
    {
        return actor.EntityId != 0
            ? $"entity:{actor.EntityId:X8}"
            : $"name:{actor.Name}";
    }

    private static bool IsWithinMatchWindow(DateTime left, DateTime right)
    {
        return Math.Abs((left - right).TotalSeconds) <= MatchWindow.TotalSeconds;
    }

    private static bool IsKnownZero(DamageHpSnapshot snapshot)
    {
        return snapshot.CurrentHp == 0 && snapshot.MaxHp > 0;
    }

    private static IReadOnlyList<ParsedDamageEvent> Replace(
        IReadOnlyList<ParsedDamageEvent> parsed,
        IReadOnlyDictionary<string, ParsedDamageEvent> replacements)
    {
        if (replacements.Count == 0)
        {
            return parsed;
        }

        return parsed
            .Select(entry => replacements.GetValueOrDefault(entry.EventId, entry))
            .ToList();
    }

    private readonly record struct DirectKey(uint ActionSequence, string TargetKey);

    private sealed record PendingDirectDamage(
        DateTime SeenAtUtc,
        IReadOnlyList<ParsedDamageEvent> Events,
        DamageHpSnapshot? Before);

    private sealed record TargetHpState(
        DamageHpSnapshot Snapshot,
        DateTime SeenAtUtc,
        bool Authoritative);
}
