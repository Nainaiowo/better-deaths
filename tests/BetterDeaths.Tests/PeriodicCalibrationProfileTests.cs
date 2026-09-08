namespace BetterDeaths.Tests;

using System.Text.Json;
using BetterDeaths.DamageParsing;

public sealed class PeriodicCalibrationProfileTests
{
    private static readonly DateTime Start = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DamageActorIdentity Source = new(0x1001, "Player", 0, "", true, 19) { Level = 100 };
    private static readonly DamageActorIdentity Target = new(0x40000001, "Target", 0, "", false, 0);
    public sealed record Row(uint Id, string Name, double Primary, double Combo, double Secondary, double SecondaryCombo);
    public sealed record Dot(uint Id, string Name, double Potency);
    public sealed record Corpus(int Schema, int ProfileVersion, Row[] Direct, Dot[] Periodic);
    private static readonly Corpus Data = JsonSerializer.Deserialize<Corpus>(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "CalibrationPotencyGolden.json")))!;

    public static TheoryData<uint, double, double, double, double> Profiles
    {
        get
        {
            var rows = new TheoryData<uint, double, double, double, double>();
            foreach (var row in Data.Direct) rows.Add(row.Id, row.Primary, row.Combo, row.Secondary, row.SecondaryCombo);
            return rows;
        }
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void AuditedProfilesOnlyChangeMeterCalibration(uint action, double primary, double combo, double secondary, double secondaryCombo)
    {
        foreach (var index in new[] { 0, 1 })
            foreach (var comboByte in new byte[] { 0, 1 })
            {
                var hit = Hit(action, 111, 10000, index, comboByte);
                Assert.Equal(111, JobDamageCalibrationPolicy.GetCalibrationPotency(hit));
                Assert.Equal(index == 0 ? comboByte == 0 ? primary : combo : comboByte == 0 ? secondary : secondaryCombo,
                    JobDamageCalibrationPolicy.GetCalibrationPotency(hit, meterProfile: true));
                Assert.Equal(10000, hit.RawMeterAmount);
                Assert.Equal(10000u, hit.Amount);
                Assert.Equal(JobDamageCalibrationPolicy.GetCalibrationPotency(hit, meterProfile: true),
                    JobDamageCalibrationPolicy.GetCalibrationPotency(hit with { CanCalibratePotency = false }, meterProfile: true));
                Assert.Equal(111, JobDamageCalibrationPolicy.GetCalibrationPotency(hit with { Source = Source with { Level = 0 }, AttributedSource = Source with { Level = 0 } }, meterProfile: true));
            }
    }

    [Fact]
    public void PeriodicProfilesAreSeparateFromCapturedTooltipsAndAmbiguousSharedStatus()
    {
        Assert.Equal(1, Data.Schema);
        Assert.Equal(1, Data.ProfileVersion); // Retain the original numeric baseline across catalog versions.
        Assert.Equal(142, Data.Direct.Select(r => r.Id).Distinct().Count());
        Assert.Equal(41, Data.Periodic.Select(r => r.Id).Distinct().Count());
        foreach (var row in Data.Periodic)
        {
            var application = Application(row.Id) with { ActionId = row.Id == 1714 ? 23290u : 100u };
            Assert.Equal(row.Potency, PeriodicCalibrationPotencyPolicy.GetPeriodicPotency(application));
            Assert.Equal(20, application.PeriodicPotency);
        }
        Assert.Equal(50, PeriodicCalibrationPotencyPolicy.GetPeriodicPotency(Application(1714) with { ActionId = 11386, PeriodicPotency = 50 }));
        Assert.Equal(20, PeriodicCalibrationPotencyPolicy.GetPeriodicPotency(Application(1714)));
        Assert.Equal(20, PeriodicCalibrationPotencyPolicy.GetPeriodicPotency(Application(50000)));
    }

    [Theory]
    [InlineData(19u, 15u, (byte)50, 100, 170)]
    [InlineData(22u, 75u, (byte)70, 170, 150)]
    [InlineData(22u, 78u, (byte)70, 100, 95)]
    [InlineData(22u, 87u, (byte)70, 100, 95)]
    [InlineData(31u, 16498u, (byte)100, 660, 650)]
    [InlineData(28u, 17870u, (byte)54, 100, 160)]
    [InlineData(28u, 17870u, (byte)64, 180, 180)]
    [InlineData(28u, 17870u, (byte)72, 200, 200)]
    [InlineData(28u, 17870u, (byte)82, 220, 220)]
    [InlineData(28u, 17870u, (byte)100, 220, 220)]
    public void MeterLevelRulesDoNotReplaceGameCalibration(uint job, uint action, byte level, double game, double expected)
    {
        var actor = Source with { ClassJobId = job, Level = level };
        var hit = Hit(action, game, 10000) with { Source = actor, AttributedSource = actor };
        Assert.Equal(game, JobDamageCalibrationPolicy.GetCalibrationPotency(hit));
        Assert.Equal(expected, JobDamageCalibrationPolicy.GetCalibrationPotency(hit, meterProfile: true));
    }

    [Fact]
    public void OverheatedAdjustsBothProfilesWithoutChangingCapturedDamage()
    {
        var actor = Source with { ClassJobId = 31 };
        var hit = Hit(16498, 660, 67000) with { Source = actor, AttributedSource = actor, SourceStatuses = [new(0xA80, actor, 0, 5)] };
        Assert.Equal(680, JobDamageCalibrationPolicy.GetCalibrationPotency(hit));
        Assert.Equal(670, JobDamageCalibrationPolicy.GetCalibrationPotency(hit, meterProfile: true));
        Assert.Equal(67000, hit.RawMeterAmount);
    }

    [Theory]
    [InlineData(31u, 16498u, 660, 1866u, 50, 3076, 3306.7)]
    [InlineData(37u, 36937u, 320, 1837u, 120, 10256, 11025.2)]
    public void DifferentCalibrationInputsReachAnAuditedDotResult(uint job, uint action, double game, uint status, double potency, double expectedBase, double expectedDamage)
    {
        var actor = Source with { ClassJobId = job };
        var tracker = new PeriodicDamageTracker();
        for (var i = 0; i < 20; i++) tracker.ObserveDirectDamage([Hit(action, game, 40000, job == 37 ? 1 : 0) with { Source = actor, AttributedSource = actor }]);
        tracker.Observe(Application(status) with { Source = actor, PeriodicPotency = potency, CriticalRateLowByte = 150 });
        var tick = Assert.Single(tracker.Process(new(1, Start.AddSeconds(3), Target, 0, "", 0, 30000, null)));
        Assert.Equal(expectedBase, tick.PeriodicCompatibilityEstimate!.Inputs!.BaseDamage);
        Assert.Equal(expectedDamage, tick.RawMeterAmount, 6);
        Assert.Equal(PeriodicCalibrationPotencyPolicy.Version, tick.PeriodicCompatibilityEstimate.CalibrationProfileVersion);
        Assert.Equal(30000u, tick.Amount);
    }

    private static ParsedDamageEvent Hit(uint action, double potency, uint amount, int index = 0, byte combo = 0) => new DirectDamageParser().Parse(
        new DamageActionPacket(1, Start, 1, Source, action, "Hit", [new(index, Target, [new(0, 3, 0, 5, 0, 0, amount) { Param2 = combo }])])
        { DirectPotency = potency, CanCalibratePotency = true, ActionCategoryId = 2 }).Single();
    private static DamageStatusApplication Application(uint status) => new(Target, Source, status, "Dot", 0, 0, "", Start.AddSeconds(1), 30, true, false, false)
    { PeriodicPotency = 20 };
}
