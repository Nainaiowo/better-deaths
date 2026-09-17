namespace BetterDeaths;

using System.Globalization;
using System.Text.Json;
using BetterDeaths.DamageParsing;

public sealed class DamageMeterDisplayPolicyTests
{
    [Fact]
    public void RedactionCoversAnEntireAllianceAndNearbyPlayersWithoutDeathReviewData()
    {
        var sources = Enumerable.Range(1, 30)
            .Select(index => Player((uint)index, $"Private Name{index}", party: index <= 8)).ToArray();

        for (var index = 0; index < sources.Length; index++)
        {
            Assert.Equal($"Player {index + 1}", DamageMeterDisplayPolicy.FormatPlayerName(
                sources[index].Source, sources, redact: true));
            Assert.Equal($"Private Name{index + 1}", sources[index].Source.Name);
        }
    }

    [Fact]
    public void RedactedLabelsDoNotFollowDamageRankOrLoseMultiDigitNumbers()
    {
        var sources = Enumerable.Range(1, 24).Select(index => Player((uint)index, $"Name {index}")).ToArray();
        var reversed = sources.Reverse().ToArray();

        foreach (var source in sources)
        {
            Assert.Equal(DamageMeterDisplayPolicy.FormatPlayerName(source.Source, sources, true),
                DamageMeterDisplayPolicy.FormatPlayerName(source.Source, reversed, true));
        }

        Assert.Equal("Player 24", DamageMeterDisplayPolicy.FormatPlayerName(sources[23].Source, reversed, true));
    }

    [Fact]
    public void SameNamePlayersRemainDistinctAndDisablingRedactionRestoresNames()
    {
        var sources = new[] { Player(1, "Same Name"), Player(2, "Same Name") };
        Assert.Equal("Player 1", DamageMeterDisplayPolicy.FormatPlayerName(sources[0].Source, sources, true));
        Assert.Equal("Player 2", DamageMeterDisplayPolicy.FormatPlayerName(sources[1].Source, sources, true));
        Assert.Equal("Same Name", DamageMeterDisplayPolicy.FormatPlayerName(sources[1].Source, sources, false));
    }

    [Fact]
    public void SavedEncountersKeepRedactionAfterThePlayersHaveLeft()
    {
        var sources = new[] { Player(1, "Former Party"), Player(2, "Nearby Player") };
        var restored = JsonSerializer.Deserialize<DamageSourceSummary[]>(JsonSerializer.Serialize(sources))!;
        Assert.Equal("Player 2", DamageMeterDisplayPolicy.FormatPlayerName(restored[1].Source, restored, true));
    }

    [Fact]
    public void MissingActorIdsAndPartyOnlyIdentitiesStillRedact()
    {
        var sources = new[] { Player(0, "Alpha Name"), Player(0, "Beta Name", isPlayer: false, party: true) };
        Assert.Equal("Player 1", DamageMeterDisplayPolicy.FormatPlayerName(sources[0].Source, sources, true));
        Assert.Equal("Player 2", DamageMeterDisplayPolicy.FormatPlayerName(sources[1].Source, sources, true));
    }

    [Fact]
    public void NonPlayerAndLimitBreakLabelsAreNotRedactedOrCountedAsPlayers()
    {
        var npc = Player(1, "Automaton Queen", isPlayer: false);
        var limitBreak = Player(2, "Limit Break", party: true);
        limitBreak = limitBreak with { Source = limitBreak.Source with { IsLimitBreak = true } };
        var player = Player(3, "Private Name");
        var sources = new[] { npc, limitBreak, player };
        Assert.Equal("Automaton Queen", DamageMeterDisplayPolicy.FormatPlayerName(npc.Source, sources, true));
        Assert.Equal("Limit Break", DamageMeterDisplayPolicy.FormatPlayerName(limitBreak.Source, sources, true));
        Assert.Equal("Player 1", DamageMeterDisplayPolicy.FormatPlayerName(player.Source, sources, true));
    }

    [Theory]
    [InlineData(null, 0.48f, 0.48f)]
    [InlineData(null, 0.87f, 0.87f)]
    [InlineData(0.6f, 0.9f, 0.6f)]
    [InlineData(0.6f, 0.4f, 0.6f)]
    [InlineData(0.2f, 0.9f, 0.35f)]
    [InlineData(2.0f, 0.9f, 1.0f)]
    [InlineData(0.0f, 0.9f, 1.0f)]
    [InlineData(float.NaN, 0.9f, 1.0f)]
    [InlineData(float.PositiveInfinity, 0.9f, 1.0f)]
    public void MeterOpacityInheritsOnlyWhenMissingAndHonorsItsOwnSetting(float? saved, float legacy, float expected)
    {
        Assert.Equal(expected, DamageMeterDisplayPolicy.GetWidgetBackgroundOpacity(saved, legacy));
    }

    [Fact]
    public void MigratedOpacityIsIndependentOfLaterDeathWidgetChanges()
    {
        var migrated = DamageMeterDisplayPolicy.GetWidgetBackgroundOpacity(null, 0.48f);
        Assert.Equal(0.48f, DamageMeterDisplayPolicy.GetWidgetBackgroundOpacity(migrated, 0.95f));
    }

