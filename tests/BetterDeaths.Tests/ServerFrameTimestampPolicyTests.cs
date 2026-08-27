using BetterDeaths.DamageParsing;

namespace BetterDeaths.Tests;

public sealed class ServerFrameTimestampPolicyTests
{
    [Fact]
    public void ConvertsUnixMillisecondsWithoutLosingFractionalTime()
    {
        var receivedAtUtc = new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);
        var expected = receivedAtUtc.AddMilliseconds(-35);

        var converted = ServerFrameTimestampPolicy.TryConvert(
            (ulong)new DateTimeOffset(expected).ToUnixTimeMilliseconds(),
            receivedAtUtc,
            out var serverSeenAtUtc);

        Assert.True(converted);
        Assert.Equal(expected, serverSeenAtUtc);
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(253402300800000UL)]
    public void RejectsInvalidUnixMilliseconds(ulong timestamp)
    {
        var converted = ServerFrameTimestampPolicy.TryConvert(
            timestamp,
            DateTime.UtcNow,
            out _);

        Assert.False(converted);
    }

    [Fact]
    public void RejectsAStaleFrameTimestamp()
    {
        var receivedAtUtc = new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);
        var stale = receivedAtUtc.AddDays(-2);

        var converted = ServerFrameTimestampPolicy.TryConvert(
            (ulong)new DateTimeOffset(stale).ToUnixTimeMilliseconds(),
            receivedAtUtc,
            out _);

        Assert.False(converted);
    }
}
