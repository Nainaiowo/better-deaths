namespace BetterDeaths.DamageParsing;

using System;

internal static class ServerFrameTimestampPolicy
{
    private const ulong MaximumUnixMilliseconds = 253402300799999;
    private static readonly TimeSpan MaximumClockDifference = TimeSpan.FromDays(1);

    public static bool TryConvert(
        ulong unixMilliseconds,
        DateTime receivedAtUtc,
        out DateTime serverSeenAtUtc)
    {
        serverSeenAtUtc = default;
        if (unixMilliseconds == 0 || unixMilliseconds > MaximumUnixMilliseconds)
        {
            return false;
        }

        var converted = DateTimeOffset
            .FromUnixTimeMilliseconds((long)unixMilliseconds)
            .UtcDateTime;
        var received = receivedAtUtc.Kind == DateTimeKind.Utc
            ? receivedAtUtc
            : receivedAtUtc.ToUniversalTime();
        if ((converted - received).Duration() > MaximumClockDifference)
        {
            return false;
        }

        serverSeenAtUtc = converted;
        return true;
    }
}
