namespace BetterDeaths.DamageParsing;

using System;
using System.Collections.Generic;
using System.Linq;

internal sealed record RaidDamageAdjustment(
    DamageActorIdentity Source,
    double ExternalBuffDamageReceived,
    double RaidBuffDamageGiven);

internal static class RaidDamageCalculator
{
    private const double DefaultCriticalRate = 0.15;
    private const double DefaultDirectHitRate = 0.05;
    private const int MinimumObservedRateSamples = 11;
    private const double DirectHitMultiplier = 1.25;

    public static IReadOnlyDictionary<string, RaidDamageAdjustment> Calculate(
        IReadOnlyList<ParsedDamageEvent> events,
        IReadOnlyList<DamageSourceSummary> sources)
    {
        var adjustments = sources.ToDictionary(
            source => GetActorKey(source.Source),
            source => new MutableAdjustment(source.Source),
            StringComparer.Ordinal);
        var rateSamples = new Dictionary<string, RateSamples>(StringComparer.Ordinal);

        foreach (var damageEvent in events
                     .OrderBy(entry => entry.SeenAtUtc)
                     .ThenBy(entry => entry.PacketSequence)
                     .ThenBy(entry => entry.TargetIndex)
                     .ThenBy(entry => entry.EffectIndex))
        {
            if (damageEvent.Outcome != DamageEventOutcome.Damage || damageEvent.Amount == 0)
            {
                continue;
            }

            var recipient = damageEvent.AttributedSource ?? damageEvent.Source;
            if (!IsPlayerCombatant(recipient) || recipient.IsLimitBreak)
            {
                continue;
            }

            var recipientKey = GetActorKey(recipient);
            if (!adjustments.TryGetValue(recipientKey, out var recipientAdjustment))
            {
                recipientAdjustment = new MutableAdjustment(recipient);
                adjustments[recipientKey] = recipientAdjustment;
            }

            var effects = GetEffects(damageEvent);
            var rates = ObserveAndEstimateBaseRates(
                damageEvent,
                recipient,
                effects,
                rateSamples);
            var externalDamageBuffs = effects
                .Where(effect => effect.Kind == RaidBuffEffectKind.DamageMultiplier &&
                    IsExternalPlayerBuff(effect.Source, recipient))
                .ToList();
            var damageAfterPercentageBuffs = RedistributePercentageDamage(
                damageEvent.Amount,
                externalDamageBuffs,
                recipientAdjustment,
                adjustments);

            RedistributeCriticalAndDirectHitDamage(
                damageEvent,
                damageAfterPercentageBuffs,
                effects,
                rates,
                recipient,
                recipientAdjustment,
                adjustments);
        }

        return adjustments.ToDictionary(
            entry => entry.Key,
            entry => new RaidDamageAdjustment(
                entry.Value.Source,
                entry.Value.ExternalBuffDamageReceived,
                entry.Value.RaidBuffDamageGiven),
            StringComparer.Ordinal);
    }

    public static string GetActorKey(DamageActorIdentity actor)
    {
        return actor.EntityId != 0
            ? $"entity:{actor.EntityId:X8}"
            : $"name:{actor.Name}";
    }

    private static double RedistributePercentageDamage(
        double damage,
        IReadOnlyList<RaidBuffEffect> buffs,
        MutableAdjustment recipient,
        IDictionary<string, MutableAdjustment> adjustments)
    {
        if (buffs.Count == 0)
        {
            return damage;
        }

        var totalMultiplier = buffs.Aggregate(1.0, (total, buff) => total * (1.0 + buff.Amount));
        if (totalMultiplier <= 1.0)
        {
            return damage;
        }

        var damageWithoutBuffs = damage / totalMultiplier;
        var buffDamage = damage - damageWithoutBuffs;
        var logTotal = Math.Log(totalMultiplier);
        foreach (var buff in buffs)
        {
            var credit = buffDamage * Math.Log(1.0 + buff.Amount) / logTotal;
            TransferCredit(buff.Source, credit, recipient, adjustments);
        }

        return damageWithoutBuffs;
    }

