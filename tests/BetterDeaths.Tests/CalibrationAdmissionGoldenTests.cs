using System.Text.Json;
using BetterDeaths.DamageParsing;

namespace BetterDeaths.Tests;

public class CalibrationAdmissionGoldenTests
{
    public sealed record Sample(uint Id, uint Job, byte Level, bool Combo, int TargetIndex, double Expected);
    private sealed record Corpus(int Schema, int ProfileVersion, string ParserHash, Sample[] Samples);
    private static readonly Corpus Data = JsonSerializer.Deserialize<Corpus>(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "CalibrationAdmissionGolden.json")))!;

    public static TheoryData<int> Cases => new(Enumerable.Range(0, Data.Samples.Length));

    [Fact]
    public void CorpusAndEmbeddedCatalogHaveVersionedProvenance()
    {
        Assert.Equal(1, Data.Schema);
        Assert.Equal(PeriodicCalibrationPotencyPolicy.Version, Data.ProfileVersion);
        Assert.Matches("^[0-9A-F]{64}$", Data.ParserHash);
        Assert.Equal(5260, Data.Samples.Length);
        Assert.True(RaidBuffPolicy.UsesApplicationParameter(0xF2F));
        Assert.True(RaidBuffPolicy.UsesApplicationParameter(0xF31));
        Assert.False(PeriodicCalibrationCatalog.Contains(50000));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void CompleteCalibrationHistoryMatchesFrozenExecution(int index)
    {
        var row = Data.Samples[index];
        var at = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
        var source = new DamageActorIdentity(0x1001, "Source", 0, "", true, row.Job) { Level = row.Level };
        var target = new DamageActorIdentity(0x4001, "Target", 0, "", false, 0);
        var tracker = new PeriodicDamageTracker();
        for (var hit = 0; hit < 20; hit++)
        {
            var packet = new DamageActionPacket(hit + 1, at.AddMilliseconds(hit * 100), (uint)hit, source, row.Id, "Action",
                [new(row.TargetIndex, target, [new(0, 3, 0, 5, 0, 0, 10000) { Param2 = row.Combo ? (byte)1 : (byte)0 }])])
            { CanCalibratePotency = false, DirectPotency = null };
            var events = new DirectDamageParser().Parse(packet);
            Assert.Equal(10000u, Assert.Single(events).Amount);
            tracker.ObserveActionCalibration(packet, events);
        }
        tracker.Observe(new(target, source, 50000, "DoT", 0, 0, "", at.AddSeconds(3), 30, true, false, false)
            { PeriodicPotency = 20 });
        var tick = Assert.Single(tracker.Process(new(100, at.AddSeconds(6), target, 0, "", 0, 30000, null)));
        Assert.Equal(row.Expected, tick.PeriodicCompatibilityEstimate!.Inputs!.DamagePerPotency, 7);
        Assert.Equal(30000u, tick.Amount);
    }
}
