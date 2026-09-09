namespace BetterDeaths.Tests;

using System.Text.Json;
using BetterDeaths.DamageParsing;

public sealed class RaidDamageDiagnosticsTests
{
    private static readonly DateTime Start = new(2026, 9, 9, 16, 0, 0, DateTimeKind.Utc);
    private static readonly DamageActorIdentity Dealer = new(1, "Player 1", 0, "", true, 23);
    private static readonly DamageActorIdentity Provider = new(2, "Player 2", 0, "", true, 22);
    private static readonly DamageActorIdentity OtherProvider = new(3, "Player 3", 0, "", true, 23);
    private static readonly DamageActorIdentity Target = new(0x40000001, "Target", 0, "", false, 0);

    [Theory]
    [InlineData(false, false, false, 100u, 0.0, 0.0)]
    [InlineData(false, true, false, 150u, 20.0, 0.0)]
    [InlineData(false, false, true, 125u, 0.0, 20.0)]
    [InlineData(false, true, true, 1875u, 225.75697249901174, 248.48605500197664)]
    [InlineData(true, false, false, 1000u, 119.41254593826464, 184.70431988817668)]
    public void PublishedRedistributionExamplesHaveExactExpectedCredit(
        bool periodic, bool critical, bool direct, uint amount, double expectedCritical, double expectedDirect)
    {
        // Independent fixtures evaluated from https://www.fflogs.com/help/rdps (5.0 math).
        var hit = Hit(amount, critical, direct) with { IsPeriodic = periodic,
            SourceStatuses = [Buff(0x312, Provider), Buff(0x8D, OtherProvider)] };
        var result = RaidDamageCalculator.CalculateBoth([hit], [], includeDiagnostics: true);
        Assert.Equal(expectedCritical, result.Raw.GetValueOrDefault(Key(Provider))?.RaidBuffDamageGiven ?? 0, 9);
        Assert.Equal(expectedDirect, result.Raw.GetValueOrDefault(Key(OtherProvider))?.RaidBuffDamageGiven ?? 0, 9);
        AssertCreditsBalance(result.Raw, result.Diagnostics.RawCredits);
    }

    [Fact]
    public void DiagnosticsDoNotChangeAnyAdjustment()
    {
        var hits = new[] {
            Hit(1875, true, true) with { SourceStatuses = [Buff(0x312, Provider), Buff(0x8D, OtherProvider)], CalculatedAmount = 1500 },
            Hit(1000) with { IsPeriodic = true, SourceStatuses = [Buff(0x4A1, Provider)], SeenAtUtc = Start.AddSeconds(3) },
            Hit(1875, true, true) with { ActionId = 36925, SourceStatuses = [Buff(0x312, Provider), Buff(0x8D, OtherProvider)] },
        };
        var plain = RaidDamageCalculator.CalculateBoth(hits, []);
        var traced = RaidDamageCalculator.CalculateBoth(hits, [], includeDiagnostics: true);
        Assert.Equal(plain.Raw.OrderBy(pair => pair.Key), traced.Raw.OrderBy(pair => pair.Key));
        Assert.Equal(plain.Effective.OrderBy(pair => pair.Key), traced.Effective.OrderBy(pair => pair.Key));
        Assert.Same(RaidDamageDiagnostics.Empty, plain.Diagnostics);
        AssertCreditsBalance(traced.Raw, traced.Diagnostics.RawCredits);
        AssertCreditsBalance(traced.Effective, traced.Diagnostics.EffectiveCredits);
    }

    [Fact]
    public void RawAndEffectiveCreditsRemainSeparateAndFractional()
    {
        var hit = Hit(105) with { MeterAmount = 105.5, CalculatedAmount = 52.75,
            SourceStatuses = [Buff(0x4A1, Provider)] };
        var result = RaidDamageCalculator.CalculateBoth([hit], [], includeDiagnostics: true);
        var raw = Assert.Single(result.Diagnostics.RawCredits);
        var effective = Assert.Single(result.Diagnostics.EffectiveCredits);
        Assert.Equal(105.5 - 105.5 / 1.05, raw.TotalCredit, 9);
        Assert.Equal(raw.TotalCredit / 2, effective.TotalCredit, 9);
        AssertCreditsBalance(result.Raw, result.Diagnostics.RawCredits);
        AssertCreditsBalance(result.Effective, result.Diagnostics.EffectiveCredits);
    }