    private static void RedistributeCriticalAndDirectHitDamage(
        ParsedDamageEvent damageEvent,
        double damage,
        IReadOnlyList<RaidBuffEffect> effects,
        BaseRates rates,
        DamageActorIdentity recipient,
        MutableAdjustment recipientAdjustment,
        IDictionary<string, MutableAdjustment> adjustments)
    {
        var allCriticalBuffs = effects
            .Where(effect => effect.Kind == RaidBuffEffectKind.CriticalChance)
            .ToList();
        var allDirectHitBuffs = effects
            .Where(effect => effect.Kind == RaidBuffEffectKind.DirectHitChance)
            .ToList();
        var guaranteedCritical = RaidBuffPolicy.IsGuaranteedCritical(damageEvent);
        var guaranteedDirectHit = RaidBuffPolicy.IsGuaranteedDirectHit(damageEvent);
        var externalCriticalBuffs = allCriticalBuffs
            .Where(effect => IsExternalPlayerBuff(effect.Source, recipient))
            .ToList();
        var externalDirectHitBuffs = allDirectHitBuffs
            .Where(effect => IsExternalPlayerBuff(effect.Source, recipient))
            .ToList();
        if (externalCriticalBuffs.Count == 0 && externalDirectHitBuffs.Count == 0)
        {
            return;
        }

        var criticalMultiplier = 1.35 + rates.Critical;
        damage = RedistributeGuaranteedHitBuffDamage(
            damage,
            guaranteedCritical,
            guaranteedDirectHit,
            allCriticalBuffs,
            allDirectHitBuffs,
            externalCriticalBuffs,
            externalDirectHitBuffs,
            criticalMultiplier,
            rates,
            recipientAdjustment,
            adjustments);
        if (guaranteedCritical)
        {
            externalCriticalBuffs = [];
        }

        if (guaranteedDirectHit)
        {
            externalDirectHitBuffs = [];
        }

        if (externalCriticalBuffs.Count == 0 && externalDirectHitBuffs.Count == 0)
        {
            return;
        }

        var criticalRate = guaranteedCritical
            ? 1.0
            : Math.Clamp(rates.Critical + allCriticalBuffs.Sum(effect => effect.Amount), 0.05, 1.0);
        var directHitRate = guaranteedDirectHit
            ? 1.0
            : Math.Clamp(rates.DirectHit + allDirectHitBuffs.Sum(effect => effect.Amount), 0.05, 1.0);
        var (criticalPortion, directHitPortion) = damageEvent.IsPeriodic
            ? GetPeriodicCriticalAndDirectHitPortions(
                damage,
                criticalRate,
                directHitRate,
                criticalMultiplier)
            : GetDirectCriticalAndDirectHitPortions(
                damage,
                damageEvent.Critical,
                damageEvent.DirectHit,
                criticalMultiplier);

        foreach (var buff in externalCriticalBuffs)
        {
            TransferCredit(
                buff.Source,
                criticalPortion * buff.Amount / criticalRate,
                recipientAdjustment,
                adjustments);
        }

        foreach (var buff in externalDirectHitBuffs)
        {
            TransferCredit(
                buff.Source,
                directHitPortion * buff.Amount / directHitRate,
                recipientAdjustment,
                adjustments);
        }
    }

    private static double RedistributeGuaranteedHitBuffDamage(
        double damage,
        bool guaranteedCritical,
        bool guaranteedDirectHit,
        IReadOnlyList<RaidBuffEffect> allCriticalBuffs,
        IReadOnlyList<RaidBuffEffect> allDirectHitBuffs,
        IReadOnlyList<RaidBuffEffect> externalCriticalBuffs,
        IReadOnlyList<RaidBuffEffect> externalDirectHitBuffs,
        double criticalMultiplier,
        BaseRates rates,
        MutableAdjustment recipient,
        IDictionary<string, MutableAdjustment> adjustments)
    {
        var criticalBonus = criticalMultiplier - 1.0;
        var criticalFactor = GetGuaranteedHitExternalFactor(
            guaranteedCritical,
            allCriticalBuffs,
            externalCriticalBuffs,
            criticalBonus);
        var directHitFactor = GetGuaranteedHitExternalFactor(
            guaranteedDirectHit,
            allDirectHitBuffs,
            externalDirectHitBuffs,
            DirectHitMultiplier - 1.0,
            rates.DirectHit);
        var combinedFactor = criticalFactor * directHitFactor;
        if (combinedFactor <= 1.0)
        {
            return damage;
        }

        var damageWithoutExternalBonuses = damage / combinedFactor;
        var buffDamage = damage - damageWithoutExternalBonuses;
        var logCombined = Math.Log(combinedFactor);
        TransferGuaranteedHitCategoryCredit(
            externalCriticalBuffs,
            guaranteedCritical,
            criticalFactor,
            buffDamage,
            logCombined,
            recipient,
            adjustments);
        TransferGuaranteedHitCategoryCredit(
            externalDirectHitBuffs,
            guaranteedDirectHit,
            directHitFactor,
            buffDamage,
            logCombined,
            recipient,
            adjustments);
        return damageWithoutExternalBonuses;
    }

