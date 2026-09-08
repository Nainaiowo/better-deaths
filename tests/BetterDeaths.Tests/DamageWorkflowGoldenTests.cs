namespace BetterDeaths.Tests;

using System.Text.Json;
using BetterDeaths.DamageParsing;

public sealed class DamageWorkflowGoldenTests
{
    private static readonly JsonElement Data = Load();

    public static TheoryData<int, string> Cases
    {
        get
        {
            var cases = new TheoryData<int, string>();
            var index = 0;
            foreach (var scenario in Data.GetProperty("Scenarios").EnumerateArray())
                cases.Add(index++, scenario.GetProperty("Name").GetString()!);
            return cases;
        }
    }

    [Fact]
    public void FixtureHasVersionedProvenanceAndNonemptyWorkflowFamilies()
    {
        Assert.Equal(1, Data.GetProperty("Schema").GetInt32());
        Assert.Equal(PeriodicCalibrationPotencyPolicy.Version, Data.GetProperty("CalibrationProfileVersion").GetInt32());
        Assert.Equal(2, Data.GetProperty("BinaryHashes").GetArrayLength());
        foreach (var hash in Data.GetProperty("BinaryHashes").EnumerateArray())
            Assert.Matches("^[0-9A-F]{64}$", hash.GetString()!);
        var scenarios = Data.GetProperty("Scenarios").EnumerateArray().ToArray();
        foreach (var family in new[] { "calibration:", "healing-opener:", "bard-refresh:", "ground-owners:", "pet-replacement:",
            "scholar-dual-effects", "samurai-application-buff:", "black-mage-thunder-replacement", "variable-buff-lifecycle:" })
            Assert.Contains(scenarios, s => s.GetProperty("Name").GetString()!.StartsWith(family, StringComparison.Ordinal));
        foreach (var scenario in scenarios)
            Assert.NotEmpty(scenario.GetProperty("Steps").EnumerateArray());
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task FrozenSequencesReachLiveFinalAndSavedTotals(int index, string _)
    {
        var module = new DamageParsingModule();
        var last = DateTime.MinValue;
        foreach (var step in Data.GetProperty("Scenarios")[index].GetProperty("Steps").EnumerateArray())
        {
            switch (step.GetProperty("Kind").GetString())
            {
                case "packet":
                    var packet = step.GetProperty("Packet").Deserialize<DamageActionPacket>()!;
                    last = packet.SeenAtUtc;
                    var direct = module.Process(packet);
                    if (step.TryGetProperty("ExpectedDirect", out var expectedDirect))
                    {
                        var hit = Assert.Single(direct);
                        Assert.Equal(expectedDirect.GetProperty("Source").GetUInt32(), hit.Source.EntityId);
                        Assert.Equal(expectedDirect.GetProperty("Owner").GetUInt32(), hit.AttributedSource!.EntityId);
                        Assert.Equal(expectedDirect.GetProperty("Amount").GetDouble(), hit.RawMeterAmount);
                    }
                    break;
                case "status":
                    var application = step.GetProperty("Application").Deserialize<DamageStatusApplication>()!;
                    last = application.SeenAtUtc;
                    module.ObserveStatus(application);
                    break;
                case "tick":
                    var input = step.GetProperty("Tick").Deserialize<PeriodicDamageTick>()!;
                    last = input.SeenAtUtc;
                    var ticks = module.ProcessPeriodicTick(input).ToList();
                    ticks.AddRange(module.FlushPendingPeriodicTicks(last, true));
                    var expected = step.GetProperty("Expected");
                    Assert.Equal(expected.GetArrayLength(), ticks.Count);
                    Assert.Equal(input.Amount, ticks.Sum(t => (double)t.Amount));
                    foreach (var item in expected.EnumerateArray())
                    {
                        var tick = Assert.Single(ticks, t => t.Source.EntityId == item.GetProperty("Source").GetUInt32() &&
                            t.StatusId == item.GetProperty("Status").GetUInt32());
                        Assert.Equal(item.GetProperty("Amount").GetDouble(), tick.RawMeterAmount, 6);
                        Assert.Equal(item.GetProperty("Estimated").GetBoolean(), tick.PeriodicMeterUsesEstimate);
                        if (item.GetProperty("Base").ValueKind == JsonValueKind.Number)
                        {
                            var estimate = tick.PeriodicCompatibilityEstimate!;
                            Assert.Equal(PeriodicCalibrationPotencyPolicy.Version, estimate.CalibrationProfileVersion);
                            Assert.Equal(item.GetProperty("Base").GetDouble(), estimate.Inputs!.BaseDamage);
                            Assert.Equal(item.GetProperty("Crit").GetDouble(), estimate.Inputs.CriticalRate, 9);
                            if (item.GetProperty("Healing").ValueKind is JsonValueKind.True or JsonValueKind.False)
                                Assert.Equal(item.GetProperty("Healing").GetBoolean(), estimate.UsedHealingCalibration);
                        }
                    }
                    break;
                default:
                    Assert.Fail("Unknown workflow input");
                    break;
            }
        }

        var current = module.GetCurrentEncounter(last)!;
        module.GetLiveEncounter();
        module.RefreshLiveEncounter(last.AddSeconds(1));
        await module.PendingLiveSnapshot!;
        module.RefreshLiveEncounter(last.AddSeconds(1));
        Check(module.GetLiveEncounter()!);
        var final = module.EndEncounter(last.AddSeconds(1), "Test")!;
        Check(final);
        Check(JsonSerializer.Deserialize<DamageEncounterSnapshot>(JsonSerializer.Serialize(final with { Events = [] }))!);

        void Check(DamageEncounterSnapshot snapshot)
        {
            Assert.Equal(current.ObservedMeterDamage, snapshot.ObservedMeterDamage, 6);
            Assert.Equal(snapshot.ObservedMeterDamage, snapshot.Sources.Sum(s => s.ObservedMeterDamage), 6);
            foreach (var source in snapshot.Sources)
                Assert.Equal(source.ObservedMeterDamage, source.Actions.Sum(a => a.ObservedMeterDamage), 6);
            Assert.Equal(snapshot.ObservedMeterDamage / snapshot.DurationSeconds, snapshot.DamagePerSecond, 6);
        }
    }

    private static JsonElement Load()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "DamageWorkflowGolden.json")));
        return document.RootElement.Clone();
    }
}
