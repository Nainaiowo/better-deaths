using BetterDeaths.DamageParsing;

namespace BetterDeaths.Tests;

public sealed class PeriodicBaseDamageTests
{
    [Theory]
    [InlineData(251.99, null, 251)]
    [InlineData(4097, null, 4353)]
    [InlineData(90.9, 100, 90)]
    [InlineData(128, 0, 0)]
    [InlineData(384, 0, 512)]
    [InlineData(128.99, 0, 0)]
    [InlineData(129, 0, 256)]
    [InlineData(4096, 0, 4096)]
    [InlineData(4097, 1, 4353)]
    [InlineData(9000, 40, 9256)]
    public void ReconstructsObservedBaseDamageCases(double estimate, int? lowByte, double expected)
    {
        Assert.Equal(expected, PeriodicDamageTracker.ReconstructBaseAmount(estimate, (byte?)lowByte));
    }
}
