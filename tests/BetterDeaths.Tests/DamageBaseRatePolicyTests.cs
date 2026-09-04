namespace BetterDeaths.DamageParsing;

public sealed class DamageBaseRatePolicyTests
{
    [Theory]
    [InlineData(0, 11, false, 0.0)]
    [InlineData(0, 100, false, 0.0)]
    [InlineData(0, 11, true, 0.05)]
    [InlineData(0, 10, false, 0.05)]
    [InlineData(100, 100, false, 1.0)]
    public void ObservedRatesDoNotInventDirectHits(int hits, int count, bool critical, double expected)
    {
        Assert.Equal(expected, DamageBaseRatePolicy.EstimateObserved(hits, count, critical), 8);
    }
    [Fact]
    public void LevelOneHundredAttributesProduceTieredBaseRates()
    {
        var rates = Assert.IsType<DamageBaseRateSnapshot>(
            DamageBaseRatePolicy.FromAttributes(2618, 1608, 420, 2780));

        Assert.Equal(0.208, rates.Critical, 6);
        Assert.Equal(0.235, rates.DirectHit, 6);
    }

    [Theory]
    [InlineData(95, 0.35, 0.35)]
    [InlineData(145, 0.15, 0.145)]
    [InlineData(145, 0.40, 0.40)]
    public void CriticalSnapshotByteResolvesToNearestWrappedRate(
        byte lowByte,
        double expected,
        double resolved)
    {
        Assert.Equal(
            resolved,
            DamageBaseRatePolicy.ResolveCriticalLowByte(lowByte, expected),
            6);
    }
}
