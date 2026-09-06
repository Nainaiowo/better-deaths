namespace BetterDeaths.Tests;

public sealed class ContentCapturePolicyTests
{
    [Theory]
    [InlineData(2, 3)]
    [InlineData(21, 31)]
    [InlineData(0, 3)]
    [InlineData(0, 31)]
    public void BlocksRegularAndDeepDungeonsIncludingUnlistedFloors(uint category, uint intendedUse)
    {
        Assert.True(ContentCapturePolicy.IsDungeon(category, intendedUse));
    }

    [Theory]
    [InlineData(30, 4)] // Variant.
    [InlineData(30, 57)] // Criterion.
    [InlineData(30, 58)] // Criterion Savage.
    [InlineData(0, 4)] // Variant/Criterion territories without a duty row.
    [InlineData(0, 57)]
    [InlineData(0, 58)]
    [InlineData(4, 10)] // Trials, including Extreme.
    [InlineData(4, 3)] // Battle on the Big Bridge / Battle in the Big Keep.
    [InlineData(5, 8)] // Alliance raids.
    [InlineData(5, 16)]
    [InlineData(5, 17)] // Raids, including Savage.
    [InlineData(28, 17)] // Ultimate, including DMU.
    [InlineData(37, 36)] // Chaotic alliance raid.
    [InlineData(0, 0)] // No territory data / outside a duty.
    [InlineData(0, 1)]
    [InlineData(0, 2)]
    public void PreservesAllowedContent(uint category, uint intendedUse)
    {
        Assert.False(ContentCapturePolicy.IsDungeon(category, intendedUse));
    }
}