    [Fact]
    public void CreditsAggregateByBuffAndRecipientInsteadOfSavingEveryHit()
    {
        var hits = Enumerable.Range(0, 1000).Select(i => Hit(105) with {
            SeenAtUtc = Start.AddMilliseconds(i), IsPeriodic = i % 2 == 0,
            SourceStatuses = [Buff(0x4A1, Provider)] }).Reverse().ToArray();
        var result = RaidDamageCalculator.CalculateBoth(hits, [], includeDiagnostics: true);
        var credit = Assert.Single(result.Diagnostics.RawCredits);
        Assert.Equal(500, credit.DirectEvents);
        Assert.Equal(500, credit.PeriodicEvents);
        Assert.Equal(2500, credit.DirectCredit, 6);
        Assert.Equal(2500, credit.PeriodicCredit, 6);
        Assert.Equal(Start, credit.FirstSeenAtUtc);
        Assert.Equal(Start.AddMilliseconds(999), credit.LastSeenAtUtc);
    }

    [Fact]
    public void PetCreditUsesOwnerIdentityAndKeepsThePetsBuffs()
    {
        var pet = new DamageActorIdentity(4, "Pet", Dealer.EntityId, Dealer.Name, false, 0) { IsPet = true };
        var hit = Hit(105) with { Source = pet, AttributedSource = Dealer, SourceStatuses = [Buff(0x4A1, Provider)] };
        var result = RaidDamageCalculator.CalculateBoth([hit], [], includeDiagnostics: true);
        Assert.Equal(Dealer, Assert.Single(result.Diagnostics.RawCredits).Recipient);
        Assert.Equal(Dealer, Assert.Single(result.Diagnostics.Rates).Source);
    }

    [Fact]
    public void RateDiagnosticsDistinguishDefaultsObservationsAndCapturedStats()
    {
        var hit = Hit(100) with { SourceBaseRates = null };
        var fallback = Assert.Single(RaidDamageCalculator.CalculateBoth([hit], [], true).Diagnostics.Rates);
        Assert.Equal(RaidDamageRateBasis.DefaultEstimate, fallback.CriticalBasis);
        Assert.Equal(RaidDamageRateBasis.DefaultEstimate, fallback.DirectHitBasis);
        Assert.Equal(1, fallback.CriticalSamples);
        var observed = Assert.Single(RaidDamageCalculator.CalculateBoth(Enumerable.Repeat(hit, 11).ToArray(), [], true).Diagnostics.Rates);
        Assert.Equal(RaidDamageRateBasis.ObservedHits, observed.CriticalBasis);
        Assert.Equal(RaidDamageRateBasis.ObservedHits, observed.DirectHitBasis);
        Assert.Equal(11, observed.DirectHitSamples);
        Assert.Equal(0, observed.DirectHitRate);
        var captured = Assert.Single(RaidDamageCalculator.CalculateBoth([Hit(100)], [], true).Diagnostics.Rates);
        Assert.Equal(RaidDamageRateBasis.CapturedAttributes, captured.CriticalBasis);
        Assert.Equal(RaidDamageRateBasis.CapturedAttributes, captured.DirectHitBasis);
        Assert.Equal(.15, captured.CriticalRate);
        Assert.Equal(.05, captured.DirectHitRate);
    }

    [Fact]
    public void PeriodicRateInferenceIsReportedAsInferenceNotCapturedAttributes()
    {
        var hit = Hit(100) with { SourceBaseRates = null, IsPeriodic = true, CriticalRateLowByte = 150 };
        var rate = Assert.Single(RaidDamageCalculator.CalculateBoth([hit], [], true).Diagnostics.Rates);
        Assert.Equal(RaidDamageRateBasis.PeriodicSnapshot, rate.CriticalBasis);
        Assert.Equal(RaidDamageRateBasis.DefaultEstimate, rate.DirectHitBasis);
        Assert.Equal(1, rate.PeriodicCriticalSamples);
        Assert.Equal(0, rate.CriticalSamples);
    }

