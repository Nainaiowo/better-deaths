namespace BetterDeaths;

using System;
using System.Collections.Generic;
using System.Linq;

internal readonly record struct LeadUpHpValue(
    uint CurrentHp,
    uint ShieldHp,
    uint MaxHp)
{
    public bool IsAvailable => MaxHp > 0;

    public bool IsZero => IsAvailable && CurrentHp == 0 && ShieldHp == 0;
}

internal readonly record struct LeadUpHpEventResolution(
    LeadUpHpValue Before,
    LeadUpHpValue After,
    bool UsesCapturedResult,
    bool UsedReconstructedBefore,
    bool UnconfirmedUnsequencedHeal,
    bool UsedCalculatedResult)
{
    public bool HpOrShieldDecreased =>
        After.CurrentHp < Before.CurrentHp || After.ShieldHp < Before.ShieldHp;

    public bool HpOrShieldIncreased =>
        After.CurrentHp > Before.CurrentHp || After.ShieldHp > Before.ShieldHp;
}

internal readonly record struct LeadUpHpSampleResolution(
    LeadUpHpValue Value,
    bool UsedReconstructedValue);

internal sealed class LeadUpHpTimelineState
{
    private static readonly TimeSpan StaleStateHoldWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan AtomicDamageBurstWindow = TimeSpan.FromMilliseconds(100);

    private readonly List<StaleHpValue> staleValues = [];
    private LeadUpHpValue? trustedValue;
    private bool lethalLock;

    public LeadUpHpValue? CurrentValue => trustedValue;

    public LeadUpHpEventResolution ResolveEvent(
        CombatEventRecord combatEvent,
        LeadUpHpValue capturedBefore,
        LeadUpHpValue? capturedHealAfter,
        bool allowCapturedDamageResult)
    {
        PruneStaleValues(combatEvent.SeenAtUtc);

        var beforeResolution = ResolveEventBefore(combatEvent, capturedBefore);
        var before = beforeResolution.Value;
        if (!before.IsAvailable)
        {
            var capturedResult = GetCapturedResult(combatEvent);
            if (capturedResult is { IsAvailable: true } result)
            {
                trustedValue = result;
                return new LeadUpHpEventResolution(
                    before,
                    result,
                    true,
                    beforeResolution.UsedReconstructedValue,
                    combatEvent.Kind == DeathEventKind.Heal && combatEvent.ActionSequence == 0,
                    false);
            }

            return new LeadUpHpEventResolution(
                before,
                before,
                false,
                beforeResolution.UsedReconstructedValue,
                combatEvent.Kind == DeathEventKind.Heal && combatEvent.ActionSequence == 0,
                false);
        }

        if (lethalLock && trustedValue is { IsZero: true })
        {
            return new LeadUpHpEventResolution(
                trustedValue.Value,
                trustedValue.Value,
                false,
                true,
                combatEvent.Kind == DeathEventKind.Heal && combatEvent.ActionSequence == 0,
                false);
        }

        var resolution = combatEvent.Kind switch
        {
            DeathEventKind.Damage => ResolveDamage(
                combatEvent,
                before,
                beforeResolution.UsedReconstructedValue,
                allowCapturedDamageResult),
            DeathEventKind.Heal => ResolveHeal(
                combatEvent,
                before,
                capturedBefore,
                capturedHealAfter,
                beforeResolution.UsedReconstructedValue),
            _ => new LeadUpHpEventResolution(
                before,
                before,
                false,
                beforeResolution.UsedReconstructedValue,
                false,
                false),
        };

        if (capturedBefore.IsAvailable &&
            !ValuesMatch(capturedBefore, resolution.Before) &&
            beforeResolution.UsedReconstructedValue)
        {
            RememberStaleValue(capturedBefore, GetStaleExpiry(combatEvent), false);
        }

        if (!ValuesMatch(resolution.Before, resolution.After))
        {
            RememberStaleValue(resolution.Before, GetStaleExpiry(combatEvent), true);
        }

        trustedValue = resolution.After.IsAvailable ? resolution.After : trustedValue;
        if (combatEvent.Kind == DeathEventKind.Damage && resolution.After.IsZero)
        {
            lethalLock = resolution.UsesCapturedResult || GetCapturedResult(combatEvent) is null;
        }

        return resolution;
    }

