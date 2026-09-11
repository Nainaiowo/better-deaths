namespace BetterDeaths;

using System.Globalization;

public sealed class DamageMeterDisplayPolicyTests
{
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