    [Theory]
    [InlineData(0xF2Fu)] [InlineData(0xF31u)] [InlineData(0xB94u)]
    [InlineData(0x71Eu)] [InlineData(0x839u)]
    public void MissingVariableStrengthIsReportedWithoutInventingCredit(uint status)
    {
        var hit = Hit(106) with { SourceStatuses = [Buff(status, Provider) with { HasParameter = false }] };
        var missing = RaidDamageCalculator.CalculateBoth([hit], [], true);
        Assert.Equal(status, Assert.Single(missing.Diagnostics.MissingStrength).StatusId);
        Assert.Empty(missing.Diagnostics.RawCredits);
        var resolved = RaidDamageCalculator.CalculateBoth([hit with { SourceStatuses = [Buff(status, Provider) with { AppliedParameter = 6 }] }], [], true);
        Assert.Empty(resolved.Diagnostics.MissingStrength);
        Assert.Equal(6, Assert.Single(resolved.Diagnostics.RawCredits).TotalCredit, 6);
        var zero = RaidDamageCalculator.CalculateBoth([hit with { SourceStatuses = [Buff(status, Provider) with { AppliedParameter = 0 }] }], [], true);
        Assert.Empty(zero.Diagnostics.MissingStrength);
        Assert.Empty(zero.Diagnostics.RawCredits);
    }

    [Fact]
    public void FriendlyDamageAndSelfBuffsDoNotProduceExternalCredits()
    {
        var hit = Hit(106) with { SourceStatuses = [Buff(0x4A1, Dealer)] };
        var result = RaidDamageCalculator.CalculateBoth([hit, hit with {
            SourceStatuses = [Buff(0xF2F, Provider) with { HasParameter = false }],
            MeterEligibility = DamageMeterEligibility.FriendlyTarget }], [], true);
        Assert.Empty(result.Diagnostics.RawCredits);
        Assert.Empty(result.Diagnostics.EffectiveCredits);
        Assert.Empty(result.Diagnostics.MissingStrength);
    }

    [Fact]
    public void EncounterSerializationKeepsTheBreakdownAndOlderDataLoadsEmpty()
    {
        var hit = Hit(105) with { SourceStatuses = [Buff(0x4A1, Provider)] };
        var diagnostics = RaidDamageCalculator.CalculateBoth([hit], [], true).Diagnostics;
        var snapshot = new DamageEncounterSnapshot(Start, Start, Start, "test", 105, 1, 0, [hit], [], [])
        { Diagnostics = DamageEncounterDiagnostics.Empty with { RaidDamage = diagnostics } };
        var restored = JsonSerializer.Deserialize<DamageEncounterSnapshot>(JsonSerializer.Serialize(snapshot))!;
        Assert.Equal(5, Assert.Single(restored.Diagnostics.RaidDamage.RawCredits).TotalCredit, 9);
        Assert.Equal(RaidDamageRateBasis.CapturedAttributes, Assert.Single(restored.Diagnostics.RaidDamage.Rates).CriticalBasis);
        var older = JsonSerializer.Deserialize<DamageEncounterSnapshot>(JsonSerializer.Serialize(snapshot with { Diagnostics = DamageEncounterDiagnostics.Empty }))!;
        Assert.Empty(older.Diagnostics.RaidDamage.RawCredits);
    }

    private static void AssertCreditsBalance(IReadOnlyDictionary<string, RaidDamageAdjustment> adjustments, IReadOnlyList<RaidBuffCreditDiagnostic> credits)
    {
        foreach (var adjustment in adjustments.Values)
        {
            Assert.Equal(adjustment.RaidBuffDamageGiven, credits.Where(c => Key(c.Provider) == Key(adjustment.Source)).Sum(c => c.TotalCredit), 6);
            Assert.Equal(adjustment.ExternalBuffDamageReceived, credits.Where(c => Key(c.Recipient) == Key(adjustment.Source)).Sum(c => c.TotalCredit), 6);
            Assert.Equal(adjustment.SingleTargetBuffDamageReceived, credits.Where(c => Key(c.Recipient) == Key(adjustment.Source) && c.Targeting == RaidBuffTargeting.SingleTarget).Sum(c => c.TotalCredit), 6);
        }
    }

    private static string Key(DamageActorIdentity actor) => RaidDamageCalculator.GetActorKey(actor);
    private static DamageStatusSnapshot Buff(uint id, DamageActorIdentity source) => new(id, source, 0, 20);
    private static ParsedDamageEvent Hit(uint amount, bool critical = false, bool direct = false) => new(
        "hit", 1, Start, 1, Dealer, Target, 7, "Attack", 0, 0, DamageEventOutcome.Damage, amount, 0,
        critical, direct, false, false, 3, 0, 0, 0, 0)
    { SourceBaseRates = new(.15, .05), HasSourceStatusSnapshot = true, HasTargetStatusSnapshot = true };
}