    public LeadUpHpSampleResolution ResolveSample(LeadUpHpValue captured, DateTime seenAtUtc)
    {
        PruneStaleValues(seenAtUtc);
        if (!captured.IsAvailable)
        {
            return trustedValue is { } trusted
                ? new LeadUpHpSampleResolution(trusted, true)
                : new LeadUpHpSampleResolution(captured, false);
        }

        if (trustedValue is not { } current)
        {
            trustedValue = captured;
            return new LeadUpHpSampleResolution(captured, false);
        }

        if (lethalLock && current.IsZero)
        {
            return new LeadUpHpSampleResolution(current, true);
        }

        if (ValuesMatch(captured, current))
        {
            return new LeadUpHpSampleResolution(current, false);
        }

        var stale = FindMatchingStaleValue(captured);
        if (stale is not null)
        {
            var resolvedShieldHp = captured.ShieldHp == stale.Value.ShieldHp
                ? current.ShieldHp
                : captured.ShieldHp;
            var resolved = current with { ShieldHp = resolvedShieldHp };
            trustedValue = resolved;
            return new LeadUpHpSampleResolution(resolved, true);
        }

        trustedValue = captured;
        return new LeadUpHpSampleResolution(captured, false);
    }

    public bool TryResolveShieldCheckpoint(CombatEventRecord combatEvent)
    {
        PruneStaleValues(combatEvent.SeenAtUtc);
        if (lethalLock ||
            combatEvent.Kind != DeathEventKind.Damage ||
            combatEvent.Amount != 0 ||
            combatEvent.HpSource == CombatEventHpSource.NoPreHitSample)
        {
            return false;
        }

        var capturedBefore = new LeadUpHpValue(
            combatEvent.CurrentHp,
            combatEvent.ShieldHp,
            combatEvent.MaxHp);
        var capturedResult = GetCapturedResult(combatEvent);
        if (!capturedBefore.IsAvailable ||
            capturedResult is not { IsAvailable: true } result ||
            result.MaxHp != capturedBefore.MaxHp ||
            result.CurrentHp != capturedBefore.CurrentHp ||
            result.ShieldHp == capturedBefore.ShieldHp)
        {
            return false;
        }

        var before = trustedValue ?? capturedBefore;
        if (result.MaxHp != before.MaxHp ||
            result.CurrentHp != before.CurrentHp ||
            result.ShieldHp == before.ShieldHp ||
            (result.ShieldHp > capturedBefore.ShieldHp) != (result.ShieldHp > before.ShieldHp))
        {
            return false;
        }

        RememberStaleValue(before, GetStaleExpiry(combatEvent), true);
        trustedValue = result;
        return true;
    }

    public static IReadOnlyList<CombatEventRecord> CombineDamageBursts(
        IReadOnlyList<CombatEventRecord> events)
    {
        if (events.Count < 2)
        {
            return events;
        }

        var combined = new List<CombatEventRecord>(events.Count);
        foreach (var combatEvent in events)
        {
            if (combined.Count > 0 && SharesDamageBurst(combined[^1], combatEvent))
            {
                combined[^1] = CombineDamageBurst(combined[^1], combatEvent);
                continue;
            }

            combined.Add(combatEvent);
        }

        return combined;
    }

