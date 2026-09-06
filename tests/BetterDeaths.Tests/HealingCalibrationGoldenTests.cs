namespace BetterDeaths.Tests;

using System.Reflection;
using System.Text.Json;
using BetterDeaths.DamageParsing;

public sealed class HealingCalibrationGoldenTests
{
    private static readonly GoldenFile Data = JsonSerializer.Deserialize<GoldenFile>(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "HealingCalibrationGolden.json")))!;
    private static readonly DateTime Start = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DamageActorIdentity Source = new(0x10001001, "Healer", 0, "", true, 24) { Level = 100 };
    private static readonly DamageActorIdentity Target = new(0x10001002, "Ally", 0, "", true, 19) { Level = 100 };

    public sealed record GoldenFile(int Schema, string[] BinaryHashes, Case[] Cases, CoverageRow[] Coverage);
    public sealed record Case(uint Action, uint Category, double Potency, uint[] SourceBuffs, uint[] TargetBuffs,
        int Count, double Scale, int CriticalCount, double CriticalRate);
    public sealed record CoverageRow(uint Id, string Name, double ReferencePotency);

    public static TheoryData<int> Cases => new(Enumerable.Range(0, Data.Cases.Length));
    public static TheoryData<int> Coverage => new(Enumerable.Range(0, Data.Coverage.Length));

    [Theory]
    [MemberData(nameof(Cases))]
    public void MatchesFrozenHealingArithmetic(int index)
    {
        var row = Data.Cases[index];
        var tracker = new PeriodicDamageTracker();
        for (var i = 0; i < row.Count; i++)
        {
            var critical = i % 4 == 0;
            tracker.ObserveActionCalibration(new(i + 1, Start, (uint)i + 1, Source, row.Action, "Heal",
                [new(0, Target, [new(0, 4, 0, critical ? (byte)0x20 : (byte)0, 0, 0, critical ? 15000u : 10000u)])
                { HasTargetStatusSnapshot = true, TargetStatuses = row.TargetBuffs.Select(Buff).ToArray() }])
            {
                HealingPotency = PeriodicCalibrationPolicy.HealingPotency(row.Action),
                ActionCategoryId = row.Category,
                HasSourceStatusSnapshot = true,
                SourceStatuses = row.SourceBuffs.Select(Buff).ToArray(),
                SourceBaseRates = new(.4, .3),
            }, []);
        }
        var snapshot = typeof(PeriodicDamageTracker).GetMethod("GetCompatibilityCalibration", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(tracker, [Source])!;
        Assert.Equal(row.Scale, (double)snapshot.GetType().GetProperty("DamagePerPotency")!.GetValue(snapshot)!, 8);
        Assert.Equal(row.CriticalCount, (int)snapshot.GetType().GetProperty("CriticalSamples")!.GetValue(snapshot)!);
        var rates = snapshot.GetType().GetProperty("BaseRates")!.GetValue(snapshot)!;
        Assert.Equal(row.CriticalRate, (double)rates.GetType().GetProperty("Critical")!.GetValue(rates)!, 10);
    }

    [Theory]
    [MemberData(nameof(Coverage))]
    public void IncludesEveryRecordedHealingDefinition(int index)
    {
        var row = Data.Coverage[index];
        Assert.Equal(row.ReferencePotency, PeriodicCalibrationPolicy.HealingPotency(row.Id).GetValueOrDefault());
    }

    [Fact]
    public void FrozenCorpusCoversPotenciesModifiersAndHistoryBoundaries()
    {
        Assert.Equal(1, Data.Schema);
        Assert.Equal(2, Data.BinaryHashes.Length);
        Assert.Equal(123, Data.Coverage.Length);
        Assert.Equal(new[] { 1, 10, 11, 20 }, Data.Cases.Select(row => row.Count).Distinct().Order().ToArray());
        Assert.Contains(Data.Cases, row => row.Category == 2);
        Assert.Contains(Data.Cases, row => row.Category == 4);
        Assert.All(Data.Cases.SelectMany(row => row.SourceBuffs.Concat(row.TargetBuffs)),
            id => Assert.True(DamageStatusCapturePolicy.IsRelevant(id)));
    }

    private static DamageStatusSnapshot Buff(uint id) => new(id, Source, 0, 30);
}