    [Theory]
    [InlineData(20.0f, 20.0f)]
    [InlineData(12.0f, 12.0f)]
    [InlineData(32.0f, 32.0f)]
    [InlineData(8.0f, 12.0f)]
    [InlineData(48.0f, 32.0f)]
    [InlineData(0.0f, 20.0f)]
    [InlineData(-1.0f, 20.0f)]
    [InlineData(float.NaN, 20.0f)]
    [InlineData(float.PositiveInfinity, 20.0f)]
    public void MeterIconsKeepTheirPreviousDefaultAndRespectSliderBounds(float saved, float expected)
    {
        Assert.Equal(expected, DamageMeterDisplayPolicy.GetWidgetIconSize(saved));
    }

    private static DamageSourceSummary Player(uint id, string name, bool isPlayer = true, bool party = false) =>
        new(new DamageActorIdentity(id, name, 0, string.Empty, isPlayer, 24) { IsPartyMember = party },
            1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, []);

    [Theory]
    [InlineData(1, "1")]
    [InlineData(999, "999")]
    [InlineData(999.49, "999")]
    [InlineData(999.5, "1.0k")]
    [InlineData(1000, "1.0k")]
    [InlineData(1234, "1.2k")]
    [InlineData(25636.278, "25.6k")]
    [InlineData(999949, "999.9k")]
    [InlineData(999950, "1.0m")]
    [InlineData(1000000, "1.0m")]
    [InlineData(2400000, "2.4m")]
    [InlineData(8998376.89614615, "9.0m")]
    [InlineData(64831850.10619742, "64.8m")]
    [InlineData(999950000, "1.0b")]
    [InlineData(1234567891, "1.2b")]
    [InlineData(1234567891000, "1.2t")]
    public void ConciseNumbersUseCompactUnitsWithCleanBoundaries(double value, string expected)
    {
        Assert.Equal(expected, DamageMeterDisplayPolicy.FormatNumber(value, true, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(25636.278, "25,636")]
    [InlineData(8998376.89614615, "8,998,377")]
    [InlineData(1000, "1,000")]
    public void NormalModeKeepsFullNumbers(double value, string expected)
    {
        Assert.Equal(expected, DamageMeterDisplayPolicy.FormatNumber(value, false, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void MissingNumbersKeepTheDash(double value)
    {
        Assert.Equal("-", DamageMeterDisplayPolicy.FormatNumber(value, false));
        Assert.Equal("-", DamageMeterDisplayPolicy.FormatNumber(value, true));
    }

    [Fact]
    public void CompactFormattingUsesTheDisplayCulture()
    {
        Assert.Equal("1,2k", DamageMeterDisplayPolicy.FormatNumber(1234, true, CultureInfo.GetCultureInfo("fr-FR")));
    }

    [Fact]
    public void NumericColumnsFitHeadersAndValuesWithoutFixedWidthLimits()
    {
        Assert.Equal(72, DamageMeterDisplayPolicy.GetColumnWidth(DamageMeterColumn.DamagePercent, 40, 56, true, 1));
        Assert.Equal(280, DamageMeterDisplayPolicy.GetColumnWidth(DamageMeterColumn.TotalDamage, 50, 264, false, 1));
        Assert.Equal(104, DamageMeterDisplayPolicy.GetColumnWidth(DamageMeterColumn.CriticalDirectHitPercent, 90, 40, true, 1));
    }

    [Theory]
    [InlineData(DamageMeterColumn.PlayerName)]
    [InlineData(DamageMeterColumn.MaxHitName)]
    public void TextColumnsGrowWithContentButHaveModeSpecificBounds(DamageMeterColumn column)
    {
        var small = DamageMeterDisplayPolicy.GetColumnWidth(column, 40, 68, true, 1);
        var longer = DamageMeterDisplayPolicy.GetColumnWidth(column, 40, 110, true, 1);
        var concise = DamageMeterDisplayPolicy.GetColumnWidth(column, 40, 600, true, 1);
        var normal = DamageMeterDisplayPolicy.GetColumnWidth(column, 40, 600, false, 1);
        Assert.Equal(80, small);
        Assert.Equal(128, longer);
        Assert.Equal(192, concise);
        Assert.Equal(336, normal);
        Assert.True(small < longer && longer < concise && concise < normal);
    }

    [Fact]
    public void ColumnSizingScalesWithTheFontAndUsesStableWidthSteps()
    {
        var first = DamageMeterDisplayPolicy.GetColumnWidth(DamageMeterColumn.DamagePerSecond, 30, 49, true, 1);
        var next = DamageMeterDisplayPolicy.GetColumnWidth(DamageMeterColumn.DamagePerSecond, 30, 50, true, 1);
        Assert.Equal(first, next);
        Assert.Equal(first * 2, DamageMeterDisplayPolicy.GetColumnWidth(DamageMeterColumn.DamagePerSecond, 60, 98, true, 2));
    }

    [Fact]
    public void ConciseFormattedValuesProduceNarrowerColumns()
    {
        var full = DamageMeterDisplayPolicy.FormatNumber(8998376, false, CultureInfo.InvariantCulture);
        var compact = DamageMeterDisplayPolicy.FormatNumber(8998376, true, CultureInfo.InvariantCulture);
        var fullWidth = DamageMeterDisplayPolicy.GetColumnWidth(DamageMeterColumn.TotalDamage, 40, full.Length * 8, false, 1);
        var conciseWidth = DamageMeterDisplayPolicy.GetColumnWidth(DamageMeterColumn.TotalDamage, 40, compact.Length * 8, true, 1);
        Assert.True(conciseWidth < fullWidth);
    }
}
