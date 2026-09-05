namespace BetterDeaths.DamageParsing;

using System;
using System.Collections.Generic;
using System.Linq;

// Diagnostic simulation inputs, not character probabilities or raid-buff rates.
internal sealed class PeriodicDirectHitCompatibility
{
    private readonly Dictionary<uint, Samples> histories = [];

    public static bool IsRelevantStatus(uint statusId) => statusId == 0x84D;

    public void Observe(ParsedDamageEvent damageEvent)
    {
        var source = damageEvent.Source;
        if (damageEvent.IsPeriodic || damageEvent.Outcome != DamageEventOutcome.Damage ||
            (!source.IsPlayer && !source.IsPet) || source.EntityId == 0 ||
            damageEvent.IsSourceEntry || damageEvent.Target.IsPlayer || damageEvent.Target.IsPartyMember)
        {
            return;
        }

        var samples = GetSamples(source, damageEvent.SourceBaseRates);
        if (RaidBuffPolicy.IsGuaranteedDirectHit(damageEvent) ||
            BuffRate(source, damageEvent.SourceStatuses, 0) > 0)
        {
            return;
        }

        // Raw-source history intentionally includes damage attributed elsewhere,
        // such as limit breaks. It must never replace the owner's real hit rate.
        samples.Count++;
        samples.DirectHits += damageEvent.DirectHit ? 1 : 0;
    }

    public void ObserveContext(DamageActorIdentity source, DamageBaseRateSnapshot? attributes)
    {
        if (source.EntityId != 0 && (source.IsPlayer || source.IsPet || source.IsPartyMember))
        {
            GetSamples(source, attributes);
        }
    }

    public PeriodicDirectHitSnapshot Capture(DamageStatusApplication application, DateTime seenAtUtc)
    {
        histories.TryGetValue(application.Source.EntityId, out var samples);
        var count = samples?.Count ?? 0;
        var hits = samples?.DirectHits ?? 0;
        var buffs = BuffRate(application.Source, application.SourceStatuses,
            Math.Max(0, (seenAtUtc - application.SeenAtUtc).TotalSeconds));
        var factor = count > 10 ? Math.Min(1, 1.25 * hits / count + buffs) : 0.05;
        return new PeriodicDirectHitSnapshot(seenAtUtc, count, hits, buffs, factor);
    }

    public void Clear() => histories.Clear();

    private Samples GetSamples(DamageActorIdentity source, DamageBaseRateSnapshot? attributes)
    {
        histories.TryGetValue(source.EntityId, out var samples);
        if (samples is null ||
            source.ClassJobId != 0 && samples.Identity.ClassJobId != 0 && source.ClassJobId != samples.Identity.ClassJobId ||
            source.Level != 0 && samples.Identity.Level != 0 && source.Level != samples.Identity.Level ||
            attributes is not null && samples.Attributes is not null && attributes != samples.Attributes)
        {
            samples = new Samples { Identity = source };
            histories[source.EntityId] = samples;
        }

        samples.Identity = source with
        {
            ClassJobId = source.ClassJobId != 0 ? source.ClassJobId : samples.Identity.ClassJobId,
            Level = source.Level != 0 ? source.Level : samples.Identity.Level,
        };
        samples.Attributes = attributes ?? samples.Attributes;
        return samples;
    }

    private static double BuffRate(DamageActorIdentity source, IReadOnlyList<DamageStatusSnapshot> statuses, double elapsed)
    {
        return statuses.Where(status => status.RemainingTime - elapsed >= 1.0)
            .DistinctBy(status => (status.StatusId, status.Source.EntityId))
            .Sum(status => status.StatusId == 0x84D ? 0.20 : RaidBuffPolicy.GetEffects(status, false, source)
                .Where(effect => effect.Kind == RaidBuffEffectKind.DirectHitChance)
                .Sum(effect => effect.Amount));
    }

    private sealed class Samples
    {
        public required DamageActorIdentity Identity { get; set; }
        public DamageBaseRateSnapshot? Attributes { get; set; }
        public int Count { get; set; }
        public int DirectHits { get; set; }
    }
}