    private static double GetGuaranteedHitExternalFactor(
        bool guaranteed,
        IReadOnlyList<RaidBuffEffect> allBuffs,
        IReadOnlyList<RaidBuffEffect> externalBuffs,
        double hitBonus,
        double baseChance = 0.0)
    {
        if (!guaranteed || externalBuffs.Count == 0)
        {
            return 1.0;
        }

        var totalChance = allBuffs.Sum(effect => effect.Amount);
        var externalChance = externalBuffs.Sum(effect => effect.Amount);
        var selfChance = Math.Max(0.0, totalChance - externalChance);
        return (1.0 + (baseChance + totalChance) * hitBonus) /
            (1.0 + (baseChance + selfChance) * hitBonus);
    }

    private static void TransferGuaranteedHitCategoryCredit(
        IReadOnlyList<RaidBuffEffect> externalBuffs,
        bool guaranteed,
        double categoryFactor,
        double buffDamage,
        double logCombined,
        MutableAdjustment recipient,
        IDictionary<string, MutableAdjustment> adjustments)
    {
        if (!guaranteed || externalBuffs.Count == 0 || categoryFactor <= 1.0 || logCombined <= 0.0)
        {
            return;
        }

        var totalChance = externalBuffs.Sum(effect => effect.Amount);
        var categoryCredit = buffDamage * Math.Log(categoryFactor) / logCombined;
        foreach (var buff in externalBuffs)
        {
            TransferCredit(
                buff.Source,
                categoryCredit * buff.Amount / totalChance,
                recipient,
                adjustments);
        }
    }

    private static (double Critical, double DirectHit) GetDirectCriticalAndDirectHitPortions(
        double damage,
        bool critical,
        bool directHit,
        double criticalMultiplier)
    {
        if (!critical && !directHit)
        {
            return (0.0, 0.0);
        }

        var combinedMultiplier = (critical ? criticalMultiplier : 1.0) *
            (directHit ? DirectHitMultiplier : 1.0);
        var bonusDamage = damage - damage / combinedMultiplier;
        var logCombined = Math.Log(combinedMultiplier);
        var criticalPortion = critical
            ? Math.Log(criticalMultiplier) / logCombined * bonusDamage
            : 0.0;
        var directHitPortion = directHit
            ? Math.Log(DirectHitMultiplier) / logCombined * bonusDamage
            : 0.0;
        return (criticalPortion, directHitPortion);
    }

    private static (double Critical, double DirectHit) GetPeriodicCriticalAndDirectHitPortions(
        double damage,
        double criticalRate,
        double directHitRate,
        double criticalMultiplier)
    {
        var nonCriticalRate = 1.0 - criticalRate;
        var nonDirectHitRate = 1.0 - directHitRate;
        var combinedMultiplier = criticalMultiplier * DirectHitMultiplier;
        var totalMultiplier =
            nonCriticalRate * nonDirectHitRate +
            criticalRate * nonDirectHitRate * criticalMultiplier +
            nonCriticalRate * directHitRate * DirectHitMultiplier +
            criticalRate * directHitRate * combinedMultiplier;
        if (totalMultiplier <= 0.0)
        {
            return (0.0, 0.0);
        }

        var criticalPortion = (
            criticalRate * nonDirectHitRate * criticalMultiplier +
            Math.Log(criticalMultiplier) / Math.Log(combinedMultiplier) *
            criticalRate * directHitRate * combinedMultiplier) * damage / totalMultiplier;
        var directHitPortion = (
            directHitRate * nonCriticalRate * DirectHitMultiplier +
            Math.Log(DirectHitMultiplier) / Math.Log(combinedMultiplier) *
            criticalRate * directHitRate * combinedMultiplier) * damage / totalMultiplier;
        return (criticalPortion, directHitPortion);
    }

    private static void TransferCredit(
        DamageActorIdentity provider,
        double amount,
        MutableAdjustment recipient,
        IDictionary<string, MutableAdjustment> adjustments)
    {
        if (!double.IsFinite(amount) || amount <= 0.0)
        {
            return;
        }

        var providerKey = GetActorKey(provider);
        if (!adjustments.TryGetValue(providerKey, out var providerAdjustment))
        {
            providerAdjustment = new MutableAdjustment(provider);
            adjustments[providerKey] = providerAdjustment;
        }

        recipient.ExternalBuffDamageReceived += amount;
        providerAdjustment.RaidBuffDamageGiven += amount;
    }