    public static bool SharesDamageBurst(CombatEventRecord first, CombatEventRecord second)
    {
        if (SharesActionSequence(first, second))
        {
            return true;
        }

        return first.Kind == DeathEventKind.Damage &&
            second.Kind == DeathEventKind.Damage &&
            first.Amount > 0 &&
            second.Amount > 0 &&
            first.HpSource == CombatEventHpSource.DirectCombatEventSnapshot &&
            second.HpSource == CombatEventHpSource.DirectCombatEventSnapshot &&
            second.SeenAtUtc >= first.SeenAtUtc &&
            second.SeenAtUtc - first.SeenAtUtc <= AtomicDamageBurstWindow &&
            string.Equals(first.MemberKey, second.MemberKey, StringComparison.Ordinal) &&
            first.ActionId == second.ActionId &&
            string.Equals(first.ActionName, second.ActionName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(first.SourceName, second.SourceName, StringComparison.OrdinalIgnoreCase) &&
            first.CurrentHp == second.CurrentHp &&
            first.ShieldHp == second.ShieldHp &&
            first.MaxHp == second.MaxHp;
    }

    public static CombatEventRecord CombineDamageBurst(
        CombatEventRecord first,
        CombatEventRecord second)
    {
        if (!SharesDamageBurst(first, second))
        {
            return first;
        }

        var totalAmount = (ulong)first.Amount + second.Amount;
        var resultSource = SelectLatestResult(first, second);
        return first with
        {
            ConstituentEvents = CombatEventComponents.Expand(new[] { first, second }).ToArray(),
            SourceEntityId = first.SourceEntityId == second.SourceEntityId ? first.SourceEntityId : 0,
            Amount = totalAmount > uint.MaxValue ? uint.MaxValue : (uint)totalAmount,
            Critical = first.Critical || second.Critical,
            DirectHit = first.DirectHit || second.DirectHit,
            Blocked = first.Blocked || second.Blocked,
            Parried = first.Parried || second.Parried,
            Statuses = MergeStatuses(first.Statuses, second.Statuses),
            SourceStatuses = first.SourceEntityId == second.SourceEntityId ? MergeStatuses(first.SourceStatuses, second.SourceStatuses) : [],
            ResultSeenAtUtc = resultSource?.ResultSeenAtUtc,
            ResultCurrentHp = resultSource?.ResultCurrentHp ?? 0,
            ResultShieldHp = resultSource?.ResultShieldHp ?? 0,
            ResultMaxHp = resultSource?.ResultMaxHp ?? 0,
            ResultStatuses = MergeStatuses(first.ResultStatuses, second.ResultStatuses),
            EventIdentity = $"leadup-group:{first.EventIdentity ?? first.EventOrdinal.ToString()}:{second.EventIdentity ?? second.EventOrdinal.ToString()}",
        };
    }

    private static bool SharesActionSequence(CombatEventRecord first, CombatEventRecord second)
    {
        return first.Kind == DeathEventKind.Damage &&
            second.Kind == DeathEventKind.Damage &&
            first.ActionSequence != 0 &&
            first.ActionSequence == second.ActionSequence &&
            string.Equals(first.MemberKey, second.MemberKey, StringComparison.Ordinal) &&
            first.SourceEntityId == second.SourceEntityId &&
            first.ActionId == second.ActionId &&
            Duration(first.SeenAtUtc, second.SeenAtUtc) <= TimeSpan.FromMilliseconds(5) &&
            CapturedResultsMatch(first, second);
    }

    private static CombatEventRecord? SelectLatestResult(
        CombatEventRecord first,
        CombatEventRecord second)
    {
        if (first.ResultSeenAtUtc is null)
        {
            return second.ResultSeenAtUtc is null ? null : second;
        }

        if (second.ResultSeenAtUtc is null)
        {
            return first;
        }

        return second.ResultSeenAtUtc >= first.ResultSeenAtUtc ? second : first;
    }

    private EventBeforeResolution ResolveEventBefore(
        CombatEventRecord combatEvent,
        LeadUpHpValue capturedBefore)
    {
        if (trustedValue is not { } current)
        {
            if (capturedBefore.IsAvailable)
            {
                trustedValue = capturedBefore;
            }

            return new EventBeforeResolution(capturedBefore, false);
        }

        if (lethalLock && current.IsZero)
        {
            return new EventBeforeResolution(current, true);
        }

        if (!capturedBefore.IsAvailable)
        {
            return new EventBeforeResolution(current, true);
        }

        if (ValuesMatch(capturedBefore, current))
        {
            return new EventBeforeResolution(current, false);
        }

        var stale = FindMatchingStaleValue(capturedBefore);
        if (stale is not null)
        {
            var resolvedShieldHp = capturedBefore.ShieldHp == stale.Value.ShieldHp
                ? current.ShieldHp
                : capturedBefore.ShieldHp;
            var resolved = current with { ShieldHp = resolvedShieldHp };
            trustedValue = resolved;
            return new EventBeforeResolution(resolved, true);
        }

        if (combatEvent.HpSource is CombatEventHpSource.LatestPriorSample or CombatEventHpSource.NoPreHitSample)
        {
            return new EventBeforeResolution(current, true);
        }

        trustedValue = capturedBefore;
        return new EventBeforeResolution(capturedBefore, false);
    }

    private LeadUpHpEventResolution ResolveDamage(
        CombatEventRecord combatEvent,
        LeadUpHpValue before,
        bool usedReconstructedBefore,
        bool allowCapturedResult)
    {
        var capturedResult = GetCapturedResult(combatEvent);
        if (allowCapturedResult &&
            capturedResult is { IsAvailable: true } result &&
            CapturedDamageResultIsConsistent(before, result, combatEvent.Amount))
        {
            return new LeadUpHpEventResolution(
                before,
                result,
                true,
                usedReconstructedBefore,
                false,
                false);
        }

        if (capturedResult is { IsAvailable: true } rejectedResult &&
            rejectedResult.MaxHp == before.MaxHp)
        {
            RememberStaleValue(rejectedResult, GetStaleExpiry(combatEvent), false);
        }

        var calculated = CalculateDamageResult(before, combatEvent.Amount);
        return new LeadUpHpEventResolution(
            before,
            calculated,
            false,
            usedReconstructedBefore,
            false,
            !ValuesMatch(before, calculated));
    }

    private LeadUpHpEventResolution ResolveHeal(
        CombatEventRecord combatEvent,
        LeadUpHpValue before,
        LeadUpHpValue capturedBefore,
        LeadUpHpValue? capturedHealAfter,
        bool usedReconstructedBefore)
    {
        var capturedResult = GetCapturedResult(combatEvent);
        if (capturedResult is { IsAvailable: true } result &&
            CapturedHealResultIsConsistent(before, result, combatEvent.Amount))
        {
            return new LeadUpHpEventResolution(
                before,
                result,
                true,
                usedReconstructedBefore,
                false,
                false);
        }

        if (capturedHealAfter is { IsAvailable: true } observedAfter &&
            CapturedHealResultIsConsistent(before, observedAfter, combatEvent.Amount))
        {
            return new LeadUpHpEventResolution(
                before,
                observedAfter,
                false,
                usedReconstructedBefore,
                false,
                false);
        }

        if (combatEvent.ActionSequence == 0)
        {
            return new LeadUpHpEventResolution(
                before,
                before,
                false,
                usedReconstructedBefore ||
                    (capturedBefore.IsAvailable && !ValuesMatch(capturedBefore, before)),
                true,
                false);
        }

        var calculated = CalculateHealResult(before, combatEvent.Amount);
        return new LeadUpHpEventResolution(
            before,
            calculated,
            false,
            usedReconstructedBefore,
            false,
            !ValuesMatch(before, calculated));
    }

    private static bool CapturedHealResultIsConsistent(
        LeadUpHpValue before,
        LeadUpHpValue result,
        uint amount)
    {
        if (before.MaxHp == 0 ||
            result.MaxHp != before.MaxHp ||
            amount == 0)
        {
            return false;
        }

        var expected = CalculateHealResult(before, amount);
        return expected.CurrentHp > before.CurrentHp &&
            result.CurrentHp == expected.CurrentHp;
    }

    private static bool CapturedDamageResultIsConsistent(
        LeadUpHpValue before,
        LeadUpHpValue result,
        uint amount)
    {
        if (before.MaxHp == 0 || result.MaxHp != before.MaxHp)
        {
            return false;
        }

        if (ValuesMatch(before, result))
        {
            return true;
        }

        if (result.CurrentHp > before.CurrentHp)
        {
            return false;
        }

        if (result.IsZero && amount >= before.CurrentHp)
        {
            return true;
        }

        var hpLoss = (ulong)before.CurrentHp - result.CurrentHp;
        if (hpLoss == amount)
        {
            return true;
        }

        if (result.ShieldHp > before.ShieldHp)
        {
            return false;
        }

        var shieldLoss = (ulong)before.ShieldHp - result.ShieldHp;
        return hpLoss + shieldLoss == amount;
    }

    private static LeadUpHpValue CalculateDamageResult(LeadUpHpValue before, uint amount)
    {
        var currentHp = (ulong)before.CurrentHp;
        var hpDamage = Math.Min(currentHp, amount);
        currentHp -= hpDamage;
        var shieldHp = before.ShieldHp;
        if (currentHp == 0)
        {
            shieldHp = 0;
        }

        return new LeadUpHpValue((uint)currentHp, shieldHp, before.MaxHp);
    }

    private static LeadUpHpValue CalculateHealResult(LeadUpHpValue before, uint amount)
    {
        var restoredCurrentHp = (ulong)before.CurrentHp + amount;
        var currentHp = (uint)Math.Min((ulong)before.MaxHp, restoredCurrentHp);
        return before with { CurrentHp = currentHp };
    }

    private static LeadUpHpValue? GetCapturedResult(CombatEventRecord combatEvent)
    {
        return combatEvent.ResultSeenAtUtc is not null && combatEvent.ResultMaxHp > 0
            ? new LeadUpHpValue(
                Math.Min(combatEvent.ResultCurrentHp, combatEvent.ResultMaxHp),
                combatEvent.ResultShieldHp,
                combatEvent.ResultMaxHp)
            : null;
    }

    private static bool CapturedResultsMatch(CombatEventRecord first, CombatEventRecord second)
    {
        if (first.ResultSeenAtUtc is null && second.ResultSeenAtUtc is null)
        {
            return true;
        }

        return first.ResultSeenAtUtc == second.ResultSeenAtUtc &&
            first.ResultCurrentHp == second.ResultCurrentHp &&
            first.ResultShieldHp == second.ResultShieldHp &&
            first.ResultMaxHp == second.ResultMaxHp;
    }

    private static IReadOnlyList<StatusSnapshot> MergeStatuses(
        IReadOnlyList<StatusSnapshot> first,
        IReadOnlyList<StatusSnapshot> second)
    {
        return first
            .Concat(second)
            .GroupBy(status => (status.Id, status.SourceId))
            .Select(group => group.OrderBy(status => status.RemainingTime).First())
            .OrderBy(status => status.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(status => status.Id)
            .ToList();
    }

    private StaleHpValue? FindMatchingStaleValue(LeadUpHpValue candidate)
    {
        StaleHpValue? matchingHp = null;
        for (var index = staleValues.Count - 1; index >= 0; index--)
        {
            var stale = staleValues[index];
            if (candidate.MaxHp == stale.Value.MaxHp &&
                candidate.CurrentHp == stale.Value.CurrentHp)
            {
                matchingHp ??= stale;
                if (candidate.ShieldHp == stale.Value.ShieldHp)
                {
                    return stale;
                }
            }
        }

        return matchingHp;
    }

    private void RememberStaleValue(
        LeadUpHpValue value,
        DateTime expiresAtUtc,
        bool extendExisting)
    {
        if (!value.IsAvailable)
        {
            return;
        }

        for (var index = 0; index < staleValues.Count; index++)
        {
            var existing = staleValues[index];
            if (ValuesMatch(existing.Value, value))
            {
                if (extendExisting && expiresAtUtc > existing.ExpiresAtUtc)
                {
                    staleValues[index] = new StaleHpValue(value, expiresAtUtc);
                }

                return;
            }
        }

        staleValues.Add(new StaleHpValue(value, expiresAtUtc));
    }

    private void PruneStaleValues(DateTime seenAtUtc)
    {
        staleValues.RemoveAll(stale => stale.ExpiresAtUtc < seenAtUtc);
    }

    private static DateTime GetStaleExpiry(CombatEventRecord combatEvent)
    {
        var fallbackExpiry = combatEvent.SeenAtUtc + StaleStateHoldWindow;
        return combatEvent.ResultSeenAtUtc is { } resultSeenAtUtc && resultSeenAtUtc > fallbackExpiry
            ? resultSeenAtUtc
            : fallbackExpiry;
    }

    private static bool ValuesMatch(LeadUpHpValue first, LeadUpHpValue second)
    {
        return first.CurrentHp == second.CurrentHp &&
            first.ShieldHp == second.ShieldHp &&
            first.MaxHp == second.MaxHp;
    }

    private static bool HpOrShieldIncreased(LeadUpHpValue before, LeadUpHpValue after)
    {
        return after.CurrentHp > before.CurrentHp || after.ShieldHp > before.ShieldHp;
    }

    private static TimeSpan Duration(DateTime first, DateTime second)
    {
        return first >= second ? first - second : second - first;
    }

    private readonly record struct EventBeforeResolution(
        LeadUpHpValue Value,
        bool UsedReconstructedValue);

    private sealed record StaleHpValue(
        LeadUpHpValue Value,
        DateTime ExpiresAtUtc);
}
