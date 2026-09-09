namespace BetterDeaths.DamageParsing;

using System;
using System.Collections.Generic;
using System.Linq;

internal enum RaidDamageRateBasis
{
    DefaultEstimate,
    ObservedHits,
    CapturedAttributes,
    PeriodicSnapshot,
}

internal sealed record RaidDamageRateDiagnostic(
    DamageActorIdentity Source,
    double CriticalRate,
    RaidDamageRateBasis CriticalBasis,
    int CriticalSamples,
    int CriticalHits,
    int PeriodicCriticalSamples,
    double DirectHitRate,
    RaidDamageRateBasis DirectHitBasis,
    int DirectHitSamples,
    int DirectHits);

internal sealed record RaidBuffCreditDiagnostic(
    DamageActorIdentity Provider,
    DamageActorIdentity Recipient,
    uint StatusId,
    RaidBuffEffectKind Kind,
    RaidBuffTargeting Targeting,
    int DirectEvents,
    int PeriodicEvents,
    double DirectCredit,
    double PeriodicCredit,
    DateTime FirstSeenAtUtc,
    DateTime LastSeenAtUtc)
{
    public double TotalCredit => DirectCredit + PeriodicCredit;
}

internal sealed record RaidBuffMissingStrengthDiagnostic(
    DamageActorIdentity Provider,
    DamageActorIdentity Recipient,
    uint StatusId,
    int EventCount);

internal sealed record RaidDamageDiagnostics(
    IReadOnlyList<RaidDamageRateDiagnostic> Rates,
    IReadOnlyList<RaidBuffCreditDiagnostic> RawCredits,
    IReadOnlyList<RaidBuffCreditDiagnostic> EffectiveCredits,
    IReadOnlyList<RaidBuffMissingStrengthDiagnostic> MissingStrength)
{
    public static RaidDamageDiagnostics Empty { get; } = new([], [], [], []);
}

// Aggregated alongside the existing transfers, never a second calculation of credit.
internal sealed class RaidDamageCreditCollector
{
    private readonly Dictionary<(string Provider, string Recipient, uint Status, RaidBuffEffectKind Kind, RaidBuffTargeting Targeting), Credit> credits = [];
    private ParsedDamageEvent current = null!;

    public void BeginEvent(ParsedDamageEvent damageEvent) => current = damageEvent;

    public void Record(RaidBuffEffect buff, DamageActorIdentity recipient, double amount)
    {
        var key = (RaidDamageCalculator.GetActorKey(buff.Source), RaidDamageCalculator.GetActorKey(recipient),
            buff.StatusId, buff.Kind, buff.Targeting);
        if (!credits.TryGetValue(key, out var credit))
            credits[key] = credit = new(buff, recipient, current.SeenAtUtc);
        credit.LastSeenAtUtc = current.SeenAtUtc;
        if (current.IsPeriodic)
        {
            credit.PeriodicEvents++;
            credit.PeriodicCredit += amount;
        }
        else
        {
            credit.DirectEvents++;
            credit.DirectCredit += amount;
        }
    }

    public IReadOnlyList<RaidBuffCreditDiagnostic> Complete() => credits.Values.Select(credit => new RaidBuffCreditDiagnostic(
        credit.Buff.Source, credit.Recipient, credit.Buff.StatusId, credit.Buff.Kind, credit.Buff.Targeting,
        credit.DirectEvents, credit.PeriodicEvents, credit.DirectCredit, credit.PeriodicCredit,
        credit.FirstSeenAtUtc, credit.LastSeenAtUtc)).ToArray();

    private sealed class Credit(RaidBuffEffect buff, DamageActorIdentity recipient, DateTime time)
    {
        public RaidBuffEffect Buff { get; } = buff;
        public DamageActorIdentity Recipient { get; } = recipient;
        public DateTime FirstSeenAtUtc { get; } = time;
        public DateTime LastSeenAtUtc { get; set; } = time;
        public int DirectEvents;
        public int PeriodicEvents;
        public double DirectCredit;
        public double PeriodicCredit;
    }
}
