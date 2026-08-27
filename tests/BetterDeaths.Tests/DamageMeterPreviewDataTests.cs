namespace BetterDeaths.DamageParsing;

using System.Text.Json;

public sealed class DamageMeterPreviewDataTests
{
    [Fact]
    public void PreviewMatchesTheRedactedReportAggregate()
    {
        var preview = DamageMeterPreviewData.Create();

        Assert.Equal(496_611_332UL, preview.TotalDamage);
        Assert.Equal(9, preview.Sources.Count);
        Assert.Equal(8, preview.Sources.Count(source => source.Source.IsPlayer));
        Assert.All(
            preview.Sources.Where(source => source.Source.IsPlayer),
            source => Assert.StartsWith("Player ", source.Source.Name, StringComparison.Ordinal));
        Assert.Equal(1, preview.Sources.Sum(source => source.Deaths));
        Assert.All(preview.Sources, source => Assert.NotEmpty(source.Actions));
        Assert.Equal(preview.TotalDamage, preview.RaidAdjustedDamage);
    }

    [Fact]
    public void AggregatedEncounterRoundTripsThroughHistoryJson()
    {
        var preview = DamageMeterPreviewData.Create() with
        {
            Events = [],
            Targets = [],
        };

        var json = JsonSerializer.Serialize(preview);
        var restored = JsonSerializer.Deserialize<DamageEncounterSnapshot>(json);

        Assert.NotNull(restored);
        Assert.Equal(preview.TotalDamage, restored.TotalDamage);
        Assert.Equal(preview.Sources.Count, restored.Sources.Count);
        Assert.Equal(preview.Sources[0].Actions.Count, restored.Sources[0].Actions.Count);
        Assert.Empty(restored.Events);
        Assert.Empty(restored.Targets);
    }
}
