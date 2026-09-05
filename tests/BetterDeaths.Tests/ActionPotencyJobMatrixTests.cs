namespace BetterDeaths.DamageParsing;

using System.Text.Json;
using Lumina.Text.ReadOnly;

public sealed class ActionPotencyJobMatrixTests
{
    public sealed record Fixture(uint ActionId, string Name, uint JobId, byte Level,
        double? Direct, double? Periodic, string Macro);

    private static readonly Fixture[] Fixtures = JsonSerializer.Deserialize<Fixture[]>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ActionPotencies.json")))!;

    public static TheoryData<string, uint, byte, double?, double?, string> Cases
    {
        get
        {
            var cases = new TheoryData<string, uint, byte, double?, double?, string>();
            foreach (var fixture in Fixtures)
            {
                cases.Add($"{fixture.ActionId} {fixture.Name}", fixture.JobId, fixture.Level,
                    fixture.Direct, fixture.Periodic, fixture.Macro);
            }
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ReadsReviewedGameData(string _, uint job, byte level, double? direct, double? periodic, string macro)
    {
        var text = ActionPotencyTextResolver.Resolve(ReadOnlySeString.FromMacroString(macro), job, level);
        var profile = ActionPotencyProfileParser.Parse(text, true);
        Assert.Equal(direct, profile.DirectPotency);
        Assert.Equal(periodic, profile.PeriodicPotency);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void PotencyResolutionDoesNotReplaceCapturedHitDamage(
        string _, uint job, byte level, double? direct, double? periodic, string macro)
    {
        var profile = ActionPotencyProfileParser.Parse(
            ActionPotencyTextResolver.Resolve(ReadOnlySeString.FromMacroString(macro), job, level), true);
        Assert.Equal(direct, profile.DirectPotency);
        Assert.Equal(periodic, profile.PeriodicPotency);
        var source = new DamageActorIdentity(0x1001, "Player", 0, "", true, job) { Level = level };
        var target = new DamageActorIdentity(0x40000001, "Target", 0, "", false, 0);
        var module = new DamageParsingModule();
        var hit = Assert.Single(module.Process(new DamageActionPacket(1, DateTime.UtcNow, 1, source, 1, "Action",
            [new DamageActionTarget(0, target, [new DamageActionEffect(0, 3, 0, 0, 0, 0, 54321)])])
        {
            DirectPotency = profile.DirectPotency,
            CanCalibratePotency = profile.DirectPotency is > 0,
        }));
        Assert.Equal(54321u, hit.Amount);
        Assert.Equal(54321.0, hit.RawMeterAmount);
    }

    [Fact]
    public void MatrixIncludesEveryImplementedCombatClassAndJob()
    {
        uint[] expectedJobs = [1, 2, 3, 4, 5, 6, 7, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28,
            29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42];
        Assert.Equal(expectedJobs, Fixtures.Select(f => f.JobId).Distinct().OrderBy(id => id));
        Assert.All(expectedJobs, job => Assert.Contains(Fixtures, f => f.JobId == job && f.Direct is > 0));
    }
}
