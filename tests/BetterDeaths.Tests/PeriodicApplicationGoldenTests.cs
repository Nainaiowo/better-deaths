namespace BetterDeaths.Tests;

using System.Text.Json;
using BetterDeaths.DamageParsing;

public sealed class PeriodicApplicationGoldenTests
{
    private static readonly DateTime Start = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);
    // A fixed caster isolates shared arithmetic. Job labels identify definitions,
    // not end-to-end job simulations.
    private static readonly DamageActorIdentity Source = new(0x10001001, "Player 1", 0, "", true, 24) { Level = 100 };
    private static readonly DamageActorIdentity Target = new(0x40001001, "Target", 0, "", false, 0);
    private static readonly GoldenFile Data = JsonSerializer.Deserialize<GoldenFile>(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "PeriodicApplicationGolden.json")))!;

    public sealed record GoldenFile(int Schema, string[] BinaryHashes, Case[] Cases);
    public sealed record Case(string Job, uint Status, string Name, string Scenario, bool Warm, uint[] Buffs,
        byte? LowCrit, byte? LowByte, bool Ground, double Potency, int MaxTicks, double BaseDamage,
        double CriticalRate, double DirectFactor, double Multiplier, double Expected);

    public static TheoryData<int, string> Cases
    {
        get
        {
            var cases = new TheoryData<int, string>();
            for (var index = 0; index < Data.Cases.Length; index++)
            {
                var item = Data.Cases[index];
                cases.Add(index, $"{item.Job} {item.Status:X} {item.Scenario} low={item.LowByte}");
            }
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void MatchesFrozenApplicationMathWithoutReplacingObservedAmounts(int index, string _)
    {
        var fixture = Data.Cases[index];
        var tracker = new PeriodicDamageTracker();
        if (fixture.Warm)
        {
            for (var i = 0; i < 20; i++)
            {
                var packet = new DamageActionPacket(i + 1, Start, (uint)i, Source, 100, "Calibration",
                    [new(0, Target, [new(0, 3, i >= 17 ? (byte)0x20 : (byte)0, 0, 0, 0,
                        i >= 17 ? 15000u : 10000u)])])
                {
                    DirectPotency = 100,
                    CanCalibratePotency = true,
                    SourceBaseRates = new(0.15, 0),
                    ActionCategoryId = 2,
                };
                tracker.ObserveDirectDamage(new DirectDamageParser().Parse(packet));
            }
        }

        tracker.Observe(Application(fixture));
        var tick = Assert.Single(tracker.Process(new(1, Start.AddSeconds(3), Target,
            fixture.Ground ? fixture.Status : 0, fixture.Name, 0, 12345, fixture.Ground ? Source : null)));
        Assert.Equal(12345u, tick.Amount);
        Assert.Equal(12345, tick.EffectiveMeterAmount);
        if (fixture.Ground)
        {
            Assert.Null(tick.PeriodicCompatibilityEstimate);
            Assert.Equal(Source.EntityId, tick.Source.EntityId);
            return;
        }

        var estimate = Assert.IsType<PeriodicCompatibilityEstimate>(tick.PeriodicCompatibilityEstimate);
        Assert.Equal(fixture.Potency, estimate.Inputs!.Potency);
        Assert.Equal(fixture.BaseDamage, estimate.Inputs.BaseDamage);
        Assert.Equal(fixture.Multiplier, estimate.Inputs.DamageMultiplier, 9);
        Assert.Equal(fixture.CriticalRate, estimate.Inputs.CriticalRate, 9);
        Assert.Equal(fixture.DirectFactor, estimate.DirectHit.Factor, 9);
        Assert.Equal(fixture.Expected, estimate.EstimatedDamage, 6);
    }

    public static TheoryData<int, string> Lifecycles
    {
        get
        {
            var cases = new TheoryData<int, string>();
            for (var index = 0; index < Data.Cases.Length; index++)
            {
                var item = Data.Cases[index];
                if (!item.Ground && item.Scenario == "plain" && item.LowByte is null)
                    cases.Add(index, $"{item.Job} {item.Status:X}");
            }
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Lifecycles))]
    public void PendingRefreshRequiresConfirmationForEveryTargetDot(int index, string _)
    {
        var fixture = Data.Cases[index];
        var tracker = new PeriodicDamageTracker();
        var application = Application(fixture) with { DurationSeconds = 60, BaseDamageLowByte = 147 };
        tracker.Observe(application);
        var first = At(3);
        tracker.Observe(application with
        {
            SeenAtUtc = Start.AddSeconds(5),
            ActionId = 100,
            DurationSeconds = 0,
            BaseDamageLowByte = 255
        });
        Assert.Equal(first.PeriodicCompatibilityEstimate, At(6).PeriodicCompatibilityEstimate);
        tracker.Observe(application with { SeenAtUtc = Start.AddSeconds(7), BaseDamageLowByte = 255 });
        var refreshed = At(9);
        Assert.Equal((byte)255, refreshed.PeriodicCompatibilityEstimate!.Inputs!.DamageLowByte);
        Assert.Equal(12345u, refreshed.Amount);
        tracker.Clear();
        Assert.Null(At(12).PeriodicCompatibilityEstimate);

        ParsedDamageEvent At(int seconds) => Assert.Single(tracker.Process(new(seconds, Start.AddSeconds(seconds),
            Target, 0, fixture.Name, 0, 12345, null)));
    }

    [Fact]
    public void CorpusHasAnExplicitVersionAndCompleteRecordedDefinitionSet()
    {
        Assert.Equal(1, Data.Schema);
        Assert.Equal(2, Data.BinaryHashes.Length);
        Assert.All(Data.BinaryHashes, hash => Assert.Matches("^[0-9A-F]{64}$", hash));
        Assert.Equal(48, Data.Cases.Select(item => item.Status).Distinct().Count());
        Assert.Equal(14, Data.Cases.Select(item => item.Scenario).Distinct().Count());
        Assert.Equal(2688, Data.Cases.Length);
    }

    private static DamageStatusApplication Application(Case fixture) => new(Target, Source, fixture.Status,
        fixture.Name, 0, 0, "", Start.AddSeconds(1), Math.Max(3, fixture.MaxTicks * 3), true, false, false)
    {
        PeriodicPotency = fixture.Potency,
        BaseDamageLowByte = fixture.LowByte,
        CriticalRateLowByte = fixture.LowCrit,
        SourceBaseRates = fixture.Warm ? new(0.15, 0) : null,
        SourceStatuses = fixture.Buffs.Select(id => new DamageStatusSnapshot(id, Source, 0, 30)).ToArray(),
    };
}