    private static IReadOnlyList<RaidBuffEffect> GetEffects(ParsedDamageEvent damageEvent)
    {
        var effects = new List<RaidBuffEffect>();
        var seen = new HashSet<(uint StatusId, RaidBuffEffectKind Kind, string SourceKey)>();
        var recipient = damageEvent.AttributedSource ?? damageEvent.Source;
        AddEffects(damageEvent, damageEvent.SourceStatuses, isTargetStatus: false, recipient, effects, seen);
        AddEffects(damageEvent, damageEvent.TargetStatuses, isTargetStatus: true, recipient, effects, seen);
        return effects;
    }

    private static void AddEffects(
        ParsedDamageEvent damageEvent,
        IReadOnlyList<DamageStatusSnapshot> statuses,
        bool isTargetStatus,
        DamageActorIdentity recipient,
        ICollection<RaidBuffEffect> effects,
        ISet<(uint StatusId, RaidBuffEffectKind Kind, string SourceKey)> seen)
    {
        foreach (var status in statuses.Where(status => status.RemainingTime > 0.0f))
        {
            foreach (var effect in RaidBuffPolicy.GetEffects(status, isTargetStatus, recipient))
            {
                if (!RaidBuffPolicy.AppliesToDamage(effect, damageEvent))
                {
                    continue;
                }

                var key = (effect.StatusId, effect.Kind, GetActorKey(effect.Source));
                if (seen.Add(key))
                {
                    effects.Add(effect);
                }
            }
        }
    }

    private static bool IsExternalPlayerBuff(
        DamageActorIdentity provider,
        DamageActorIdentity recipient)
    {
        return IsPlayerCombatant(provider) &&
            !string.Equals(GetActorKey(provider), GetActorKey(recipient), StringComparison.Ordinal);
    }

    private static bool IsPlayerCombatant(DamageActorIdentity actor)
    {
        return actor.IsPlayer || actor.IsPartyMember;
    }

    private static BaseRates ObserveAndEstimateBaseRates(
        ParsedDamageEvent damageEvent,
        DamageActorIdentity source,
        IReadOnlyList<RaidBuffEffect> effects,
        IDictionary<string, RateSamples> samples)
    {
        if (damageEvent.IsPeriodic)
        {
            return GetEstimatedRates(samples, GetRateActorKey(damageEvent, source));
        }

        var key = GetRateActorKey(damageEvent, source);
        if (!samples.TryGetValue(key, out var sample))
        {
            sample = new RateSamples();
            samples[key] = sample;
        }

        if (!RaidBuffPolicy.IsGuaranteedCritical(damageEvent) &&
            effects.All(effect => effect.Kind != RaidBuffEffectKind.CriticalChance))
        {
            sample.CriticalSwings++;
            sample.CriticalHits += damageEvent.Critical ? 1 : 0;
        }

        if (!RaidBuffPolicy.IsGuaranteedDirectHit(damageEvent) &&
            effects.All(effect => effect.Kind != RaidBuffEffectKind.DirectHitChance))
        {
            sample.DirectHitSwings++;
            sample.DirectHits += damageEvent.DirectHit ? 1 : 0;
        }

        return GetEstimatedRates(samples, key);
    }

    private static BaseRates GetEstimatedRates(
        IDictionary<string, RateSamples> samples,
        string key)
    {
        if (!samples.TryGetValue(key, out var sample))
        {
            return BaseRates.Default;
        }

        return new BaseRates(
            sample.CriticalSwings >= MinimumObservedRateSamples
                ? Math.Clamp(sample.CriticalHits / (double)sample.CriticalSwings, 0.05, 0.95)
                : DefaultCriticalRate,
            sample.DirectHitSwings >= MinimumObservedRateSamples
                ? Math.Clamp(sample.DirectHits / (double)sample.DirectHitSwings, 0.05, 0.95)
                : DefaultDirectHitRate);
    }

    private sealed class MutableAdjustment(DamageActorIdentity source)
    {
        public DamageActorIdentity Source { get; } = source;

        public double ExternalBuffDamageReceived { get; set; }

        public double RaidBuffDamageGiven { get; set; }
    }

    private sealed class RateSamples
    {
        public int CriticalSwings { get; set; }

        public int CriticalHits { get; set; }

        public int DirectHitSwings { get; set; }

        public int DirectHits { get; set; }
    }

    private sealed record BaseRates(double Critical, double DirectHit)
    {
        public static BaseRates Default { get; } = new(DefaultCriticalRate, DefaultDirectHitRate);
    }

    private static string GetRateActorKey(
        ParsedDamageEvent damageEvent,
        DamageActorIdentity attributedSource)
    {
        return damageEvent.Source.IsPet && damageEvent.Source.EntityId != 0
            ? GetActorKey(damageEvent.Source)
            : GetActorKey(attributedSource);
    }
}
