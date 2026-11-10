using System.Text.Json;
using System.Reflection;

namespace BetterDeaths.Tests;

public sealed class DefensiveStatusContractTests
{
    public static IEnumerable<object[]> ClientStatuses()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "DefensiveStatuses.json")));
        foreach (var status in fixture.RootElement.GetProperty("Statuses").EnumerateArray())
            yield return [status.GetProperty("Id").GetUInt32(), status.GetProperty("Name").GetString()!,
                status.GetProperty("ExpectedDefensive").GetBoolean()];
    }

    [Theory]
    [MemberData(nameof(ClientStatuses))]
    public void ClassificationMatchesAuditedClientStatusIdsRatherThanReusedNames(uint id, string name, bool expected)
    {
        var status = new StatusSnapshot(id, name, 0, 1, 0, 10);
        Assert.Equal(expected, Plugin.IsDefensiveStatus(status));
        if (expected)
            Assert.True(Plugin.IsDefensiveStatus(status with { Name = "Localized name" }));
    }

    [Fact]
    public void EveryProductionDefinitionHasARealClientContract()
    {
        var ids = ClientStatuses().Where(row => (bool)row[2]).Select(row => (uint)row[0]).ToHashSet();
        foreach (var definition in MitigationStatusTests.DefensiveCatalogue())
            Assert.Contains((uint)definition[0], ids);
    }

    [Fact]
    public void EveryWhatIfTimingAliasIsARealDefensiveOrBossEffect()
    {
        var ids = ClientStatuses().Where(row => (bool)row[2]).Select(row => (uint)row[0]).ToHashSet();
        foreach (var row in PossibleMitigationTimingTests.CurrentWhatIfEffects())
        {
            var id = (uint)row[2];
            if (!Plugin.IsBossMitigationStatus(new(id, "", 0, 0, 0, 1)))
                Assert.Contains(id, ids);
        }
    }

    [Fact]
    public void HealingPotencyAndShieldStatusesWithTheSameNameRemainDistinct()
    {
        var potency = Plugin.GetMitigationDisplayInfo(new(1892, "Neutral Sect", 0, 1, 0, 20));
        var shield = Plugin.GetMitigationDisplayInfo(new(1921, "Neutral Sect", 0, 1, 0, 30));
        Assert.Contains(potency.Types, value => value.Label == "Heal potency");
        Assert.DoesNotContain(potency.Types, value => value.Label == "Shield");
        Assert.Contains(shield.Types, value => value.Label == "Shield");
    }

    [Fact]
    public void SheltronBlockDoesNotInventFlatMitigation()
    {
        var block = Plugin.GetMitigationDisplayInfo(new(728, "Sheltron", 0, 1, 0, 6));
        Assert.Contains(block.Types, value => value.Label == "Block");
        Assert.Empty(block.MitigationPercents);
        Assert.Equal(15, Assert.Single(Plugin.GetMitigationDisplayInfo(new(1856, "Sheltron", 0, 1, 0, 6)).MitigationPercents).Percent);
    }

    [Fact]
    public void EveryWhatIfActionUsesRealMitigationStatusIdsAndStillHasCalculableReduction()
    {
        var definitions = (Array)typeof(Plugin).GetField("PossibleMitigationDefinitions", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var clientIds = ClientStatuses().Where(row => (bool)row[2]).Select(row => (uint)row[0]).ToHashSet();
        foreach (var definition in definitions)
        {
            var statuses = (System.Collections.IEnumerable)definition!.GetType().GetProperty("Statuses")!.GetValue(definition)!;
            var calculated = false;
            foreach (var value in statuses)
            {
                var type = value!.GetType();
                var id = (uint)type.GetProperty("StatusId")!.GetValue(value)!;
                var name = (string)type.GetProperty("Name")!.GetValue(value)!;
                var snapshot = new StatusSnapshot(id, name, 0, 0, 0, 15);
                Assert.True(Plugin.IsBossMitigationStatus(snapshot) || Plugin.IsDefensiveStatus(snapshot), name);
                if (id != 0 && !Plugin.IsBossMitigationStatus(snapshot))
                    Assert.Contains(id, clientIds);
                calculated |= Plugin.GetMitigationDisplayInfo(snapshot).MitigationPercents.Count > 0;
            }
            Assert.True(calculated, definition.GetType().GetProperty("ActionName")!.GetValue(definition)?.ToString());
        }
    }
}
